using System.Diagnostics;
using VMS.Interfaces;
using VMS.PLC.Interfaces;
using VMS.PLC.Models;

namespace VMS.Services
{
    /// <summary>
    /// PLC trigger-based automatic inspection state machine.
    /// Each camera channel runs an independent state machine loop:
    /// WaitTrigger -> Grabbing -> Inspecting -> WritingResult -> WaitAck -> WaitTrigger
    ///
    /// Uses IPlcConnection.StartMonitoringAsync/BitChanged events for reactive triggers
    /// instead of direct polling, improving response time and reducing CPU load.
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

        private readonly Dictionary<string, AutoProcessState> _cameraStates = new();
        private readonly List<Task> _channelTasks = new();
        private CancellationTokenSource? _cts;
        private Task? _heartbeatTask;

        // Event-based trigger: per-address TaskCompletionSource for BitChanged events
        private readonly Dictionary<string, TaskCompletionSource<bool>> _bitWaiters = new();
        private readonly object _waiterLock = new();

        private const int ErrorRecoveryDelayMs = 5000;
        private const int AckTimeoutMs = 30000;

        public bool IsRunning { get; private set; }

        public event EventHandler<AutoProcessStateChangedEventArgs>? StateChanged;

        public AutoProcessService(
            IPlcConnection plc,
            PlcSignalConfiguration signalConfig,
            PlcVendor vendor,
            Func<string, Task<bool>> grabFunc,
            Func<string, Task<bool>> inspectFunc,
            Action<string, bool> setResultFunc,
            Action<string> resetFunc)
        {
            _plc = plc;
            _signalConfig = signalConfig;
            _vendor = vendor;
            _grabFunc = grabFunc;
            _inspectFunc = inspectFunc;
            _setResultFunc = setResultFunc;
            _resetFunc = resetFunc;

            _plc.BitChanged += OnPlcBitChanged;
        }

        public async Task StartAsync(CancellationToken cancellationToken = default)
        {
            if (IsRunning) return;

            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            IsRunning = true;

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

            // Start per-camera channel tasks
            foreach (var signalMap in _signalConfig.SignalMaps)
            {
                _cameraStates[signalMap.CameraId] = AutoProcessState.WaitTrigger;
                var task = RunChannelAsync(signalMap, _cts.Token);
                _channelTasks.Add(task);
            }

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

            // Cancel all pending bit waiters
            CancelAllWaiters();

            // Stop all PLC monitoring
            await _plc.StopAllMonitoringAsync();

            // Wait for all channel tasks to complete
            try
            {
                await Task.WhenAll(_channelTasks);
            }
            catch (OperationCanceledException) { }

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

            _channelTasks.Clear();
            _cameraStates.Clear();
            _cts?.Dispose();
            _cts = null;
            IsRunning = false;
        }

        public AutoProcessState GetCameraState(string cameraId)
        {
            return _cameraStates.GetValueOrDefault(cameraId, AutoProcessState.Idle);
        }

        // --- BitChanged event handler ---

