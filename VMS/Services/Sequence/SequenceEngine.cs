using System.Diagnostics;
using VMS.Interfaces;
using VMS.PLC.Interfaces;
using VMS.PLC.Models;
using VMS.PLC.Models.Sequence;

namespace VMS.Services.Sequence
{
    /// <summary>
    /// 시퀀스 노드 그래프를 순차 실행하는 엔진.
    /// AutoProcessService의 WaitForBitValueAsync, WriteToolResultsAsync 로직을 재활용.
    /// </summary>
    public class SequenceEngine : ISequenceEngine
    {
        private readonly IPlcConnection _plc;
        private readonly PlcVendor _vendor;
        private readonly Func<string, Task<bool>> _grabFunc;
        private readonly Func<string, Task<bool>> _inspectFunc;
        private readonly Action<string, bool> _setResultFunc;
        private readonly Action<string> _resetFunc;
        private readonly Func<string, IReadOnlyList<ToolInspectionResult>?>? _getToolResultsFunc;
        private readonly Func<int, Task>? _recipeChangeByIndexFunc;
        private readonly Action<int>? _stepChangeFunc;

        // Phase 2 — IO 디바이스 dispatch. null 이면 기존 단일 PLC 모드 (후방호환).
        // null 이 아니면 SequenceNodeConfig.DeviceId 가 비어있거나 "MainPLC" 일 때만 _plc 사용,
        // 그 외 DeviceId 는 registry 에서 IIoBoardConnection lookup.
        private readonly IIoDeviceRegistry? _ioRegistry;
        private const string DefaultPlcDeviceId = "MainPLC";

        // Event-based trigger: per-address TaskCompletionSource for BitChanged events
        private readonly Dictionary<string, TaskCompletionSource<bool>> _bitWaiters = new();
        private readonly object _waiterLock = new();

        // 마지막 Inspection 결과 (Branch 노드용)
        private bool _lastInspectionOk;

        // 카메라별 Inspection 결과 추적
        private readonly Dictionary<string, bool> _cameraResults = new();

        /// <summary>전체 카메라 검사 결과 (모든 Inspection 통과 여부)</summary>
        public bool AllInspectionsOk => _cameraResults.Count == 0 || _cameraResults.Values.All(v => v);

        // Repeat 카운터 (노드 ID → 현재 반복 횟수)
        private readonly Dictionary<string, int> _repeatCounters = new();

        // "1사이클 = 1개" 집계 — 이번 사이클(직전 Repeat 통과 이후)에 실행된 Inspection 수
        private int _cycleInspectionCount;

        // Reset 신호 모니터링
        private CancellationTokenSource? _resetCts;
        private string? _resetAddress;
        private InputCheckMode _resetCheckMode;
        private int? _resetCompareValue;

        public bool IsRunning { get; private set; }
        public string? CurrentNodeId { get; private set; }
        public bool WasReset { get; private set; }

        public event EventHandler<SequenceNodeEventArgs>? NodeExecuting;
        public event EventHandler<SequenceNodeEventArgs>? NodeCompleted;
        public event EventHandler<SequenceErrorEventArgs>? SequenceError;
        public event EventHandler? SequenceCompleted;

        /// <summary>
        /// 사이클 완료 — Repeat 노드 통과 시점(및 시퀀스 정상 종료 시점)에, 그 사이클에서
        /// Inspection 이 1회 이상 실행됐으면 1회 발생. "1사이클 = 1개" WO 집계의 근거 이벤트.
        /// 오류 중단·Reset 중단 시에는 미완성 사이클이므로 발생하지 않는다.
        /// </summary>
        public event EventHandler<SequenceCycleCompletedEventArgs>? CycleCompleted;

        public SequenceEngine(
            IPlcConnection plc,
            PlcVendor vendor,
            Func<string, Task<bool>> grabFunc,
            Func<string, Task<bool>> inspectFunc,
            Action<string, bool> setResultFunc,
            Action<string> resetFunc,
            Func<string, IReadOnlyList<ToolInspectionResult>?>? getToolResultsFunc = null,
            Func<int, Task>? recipeChangeByIndexFunc = null,
            Action<int>? stepChangeFunc = null,
            IIoDeviceRegistry? ioRegistry = null)
        {
            _plc = plc;
            _vendor = vendor;
            _grabFunc = grabFunc;
            _inspectFunc = inspectFunc;
            _setResultFunc = setResultFunc;
            _resetFunc = resetFunc;
            _getToolResultsFunc = getToolResultsFunc;
            _recipeChangeByIndexFunc = recipeChangeByIndexFunc;
            _stepChangeFunc = stepChangeFunc;
            _ioRegistry = ioRegistry;

            _plc.BitChanged += OnPlcBitChanged;
        }

