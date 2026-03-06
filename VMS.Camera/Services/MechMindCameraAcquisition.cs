#if MECHMIND_AVAILABLE
using MMind.Eye;
using MechCamera = MMind.Eye.Camera;
using MechCameraInfo = MMind.Eye.CameraInfo;
#endif
using OpenCvSharp;
using System.Numerics;
using VMS.Camera.Interfaces;
using VMS.Camera.Models;

namespace VMS.Camera.Services
{
    /// <summary>
    /// Mech-Mind 3D 카메라 SDK 연동 구현
    /// MECHMIND_AVAILABLE 심볼이 없으면 컴파일에서 제외됨
    /// </summary>
#if MECHMIND_AVAILABLE
    public class MechMindCameraAcquisition : ICameraAcquisition
    {
        private Models.CameraInfo? _camera;
        private Profiler? _profiler;
        private MechCamera? _mechCamera;
        private bool _disposed;

        public bool IsConnected { get; private set; }

        public async Task<bool> ConnectAsync(Models.CameraInfo camera)
        {
            _camera = camera;

            try
            {
                _mechCamera = new MechCamera();

                // Try to connect by IP (ConnectionString) first
                if (!string.IsNullOrWhiteSpace(camera.ConnectionString))
                {
                    var status = _mechCamera.Connect(camera.ConnectionString);
                    if (status.IsOK())
                    {
                        IsConnected = true;
                        System.Diagnostics.Debug.WriteLine($"Mech-Mind 카메라 연결 성공 (IP: {camera.ConnectionString})");
                        return true;
                    }
                }

                // Fallback: discover cameras and match by serial number
                if (!string.IsNullOrWhiteSpace(camera.SerialNumber))
                {
                    var cameraInfoList = MechCamera.DiscoverCameras();
                    foreach (var info in cameraInfoList)
                    {
                        if (info.SerialNumber == camera.SerialNumber)
                        {
                            var status = _mechCamera.Connect(info);
                            if (status.IsOK())
                            {
                                IsConnected = true;
                                System.Diagnostics.Debug.WriteLine($"Mech-Mind 카메라 연결 성공 (S/N: {camera.SerialNumber})");
                                return true;
                            }
                        }
                    }
                }

                System.Diagnostics.Debug.WriteLine("Mech-Mind 카메라 연결 실패: 일치하는 카메라를 찾을 수 없습니다.");
                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Mech-Mind 카메라 연결 오류: {ex.Message}");
                return false;
            }
        }

        public async Task DisconnectAsync()
        {
            try
            {
                _mechCamera?.Disconnect();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Mech-Mind 카메라 연결 해제 오류: {ex.Message}");
            }
            finally
            {
                IsConnected = false;
            }
        }

        public async Task<AcquisitionResult> AcquireAsync()
        {
            if (!IsConnected || _mechCamera == null || _camera == null)
            {
                return new AcquisitionResult
                {
                    Success = false,
                    Message = "카메라가 연결되지 않았습니다."
                };
            }

            try
            {
                var frame2DAnd3D = new Frame2DAnd3D();

                var status = _mechCamera.Capture2DAnd3D(ref frame2DAnd3D);
                if (!status.IsOK())
                {
                    return new AcquisitionResult
                    {
                        Success = false,
                        Message = $"획득 실패: {status.ErrorDescription}"
                    };
                }

                var frame2D = frame2DAnd3D.Frame2D();
                var frame3D = frame2DAnd3D.Frame3D();

                var result = new AcquisitionResult
                {
                    Image2D = ConvertFrame2DToMat(frame2D),
                    PointCloud = ConvertFrame3DToPointCloud(frame3D, frame2D),
                    Success = true,
                    Message = "Mech-Mind 2D+3D 획득 완료"
                };

                return result;
            }
            catch (Exception ex)
            {
                return new AcquisitionResult
                {
                    Success = false,
                    Message = $"획득 오류: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// Frame2D → OpenCV Mat (BGR) 변환
        /// </summary>
        private static Mat ConvertFrame2DToMat(Frame2D frame)
        {
            var colorMap = frame.GetColorImage();
            int width = (int)colorMap.Width();
            int height = (int)colorMap.Height();

            if (width == 0 || height == 0)
                return new Mat(1, 1, MatType.CV_8UC3, Scalar.All(0));

            var mat = new Mat(height, width, MatType.CV_8UC3);
            nint dataPtr = colorMap.Data();
            int totalBytes = height * width * 3;

            unsafe
            {
                byte* src = (byte*)dataPtr;
                byte* dst = (byte*)mat.Data;
                for (int i = 0; i < height * width; i++)
                {
                    // SDK provides RGB, OpenCV expects BGR
                    dst[i * 3 + 0] = src[i * 3 + 2]; // B
                    dst[i * 3 + 1] = src[i * 3 + 1]; // G
                    dst[i * 3 + 2] = src[i * 3 + 0]; // R
                }
            }

            return mat;
        }

        /// <summary>
        /// Frame3D → PointCloudData 변환
        /// </summary>
        private static PointCloudData ConvertFrame3DToPointCloud(Frame3D frame3D, Frame2D frame2D)
        {
            var depthMap = frame3D.GetDepthMap();
            var colorMap = frame2D.GetColorImage();

            int width = (int)depthMap.Width();
            int height = (int)depthMap.Height();
            int count = width * height;

            var positions = new Vector3[count];
            var colors = new System.Windows.Media.Color[count];

            nint depthPtr = depthMap.Data();
            nint colorPtr = colorMap.Data();
            int colorWidth = (int)colorMap.Width();
            int colorHeight = (int)colorMap.Height();
            bool hasColor = colorPtr != nint.Zero && colorWidth > 0 && colorHeight > 0;

            unsafe
            {
                float* depthData = (float*)depthPtr;
                byte* colorData = hasColor ? (byte*)colorPtr : null;

                for (int row = 0; row < height; row++)
                {
                    for (int col = 0; col < width; col++)
                    {
                        int i = row * width + col;
                        float z = depthData[i];

                        // Use pixel coordinates as X/Y, depth as Z (in mm)
                        positions[i] = new Vector3(col, row, float.IsNaN(z) ? 0f : z);

                        if (hasColor && colorData != null)
                        {
                            colors[i] = System.Windows.Media.Color.FromRgb(
                                colorData[i * 3],
                                colorData[i * 3 + 1],
                                colorData[i * 3 + 2]);
                        }
                        else
                        {
                            // Colorize by depth using jet colormap
                            float t = Math.Clamp(z / 1000f, 0f, 1f);
                            byte r = (byte)(255 * Math.Clamp(1.5f - Math.Abs(t - 0.75f) * 4f, 0f, 1f));
                            byte g = (byte)(255 * Math.Clamp(1.5f - Math.Abs(t - 0.5f) * 4f, 0f, 1f));
                            byte b = (byte)(255 * Math.Clamp(1.5f - Math.Abs(t - 0.25f) * 4f, 0f, 1f));
                            colors[i] = System.Windows.Media.Color.FromRgb(r, g, b);
                        }
                    }
                }
            }

            return new PointCloudData
            {
                Name = "Mech-Mind 3D Scan",
                Positions = positions,
                Colors = colors,
                GridWidth = width,
                GridHeight = height
            };
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                try
                {
                    _mechCamera?.Disconnect();
                }
                catch { }
                IsConnected = false;
            }
        }
    }
#endif
}
