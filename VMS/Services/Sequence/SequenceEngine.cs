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

        public SequenceEngine(
            IPlcConnection plc,
            PlcVendor vendor,
            Func<string, Task<bool>> grabFunc,
            Func<string, Task<bool>> inspectFunc,
            Action<string, bool> setResultFunc,
            Action<string> resetFunc,
            Func<string, IReadOnlyList<ToolInspectionResult>?>? getToolResultsFunc = null,
            Func<int, Task>? recipeChangeByIndexFunc = null,
            Action<int>? stepChangeFunc = null)
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
                                    var met = await CheckSignalAsync(
                                        config.ResetSignalAddress, config.ResetSignalCheckMode, config.ResetSignalCompareValue);
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
                        break;
                    }
                }

                // 외부 취소가 아닌지 확인 후 전파
                ct.ThrowIfCancellationRequested();

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

        // --- InputCheck: PLC 비트/워드 조건 대기 ---
        private async Task<string?> ExecuteInputCheckAsync(SequenceNodeConfig node, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(node.PlcAddress))
                return node.NextNodeId;

            var addr = PlcAddress.Parse(node.PlcAddress, _vendor);

            switch (node.CheckMode)
            {
                case InputCheckMode.BitOn:
                    await WaitForBitValueAsync(addr, true, ct, node.TimeoutMs);
                    break;

                case InputCheckMode.BitOff:
                    await WaitForBitValueAsync(addr, false, ct, node.TimeoutMs);
                    break;

                case InputCheckMode.WordEquals:
                case InputCheckMode.WordGreaterThan:
                case InputCheckMode.WordLessThan:
                    await WaitForWordConditionAsync(addr, node.CheckMode, node.CompareValue ?? 0, ct, node.TimeoutMs);
                    break;
            }

            return node.NextNodeId;
        }

        // --- OutputAction: PLC 비트/워드 쓰기 ---
        private async Task<string?> ExecuteOutputActionAsync(SequenceNodeConfig node, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(node.PlcAddress))
                return node.NextNodeId;

            ct.ThrowIfCancellationRequested();
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
            _setResultFunc(cameraId, inspectOk);

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

        // --- Repeat: 반복 제어 ---
        private string? ExecuteRepeat(SequenceNodeConfig node)
        {
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
        private async Task<bool> CheckSignalAsync(string address, InputCheckMode checkMode, int? compareValue)
        {
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

        // --- RecipeChange: 신호 조건 확인 → PLC 레시피 인덱스 읽기 → 비교 → 변경 ---
        private async Task<string?> ExecuteRecipeChangeAsync(SequenceNodeConfig node)
        {
            if (_recipeChangeByIndexFunc == null || string.IsNullOrEmpty(node.RecipeIndexAddress))
                return node.NextNodeId;

            // 신호 주소가 설정된 경우: 조건 미충족이면 스킵
            if (!string.IsNullOrEmpty(node.RecipeSignalAddress))
            {
                var conditionMet = await CheckSignalAsync(
                    node.RecipeSignalAddress, node.RecipeSignalCheckMode, node.RecipeSignalCompareValue);
                if (!conditionMet)
                    return node.NextNodeId; // 조건 미충족 → 레시피 변경 불필요
            }

            // 인덱스 주소에서 Word 읽기 → 델리게이트 호출 (비교/변경)
            var indexAddr = PlcAddress.Parse(node.RecipeIndexAddress, _vendor);
            int recipeIndex = await _plc.ReadWordAsync(indexAddr);
            await _recipeChangeByIndexFunc(recipeIndex);

            return node.NextNodeId;
        }

        // --- StepChange: 신호 조건 확인 → PLC 스텝 인덱스 읽기 → 카메라 스텝 설정 ---
        private async Task<string?> ExecuteStepChangeAsync(SequenceNodeConfig node)
        {
            if (_stepChangeFunc == null)
                return node.NextNodeId;

            int stepIndex = 0; // 기본값: 스텝 1 (index 0)

            if (!string.IsNullOrEmpty(node.StepSignalAddress) && !string.IsNullOrEmpty(node.StepIndexAddress))
            {
                // 신호 조건 확인
                var conditionMet = await CheckSignalAsync(
                    node.StepSignalAddress, node.StepSignalCheckMode, node.StepSignalCompareValue);

                if (conditionMet)
                {
                    // 조건 충족 → 스텝 인덱스 읽기
                    var indexAddr = PlcAddress.Parse(node.StepIndexAddress, _vendor);
                    stepIndex = await _plc.ReadWordAsync(indexAddr);
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
                        Debug.WriteLine($"[SequenceEngine] Error writing tool result '{tr.ToolName}' key '{mapping.ResultKey}' to {mapping.PlcAddress}: {ex.Message}");
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
