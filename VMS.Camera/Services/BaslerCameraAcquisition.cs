#if BASLER_AVAILABLE
using Basler.Pylon;
#endif
using OpenCvSharp;
using System.Runtime.InteropServices;
using VMS.Camera.Interfaces;
using VMS.Camera.Models;

namespace VMS.Camera.Services
{
    /// <summary>
    /// Basler GigE/USB3 카메라 Pylon SDK 연동 구현
    /// BASLER_AVAILABLE 심볼이 없으면 컴파일에서 제외됨
    /// </summary>
#if BASLER_AVAILABLE
    public class BaslerCameraAcquisition : ICameraAcquisition
    {
        private Models.CameraInfo? _camera;
        private Basler.Pylon.Camera? _pylonCamera;
        private PixelDataConverter? _converter;
        private bool _disposed;

        // pylon SDK 접근 직렬화 — grab(Start/Retrieve/Stop)과 파라미터 읽기/쓰기가 서로 다른
        // 스레드에서 겹치면, USB 재열거·케이블 분리 등으로 grab 이 SDK 내부에서 블록된 상태일 때
        // 파라미터 접근 스레드까지 같은 내부 잠금에 걸려 연쇄 정지한다 (세연공장 2026-08-28:
        // 라이브 중 USB 삽입/해제 → 앱 전체 프리즈). 파라미터 쪽은 타임아웃으로 스킵해
        // grab 이 행 상태여도 절대 따라 잠기지 않는다.
        private readonly SemaphoreSlim _sdkLock = new(1, 1);
        private const int ParamLockTimeoutMs = 2000;

        public bool IsConnected { get; private set; }

        /// <summary>
        /// 케이블 분리·하트비트 타임아웃 등 런타임 끊김 통지. pylon ConnectionLost 이벤트와
        /// grab 중 디바이스 제거 감지 양쪽에서 발생한다 (2026-08-19 현장: 끊김 미감지).
        /// </summary>
        public event EventHandler<string>? ConnectionLost;

        public Task<bool> ConnectAsync(Models.CameraInfo camera)
        {
            _camera = camera;

            // 재연결 시 적용 캐시 무효화 — 카메라 교체/전원 재투입일 수 있음 (Mech-Eye 와 동일 규칙)
            _appliedExposureUs = -1;
            _appliedGain = double.NaN;

            // Enumerate/Open 은 GigE 네트워크 왕복(수 초 가능) — 호출자(UI 스레드)를 막지 않는다
            return Task.Run(() => ConnectCore(camera));
        }