        public async Task RunAsync(SequenceConfig config, CancellationToken ct)
        {
            if (IsRunning) return;
            IsRunning = true;
            WasReset = false;
            _repeatCounters.Clear();
            _cameraResults.Clear();
            _lastInspectionOk = true;
            _cycleInspectionCount = 0;

            VMS.Core.Security.AuditLogger.Instance.Log(
                VMS.Core.Security.AuditCategory.SequenceControl, "SequenceStart",
                VMS.Core.Security.AuditOutcome.Success,
                source: nameof(SequenceEngine),
                details: $"Sequence='{config.Name}', Nodes={config.Nodes.Count}");

            // Reset 신호 모니터링 설정
            using var resetCts = new CancellationTokenSource();
            using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, resetCts.Token);
            PlcAddress? resetAddr = null;
            Task? resetPollingTask = null;
            var isBitMode = config.ResetSignalCheckMode == InputCheckMode.BitOn
                         || config.ResetSignalCheckMode == InputCheckMode.BitOff;

            if (!string.IsNullOrEmpty(config.ResetSignalAddress))
            {
                try
                {
                    var parsed = PlcAddress.Parse(config.ResetSignalAddress, _vendor);
                    resetAddr = parsed;
                    _resetCts = resetCts;
                    _resetCheckMode = config.ResetSignalCheckMode;
                    _resetCompareValue = config.ResetSignalCompareValue;

                    if (isBitMode)
                    {
                        // Bit 모드: 이벤트 기반 모니터링
                        _resetAddress = parsed.RawAddress.ToUpperInvariant();
                        await _plc.StartMonitoringAsync(parsed, 50);
                    }
                    else
                    {
                        // Word 모드: 폴링 기반 모니터링
                        _resetAddress = null; // BitChanged 이벤트 무시
                        resetPollingTask = Task.Run(async () =>
                        {
                            while (!ct.IsCancellationRequested && !resetCts.IsCancellationRequested)
                            {
                                try
                                {
                                    // Reset 신호는 시스템 레벨 — 디바이스 별도 지정 안 함 (PLC 경로 사용).
                                    var met = await CheckSignalAsync(
                                        null, config.ResetSignalAddress, config.ResetSignalCheckMode, config.ResetSignalCompareValue);
                                    if (met)
                                    {
                                        resetCts.Cancel();
                                        return;
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Debug.WriteLine($"[SequenceEngine] Reset polling error: {ex.Message}");
                                }
                                await Task.Delay(50, CancellationToken.None);
                            }
                        });
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[SequenceEngine] Reset signal monitoring setup failed: {ex.Message}");
                    resetAddr = null;
                    _resetCts = null;
                    _resetAddress = null;
                }
            }

            try
            {
                // 노드 딕셔너리 구축
                var lookup = config.Nodes.ToDictionary(n => n.Id);

                // Start 노드 찾기
                var startNode = config.Nodes.FirstOrDefault(n => n.NodeType == SequenceNodeType.Start);
                if (startNode == null)
                {
                    Debug.WriteLine("[SequenceEngine] No Start node found");
                    return;
                }

                var currentNodeId = startNode.NextNodeId;
                var faulted = false;

                while (currentNodeId != null && !combinedCts.Token.IsCancellationRequested)
                {
                    if (!lookup.TryGetValue(currentNodeId, out var node))
                    {
                        Debug.WriteLine($"[SequenceEngine] Node not found: {currentNodeId}");
                        break;
                    }

                    CurrentNodeId = currentNodeId;
                    var cameraId = node.NodeType == SequenceNodeType.Inspection ? node.CameraId : null;
                    var args = new SequenceNodeEventArgs(node.Id, node.Name, node.NodeType, cameraId);
                    NodeExecuting?.Invoke(this, args);

                    try
                    {
                        var nextId = await ExecuteNodeAsync(node, config, combinedCts.Token);
                        NodeCompleted?.Invoke(this, args);
                        currentNodeId = nextId;
                    }
                    catch (OperationCanceledException) when (resetCts.IsCancellationRequested && !ct.IsCancellationRequested)
                    {
                        // Reset 신호 감지 → Start로 복귀 (RunAsync 정상 리턴)
                        WasReset = true;
                        Debug.WriteLine("[SequenceEngine] Reset signal detected — returning to Start");
                        break;
                    }
                    catch (OperationCanceledException)
                    {
                        throw; // 외부 취소 (Stop 버튼) → 상위로 전파
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[SequenceEngine] Error at node '{node.Name}': {ex.Message}");
                        SequenceError?.Invoke(this, new SequenceErrorEventArgs(node.Id, node.Name, ex));
                        faulted = true;
                        break;
                    }
                }

                // 외부 취소가 아닌지 확인 후 전파
                ct.ThrowIfCancellationRequested();

                // 시퀀스 정상 종료 = 마지막(또는 유일한) 사이클의 경계. Repeat 없이 End 로
                // 끝나는 시퀀스도 여기서 사이클 1회로 집계된다. 오류/Reset 중단은 미완성
                // 사이클이므로 세지 않는다.
                if (!faulted && !WasReset)
                    FireCycleCompletedIfInspected(resetForNextCycle: false);

                CurrentNodeId = null;
                SequenceCompleted?.Invoke(this, EventArgs.Empty);
            }
            finally
            {
                // Reset 모니터링 정리
                if (resetAddr != null)
                {
                    _resetCts = null;
                    _resetAddress = null;

                    if (isBitMode)
                    {
                        try { await _plc.StopMonitoringAsync(resetAddr); }
                        catch { /* 이미 중단됐을 수 있음 */ }
                    }

                    // 폴링 태스크 정리 (이미 resetCts 취소됨)
                    if (resetPollingTask != null)
                    {
                        try { await resetPollingTask; }
                        catch { /* 무시 */ }
                    }
                }

                IsRunning = false;
                CancelAllWaiters();
                VMS.Core.Security.AuditLogger.Instance.Log(
                    VMS.Core.Security.AuditCategory.SequenceControl,
                    WasReset ? "SequenceReset" : "SequenceStop",
                    VMS.Core.Security.AuditOutcome.Success,
                    source: nameof(SequenceEngine),
                    details: $"Sequence='{config.Name}', AllInspectionsOk={AllInspectionsOk}");
            }
        }

        private async Task<string?> ExecuteNodeAsync(SequenceNodeConfig node, SequenceConfig config, CancellationToken ct)
        {
            return node.NodeType switch
            {
                SequenceNodeType.Start => node.NextNodeId,
                SequenceNodeType.End => null,
                SequenceNodeType.InputCheck => await ExecuteInputCheckAsync(node, ct),
                SequenceNodeType.OutputAction => await ExecuteOutputActionAsync(node, ct),
                SequenceNodeType.Inspection => await ExecuteInspectionAsync(node, config, ct),
                SequenceNodeType.Branch => ExecuteBranch(node),
                SequenceNodeType.Delay => await ExecuteDelayAsync(node, ct),
                SequenceNodeType.Repeat => ExecuteRepeat(node),
                SequenceNodeType.RecipeChange => await ExecuteRecipeChangeAsync(node),
                SequenceNodeType.StepChange => await ExecuteStepChangeAsync(node),
                _ => node.NextNodeId
            };
        }

        // --- InputCheck: PLC 비트/워드 조건 대기 (또는 IO 보드 채널) ---
        private async Task<string?> ExecuteInputCheckAsync(SequenceNodeConfig node, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(node.PlcAddress))
                return node.NextNodeId;

            // Phase 2 — DeviceId 분기. null/MainPLC = 기본 PLC, 그 외 = IO 보드 lookup.
            if (TryGetBoardForNode(node, out var board))
            {
                await ExecuteBoardInputCheckAsync(board!, node, ct);
                return node.NextNodeId;
            }

            var addr = PlcAddress.Parse(node.PlcAddress, _vendor);

            switch (node.CheckMode)
            {
                case InputCheckMode.BitOn:
                    await WaitForBitValueAsync(addr, true, ct, node.TimeoutMs);
                    break;

                case InputCheckMode.BitOff:
                    await WaitForBitValueAsync(addr, false, ct, node.TimeoutMs);
                    break;

                case InputCheckMode.BitRisingEdge:
                    await WaitForPlcBitEdgeAsync(addr, true, node, ct);
                    break;

                case InputCheckMode.BitFallingEdge:
                    await WaitForPlcBitEdgeAsync(addr, false, node, ct);
                    break;

                case InputCheckMode.WordEquals:
                case InputCheckMode.WordGreaterThan:
                case InputCheckMode.WordLessThan:
                    await WaitForWordConditionAsync(addr, node.CheckMode, node.CompareValue ?? 0, ct, node.TimeoutMs);
                    break;
            }

            return node.NextNodeId;
        }

