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
        private readonly ISystemLogService? _logService;

        // Phase 2 — PLC + IO 보드 동시 사용 시 SequenceEngine 의 multi-device dispatch.
        // null 이면 기존 단일 PLC 모드 (후방호환).
        private readonly IIoDeviceRegistry? _ioRegistry;

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
            IIoDeviceRegistry? ioRegistry = null)
        {
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
            var config = _processSequence
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
