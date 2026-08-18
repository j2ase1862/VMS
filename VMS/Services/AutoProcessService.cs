using System.Collections.Generic;
using System.Diagnostics;
using VMS.Interfaces;
using VMS.PLC.Interfaces;
using VMS.PLC.Models;
using VMS.PLC.Models.Sequence;
using VMS.Services.Sequence;

// ISystemLogService integration for runtime logging

namespace VMS.Services
{
    /// <summary>
    /// PLC trigger-based automatic inspection service.
    /// Runs a single unified process sequence via SequenceEngine.
    /// The sequence contains Inspection nodes targeting individual cameras.
    /// If no custom sequence is provided, DefaultSequenceBuilder generates one
    /// from PlcSignalConfiguration that reproduces the original behavior.
    /// </summary>
    public class AutoProcessService : IAutoProcessService
    {
        private readonly IPlcConnection _plc;
        private readonly PlcSignalConfiguration _signalConfig;
        private readonly PlcVendor _vendor;

        // Camera operation delegates (injected to avoid direct ViewModel reference)
        private readonly Func<string, Task<bool>> _grabFunc;
        private readonly Func<string, Task<bool>> _inspectFunc;
        private readonly Action<string, bool> _setResultFunc;
        private readonly Action<string> _resetFunc;
        private readonly Func<string, IReadOnlyList<ToolInspectionResult>?>? _getToolResultsFunc;
        private readonly Func<int, Task>? _recipeChangeByIndexFunc;
        private readonly Action<int>? _stepChangeFunc;

        // Single process sequence config (from Recipe or auto-generated)
        private readonly SequenceConfig? _processSequence;

        /// <summary>
        /// 운전 시작 때마다 시퀀스를 다시 읽는 provider. 과거에는 앱 시작 시 1회만 로드해
        /// VisionSetup 에서 시퀀스를 고쳐도 VMS 를 재시작해야 반영됐다.
        /// null 이면 생성자에서 받은 _processSequence 를 그대로 사용(기존 동작).
        /// </summary>
        private readonly Func<SequenceConfig?>? _sequenceProvider;
        private readonly ISystemLogService? _logService;

        // Phase 2 — PLC + IO 보드 동시 사용 시 SequenceEngine 의 multi-device dispatch.
        // null 이면 기존 단일 PLC 모드 (후방호환).
        private readonly IIoDeviceRegistry? _ioRegistry;

        // "1사이클 = 1개" WO 집계 (2026-08-18) — null 이면 비활성 (후방호환).
        // cycleAccumulationFunc: 운전 시작/중지 시 검사별 Web 업로드를 사이클 버퍼로 전환.
        // cycleUploadFunc: 사이클 완료(엔진 CycleCompleted) 시 판정 업로드 1건.
        private readonly Action<bool>? _cycleAccumulationFunc;
        private readonly Func<bool, Task>? _cycleUploadFunc;

        private readonly Dictionary<string, AutoProcessState> _cameraStates = new();
        private CancellationTokenSource? _cts;
        private Task? _processTask;
        private Task? _heartbeatTask;

        private const int ErrorRecoveryDelayMs = 5000;

        public bool IsRunning { get; private set; }

        public event EventHandler<AutoProcessStateChangedEventArgs>? StateChanged;

        public AutoProcessService(
            IPlcConnection plc,
            PlcSignalConfiguration signalConfig,
            PlcVendor vendor,
            Func<string, Task<bool>> grabFunc,
            Func<string, Task<bool>> inspectFunc,
            Action<string, bool> setResultFunc,
            Action<string> resetFunc,
            Func<string, IReadOnlyList<ToolInspectionResult>?>? getToolResultsFunc = null,
            Func<int, Task>? recipeChangeByIndexFunc = null,
            Action<int>? stepChangeFunc = null,
            SequenceConfig? processSequence = null,
            ISystemLogService? logService = null,
            IIoDeviceRegistry? ioRegistry = null,
            Func<SequenceConfig?>? sequenceProvider = null,
            Action<bool>? cycleAccumulationFunc = null,
            Func<bool, Task>? cycleUploadFunc = null)
        {
            _cycleAccumulationFunc = cycleAccumulationFunc;
            _cycleUploadFunc = cycleUploadFunc;
            _sequenceProvider = sequenceProvider;
            _plc = plc;
            _signalConfig = signalConfig;
            _vendor = vendor;
            _grabFunc = grabFunc;
            _inspectFunc = inspectFunc;
            _setResultFunc = setResultFunc;
            _resetFunc = resetFunc;
            _getToolResultsFunc = getToolResultsFunc;
            _recipeChangeByIndexFunc = recipeChangeByIndexFunc;
            _stepChangeFunc = stepChangeFunc;
            _processSequence = processSequence;
            _logService = logService;
            _ioRegistry = ioRegistry;
        }