        // --- OutputAction: PLC 비트/워드 쓰기 (또는 IO 보드 채널) ---
        private async Task<string?> ExecuteOutputActionAsync(SequenceNodeConfig node, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(node.PlcAddress))
                return node.NextNodeId;

            ct.ThrowIfCancellationRequested();

            // Phase 2 — DeviceId 분기. 보드는 Bit 만 지원, Int16/Int32/Float 는 경고 후 skip.
            if (TryGetBoardForNode(node, out var board))
            {
                await ExecuteBoardOutputActionAsync(board!, node);
                return node.NextNodeId;
            }

            var addr = PlcAddress.Parse(node.PlcAddress, _vendor);

            switch (node.OutputDataType)
            {
                case PlcDataType.Bit:
                    await _plc.WriteBitAsync(addr, node.BitValue ?? false);
                    break;
                case PlcDataType.Int16:
                    await _plc.WriteWordAsync(addr, (short)(node.WordValue ?? 0));
                    break;
                case PlcDataType.Int32:
                    await _plc.WriteDWordAsync(addr, node.WordValue ?? 0);
                    break;
                case PlcDataType.Float:
                    await _plc.WriteDWordAsync(addr, BitConverter.SingleToInt32Bits(node.FloatValue ?? 0f));
                    break;
            }

            return node.NextNodeId;
        }

        // ─── Phase 2 — IO 보드 디바이스 dispatch helpers ───

