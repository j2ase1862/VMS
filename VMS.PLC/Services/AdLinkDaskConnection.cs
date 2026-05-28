using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using VMS.PLC.Interfaces;
using VMS.PLC.Models;
using VMS.PLC.Services.Native;

namespace VMS.PLC.Services
{
    /// <summary>
    /// ADLink PCI-743x 시리즈 (PCI-7432/7433/7434) IIoBoardConnection 구현.
    /// DASK SDK 의 P/Invoke 함수 직접 호출.
    ///
    /// 채널 매핑:
    ///   • PCI-7432: 32 isolated DI + 32 isolated DO. DI/DO 모두 Port=0, Line=0~31.
    ///   • PCI-7433: 32 isolated DI only. ReadBit 만 의미.
    ///   • PCI-7434: 32 isolated DO only. WriteBit 만 의미.
    /// 채널 번호는 Line 그대로 사용 (0~31).
    ///
    /// 모니터링: native event 없음 → polling Timer 로 BitChanged 발생.
    /// PLC 의 IPlcConnection 처럼 thread-safe.
    /// </summary>
    public sealed class AdLinkDaskConnection : IIoBoardConnection
    {
        private readonly ushort _cardType;
        private readonly ushort _boardNum;
        private short _cardId = -1;
        private bool _connected;
        private bool _disposed;
        private readonly object _ioLock = new();
        private readonly ConcurrentDictionary<int, Timer> _monitors = new();
        private readonly ConcurrentDictionary<int, bool> _lastValues = new();

        public AdLinkDaskConnection(string deviceId, string model, int boardId,
            int inputChannelCount, int outputChannelCount)
        {
            DeviceId = deviceId;
            _cardType = DaskNativeMethods.ModelToCardType(model);
            _boardNum = (ushort)boardId;
            InputChannelCount = inputChannelCount;
            OutputChannelCount = outputChannelCount;
        }

        public string DeviceId { get; }
        public IoDeviceType DeviceType => IoDeviceType.AdLinkPci743x;
        public int InputChannelCount { get; }
        public int OutputChannelCount { get; }
        public bool IsConnected => _connected;

        public event EventHandler<IoBitChangedEventArgs>? BitChanged;

        public Task<bool> ConnectAsync()
        {
            try
            {
                var result = DaskNativeMethods.Register_Card(_cardType, _boardNum);
                if (result < 0)
                {
                    Debug.WriteLine($"[ADLink:{DeviceId}] Register_Card failed: {result}");
                    return Task.FromResult(false);
                }
                _cardId = result;
                _connected = true;
                Debug.WriteLine($"[ADLink:{DeviceId}] connected (cardId={_cardId}, type=0x{_cardType:X})");
                return Task.FromResult(true);
            }
            catch (DllNotFoundException)
            {
                Debug.WriteLine($"[ADLink:{DeviceId}] dask.dll not found — ADLink driver 미설치");
                return Task.FromResult(false);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ADLink:{DeviceId}] Connect error: {ex.Message}");
                return Task.FromResult(false);
            }
        }

        public Task DisconnectAsync()
        {
            if (_cardId >= 0)
            {
                try { DaskNativeMethods.Release_Card((ushort)_cardId); }
                catch (Exception ex) { Debug.WriteLine($"[ADLink:{DeviceId}] Release_Card: {ex.Message}"); }
            }
            _cardId = -1;
            _connected = false;
            return Task.CompletedTask;
        }

        public Task<bool> ReadBitAsync(int channel)
        {
            EnsureConnected();
            ushort state = 0;
            lock (_ioLock)
            {
                // PCI-743x: 32-bit isolated I/O 단일 포트 (Port=0)
                var ret = DaskNativeMethods.DI_ReadLine((ushort)_cardId, 0, (ushort)channel, ref state);
                if (ret < 0) Debug.WriteLine($"[ADLink:{DeviceId}] DI_ReadLine ch={channel} ret={ret}");
            }
            return Task.FromResult(state != 0);
        }

        public Task<uint> ReadPortAsync(int portNo)
        {
            EnsureConnected();
            uint value = 0;
            lock (_ioLock)
            {
                var ret = DaskNativeMethods.DI_ReadPort((ushort)_cardId, (ushort)portNo, ref value);
                if (ret < 0) Debug.WriteLine($"[ADLink:{DeviceId}] DI_ReadPort port={portNo} ret={ret}");
            }
            return Task.FromResult(value);
        }

        public Task WriteBitAsync(int channel, bool value)
        {
            EnsureConnected();
            lock (_ioLock)
            {
                var ret = DaskNativeMethods.DO_WriteLine((ushort)_cardId, 0, (ushort)channel, (ushort)(value ? 1 : 0));
                if (ret < 0) Debug.WriteLine($"[ADLink:{DeviceId}] DO_WriteLine ch={channel} ret={ret}");
            }
            return Task.CompletedTask;
        }

        public Task WritePortAsync(int portNo, uint value)
        {
            EnsureConnected();
            lock (_ioLock)
            {
                var ret = DaskNativeMethods.DO_WritePort((ushort)_cardId, (ushort)portNo, value);
                if (ret < 0) Debug.WriteLine($"[ADLink:{DeviceId}] DO_WritePort port={portNo} ret={ret}");
            }
            return Task.CompletedTask;
        }

        public Task StartMonitoringAsync(int channel, int pollingIntervalMs = 50)
        {
            StopMonitoringInternal(channel);
            _lastValues[channel] = false;
            var timer = new Timer(_ => PollChannel(channel), null,
                TimeSpan.FromMilliseconds(pollingIntervalMs),
                TimeSpan.FromMilliseconds(pollingIntervalMs));
            _monitors[channel] = timer;
            return Task.CompletedTask;
        }

        public Task StopMonitoringAsync(int channel)
        {
            StopMonitoringInternal(channel);
            return Task.CompletedTask;
        }

        public Task StopAllMonitoringAsync()
        {
            foreach (var ch in new System.Collections.Generic.List<int>(_monitors.Keys))
                StopMonitoringInternal(ch);
            return Task.CompletedTask;
        }

        private void StopMonitoringInternal(int channel)
        {
            if (_monitors.TryRemove(channel, out var timer))
                timer.Dispose();
            _lastValues.TryRemove(channel, out _);
        }

        private void PollChannel(int channel)
        {
            if (_disposed || !_connected) return;
            try
            {
                var current = ReadBitAsync(channel).GetAwaiter().GetResult();
                if (_lastValues.TryGetValue(channel, out var prev) && prev == current) return;
                _lastValues[channel] = current;
                BitChanged?.Invoke(this, new IoBitChangedEventArgs(channel, current));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ADLink:{DeviceId}] poll ch={channel} error: {ex.Message}");
            }
        }

        private void EnsureConnected()
        {
            if (!_connected) throw new InvalidOperationException(
                $"AdLinkDaskConnection '{DeviceId}' not connected.");
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _ = StopAllMonitoringAsync();
            _ = DisconnectAsync();
            GC.SuppressFinalize(this);
        }
    }
}