        private bool ConnectCore(Models.CameraInfo camera)
        {
            // 행 상태의 grab 이 잠금을 영구 점유했을 수 있다 — 무한 대기 대신 포기하고 실패 보고
            if (!_sdkLock.Wait(10000))
            {
                CameraLog.Write("[Basler] 연결 스킵 — SDK 사용 중 (이전 grab 행 의심, 앱 재시작 필요 가능)");
                return false;
            }

            try
            {
                // 재연결 경로 — 이전 카메라 핸들이 살아 있으면(케이블 재연결 후 등)
                // 죽은 핸들로는 절대 복구되지 않으므로 완전히 정리 후 새로 연다.
                CleanupPylonCamera();

                // Discover all available Basler cameras
                var allCameras = CameraFinder.Enumerate();

                if (allCameras.Count == 0)
                {
                    CameraLog.Write("[Basler] 카메라를 찾을 수 없습니다 (Enumerate 0대)");
                    return false;
                }

                ICameraInfo? targetCamera = null;

                // Try to match by IP address (ConnectionString)
                if (!string.IsNullOrWhiteSpace(camera.ConnectionString))
                {
                    targetCamera = allCameras.FirstOrDefault(c =>
                    {
                        try
                        {
                            return c[CameraInfoKey.DeviceIpAddress] == camera.ConnectionString;
                        }
                        catch { return false; }
                    });
                }

                // Fallback: match by serial number
                if (targetCamera == null && !string.IsNullOrWhiteSpace(camera.SerialNumber))
                {
                    targetCamera = allCameras.FirstOrDefault(c =>
                    {
                        try
                        {
                            return c[CameraInfoKey.SerialNumber] == camera.SerialNumber;
                        }
                        catch { return false; }
                    });
                }

                // Fallback: use first available camera
                targetCamera ??= allCameras[0];

                _pylonCamera = new Basler.Pylon.Camera(targetCamera);

                // 케이블 분리/네트워크 단절을 pylon 이 하트비트 만료로 알려준다 —
                // 구독하지 않으면 IsConnected 가 영원히 true 로 남아 재연결 분기가 막힌다.
                _pylonCamera.ConnectionLost += OnPylonConnectionLost;

                _pylonCamera.Open();

                // GigE 하트비트 타임아웃 명시 (기본값이 환경에 따라 수십 초일 수 있음) —
                // 3초로 줄여 케이블 분리를 빠르게 감지. USB3 등 미지원 모델은 조용히 무시.
                try
                {
                    if (_pylonCamera.Parameters.Contains(PLCamera.GevHeartbeatTimeout))
                        _pylonCamera.Parameters[PLCamera.GevHeartbeatTimeout]
                            .TrySetValue(3000, IntegerValueCorrection.Nearest);
                }
                catch { /* 하트비트 설정 실패는 연결 자체를 막지 않는다 */ }

                // 자동 노출/게인 Off — 단발 Start/Stop grab 반복 구조에서는 AE 가 수렴하지
                // 못해 프레임마다 밝기가 요동친다 (현장 실증 2026-08-06). 미지원 모델이면
                // TrySetValue 가 조용히 false 를 반환하므로 안전.
                var expAutoOff = _pylonCamera.Parameters[PLCamera.ExposureAuto]
                    .TrySetValue(PLCamera.ExposureAuto.Off);
                var gainAutoOff = _pylonCamera.Parameters[PLCamera.GainAuto]
                    .TrySetValue(PLCamera.GainAuto.Off);

                // Initialize pixel data converter (BGR8 for OpenCV compatibility)
                _converter = new PixelDataConverter();
                _converter.OutputPixelFormat = PixelType.BGR8packed;

                IsConnected = true;
                CameraLog.Write(
                    $"[Basler] 연결 성공: {targetCamera[CameraInfoKey.FriendlyName]} " +
                    $"(ExposureAuto Off={expAutoOff}, GainAuto Off={gainAutoOff})");
                return true;
            }
            catch (Exception ex)
            {
                CameraLog.Write($"[Basler] 연결 오류: {ex.Message}");
                return false;
            }
            finally
            {
                _sdkLock.Release();
            }
        }

        private void OnPylonConnectionLost(object? sender, EventArgs e)
        {
            IsConnected = false;
            CameraLog.Write("[Basler] 연결 끊김 감지 (pylon ConnectionLost) — 케이블/전원/네트워크 확인 필요");
            ConnectionLost?.Invoke(this, "pylon ConnectionLost (하트비트 만료)");
        }

        /// <summary>이전 pylon 카메라 핸들 완전 정리 — 이벤트 해제 + Close + Dispose.</summary>
        private void CleanupPylonCamera()
        {
            var old = _pylonCamera;
            _pylonCamera = null;
            if (old == null) return;

            try
            {
                old.ConnectionLost -= OnPylonConnectionLost;
                var grabber = old.StreamGrabber;
                if (grabber != null && grabber.IsGrabbing)
                    grabber.Stop();
                if (old.IsOpen)
                    old.Close();
                old.Dispose();
            }
            catch (Exception ex)
            {
                CameraLog.Write($"[Basler] 이전 카메라 핸들 정리 오류 (무시): {ex.Message}");
            }
        }