        /// <summary>
        /// node.DeviceId 가 IO 보드를 가리키면 true + board 출력. 그렇지 않으면 false (기본 PLC 경로 사용).
        /// </summary>
        private bool TryGetBoardForNode(SequenceNodeConfig node, out IIoBoardConnection? board)
        {
            board = null;
            if (_ioRegistry is null) return false;
            if (string.IsNullOrEmpty(node.DeviceId)) return false;
            if (string.Equals(node.DeviceId, DefaultPlcDeviceId, StringComparison.Ordinal)) return false;

            var device = _ioRegistry.Get(node.DeviceId);
            if (device is IIoBoardConnection b)
            {
                board = b;
                return true;
            }
            // 등록된 다른 디바이스가 PLC 면 기본 PLC 경로로 폴백
            return false;
        }

        /// <summary>
        /// Phase B — Tool 결과 매핑 (PlcResultMapping) 의 DeviceId 가 IO 보드면 true + board 반환.
        /// node 와 달리 mapping 은 DeviceId 만 갖고 있으므로 TryGetBoardForNode 와 별도 helper.
        /// </summary>
        private bool TryGetBoardForMapping(string? deviceId, out IIoBoardConnection? board)
        {
            board = null;
            if (_ioRegistry is null) return false;
            if (string.IsNullOrWhiteSpace(deviceId)) return false;
            if (string.Equals(deviceId, DefaultPlcDeviceId, StringComparison.Ordinal)) return false;

            var device = _ioRegistry.Get(deviceId);
            if (device is IIoBoardConnection b)
            {
                board = b;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Phase B — Tool 결과 매핑 → IO 보드 출력 채널 write.
        /// 보드는 Bit 만 지원 — Int16/Int32/Float 는 경고 후 skip (보드 채널 비트맵 모델).
        /// ResultKey="Success" → tr.Success, 그 외 numeric 값 != 0 으로 변환.
        /// </summary>
        private async Task WriteBoardMappingAsync(IIoBoardConnection board, Models.PlcResultMapping mapping, ToolInspectionResult tr)
        {
            if (!int.TryParse(mapping.PlcAddress?.Trim(), out var channel) || channel < 0)
            {
                Debug.WriteLine($"[SequenceEngine] Board '{mapping.DeviceId}' channel parse failed: '{mapping.PlcAddress}' — skip.");
                return;
            }

            if (mapping.DataType != PlcDataType.Bit)
            {
                Debug.WriteLine(
                    $"[SequenceEngine] Board '{mapping.DeviceId}' mapping type {mapping.DataType} not supported — Bit only. skip.");
                return;
            }

            bool value;
            if (mapping.ResultKey == "Success")
            {
                value = tr.Success;
            }
            else if (TryGetNumericValue(tr, mapping.ResultKey, out var num))
            {
                value = num != 0;
            }
            else
            {
                Debug.WriteLine(
                    $"[SequenceEngine] Board '{mapping.DeviceId}' mapping key '{mapping.ResultKey}' not found in tool '{tr.ToolName}' — skip.");
                return;
            }

            await board.WriteBitAsync(channel, value);
        }

        /// <summary>
        /// IO 보드의 채널 polling. PLC 의 WaitForBitValueAsync 와 의미 동등하지만 native event 가 없어
        /// 단순 polling (50ms 주기). InputCheck.TimeoutMs 적용.
        /// Word 모드는 ReadPort 로 32-bit 읽어 비교.
        /// </summary>
        private async Task ExecuteBoardInputCheckAsync(IIoBoardConnection board, SequenceNodeConfig node, CancellationToken ct)
        {
            if (!int.TryParse(node.PlcAddress, out var channel))
            {
                // 파싱 실패를 조용히 return 하면 InputCheck 가 무조건 통과 = Wait Trigger 가
                // 스위치와 무관하게 최고속 무한 사이클을 돈다. SequenceError 로 표면화한다.
                throw new InvalidOperationException(
                    $"IO 보드 '{node.DeviceId}' InputCheck 채널이 숫자가 아닙니다: '{node.PlcAddress}' " +
                    $"(노드 '{node.Name}') — 보드 노드의 주소는 채널 번호(예: 0, 5)여야 합니다.");
            }

            var deadline = node.TimeoutMs > 0
                ? DateTime.UtcNow.AddMilliseconds(node.TimeoutMs)
                : DateTime.MaxValue;

            if (node.CheckMode is InputCheckMode.BitRisingEdge or InputCheckMode.BitFallingEdge)
            {
                await WaitForBoardEdgeAsync(board, channel, node, deadline, ct);
                return;
            }

            while (!ct.IsCancellationRequested && DateTime.UtcNow < deadline)
            {
                bool satisfied = node.CheckMode switch
                {
                    InputCheckMode.BitOn => await board.ReadBitAsync(channel),
                    InputCheckMode.BitOff => !await board.ReadBitAsync(channel),
                    InputCheckMode.WordEquals => (long)(await board.ReadPortAsync(channel / 32)) == (node.CompareValue ?? 0),
                    InputCheckMode.WordGreaterThan => (long)(await board.ReadPortAsync(channel / 32)) > (node.CompareValue ?? 0),
                    InputCheckMode.WordLessThan => (long)(await board.ReadPortAsync(channel / 32)) < (node.CompareValue ?? 0),
                    _ => false
                };
                if (satisfied) return;
                try { await Task.Delay(50, ct); } catch (TaskCanceledException) { return; }
            }
        }

        /// <summary>
        /// IO 보드 채널의 에지 대기 — 비활성 레벨을 한 번 관측한 뒤(armed)에만 전환을 인정한다.
        /// 노드 진입 시 신호가 이미 활성이면(스위치를 계속 누르고 있는 경우) 해제될 때까지
        /// 통과하지 않으므로, 1회 누름 = 1회 트리거가 보장된다 (2026-08-19 현장).
        /// DebounceMs > 0 이면 전환 감지 후 해당 시간 뒤 레벨 재확인 — 채터링 글리치 무시.
        /// </summary>
        private static async Task WaitForBoardEdgeAsync(
            IIoBoardConnection board, int channel, SequenceNodeConfig node, DateTime deadline, CancellationToken ct)
        {
            var target = node.CheckMode == InputCheckMode.BitRisingEdge;
            var armed = false;

            while (!ct.IsCancellationRequested && DateTime.UtcNow < deadline)
            {
                var current = await board.ReadBitAsync(channel);

                if (!armed)
                {
                    if (current != target) armed = true;
                }
                else if (current == target)
                {
                    if (node.DebounceMs > 0)
                    {
                        try { await Task.Delay(node.DebounceMs, ct); } catch (TaskCanceledException) { return; }
                        if (await board.ReadBitAsync(channel) != target)
                            continue;   // 글리치 — armed 유지, 다음 전환 대기
                    }
                    return;
                }

                try { await Task.Delay(50, ct); } catch (TaskCanceledException) { return; }
            }
        }

        /// <summary>
        /// IO 보드의 디지털 출력. 보드는 Bit 만 지원 — Int16/Int32/Float 는 경고 후 skip.
        /// (Phase 3 SDK 통합 시 보드별로 word port write 가 가능할 수 있음 — 추후 확장.)
        /// </summary>
        private async Task ExecuteBoardOutputActionAsync(IIoBoardConnection board, SequenceNodeConfig node)
        {
            if (!int.TryParse(node.PlcAddress, out var channel))
            {
                // InputCheck 와 동일 — 조용한 skip 은 출력이 안 나가는 이유를 숨긴다.
                throw new InvalidOperationException(
                    $"IO 보드 '{node.DeviceId}' OutputAction 채널이 숫자가 아닙니다: '{node.PlcAddress}' " +
                    $"(노드 '{node.Name}') — 보드 노드의 주소는 채널 번호(예: 0, 5)여야 합니다.");
            }

            switch (node.OutputDataType)
            {
                case PlcDataType.Bit:
                    await board.WriteBitAsync(channel, node.BitValue ?? false);
                    break;
                default:
                    Debug.WriteLine(
                        $"[Sequence] Board '{node.DeviceId}' output type {node.OutputDataType} not supported — skip.");
                    break;
            }
        }

        // --- Inspection: 카메라별 그랩 + 검사 + 결과 쓰기 ---
        private async Task<string?> ExecuteInspectionAsync(SequenceNodeConfig node, SequenceConfig config, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            var cameraId = node.CameraId ?? string.Empty;
            if (string.IsNullOrEmpty(cameraId))
            {
                Debug.WriteLine($"[SequenceEngine] Inspection node '{node.Name}' has no CameraId");
                _lastInspectionOk = false;
                return node.NextNodeId;
            }

            _resetFunc(cameraId);
            _cycleInspectionCount++;

            var grabOk = await _grabFunc(cameraId);
            if (!grabOk)
            {
                _lastInspectionOk = false;
                _cameraResults[cameraId] = false;
                _setResultFunc(cameraId, false);
                return node.NextNodeId;
            }

            var inspectOk = await _inspectFunc(cameraId);
            _lastInspectionOk = inspectOk;
            _cameraResults[cameraId] = inspectOk;
            // _setResultFunc 호출 금지 — _inspectFunc(ManualInspect 경로)가 이미
            // SetInspectionResult 를 호출해 InspectionCompleted 를 발생시켰다. 여기서 다시
            // 부르면 트리거 1회에 카운트·이미지 저장·업로드가 2배가 된다 (2026-08-18 현장).
            // grab 실패 경로(위)는 검사가 돌지 않아 이벤트가 없으므로 호출을 유지한다.

            // Write individual tool results to PLC
            await WriteToolResultsAsync(cameraId);

            return node.NextNodeId;
        }

        // --- Branch: Inspection 결과 기반 분기 ---
        // BranchOnAllCameras=true → 전체 카메라 결과, false → 직전 Inspection 결과
        private string? ExecuteBranch(SequenceNodeConfig node)
        {
            var result = node.BranchOnAllCameras ? AllInspectionsOk : _lastInspectionOk;
            return result ? node.TrueBranchNodeId : node.FalseBranchNodeId;
        }

        // --- Delay: 지연 ---
        private async Task<string?> ExecuteDelayAsync(SequenceNodeConfig node, CancellationToken ct)
        {
            if (node.DelayMs > 0)
                await Task.Delay(node.DelayMs, ct);
            return node.NextNodeId;
        }

        /// <summary>
        /// Repeat 통과(또는 시퀀스 정상 종료) 시점에 이번 사이클을 마감 — Inspection 이
        /// 1회 이상 돌았으면 CycleCompleted 를 발생시키고 다음 사이클을 위해 결과를 비운다.
        /// 구독자 예외는 시퀀스 운전을 멈추지 않도록 삼킨다.
        /// </summary>
        /// <param name="resetForNextCycle">Repeat 경계에서만 true — 다음 사이클 판정을 새로
        /// 누적하도록 결과를 비운다. 시퀀스 종료 시점(false)에는 비우지 않아 종료 후에도
        /// AllInspectionsOk 관측·감사 로그가 마지막 결과를 유지한다 (다음 RunAsync 가 초기화).</param>
        private void FireCycleCompletedIfInspected(bool resetForNextCycle)
        {
            if (_cycleInspectionCount == 0) return;

            var args = new SequenceCycleCompletedEventArgs(AllInspectionsOk, _cycleInspectionCount);
            _cycleInspectionCount = 0;
            if (resetForNextCycle)
                _cameraResults.Clear();

            try { CycleCompleted?.Invoke(this, args); }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SequenceEngine] CycleCompleted handler error: {ex.Message}");
            }
        }