        public async Task StartAsync(CancellationToken cancellationToken = default)
        {
            if (IsRunning) return;

            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            IsRunning = true;
            _logService?.Log("AutoProcess started", LogLevel.Success, "AutoProcess");

            // AUTO RUN 동안 검사별 Web 업로드를 사이클 버퍼로 전환 ("1사이클 = 1개")
            try { _cycleAccumulationFunc?.Invoke(true); }
            catch (Exception ex) { Debug.WriteLine($"[AutoProcess] cycle accumulation on failed: {ex.Message}"); }

            // Connect PLC if not already connected
            if (!_plc.IsConnected)
            {
                var config = new PlcConnectionConfig { Vendor = _vendor };
                await _plc.ConnectAsync(config);
            }

            // Start monitoring all trigger and ack addresses via BitChanged events
            foreach (var signalMap in _signalConfig.SignalMaps)
            {
                var triggerAddr = PlcAddress.Parse(signalMap.TriggerAddress, _vendor);
                await _plc.StartMonitoringAsync(triggerAddr, _signalConfig.TriggerPollingIntervalMs);

                if (!string.IsNullOrEmpty(signalMap.AckAddress))
                {
                    var ackAddr = PlcAddress.Parse(signalMap.AckAddress, _vendor);
                    await _plc.StartMonitoringAsync(ackAddr, _signalConfig.TriggerPollingIntervalMs);
                }
            }

            // Initialize camera states
            foreach (var signalMap in _signalConfig.SignalMaps)
            {
                _cameraStates[signalMap.CameraId] = AutoProcessState.WaitTrigger;
            }

            // Start single unified process task on ThreadPool to avoid UI thread blocking
            _processTask = Task.Run(() => RunProcessAsync(_cts.Token));

            // Start heartbeat task if any signal map has a heartbeat address
            var heartbeatMap = _signalConfig.SignalMaps.FirstOrDefault(m => !string.IsNullOrEmpty(m.HeartbeatAddress));
            if (heartbeatMap != null)
            {
                _heartbeatTask = RunHeartbeatAsync(heartbeatMap.HeartbeatAddress, _cts.Token);
            }
        }

        public async Task StopAsync()
        {
            if (!IsRunning) return;

            _cts?.Cancel();

            // Stop all PLC monitoring
            await _plc.StopAllMonitoringAsync();

            // Wait for process task to complete
            if (_processTask != null)
            {
                try { await _processTask; }
                catch (OperationCanceledException) { }
            }

            if (_heartbeatTask != null)
            {
                try { await _heartbeatTask; }
                catch (OperationCanceledException) { }
            }

            // Clear all output signals
            foreach (var signalMap in _signalConfig.SignalMaps)
            {
                await ClearOutputSignals(signalMap);
            }

            // Disconnect PLC
            if (_plc.IsConnected)
            {
                await _plc.DisconnectAsync();
            }

            _processTask = null;
            _cameraStates.Clear();
            _cts?.Dispose();
            _cts = null;
            IsRunning = false;

            // 사이클 버퍼 모드 해제 (수동 검사는 기존처럼 검사별 즉시 업로드)
            try { _cycleAccumulationFunc?.Invoke(false); }
            catch (Exception ex) { Debug.WriteLine($"[AutoProcess] cycle accumulation off failed: {ex.Message}"); }

            _logService?.Log("AutoProcess stopped", LogLevel.Info, "AutoProcess");
        }

        public AutoProcessState GetCameraState(string cameraId)
        {
            return _cameraStates.GetValueOrDefault(cameraId, AutoProcessState.Idle);
        }

