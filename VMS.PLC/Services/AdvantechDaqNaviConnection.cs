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
    /// Advantech PCI-17xx 시리즈 IIoBoardConnection 구현. DAQNavi (Automation.BDaq4.dll)
    /// 를 reflection 으로 동적 로드 — CSProj 의존성 없음.
    ///
    /// 채널 매핑:
    ///   • Port 단위 8-bit (DAQNavi 표준). channel N → portNo = N/8, bitNo = N%8
    ///   • ReadPortAsync(portNo) 는 32-bit (uint) — DAQNavi 의 8-bit port 4개를 묶어 반환
    ///
    /// 사용 가능한 보드 예 (PCI-17xx 시리즈):
    ///   PCI-1710/1711/1714/1716 (multifunction)
    ///   PCI-1730/1733/1734/1735/1736 (digital I/O)
    ///   PCI-1750/1751/1752/1753/1754/1756/1758 (isolated DIO)
    ///
    /// DeviceDescription: "BID#{BoardId}" 형식 (Advantech 표준).
    /// </summary>
    public sealed class AdvantechDaqNaviConnection : IIoBoardConnection
    {
        private readonly string _deviceDescription;
        private readonly int _boardId;
        private object? _instantDi;
        private object? _instantDo;
        private bool _connected;
        private bool _disposed;
        private readonly object _ioLock = new();
        private readonly ConcurrentDictionary<int, Timer> _monitors = new();
        private readonly ConcurrentDictionary<int, bool> _lastValues = new();

        public AdvantechDaqNaviConnection(string deviceId, int boardId,
            int inputChannelCount, int outputChannelCount)
        {
            DeviceId = deviceId;
            _boardId = boardId;
            _deviceDescription = $"BID#{boardId}";
            InputChannelCount = inputChannelCount;
            OutputChannelCount = outputChannelCount;
        }

        public string DeviceId { get; }
        public IoDeviceType DeviceType => IoDeviceType.AdvantechPci17xx;
        public int InputChannelCount { get; }
        public int OutputChannelCount { get; }
        public bool IsConnected => _connected;

        public event EventHandler<IoBitChangedEventArgs>? BitChanged;

        public Task<bool> ConnectAsync()
        {
            if (!DaqNaviReflection.EnsureLoaded())
            {
                Debug.WriteLine($"[Advantech:{DeviceId}] Automation.BDaq4.dll 미설치 또는 로드 실패");
                return Task.FromResult(false);
            }

            try
            {
                _instantDi = DaqNaviReflection.CreateInstantDi();
                _instantDo = DaqNaviReflection.CreateInstantDo();
                if (_instantDi is null || _instantDo is null)
                {
                    Debug.WriteLine($"[Advantech:{DeviceId}] InstantDi/Do ctrl 생성 실패");
                    return Task.FromResult(false);
                }

                var deviceInfo = DaqNaviReflection.CreateDeviceInformation(_deviceDescription, _boardId);
                if (deviceInfo is not null)
                {
                    DaqNaviReflection.SetSelectedDevice(_instantDi, deviceInfo);
                    DaqNaviReflection.SetSelectedDevice(_instantDo, deviceInfo);
                }

                _connected = true;
                Debug.WriteLine($"[Advantech:{DeviceId}] connected (device={_deviceDescription})");
                return Task.FromResult(true);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Advantech:{DeviceId}] Connect error: {ex.Message}");
                return Task.FromResult(false);
            }
        }

        public Task DisconnectAsync()
        {
            // DAQNavi 의 InstantDi/DoCtrl 는 IDisposable — reflection 으로 호출
            try
            {
                (_instantDi as IDisposable)?.Dispose();
                (_instantDo as IDisposable)?.Dispose();
            }
            catch (Exception ex) { Debug.WriteLine($"[Advantech:{DeviceId}] Dispose: {ex.Message}"); }
            _instantDi = null;
            _instantDo = null;
            _connected = false;
            return Task.CompletedTask;
        }

        public Task<bool> ReadBitAsync(int channel)
        {
            EnsureConnected();
            int port = channel / 8;
            int bit = channel % 8;
            bool result;
            lock (_ioLock)
            {
                result = DaqNaviReflection.ReadBit(_instantDi!, port, bit);
            }
            return Task.FromResult(result);
        }

        public Task<uint> ReadPortAsync(int portNo)
        {
            EnsureConnected();
            // portNo = 32-bit 묶음. DAQNavi 의 8-bit port 4개 → 32-bit.
            byte[] data;
            lock (_ioLock)
            {
                data = DaqNaviReflection.ReadPorts(_instantDi!, portNo * 4, 4);
            }
            uint v = 0;
            for (int i = 0; i < 4 && i < data.Length; i++)
                v |= (uint)(data[i] << (i * 8));
            return Task.FromResult(v);
        }

        public Task WriteBitAsync(int channel, bool value)
        {
            EnsureConnected();
            int port = channel / 8;
            int bit = channel % 8;
            lock (_ioLock)
            {
                DaqNaviReflection.WriteBit(_instantDo!, port, bit, value);
            }
            return Task.CompletedTask;
        }

        public Task WritePortAsync(int portNo, uint value)
        {
            EnsureConnected();
            var data = new byte[4];
            for (int i = 0; i < 4; i++)
                data[i] = (byte)((value >> (i * 8)) & 0xFF);
            lock (_ioLock)
            {
                DaqNaviReflection.WritePorts(_instantDo!, portNo * 4, 4, data);
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
                Debug.WriteLine($"[Advantech:{DeviceId}] poll ch={channel} error: {ex.Message}");
            }
        }

        private void EnsureConnected()
        {
            if (!_connected) throw new InvalidOperationException(
                $"AdvantechDaqNaviConnection '{DeviceId}' not connected.");
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
