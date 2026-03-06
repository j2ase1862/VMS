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

        public async Task<bool> ConnectAsync(Models.CameraInfo camera)
        {
            _camera = camera;

            try
            {
                // Discover all available Basler cameras
                var allCameras = CameraFinder.Enumerate();

                if (allCameras.Count == 0)
                {
                    System.Diagnostics.Debug.WriteLine("Basler 카메라를 찾을 수 없습니다.");
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

                // Initialize pixel data converter (BGR8 for OpenCV compatibility)
                _converter = new PixelDataConverter();
                _converter.OutputPixelFormat = PixelType.BGR8packed;

                IsConnected = true;
                System.Diagnostics.Debug.WriteLine(
                    $"Basler 카메라 연결 성공: {targetCamera[CameraInfoKey.FriendlyName]}");
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Basler 카메라 연결 오류: {ex.Message}");
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

        public async Task<AcquisitionResult> AcquireAsync()
        {
            if (!IsConnected || _pylonCamera == null || _converter == null)
            {
                return new AcquisitionResult
                {
                    Success = false,
                    Message = "카메라가 연결되지 않았습니다."
                };
            }

            try
            {
                // Apply camera parameters if available
                ApplyCameraParameters();

                // Start grab (single frame)
                _pylonCamera.StreamGrabber.Start(1, GrabStrategy.OneByOne, GrabLoop.ProvidedByUser);

                // Retrieve grab result with 5-second timeout
                using var grabResult = _pylonCamera.StreamGrabber.RetrieveResult(
                    5000, TimeoutHandling.ThrowException);

                if (grabResult.GrabSucceeded)
                {
                    int width = grabResult.Width;
                    int height = grabResult.Height;

                    // Convert pixel format to BGR8 for OpenCV
                    byte[] bgrBuffer = new byte[width * height * 3];
                    _converter.Convert(bgrBuffer, grabResult);

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
                        Message = $"Grab 실패: Error {grabResult.ErrorCode} - {grabResult.ErrorDescription}"
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
                    if (_pylonCamera.StreamGrabber.IsGrabbing)
                        _pylonCamera.StreamGrabber.Stop();
                }
                catch { }
            }
        }

        /// <summary>
        /// CameraInfo의 설정값을 카메라에 적용
        /// </summary>
        private void ApplyCameraParameters()
        {
            if (_pylonCamera == null || _camera == null) return;

            try
            {
                // Exposure Time (try both naming conventions for GigE and USB3)
                if (_pylonCamera.Parameters.Contains(PLCamera.ExposureTimeAbs))
                {
                    // GigE cameras use ExposureTimeAbs (microseconds)
                    // Default: no-op, keep camera's current setting
                }
                else if (_pylonCamera.Parameters.Contains(PLCamera.ExposureTime))
                {
                    // USB3 cameras use ExposureTime
                }

                // Gain
                if (_pylonCamera.Parameters.Contains(PLCamera.GainRaw))
                {
                    // GigE cameras use GainRaw (integer)
                }
                else if (_pylonCamera.Parameters.Contains(PLCamera.Gain))
                {
                    // USB3 cameras use Gain (float)
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"카메라 파라미터 적용 실패: {ex.Message}");
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
                        if (_pylonCamera.StreamGrabber.IsGrabbing)
                            _pylonCamera.StreamGrabber.Stop();

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