        /// <summary>
        /// Run the unified process sequence using a single SequenceEngine.
        /// Uses custom SequenceConfig if available, otherwise generates default from PlcSignalConfiguration.
        /// </summary>
        private async Task RunProcessAsync(CancellationToken ct)
        {
            // 운전 시작 시점에 시퀀스를 다시 읽는다 — VisionSetup 에서 저장한 변경이 VMS 재시작
            // 없이 반영되도록. provider 가 없거나 파일이 없으면 기존 경로(생성자 주입 → 기본 생성).
            var reloaded = _sequenceProvider?.Invoke();
            if (reloaded != null && _processSequence != null && !ReferenceEquals(reloaded, _processSequence))
                _logService?.Log("시퀀스를 다시 불러왔습니다.", LogLevel.Info, "AutoProcess");

            var config = reloaded
                         ?? _processSequence
                         ?? DefaultSequenceBuilder.BuildFromSignalConfiguration(_signalConfig);

            var engine = new SequenceEngine(
                _plc, _vendor, _grabFunc, _inspectFunc,
                _setResultFunc, _resetFunc, _getToolResultsFunc,
                _recipeChangeByIndexFunc, _stepChangeFunc,
                ioRegistry: _ioRegistry);

            // 시퀀스가 비었으면 RunAsync 가 즉시 리턴해 아래 루프가 무한 스핀한다 —
            // 시작 전에 한 번 검증하고 원인을 남긴 뒤 중단.
            if (!config.Nodes.Any(n => n.NodeType == SequenceNodeType.Start))
            {
                _logService?.Log(
                    "시퀀스에 Start 노드가 없어 자동 운전을 시작할 수 없습니다. " +
                    "VisionSetup 의 시퀀스 편집기에서 시퀀스를 만들고 저장한 뒤 VMS 를 다시 시작하세요.",
                    LogLevel.Error, "AutoProcess");
                SetAllCameraStates(AutoProcessState.Error);
                return;
            }

            engine.NodeExecuting += (s, e) => MapNodeToState(e);

            // 사이클 완료 → 판정 업로드 1건 (fire-and-forget — 업로드 지연이 다음 사이클의
            // 트리거 대기를 막으면 안 된다. 실패는 로그만 남기고 운전은 계속.)
            if (_cycleUploadFunc != null)
            {
                engine.CycleCompleted += (s, e) =>
                {
                    _ = Task.Run(async () =>
                    {
                        try { await _cycleUploadFunc(e.AllInspectionsOk); }
                        catch (Exception ex)
                        {
                            _logService?.Log($"사이클 결과 업로드 실패: {ex.Message}",
                                LogLevel.Warning, "AutoProcess");
                        }
                    });
                };
            }

            // 노드 실행 중 예외는 엔진이 내부에서 삼키고 RunAsync 는 정상 리턴한다.
            // 따라서 아래 catch 로는 잡히지 않으므로, 플래그로 받아 재시도 전에 지연을 준다.
            var sequenceFaulted = false;
            engine.SequenceError += (s, e) =>
            {
                sequenceFaulted = true;
                Debug.WriteLine($"[AutoProcess] Sequence error at '{e.NodeName}': {e.Error.Message}");
                _logService?.Log(
                    $"시퀀스 '{e.NodeName}' 노드 오류 — {e.Error.Message}",
                    LogLevel.Error, "AutoProcess");
                SetAllCameraStates(AutoProcessState.Error);
            };

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    sequenceFaulted = false;
                    SetAllCameraStates(AutoProcessState.WaitTrigger);
                    await engine.RunAsync(config, ct);

                    // 노드 오류로 중단된 경우 — 즉시 재시도하면 초당 수천 회 실패를 반복(busy loop)하므로
                    // 오류 복구 지연을 두고 다시 시도한다.
                    if (sequenceFaulted)
                    {
                        _logService?.Log(
                            $"시퀀스가 오류로 중단됨 — {ErrorRecoveryDelayMs / 1000}초 후 재시도합니다.",
                            LogLevel.Warning, "AutoProcess");
                        await Task.Delay(ErrorRecoveryDelayMs, ct);
                    }

                    // Reset 신호에 의한 재시작
                    if (engine.WasReset)
                    {
                        _logService?.Log("Reset signal detected — clearing outputs and restarting sequence", LogLevel.Warning, "AutoProcess");
                        foreach (var signalMap in _signalConfig.SignalMaps)
                            await ClearOutputSignals(signalMap);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[AutoProcess] Process error: {ex.Message}");
                    _logService?.Log($"Process error: {ex.Message}", LogLevel.Error, "AutoProcess");
                    SetAllCameraStates(AutoProcessState.Error);
                    await Task.Delay(ErrorRecoveryDelayMs, ct);

                    foreach (var signalMap in _signalConfig.SignalMaps)
                        await ClearOutputSignals(signalMap);
                }
            }