        // --- Repeat: 반복 제어 ---
        private string? ExecuteRepeat(SequenceNodeConfig node)
        {
            // Repeat 노드 도달 = 사이클 경계 (무한/유한, 루프백/탈출 모두 동일)
            FireCycleCompletedIfInspected(resetForNextCycle: true);

            if (node.RepeatCount == -1)
            {
                // 무한 반복
                return node.RepeatTargetNodeId ?? node.NextNodeId;
            }

            if (!_repeatCounters.TryGetValue(node.Id, out var count))
                count = 0;

            count++;
            _repeatCounters[node.Id] = count;

            if (count < node.RepeatCount)
            {
                return node.RepeatTargetNodeId ?? node.NextNodeId;
            }

            // 반복 완료
            _repeatCounters[node.Id] = 0;
            return node.NextNodeId;
        }

        // --- 공용: 신호 조건 확인 (Bit/Word 모두 지원) ---
        // deviceId 가 IO 보드를 가리키면 채널 정수 + ReadBit/ReadPort 경로,
        // 그 외(빈/MainPLC/미등록 ID) 는 기본 PLC 어드레스 경로.
        private async Task<bool> CheckSignalAsync(string? deviceId, string address, InputCheckMode checkMode, int? compareValue)
        {
            // 순간 판정 함수라 에지를 표현할 수 없다 — Reset/Recipe/Step 신호에 에지 모드를
            // 고르면 조용히 영원 미충족이 되는 것을 막기 위해 레벨 동치로 해석한다.
            checkMode = checkMode switch
            {
                InputCheckMode.BitRisingEdge => InputCheckMode.BitOn,
                InputCheckMode.BitFallingEdge => InputCheckMode.BitOff,
                _ => checkMode
            };

            if (TryGetBoardForMapping(deviceId, out var board) && board != null)
            {
                if (!int.TryParse(address?.Trim(), out var ch) || ch < 0)
                {
                    Debug.WriteLine($"[SequenceEngine] Board '{deviceId}' signal channel parse failed: '{address}'");
                    return false;
                }
                return checkMode switch
                {
                    InputCheckMode.BitOn => await board.ReadBitAsync(ch),
                    InputCheckMode.BitOff => !await board.ReadBitAsync(ch),
                    InputCheckMode.WordEquals => (long)(await board.ReadPortAsync(ch / 32)) == (compareValue ?? 0),
                    InputCheckMode.WordGreaterThan => (long)(await board.ReadPortAsync(ch / 32)) > (compareValue ?? 0),
                    InputCheckMode.WordLessThan => (long)(await board.ReadPortAsync(ch / 32)) < (compareValue ?? 0),
                    _ => false
                };
            }

            var addr = PlcAddress.Parse(address, _vendor);

            switch (checkMode)
            {
                case InputCheckMode.BitOn:
                    return await _plc.ReadBitAsync(addr);

                case InputCheckMode.BitOff:
                    return !await _plc.ReadBitAsync(addr);

                case InputCheckMode.WordEquals:
                    return await _plc.ReadWordAsync(addr) == (compareValue ?? 0);

                case InputCheckMode.WordGreaterThan:
                    return await _plc.ReadWordAsync(addr) > (compareValue ?? 0);

                case InputCheckMode.WordLessThan:
                    return await _plc.ReadWordAsync(addr) < (compareValue ?? 0);

                default:
                    return false;
            }
        }