        public Task DisconnectAsync()
        {
            IsConnected = false;
            // Close/Stop 은 죽은 전송 계층에서 블록될 수 있다 — 호출자(UI)를 절대 막지 않는다
            return Task.Run(() =>
            {
                bool locked = _sdkLock.Wait(ParamLockTimeoutMs);
                try
                {
                    CleanupPylonCamera();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Basler 카메라 연결 해제 오류: {ex.Message}");
                }
                finally
                {
                    if (locked) _sdkLock.Release();
                }
            });
        }

        public Task<AcquisitionResult> AcquireAsync(int timeoutMs = 5000)
        {
            if (!IsConnected || _pylonCamera == null || _converter == null)
            {
                return Task.FromResult(new AcquisitionResult
                {
                    Success = false,
                    Message = "카메라가 연결되지 않았습니다."
                });
            }

            // 필드는 중간 메서드 호출로 null 흐름분석이 무효화됨 — 지역 캡처로 고정
            var converter = _converter;
            var grabber = _pylonCamera.StreamGrabber;
            if (grabber == null)
            {
                return Task.FromResult(new AcquisitionResult
                {
                    Success = false,
                    Message = "StreamGrabber 를 사용할 수 없습니다."
                });
            }

            // RetrieveResult 는 블로킹 대기 — async 시그니처만으로는 동기 완료라서
            // UI 스레드 호출자(라이브 반복 루프 포함)가 통째로 잠겼다 (현장 프리즈 2026-08-06).
            // grab 전체를 스레드풀에서 수행해 호출자를 절대 막지 않는다.
            return Task.Run(() => AcquireCore(converter, grabber, timeoutMs));
        }

        private AcquisitionResult AcquireCore(
            PixelDataConverter converter, IStreamGrabber grabber, int timeoutMs)
        {
            _sdkLock.Wait();
            try
            {
                // Start grab (single frame)
                grabber.Start(1, GrabStrategy.OneByOne, GrabLoop.ProvidedByUser);

                // Retrieve grab result with timeout
                using var grabResult = grabber.RetrieveResult(
                    timeoutMs, TimeoutHandling.ThrowException);

                if (grabResult is { GrabSucceeded: true })
                {
                    int width = grabResult.Width;
                    int height = grabResult.Height;

                    // Convert pixel format to BGR8 for OpenCV
                    byte[] bgrBuffer = new byte[width * height * 3];
                    converter.Convert(bgrBuffer, grabResult);

                    // Create OpenCV Mat from BGR buffer
                    var mat = new Mat(height, width, MatType.CV_8UC3);
                    Marshal.Copy(bgrBuffer, 0, mat.Data, bgrBuffer.Length);

                    return new AcquisitionResult
                    {
                        Success = true,
                        Image2D = mat,
                        Message = $"Basler 획득 완료 ({width}x{height})"
                    };
                }
                else
                {
                    return new AcquisitionResult
                    {
                        Success = false,
                        Message = $"Grab 실패: Error {grabResult?.ErrorCode} - {grabResult?.ErrorDescription}"
                    };
                }
            }
            catch (Exception ex)
            {
                // grab 예외를 문자열로만 삼키면 끊김이 상태에 반영되지 않는다 (2026-08-19 현장).
                // 디바이스 제거가 확인되면 IsConnected 를 내리고 ConnectionLost 로 통지 —
                // 이후 호출자의 lazy 재연결(!IsConnected 분기)이 실제로 동작하게 된다.
                // pylon Camera.IsConnected 는 디바이스 제거/연결 상실 시 false 가 된다
                var removed = false;
                try { removed = !(_pylonCamera?.IsConnected ?? false); } catch { }

                if (removed && IsConnected)
                {
                    IsConnected = false;
                    CameraLog.Write($"[Basler] grab 중 카메라 제거 감지: {ex.Message}");
                    ConnectionLost?.Invoke(this, "grab 중 디바이스 제거 감지");
                }
                else
                {
                    CameraLog.Write($"[Basler] 획득 오류: {ex.Message}");
                }

                return new AcquisitionResult
                {
                    Success = false,
                    Message = removed ? $"카메라 연결 끊김: {ex.Message}" : $"획득 오류: {ex.Message}"
                };
            }
            finally
            {
                try
                {
                    // 장치가 이미 사라졌으면 Stop 을 시도하지 않는다 — 죽은 전송 계층에
                    // 대한 Stop 은 무기한 블록될 수 있다 (USB 재열거/케이블 분리 중)
                    bool stillConnected = false;
                    try { stillConnected = _pylonCamera?.IsConnected ?? false; } catch { }
                    if (stillConnected && grabber.IsGrabbing)
                        grabber.Stop();
                }
                catch { }
                finally
                {
                    _sdkLock.Release();
                }
            }
        }

