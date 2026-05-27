using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using VMS.PLC.Interfaces;
using VMS.PLC.Models;

namespace VMS.PLC.Services
{
    /// <summary>
    /// Phase 1 — IO 보드 Mock 베이스. 실제 SDK 없이 in-memory 채널 상태로 동작.
    /// 운영 환경에서 보드 미연결/SDK 미설치 시 안전 fallback.
    ///
    /// 동작:
    ///   • 모든 채널 false 로 시작
    ///   • WriteBit/WritePort 시 in-memory 저장 + BitChanged event
    ///   • ReadBit/ReadPort 는 저장된 값 그대로 반환 (외부 입력 시뮬레이션 없음)
    ///   • StartMonitoring 은 Timer 로 폴링 (다른 polling 서비스가 WriteBit 으로 값 변경 시 감지)
    /// </summary>
    public abstract class IoBoardMockBase : IIoBoardConnection
    {
        protected readonly object _stateLock = new();
        protected readonly bool[] _inputBits;
        protected readonly bool[] _outputBits;
        private readonly ConcurrentDictionary<int, Timer> _monitors = new();
        private readonly ConcurrentDictionary<int, bool> _lastValues = new();
        private bool _connected;
        private bool _disposed;

        protected IoBoardMockBase(string deviceId, IoDeviceType deviceType,
            int inputChannelCount, int outputChannelCount)
        {
            DeviceId = deviceId;
            DeviceType = deviceType;
            InputChannelCount = inputChannelCount;
            OutputChannelCount = outputChannelCount;
            _inputBits = new bool[inputChannelCount];
            _outputBits = new bool[outputChannelCount];
        }

        public string DeviceId { get; }
        public IoDeviceType DeviceType { get; }
        public int InputChannelCount { get; }
        public int OutputChannelCount { get; }
        public bool IsConnected => _connected;

        public event EventHandler<IoBitChangedEventArgs>? BitChanged;

        public virtual Task<bool> ConnectAsync()
        {
            _connected = true;
            Debug.WriteLine($"[IoMock:{DeviceId}] connected ({DeviceType})");
            return Task.FromResult(true);
        }

        public virtual Task DisconnectAsync()
        {
            _connected = false;
            Debug.WriteLine($"[IoMock:{DeviceId}] disconnected");
            return Task.CompletedTask;
        }

        public Task<bool> ReadBitAsync(int channel)
        {
            EnsureChannel(channel, _inputBits.Length, isInput: true);
            lock (_stateLock) return Task.FromResult(_inputBits[channel]);
        }

        public Task<uint> ReadPortAsync(int portNo)
        {
            // Port = 32 channel 묶음. portNo=0 → channel 0~31.
            uint v = 0;
            lock (_stateLock)
            {
                int baseCh = portNo * 32;
                for (int i = 0; i < 32 && baseCh + i < _inputBits.Length; i++)
                {
                    if (_inputBits[baseCh + i]) v |= (uint)(1 << i);
                }
            }
            return Task.FromResult(v);
        }

        public Task WriteBitAsync(int channel, bool value)
        {
            EnsureChannel(channel, _outputBits.Length, isInput: false);
            bool changed;
            lock (_stateLock)
            {
                changed = _outputBits[channel] != value;
                _outputBits[channel] = value;
            }
            if (changed)
                BitChanged?.Invoke(this, new IoBitChangedEventArgs(channel, value));
            return Task.CompletedTask;
        }

        public Task WritePortAsync(int portNo, uint value)
        {
            lock (_stateLock)
            {
                int baseCh = portNo * 32;
                for (int i = 0; i < 32 && baseCh + i < _outputBits.Length; i++)
                {
                    bool bit = ((value >> i) & 1u) == 1u;
                    _outputBits[baseCh + i] = bit;
                }
            }
            return Task.CompletedTask;
        }

        public Task StartMonitoringAsync(int channel, int pollingIntervalMs = 50)
        {
            StopMonitoringAsyncInternal(channel);
            _lastValues[channel] = false;
            var timer = new Timer(_ => PollChannel(channel), null,
                TimeSpan.FromMilliseconds(pollingIntervalMs),
                TimeSpan.FromMilliseconds(pollingIntervalMs));
            _monitors[channel] = timer;
            return Task.CompletedTask;
        }

        public Task StopMonitoringAsync(int channel)
        {
            StopMonitoringAsyncInternal(channel);
            return Task.CompletedTask;
        }

        public Task StopAllMonitoringAsync()
        {
            foreach (var ch in new List<int>(_monitors.Keys))
                StopMonitoringAsyncInternal(ch);
            return Task.CompletedTask;
        }

        private void StopMonitoringAsyncInternal(int channel)
        {
            if (_monitors.TryRemove(channel, out var timer))
                timer.Dispose();
            _lastValues.TryRemove(channel, out _);
        }

        private void PollChannel(int channel)
        {
            if (_disposed) return;
            bool current;
            lock (_stateLock)
            {
                if (channel < 0 || channel >= _inputBits.Length) return;
                current = _inputBits[channel];
            }
            if (_lastValues.TryGetValue(channel, out var prev) && prev == current) return;
            _lastValues[channel] = current;
            BitChanged?.Invoke(this, new IoBitChangedEventArgs(channel, current));
        }

        private static void EnsureChannel(int channel, int count, bool isInput)
        {
            if (channel < 0 || channel >= count)
                throw new ArgumentOutOfRangeException(nameof(channel),
                    $"{(isInput ? "Input" : "Output")} channel {channel} out of range [0..{count - 1}].");
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _ = StopAllMonitoringAsync();
            _connected = false;
            GC.SuppressFinalize(this);
        }
    }
}
