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
    /// 채널 매핑 (공식 PCIS-DASK 샘플 기준):
    ///   • 채널 0~31 = Port 0, 채널 32~63 = Port 1, Line = 채널 % 32. 방향은 DI_/DO_ 함수가 구분.
    ///   • PCI-7432: 32 DI + 32 DO (각 방향 채널 0~31) / 7433: 64 DI / 7434: 64 DO.
    ///
    /// 2026-08-18 현장 실증(PCI-7432) 대조 수정 — 후속 호출의 카드 핸들은 공식 샘플과 같이
    /// <b>Register_Card 반환값</b>을 쓴다 (hCard=Register_Card(...) 후 DI_ReadPort(hCard, ...)).
    ///
    /// 모니터링: native event 없음 → polling Timer 로 BitChanged 발생.
    /// PLC 의 IPlcConnection 처럼 thread-safe.
    /// </summary>
    public sealed class AdLinkDaskConnection : IIoBoardConnection, IIoBoardDiagnostics
    {
        private readonly ushort _cardType;
        private readonly ushort _boardNum;
        private ushort _cardHandle;
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

        /// <summary>연결 실패 사유 — 호출자(App/편집기)가 로그에 남긴다.</summary>
        public string? LastError { get; private set; }

        public Task<bool> ConnectAsync()
        {
            try
            {
                if (!DaskNativeMethods.IsCardTypeVerified(_cardType))
                {
                    Debug.WriteLine(
                        $"[ADLink:{DeviceId}] card type 0x{_cardType:X} 는 현장 미검증 값입니다 " +
                        "— 연결 실패 시 DaskNativeMethods 의 상수를 먼저 확인하세요.");
                }

                var result = DaskNativeMethods.Register_Card(_cardType, _boardNum);
                if (result < 0)
                {
                    LastError = $"Register_Card 실패 (에러 {result}, cardType=0x{_cardType:X}, board={_boardNum}). " +
                                $"모델·보드 번호가 실제 카드와 일치하는지 확인하세요. {DaskNativeMethods.DiagnosticsText}";
                    Debug.WriteLine($"[ADLink:{DeviceId}] {LastError}");
                    return Task.FromResult(false);
                }

                // 공식 샘플과 동일하게 Register_Card 반환값을 카드 핸들로 사용
                _cardHandle = (ushort)result;
                _connected = true;
                LastError = null;
                Debug.WriteLine(
                    $"[ADLink:{DeviceId}] connected (board={_boardNum}, type={_cardType}, " +
                    $"handle={_cardHandle}, {DaskNativeMethods.DiagnosticsText})");
                return Task.FromResult(true);
            }
            catch (DllNotFoundException)
            {
                LastError = $"ADLink DASK 라이브러리를 찾을 수 없습니다. {DaskNativeMethods.DiagnosticsText}";
                Debug.WriteLine($"[ADLink:{DeviceId}] {LastError}");
                return Task.FromResult(false);
            }
            catch (BadImageFormatException)
            {
                LastError = "ADLink DASK 라이브러리의 비트수가 프로세스와 다릅니다 " +
                            $"(VMS 는 {(Environment.Is64BitProcess ? "64-bit" : "32-bit")}). " +
                            "같은 비트수의 DASK 드라이버를 설치하세요.";
                Debug.WriteLine($"[ADLink:{DeviceId}] {LastError}");
                return Task.FromResult(false);
            }
            catch (Exception ex)
            {
                LastError = $"연결 오류: {ex.Message}. {DaskNativeMethods.DiagnosticsText}";
                Debug.WriteLine($"[ADLink:{DeviceId}] {LastError}");
                return Task.FromResult(false);
            }
        }

        public Task DisconnectAsync()
        {
            if (_connected)
            {
                try { DaskNativeMethods.Release_Card(_cardHandle); }
                catch (Exception ex) { Debug.WriteLine($"[ADLink:{DeviceId}] Release_Card: {ex.Message}"); }
            }
            _connected = false;
            return Task.CompletedTask;
        }

        public Task<bool> ReadBitAsync(int channel)
        {
            EnsureConnected();
            ushort state = 0;
            lock (_ioLock)
            {
                var ret = DaskNativeMethods.DI_ReadLine(_cardHandle,
                    DaskNativeMethods.PortForChannel(channel), DaskNativeMethods.LineForChannel(channel), ref state);
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
                var ret = DaskNativeMethods.DI_ReadPort(_cardHandle, (ushort)portNo, ref value);
                if (ret < 0) Debug.WriteLine($"[ADLink:{DeviceId}] DI_ReadPort port={portNo} ret={ret}");
            }
            return Task.FromResult(value);
        }

        public Task WriteBitAsync(int channel, bool value)
        {
            EnsureConnected();
            lock (_ioLock)
            {
                var ret = DaskNativeMethods.DO_WriteLine(_cardHandle,
                    DaskNativeMethods.PortForChannel(channel), DaskNativeMethods.LineForChannel(channel),
                    (ushort)(value ? 1 : 0));
                if (ret < 0) Debug.WriteLine($"[ADLink:{DeviceId}] DO_WriteLine ch={channel} ret={ret}");
            }
            return Task.CompletedTask;
        }

        public Task WritePortAsync(int portNo, uint value)
        {
            EnsureConnected();
            lock (_ioLock)
            {
                var ret = DaskNativeMethods.DO_WritePort(_cardHandle, (ushort)portNo, value);
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
