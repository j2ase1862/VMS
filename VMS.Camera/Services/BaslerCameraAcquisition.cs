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

        public bool IsConnected { get; private set; }

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
            try
            {
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
                _pylonCamera.Open();

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
        }

        public async Task DisconnectAsync()
        {
            try
            {
                if (_pylonCamera != null && _pylonCamera.IsOpen)
                {
                    _pylonCamera.Close();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Basler 카메라 연결 해제 오류: {ex.Message}");
            }
            finally
            {
                IsConnected = false;
            }
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
                return new AcquisitionResult
                {
                    Success = false,
                    Message = $"획득 오류: {ex.Message}"
                };
            }
            finally
            {
                try
                {
                    if (grabber.IsGrabbing)
                        grabber.Stop();
                }
                catch { }
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

            try
            {
                var p = _pylonCamera.Parameters;
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
                return Task.FromResult(ok);
            }
            catch (Exception ex)
            {
                CameraLog.Write($"[Basler] 파라미터 적용 오류: {ex.Message}");
                return Task.FromResult(false);
            }
        }

        /// <summary>
        /// 카메라 현재 노출(µs)/게인 읽기 (read-back). 노출 읽기 실패 시 null,
        /// 게인은 미지원 모델이 있어 실패해도 0 으로 대체하고 노출만이라도 반환한다.
        /// </summary>
        public Task<CameraSettings2D?> ReadSettingsAsync()
        {
            if (!IsConnected || _pylonCamera == null) return Task.FromResult<CameraSettings2D?>(null);

            try
            {
                var p = _pylonCamera.Parameters;

                double exposureUs;
                if (p.Contains(PLCamera.ExposureTimeAbs) && p[PLCamera.ExposureTimeAbs].IsReadable)
                    exposureUs = p[PLCamera.ExposureTimeAbs].GetValue();
                else if (p.Contains(PLCamera.ExposureTime) && p[PLCamera.ExposureTime].IsReadable)
                    exposureUs = p[PLCamera.ExposureTime].GetValue();
                else
                    return Task.FromResult<CameraSettings2D?>(null);

                double gain = 0;
                if (p.Contains(PLCamera.Gain) && p[PLCamera.Gain].IsReadable)
                    gain = p[PLCamera.Gain].GetValue();
                else if (p.Contains(PLCamera.GainRaw) && p[PLCamera.GainRaw].IsReadable)
                    gain = p[PLCamera.GainRaw].GetValue();   // 장치 단위 (dB 아님)

                return Task.FromResult<CameraSettings2D?>(new CameraSettings2D(exposureUs, gain));
            }
            catch (Exception ex)
            {
                CameraLog.Write($"[Basler] 파라미터 읽기 오류: {ex.Message}");
                return Task.FromResult<CameraSettings2D?>(null);
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                try
                {
                    if (_pylonCamera != null)
                    {
                        var grabber = _pylonCamera.StreamGrabber;
                        if (grabber != null && grabber.IsGrabbing)
                            grabber.Stop();

                        if (_pylonCamera.IsOpen)
                            _pylonCamera.Close();

                        _pylonCamera.Dispose();
                    }
                    _converter?.Dispose();
                }
                catch { }
                IsConnected = false;
            }
        }
    }
#endif
}