        /// <summary>
        /// Index (Word) 읽기 — Recipe/Step Change 의 인덱스 주소.
        /// deviceId 가 IO 보드면 ReadPortAsync(portNo), 그 외 PLC ReadWordAsync.
        /// </summary>
        private async Task<int> ReadIndexAsync(string? deviceId, string address)
        {
            if (TryGetBoardForMapping(deviceId, out var board) && board != null)
            {
                if (!int.TryParse(address?.Trim(), out var portNo) || portNo < 0)
                {
                    Debug.WriteLine($"[SequenceEngine] Board '{deviceId}' index port parse failed: '{address}'");
                    return 0;
                }
                var word = await board.ReadPortAsync(portNo);
                return (int)word;
            }

            var addr = PlcAddress.Parse(address, _vendor);
            return await _plc.ReadWordAsync(addr);
        }

        // --- RecipeChange: 신호 조건 확인 → 인덱스 읽기 → 비교 → 변경 ---
        // node.DeviceId 가 IO 보드면 board.ReadBit/ReadPort 경로, 아니면 PLC 어드레스 경로.
        private async Task<string?> ExecuteRecipeChangeAsync(SequenceNodeConfig node)
        {
            if (_recipeChangeByIndexFunc == null || string.IsNullOrEmpty(node.RecipeIndexAddress))
                return node.NextNodeId;

            // 신호 주소가 설정된 경우: 조건 미충족이면 스킵
            if (!string.IsNullOrEmpty(node.RecipeSignalAddress))
            {
                var conditionMet = await CheckSignalAsync(
                    node.DeviceId,
                    node.RecipeSignalAddress, node.RecipeSignalCheckMode, node.RecipeSignalCompareValue);
                if (!conditionMet)
                    return node.NextNodeId; // 조건 미충족 → 레시피 변경 불필요
            }

            // 인덱스 주소에서 Word 읽기 (PLC) 또는 ReadPort (Board) → 델리게이트 호출.
            int recipeIndex = await ReadIndexAsync(node.DeviceId, node.RecipeIndexAddress);
            await _recipeChangeByIndexFunc(recipeIndex);

            return node.NextNodeId;
        }