        private void OnPlcBitChanged(object? sender, PlcBitChangedEventArgs e)
        {
            var key = e.Address.RawAddress.ToUpperInvariant();

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
        /// Wait until a specific bit address changes to the expected value.
        /// Uses BitChanged events instead of polling.
        /// </summary>
        private async Task<bool> WaitForBitValueAsync(PlcAddress address, bool expectedValue, CancellationToken ct, int timeoutMs = -1)
        {
            var key = address.RawAddress.ToUpperInvariant();

            while (!ct.IsCancellationRequested)
            {
                // Check current value first
                try
                {
                    var currentValue = await _plc.ReadBitAsync(address);
                    if (currentValue == expectedValue)
                        return true;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[AutoProcess] ReadBit error during wait: {ex.Message}");
                }

                // Set up a waiter for the next BitChanged event
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
                    // Value changed but not to the expected value, loop and wait again
                }
                catch (OperationCanceledException) when (timeoutMs > 0 && timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
                {
                    // Timeout reached
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

        private void CancelAllWaiters()
        {
            lock (_waiterLock)
            {
                foreach (var tcs in _bitWaiters.Values)
                {
                    tcs.TrySetCanceled();
                }
                _bitWaiters.Clear();
            }
        }

        /// <summary>
        /// Main state machine loop for a single camera channel.
        /// </summary>
        private async Task RunChannelAsync(PlcSignalMap signalMap, CancellationToken ct)
        {
            var cameraId = signalMap.CameraId;
            SetState(cameraId, AutoProcessState.WaitTrigger);

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    switch (_cameraStates[cameraId])
                    {
                        case AutoProcessState.WaitTrigger:
                            await WaitForTriggerAsync(signalMap, ct);
                            break;

                        case AutoProcessState.Grabbing:
                            await ExecuteGrabAsync(signalMap, ct);
                            break;

                        case AutoProcessState.Inspecting:
                            await ExecuteInspectAsync(signalMap, ct);
                            break;

                        case AutoProcessState.WritingResult:
                            // WritingResult is handled inline in ExecuteInspectAsync
                            break;

                        case AutoProcessState.WaitAck:
                            await WaitForAckAsync(signalMap, ct);
                            break;

                        case AutoProcessState.Error:
                            await HandleErrorAsync(signalMap, ct);
                            break;
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[AutoProcess] Channel {cameraId} error: {ex.Message}");
                    SetState(cameraId, AutoProcessState.Error);
                }
            }

            SetState(cameraId, AutoProcessState.Idle);
        }

        /// <summary>
        /// Wait for PLC trigger bit using event-based monitoring.
        /// </summary>
        private async Task WaitForTriggerAsync(PlcSignalMap signalMap, CancellationToken ct)
        {
            var triggerAddr = PlcAddress.Parse(signalMap.TriggerAddress, _vendor);

            var triggered = await WaitForBitValueAsync(triggerAddr, expectedValue: true, ct);
            if (!triggered) return;

            // Set Busy ON
            if (!string.IsNullOrEmpty(signalMap.BusyAddress))
            {
                var busyAddr = PlcAddress.Parse(signalMap.BusyAddress, _vendor);
                await _plc.WriteBitAsync(busyAddr, true);
            }

            _resetFunc(signalMap.CameraId);
            SetState(signalMap.CameraId, AutoProcessState.Grabbing);
        }

        /// <summary>
        /// Execute camera grab via injected delegate.
        /// </summary>
        private async Task ExecuteGrabAsync(PlcSignalMap signalMap, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            var grabOk = await _grabFunc(signalMap.CameraId);
            if (!grabOk)
            {
                Debug.WriteLine($"[AutoProcess] Grab failed for {signalMap.CameraId}");
                await WriteErrorSignal(signalMap, true);
                SetState(signalMap.CameraId, AutoProcessState.Error);
                return;
            }

            SetState(signalMap.CameraId, AutoProcessState.Inspecting);
        }

        /// <summary>
        /// Execute inspection via injected delegate and write result to PLC.
        /// </summary>
        private async Task ExecuteInspectAsync(PlcSignalMap signalMap, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            var inspectOk = await _inspectFunc(signalMap.CameraId);

            // Update ViewModel result
            _setResultFunc(signalMap.CameraId, inspectOk);

            SetState(signalMap.CameraId, AutoProcessState.WritingResult);

            // Write result to PLC
            if (!string.IsNullOrEmpty(signalMap.ResultOkAddress))
            {
                var resultAddr = PlcAddress.Parse(signalMap.ResultOkAddress, _vendor);
                await _plc.WriteBitAsync(resultAddr, inspectOk);
            }

            if (!string.IsNullOrEmpty(signalMap.ResultNgAddress))
            {
                var ngAddr = PlcAddress.Parse(signalMap.ResultNgAddress, _vendor);
                await _plc.WriteBitAsync(ngAddr, !inspectOk);
            }

            // Set Complete ON
            if (!string.IsNullOrEmpty(signalMap.CompleteAddress))
            {
                var completeAddr = PlcAddress.Parse(signalMap.CompleteAddress, _vendor);
                await _plc.WriteBitAsync(completeAddr, true);
            }

            SetState(signalMap.CameraId, AutoProcessState.WaitAck);
        }

        /// <summary>
        /// Wait for PLC to acknowledge result using event-based monitoring.
        /// Ack condition: Ack ON (if configured) AND Trigger OFF.
        /// </summary>
        private async Task WaitForAckAsync(PlcSignalMap signalMap, CancellationToken ct)
        {
            var triggerAddr = PlcAddress.Parse(signalMap.TriggerAddress, _vendor);
            var ackAddr = !string.IsNullOrEmpty(signalMap.AckAddress)
                ? PlcAddress.Parse(signalMap.AckAddress, _vendor)
                : null;

            var sw = Stopwatch.StartNew();

            while (!ct.IsCancellationRequested)
            {
                // Check timeout
                var remainingMs = AckTimeoutMs - (int)sw.ElapsedMilliseconds;
                if (remainingMs <= 0)
                {
                    Debug.WriteLine($"[AutoProcess] Ack timeout for {signalMap.CameraId}");
                    await WriteErrorSignal(signalMap, true);
                    SetState(signalMap.CameraId, AutoProcessState.Error);
                    return;
                }

                // Check ack condition: Ack ON (if configured) AND Trigger OFF
                bool ackOk = true;
                if (ackAddr != null)
                {
                    try { ackOk = await _plc.ReadBitAsync(ackAddr); }
                    catch { ackOk = false; }
                }

                bool triggerOff;
                try { triggerOff = !(await _plc.ReadBitAsync(triggerAddr)); }
                catch { triggerOff = false; }

                if (ackOk && triggerOff)
                {
                    await ClearOutputSignals(signalMap);
                    SetState(signalMap.CameraId, AutoProcessState.WaitTrigger);
                    return;
                }

                // Wait for any relevant bit change before re-checking
                var waitAddr = !ackOk && ackAddr != null ? ackAddr : triggerAddr;
                var expectedValue = !ackOk && ackAddr != null ? true : false;

                await WaitForBitValueAsync(waitAddr, expectedValue, ct, timeoutMs: Math.Min(remainingMs, 1000));
            }
        }

        /// <summary>
        /// Wait in error state then recover to WaitTrigger.
        /// </summary>
        private async Task HandleErrorAsync(PlcSignalMap signalMap, CancellationToken ct)
        {
            await Task.Delay(ErrorRecoveryDelayMs, ct);
            await ClearOutputSignals(signalMap);
            await WriteErrorSignal(signalMap, false);
            SetState(signalMap.CameraId, AutoProcessState.WaitTrigger);
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
        /// Write or clear the error signal.
        /// </summary>
        private async Task WriteErrorSignal(PlcSignalMap signalMap, bool value)
        {
            try
            {
                if (!string.IsNullOrEmpty(signalMap.ErrorAddress))
                    await _plc.WriteBitAsync(PlcAddress.Parse(signalMap.ErrorAddress, _vendor), value);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AutoProcess] Error writing error signal: {ex.Message}");
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
    }
}