        // 마지막으로 적용 성공한 값 — 같은 값이면 SDK 왕복 생략 (라이브 루프에서 매 프레임 호출됨)
        private double _appliedExposureUs = -1;
        private double _appliedGain = double.NaN;

        /// <summary>
        /// 노출(µs)/게인을 카메라에 적용. GigE 는 ExposureTimeAbs(µs)/GainRaw(장치 단위),
        /// USB3 는 ExposureTime(µs)/Gain(dB). GainRaw 는 dB 가 아니라 장치 고유 단위라서
        /// UI 의 dB 값을 반올림해 그대로 쓴다 (근사 — 모델별 스케일 상이).
        /// 게인은 모델별 미지원이 있어 실패해도 전체 실패로 치지 않는다 (Mech-Eye 와 동일 규칙).
        /// </summary>
        public Task<bool> ApplySettingsAsync(double exposureUs, double gain)
        {
            if (!IsConnected || _pylonCamera == null) return Task.FromResult(false);
            if (exposureUs == _appliedExposureUs && gain == _appliedGain) return Task.FromResult(true);

            // 파라미터 접근도 SDK 왕복(GigE 네트워크 포함) — 호출 스레드(UI 포함)에서 실행하지
            // 않는다. grab 이 SDK 내부에서 행 상태면 타임아웃으로 포기 (프리즈 전파 차단).
            return Task.Run(() => ApplySettingsCore(exposureUs, gain));
        }

        private bool ApplySettingsCore(double exposureUs, double gain)
        {
            var cam = _pylonCamera;
            if (cam == null) return false;
            if (!_sdkLock.Wait(ParamLockTimeoutMs))
            {
                CameraLog.Write("[Basler] 파라미터 적용 스킵 — SDK 사용 중 (grab 지연/행 의심)");
                return false;
            }

            try
            {
                var p = cam.Parameters;
                bool ok = true;

                if (exposureUs > 0)
                {
                    // 자동 노출이 켜져 있으면 수동 값이 무시/거부됨 — 항상 Off 를 먼저 시도
                    p[PLCamera.ExposureAuto].TrySetValue(PLCamera.ExposureAuto.Off);

                    if (p.Contains(PLCamera.ExposureTimeAbs))
                        ok = p[PLCamera.ExposureTimeAbs].TrySetValue(exposureUs, FloatValueCorrection.ClipToRange);
                    else if (p.Contains(PLCamera.ExposureTime))
                        ok = p[PLCamera.ExposureTime].TrySetValue(exposureUs, FloatValueCorrection.ClipToRange);
                    else
                        ok = false;

                    if (!ok)
                        CameraLog.Write($"[Basler] 노출 {exposureUs}µs 적용 실패");
                }

                p[PLCamera.GainAuto].TrySetValue(PLCamera.GainAuto.Off);
                bool gainOk = false;
                if (p.Contains(PLCamera.Gain))
                {
                    gainOk = p[PLCamera.Gain].TrySetValue(gain, FloatValueCorrection.ClipToRange);
                }
                else if (p.Contains(PLCamera.GainRaw))
                {
                    // 정수 파라미터에는 TrySetValue 확장이 없음 — 범위 클램프 후 SetValue
                    try
                    {
                        var raw = p[PLCamera.GainRaw];
                        raw.SetValue(Math.Clamp((long)Math.Round(gain), raw.GetMinimum(), raw.GetMaximum()));
                        gainOk = true;
                    }
                    catch { gainOk = false; }
                }

                if (!gainOk)
                    CameraLog.Write($"[Basler] 게인 {gain} 적용 실패 (모델별 미지원 가능) — 노출만 반영");

                if (ok)
                {
                    _appliedExposureUs = exposureUs;
                    _appliedGain = gain;
                    CameraLog.Write($"[Basler] 노출 {exposureUs}µs / 게인 {gain} 적용 (게인 성공={gainOk})");
                }
                return ok;
            }
            catch (Exception ex)
            {
                CameraLog.Write($"[Basler] 파라미터 적용 오류: {ex.Message}");
                return false;
            }
            finally
            {
                _sdkLock.Release();
            }
        }