        // --- StepChange: 신호 조건 확인 → 스텝 인덱스 읽기 → 카메라 스텝 설정 ---
        private async Task<string?> ExecuteStepChangeAsync(SequenceNodeConfig node)
        {
            if (_stepChangeFunc == null)
                return node.NextNodeId;

            int stepIndex = 0; // 기본값: 스텝 1 (index 0)

            if (!string.IsNullOrEmpty(node.StepSignalAddress) && !string.IsNullOrEmpty(node.StepIndexAddress))
            {
                // 신호 조건 확인
                var conditionMet = await CheckSignalAsync(
                    node.DeviceId,
                    node.StepSignalAddress, node.StepSignalCheckMode, node.StepSignalCompareValue);

                if (conditionMet)
                {
                    // 조건 충족 → 스텝 인덱스 읽기 (PLC Word 또는 Board ReadPort)
                    stepIndex = await ReadIndexAsync(node.DeviceId, node.StepIndexAddress);
                }
            }

            _stepChangeFunc(stepIndex);
            return node.NextNodeId;
        }

        // --- PLC 비트 대기 (AutoProcessService에서 이전) ---

        private void OnPlcBitChanged(object? sender, PlcBitChangedEventArgs e)
        {
            var key = e.Address.RawAddress.ToUpperInvariant();

            // Reset 신호 감지 — Bit 모드일 때 조건 충족 시 시퀀스 즉시 중단
            if (_resetAddress != null && key == _resetAddress)
            {
                var expectedValue = _resetCheckMode == InputCheckMode.BitOn;
                if (e.NewValue == expectedValue)
                {
                    _resetCts?.Cancel();
                    return;
                }
            }

            lock (_waiterLock)
            {
                if (_bitWaiters.TryGetValue(key, out var tcs))
                {
                    tcs.TrySetResult(e.NewValue);
                    _bitWaiters.Remove(key);
                }
            }
        }