            SetAllCameraStates(AutoProcessState.Idle);
        }

        /// <summary>
        /// Map sequence node execution to per-camera AutoProcessState for UI display.
        /// </summary>
        private void MapNodeToState(SequenceNodeEventArgs e)
        {
            switch (e.NodeType)
            {
                case SequenceNodeType.InputCheck:
                    SetAllCameraStates(AutoProcessState.WaitTrigger);
                    break;

                case SequenceNodeType.Inspection:
                    // Inspection 노드는 특정 카메라를 대상으로 함
                    if (!string.IsNullOrEmpty(e.CameraId))
                        SetState(e.CameraId, AutoProcessState.Inspecting);
                    break;

                case SequenceNodeType.OutputAction:
                case SequenceNodeType.Branch:
                    SetAllCameraStates(AutoProcessState.WritingResult);
                    break;

                case SequenceNodeType.RecipeChange:
                case SequenceNodeType.StepChange:
                    SetAllCameraStates(AutoProcessState.WaitTrigger);
                    break;
            }
        }

        /// <summary>
        /// Clear all VMS->PLC output signals.
        /// </summary>
        private async Task ClearOutputSignals(PlcSignalMap signalMap)
        {
            try
            {
                if (!string.IsNullOrEmpty(signalMap.BusyAddress))
                    await _plc.WriteBitAsync(PlcAddress.Parse(signalMap.BusyAddress, _vendor), false);
                if (!string.IsNullOrEmpty(signalMap.CompleteAddress))
                    await _plc.WriteBitAsync(PlcAddress.Parse(signalMap.CompleteAddress, _vendor), false);
                if (!string.IsNullOrEmpty(signalMap.ResultOkAddress))
                    await _plc.WriteBitAsync(PlcAddress.Parse(signalMap.ResultOkAddress, _vendor), false);
                if (!string.IsNullOrEmpty(signalMap.ResultNgAddress))
                    await _plc.WriteBitAsync(PlcAddress.Parse(signalMap.ResultNgAddress, _vendor), false);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AutoProcess] Error clearing signals: {ex.Message}");
            }
        }

        /// <summary>
        /// Heartbeat task: periodically increment a PLC word to indicate VMS is alive.
        /// </summary>
        private async Task RunHeartbeatAsync(string heartbeatAddress, CancellationToken ct)
        {
            var addr = PlcAddress.Parse(heartbeatAddress, _vendor);
            short counter = 0;

            try
            {
                using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(_signalConfig.HeartbeatIntervalMs));
                while (await timer.WaitForNextTickAsync(ct))
                {
                    try
                    {
                        counter = (short)((counter + 1) % short.MaxValue);
                        await _plc.WriteWordAsync(addr, counter);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[AutoProcess] Heartbeat error: {ex.Message}");
                    }
                }
            }
            catch (OperationCanceledException) { }
        }

        private void SetState(string cameraId, AutoProcessState newState)
        {
            if (_cameraStates.TryGetValue(cameraId, out var oldState) && oldState == newState)
                return;

            _cameraStates[cameraId] = newState;
            StateChanged?.Invoke(this, new AutoProcessStateChangedEventArgs(cameraId, oldState, newState));
        }

        private void SetAllCameraStates(AutoProcessState newState)
        {
            foreach (var cameraId in _cameraStates.Keys.ToList())
            {
                SetState(cameraId, newState);
            }
        }
    }
}