        /// <summary>
        /// 카메라 현재 노출(µs)/게인 읽기 (read-back). 노출 읽기 실패 시 null,
        /// 게인은 미지원 모델이 있어 실패해도 0 으로 대체하고 노출만이라도 반환한다.
        /// </summary>
        public Task<CameraSettings2D?> ReadSettingsAsync()
        {
            if (!IsConnected || _pylonCamera == null) return Task.FromResult<CameraSettings2D?>(null);

            // UI 스레드(설정 패널 read-back)가 직접 호출한다 — SDK 왕복을 호출 스레드에서
            // 수행하면 grab 이 행 상태일 때 UI 가 같은 내부 잠금에 걸려 앱 전체가 멈춘다
            // (세연공장 2026-08-28 라이브 중 USB 이벤트 프리즈). 반드시 스레드풀 + 타임아웃.
            return Task.Run(() => ReadSettingsCore());
        }

        private CameraSettings2D? ReadSettingsCore()
        {
            var cam = _pylonCamera;
            if (cam == null) return null;
            if (!_sdkLock.Wait(ParamLockTimeoutMs))
            {
                CameraLog.Write("[Basler] 파라미터 읽기 스킵 — SDK 사용 중 (grab 지연/행 의심)");
                return null;
            }

            try
            {
                var p = cam.Parameters;

                double exposureUs;
                if (p.Contains(PLCamera.ExposureTimeAbs) && p[PLCamera.ExposureTimeAbs].IsReadable)
                    exposureUs = p[PLCamera.ExposureTimeAbs].GetValue();
                else if (p.Contains(PLCamera.ExposureTime) && p[PLCamera.ExposureTime].IsReadable)
                    exposureUs = p[PLCamera.ExposureTime].GetValue();
                else
                    return null;

                double gain = 0;
                if (p.Contains(PLCamera.Gain) && p[PLCamera.Gain].IsReadable)
                    gain = p[PLCamera.Gain].GetValue();
                else if (p.Contains(PLCamera.GainRaw) && p[PLCamera.GainRaw].IsReadable)
                    gain = p[PLCamera.GainRaw].GetValue();   // 장치 단위 (dB 아님)

                return new CameraSettings2D(exposureUs, gain);
            }
            catch (Exception ex)
            {
                CameraLog.Write($"[Basler] 파라미터 읽기 오류: {ex.Message}");
                return null;
            }
            finally
            {
                _sdkLock.Release();
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            IsConnected = false;

            // 카메라 전환 시 UI 스레드에서 호출된다 — SDK 정리(Close/Stop)가 죽은 전송
            // 계층에서 블록될 수 있으므로 백그라운드로 위임 (fire-and-forget, 실패 무해)
            var converter = _converter;
            _converter = null;
            _ = Task.Run(() =>
            {
                bool locked = _sdkLock.Wait(ParamLockTimeoutMs);
                try
                {
                    CleanupPylonCamera();
                    converter?.Dispose();
                }
                catch { }
                finally
                {
                    if (locked) _sdkLock.Release();
                }
            });
        }
    }
#endif
}