        /// <summary>
        /// PLC 비트의 에지 대기 — 비활성 레벨을 먼저 확인한 뒤 활성 전환을 기다린다.
        /// 신호가 이미 활성인 채 유지되면(입력 홀드) 해제될 때까지 통과하지 않는다.
        /// 타임아웃은 기존 레벨 모드와 동일하게 각 대기 단계에 적용되고, 만료 시 통과한다.
        /// </summary>
        private async Task WaitForPlcBitEdgeAsync(PlcAddress address, bool risingTarget, SequenceNodeConfig node, CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                if (!await WaitForBitValueAsync(address, !risingTarget, ct, node.TimeoutMs)) return;
                if (!await WaitForBitValueAsync(address, risingTarget, ct, node.TimeoutMs)) return;

                if (node.DebounceMs > 0)
                {
                    try { await Task.Delay(node.DebounceMs, ct); } catch (TaskCanceledException) { return; }
                    bool held;
                    try { held = await _plc.ReadBitAsync(address) == risingTarget; }
                    catch { held = false; }
                    if (!held) continue;    // 글리치 — 다시 비활성→활성 전환 대기
                }

                return;
            }
        }

        private async Task<bool> WaitForBitValueAsync(PlcAddress address, bool expectedValue, CancellationToken ct, int timeoutMs = -1)
        {
            var key = address.RawAddress.ToUpperInvariant();

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var currentValue = await _plc.ReadBitAsync(address);
                    if (currentValue == expectedValue)
                        return true;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[SequenceEngine] ReadBit error: {ex.Message}");
                }

                var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                lock (_waiterLock)
                {
                    _bitWaiters[key] = tcs;
                }

                using var timeoutCts = timeoutMs > 0
                    ? new CancellationTokenSource(timeoutMs)
                    : new CancellationTokenSource();
                using var linkedCts = timeoutMs > 0
                    ? CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token)
                    : CancellationTokenSource.CreateLinkedTokenSource(ct);

                try
                {
                    using var reg = linkedCts.Token.Register(() => tcs.TrySetCanceled(linkedCts.Token));
                    var newValue = await tcs.Task;
                    if (newValue == expectedValue)
                        return true;
                }
                catch (OperationCanceledException) when (timeoutMs > 0 && timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
                {
                    return false;
                }
                finally
                {
                    lock (_waiterLock)
                    {
                        if (_bitWaiters.TryGetValue(key, out var existing) && existing == tcs)
                            _bitWaiters.Remove(key);
                    }
                }
            }

            ct.ThrowIfCancellationRequested();
            return false;
        }

        private async Task WaitForWordConditionAsync(PlcAddress address, InputCheckMode mode, int compareValue, CancellationToken ct, int timeoutMs = -1)
        {
            var sw = Stopwatch.StartNew();

            while (!ct.IsCancellationRequested)
            {
                if (timeoutMs > 0 && sw.ElapsedMilliseconds >= timeoutMs)
                    return;

                try
                {
                    var currentValue = await _plc.ReadWordAsync(address);
                    bool conditionMet = mode switch
                    {
                        InputCheckMode.WordEquals => currentValue == compareValue,
                        InputCheckMode.WordGreaterThan => currentValue > compareValue,
                        InputCheckMode.WordLessThan => currentValue < compareValue,
                        _ => false
                    };

                    if (conditionMet)
                        return;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[SequenceEngine] ReadWord error: {ex.Message}");
                }

                await Task.Delay(50, ct);
            }
        }

        private void CancelAllWaiters()
        {
            lock (_waiterLock)
            {
                foreach (var tcs in _bitWaiters.Values)
                    tcs.TrySetCanceled();
                _bitWaiters.Clear();
            }
        }

        // --- 도구 결과 PLC 쓰기 (AutoProcessService에서 이전) ---

        private async Task WriteToolResultsAsync(string cameraId)
        {
            var toolResults = _getToolResultsFunc?.Invoke(cameraId);
            if (toolResults == null) return;

            foreach (var tr in toolResults)
            {
                if (tr.PlcMappings == null || tr.PlcMappings.Count == 0) continue;

                foreach (var mapping in tr.PlcMappings)
                {
                    if (string.IsNullOrEmpty(mapping.PlcAddress)) continue;

                    try
                    {
                        // Phase B — mapping.DeviceId 가 IO 보드를 가리키면 채널 정수 write 경로로 분기.
                        // 빈 / "MainPLC" 는 기본 PLC 경로 (후방호환).
                        if (TryGetBoardForMapping(mapping.DeviceId, out var board) && board != null)
                        {
                            await WriteBoardMappingAsync(board, mapping, tr);
                            continue;
                        }

                        var addr = PlcAddress.Parse(mapping.PlcAddress, _vendor);

                        switch (mapping.DataType)
                        {
                            case PlcDataType.Bit:
                                if (mapping.ResultKey == "Success")
                                    await _plc.WriteBitAsync(addr, tr.Success);
                                else if (TryGetNumericValue(tr, mapping.ResultKey, out var bitVal))
                                    await _plc.WriteBitAsync(addr, bitVal != 0);
                                break;
                            case PlcDataType.Int16:
                                if (TryGetNumericValue(tr, mapping.ResultKey, out var shortVal))
                                    await _plc.WriteWordAsync(addr, (short)shortVal);
                                break;
                            case PlcDataType.Int32:
                                if (TryGetNumericValue(tr, mapping.ResultKey, out var intVal))
                                    await _plc.WriteDWordAsync(addr, (int)intVal);
                                break;
                            case PlcDataType.Float:
                                if (TryGetNumericValue(tr, mapping.ResultKey, out var floatVal))
                                    await _plc.WriteDWordAsync(addr, BitConverter.SingleToInt32Bits((float)floatVal));
                                break;
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[SequenceEngine] Error writing tool result '{tr.ToolName}' key '{mapping.ResultKey}' to {mapping.DeviceId}/{mapping.PlcAddress}: {ex.Message}");
                    }
                }
            }
        }

        private static bool TryGetNumericValue(ToolInspectionResult tr, string resultKey, out double value)
        {
            value = 0;
            if (string.IsNullOrEmpty(resultKey) || tr.Data == null)
                return false;

            if (resultKey == "Success")
            {
                value = tr.Success ? 1 : 0;
                return true;
            }

            if (!tr.Data.TryGetValue(resultKey, out var obj) || obj == null)
                return false;

            try
            {
                value = Convert.ToDouble(obj);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
