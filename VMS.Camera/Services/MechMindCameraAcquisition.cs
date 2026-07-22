#if MECHMIND_AVAILABLE
using MMind.Eye;
using MechCamera = MMind.Eye.Camera;
using MechCameraInfo = MMind.Eye.CameraInfo;
using Scan2D = MMind.Eye.Scanning2DSetting;
using Scan3D = MMind.Eye.Scanning3DSetting;
using PcProc = MMind.Eye.PointCloudProcessingSetting;
#endif
using OpenCvSharp;
using System.Numerics;
using System.Runtime.InteropServices;
using VMS.Camera.Interfaces;
using VMS.Camera.Models;
using WpfColor = System.Windows.Media.Color;

namespace VMS.Camera.Services
{
    /// <summary>
    /// Mech-Mind 3D 카메라 SDK 연동 구현
    /// MECHMIND_AVAILABLE 심볼이 없으면 컴파일에서 제외됨
    /// </summary>
#if MECHMIND_AVAILABLE
    public class MechMindCameraAcquisition : ICameraAcquisition
    {
        private static readonly string[] SdkSearchPaths = new[]
        {
            @"C:\Mech-Mind\Mech-Eye SDK-2.5.4",
            @"C:\Mech-Mind\Mech-Eye SDK-2.5.4\API\dll"
        };

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool AddDllDirectory(string lpPathName);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SetDefaultDllDirectories(uint directoryFlags);

        private const uint LOAD_LIBRARY_SEARCH_DEFAULT_DIRS = 0x00001000;

        static MechMindCameraAcquisition()
        {
            try
            {
                SetDefaultDllDirectories(LOAD_LIBRARY_SEARCH_DEFAULT_DIRS);
                foreach (var path in SdkSearchPaths)
                {
                    if (System.IO.Directory.Exists(path))
                    {
                        AddDllDirectory(path);
                        System.Diagnostics.Debug.WriteLine($"[MechMind] DLL search path added: {path}");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MechMind] DLL path setup failed: {ex.Message}");
            }
        }

        private Models.CameraInfo? _camera;
        private MechCamera? _mechCamera;
        private bool _disposed;

        public bool IsConnected { get; private set; }
        public int DownsampleStride { get; set; } = 1;

        public async Task<bool> ConnectAsync(Models.CameraInfo camera)
        {
            _camera = camera;
            // 재연결 시 이전 적용 캐시 무효화 — 카메라가 바뀌었거나 전원 재투입일 수 있음
            _appliedExposureUs = -1;
            _appliedGain = double.NaN;
            _applied3D = null;

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

        // 마지막으로 적용 성공한 값 — 같은 값이면 UserSet 왕복 생략 (Grab 마다 호출되므로)
        private double _appliedExposureUs = -1;
        private double _appliedGain = double.NaN;

        /// <summary>
        /// 2D 이미지 노출/게인 적용. Mech-Eye 규칙 두 가지에 주의:
        /// 1) Scan2DExposureTime 은 Scan2DExposureMode=Timed 일 때만 유효 (Auto/HDR/Flash 면 무시됨)
        ///    → 모드를 Timed 로 함께 강제한다.
        /// 2) 단위가 ms (VMS 스텝 설정은 µs) → 변환 + SDK 허용 범위로 clamp.
        /// Gain(Scan2DGain, dB)은 모델별 미지원이 있어 실패해도 전체 실패로 치지 않는다.
        /// </summary>
        public Task<bool> ApplySettingsAsync(double exposureUs, double gain)
        {
            if (!IsConnected || _mechCamera == null) return Task.FromResult(false);
            if (exposureUs == _appliedExposureUs && gain == _appliedGain) return Task.FromResult(true);

            try
            {
                var userSet = _mechCamera.CurrentUserSet();
                bool ok = true;

                if (exposureUs > 0)
                {
                    var modeStatus = userSet.SetEnumValue(
                        Scan2D.ExposureMode.Name,
                        (int)Scan2D.ExposureMode.Value.Timed);
                    if (!modeStatus.IsOK())
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"[MechMind] Scan2DExposureMode=Timed 실패: {modeStatus.ErrorDescription}");
                        ok = false;
                    }

                    double exposureMs = Math.Clamp(exposureUs / 1000.0, 0.1, 999.0);
                    var expStatus = userSet.SetFloatValue(
                        Scan2D.ExposureTime.Name, exposureMs);
                    if (!expStatus.IsOK())
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"[MechMind] Scan2DExposureTime={exposureMs}ms 실패: {expStatus.ErrorDescription}");
                        ok = false;
                    }
                }

                if (gain >= 0)
                {
                    var gainStatus = userSet.SetFloatValue(Scan2D.Gain.Name, gain);
                    if (!gainStatus.IsOK())
                        System.Diagnostics.Debug.WriteLine(
                            $"[MechMind] Scan2DGain={gain}dB 실패(모델 미지원 가능): {gainStatus.ErrorDescription}");
                }

                if (ok)
                {
                    _appliedExposureUs = exposureUs;
                    _appliedGain = gain;
                    System.Diagnostics.Debug.WriteLine(
                        $"[MechMind] 2D 노출 {exposureUs}µs(→{Math.Clamp(exposureUs / 1000.0, 0.1, 999.0)}ms) / 게인 {gain}dB 적용");
                }
                return Task.FromResult(ok);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MechMind] 파라미터 적용 오류: {ex.Message}");
                return Task.FromResult(false);
            }
        }

        // 마지막으로 적용 성공한 3D 설정 — record 값 동등성으로 동일 설정 재적용 스킵
        private Scan3DSettings? _applied3D;

        /// <summary>
        /// 3D 스캔 후처리·뎁스 범위 적용. Mech-Eye Viewer 의 '포인트 클라우드 후처리'
        /// 그룹(표면 스무딩/노이즈 제거/이상점 제거)에 프리셋 강도를 일괄 적용하고,
        /// 뎁스 범위는 Scanning3D DepthRange(mm)에 반영한다.
        /// CameraDefault + 범위 미사용이면 카메라 현재 설정을 건드리지 않는다.
        /// </summary>
        public Task<bool> Apply3DSettingsAsync(Scan3DSettings settings)
        {
            if (!IsConnected || _mechCamera == null) return Task.FromResult(false);
            if (settings.PostProcessPreset == PointCloudPostProcessPreset.CameraDefault &&
                !settings.UseDepthRange)
                return Task.FromResult(true);   // 유지 모드 — SDK 왕복 없음
            if (settings == _applied3D) return Task.FromResult(true);

            try
            {
                var userSet = _mechCamera.CurrentUserSet();
                bool ok = true;

                if (settings.PostProcessPreset != PointCloudPostProcessPreset.CameraDefault)
                {
                    ok &= SetEnum(userSet, PcProc.SurfaceSmoothing.Name,
                        (int)MapSmoothing(settings.PostProcessPreset));
                    ok &= SetEnum(userSet, PcProc.NoiseRemoval.Name,
                        (int)MapNoise(settings.PostProcessPreset));
                    ok &= SetEnum(userSet, PcProc.OutlierRemoval.Name,
                        (int)MapOutlier(settings.PostProcessPreset));
                }

                if (settings.UseDepthRange && settings.DepthRangeMaxMm > settings.DepthRangeMinMm)
                {
                    var rangeStatus = userSet.SetRangeValue(
                        Scan3D.DepthRange.Name,
                        new IntRange((int)settings.DepthRangeMinMm, (int)settings.DepthRangeMaxMm));
                    if (!rangeStatus.IsOK())
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"[MechMind] DepthRange=[{settings.DepthRangeMinMm}, {settings.DepthRangeMaxMm}]mm 실패: {rangeStatus.ErrorDescription}");
                        ok = false;
                    }
                }

                if (ok)
                {
                    _applied3D = settings;
                    System.Diagnostics.Debug.WriteLine(
                        $"[MechMind] 3D 설정 적용: 후처리={settings.PostProcessPreset}" +
                        (settings.UseDepthRange
                            ? $", DepthRange=[{settings.DepthRangeMinMm}, {settings.DepthRangeMaxMm}]mm"
                            : ""));
                }
                return Task.FromResult(ok);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MechMind] 3D 설정 적용 오류: {ex.Message}");
                return Task.FromResult(false);
            }
        }

        private static bool SetEnum(UserSet userSet, string name, int value)
        {
            var status = userSet.SetEnumValue(name, value);
            if (!status.IsOK())
                System.Diagnostics.Debug.WriteLine(
                    $"[MechMind] {name}={value} 실패(모델 미지원 가능): {status.ErrorDescription}");
            return status.IsOK();
        }

        private static PcProc.SurfaceSmoothing.Value MapSmoothing(PointCloudPostProcessPreset p) => p switch
        {
            PointCloudPostProcessPreset.Weak => PcProc.SurfaceSmoothing.Value.Weak,
            PointCloudPostProcessPreset.Normal => PcProc.SurfaceSmoothing.Value.Normal,
            PointCloudPostProcessPreset.Strong => PcProc.SurfaceSmoothing.Value.Strong,
            _ => PcProc.SurfaceSmoothing.Value.Off
        };

        private static PcProc.NoiseRemoval.Value MapNoise(PointCloudPostProcessPreset p) => p switch
        {
            PointCloudPostProcessPreset.Weak => PcProc.NoiseRemoval.Value.Weak,
            PointCloudPostProcessPreset.Normal => PcProc.NoiseRemoval.Value.Normal,
            PointCloudPostProcessPreset.Strong => PcProc.NoiseRemoval.Value.Strong,
            _ => PcProc.NoiseRemoval.Value.Off
        };

        private static PcProc.OutlierRemoval.Value MapOutlier(PointCloudPostProcessPreset p) => p switch
        {
            PointCloudPostProcessPreset.Weak => PcProc.OutlierRemoval.Value.Weak,
            PointCloudPostProcessPreset.Normal => PcProc.OutlierRemoval.Value.Normal,
            PointCloudPostProcessPreset.Strong => PcProc.OutlierRemoval.Value.Strong,
            _ => PcProc.OutlierRemoval.Value.Off
        };

        public async Task<AcquisitionResult> AcquireAsync(int timeoutMs = 5000)
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

                var stride = Math.Max(1, DownsampleStride);
                var result = new AcquisitionResult
                {
                    Image2D = ConvertFrame2DToMat(frame2D),
                    PointCloud = ConvertFrame3DToPointCloud(frame3D, frame2D, stride),
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
        /// Frame3D → PointCloudData 변환 (Parallel.For + ArrayPool + Stride)
        /// </summary>
        private static PointCloudData ConvertFrame3DToPointCloud(Frame3D frame3D, Frame2D frame2D, int stride = 1)
        {
            var depthMap = frame3D.GetDepthMap();
            var colorMap = frame2D.GetColorImage();

            int srcWidth = (int)depthMap.Width();
            int srcHeight = (int)depthMap.Height();

            // Stride 적용 후 출력 크기
            int outWidth = (srcWidth + stride - 1) / stride;
            int outHeight = (srcHeight + stride - 1) / stride;
            int outCount = outWidth * outHeight;

            // ArrayPool에서 배열 대여
            var data = PointCloudData.CreatePooled(outCount, "Mech-Mind 3D Scan", outWidth, outHeight);
            var positions = data.Positions;
            var colors = data.Colors;

            nint depthPtr = depthMap.Data();
            nint colorPtr = colorMap.Data();
            int colorWidth = (int)colorMap.Width();
            int colorHeight = (int)colorMap.Height();
            bool hasColor = colorPtr != nint.Zero && colorWidth > 0 && colorHeight > 0;

            // Pointer를 nint로 캡처 (lambda에서 포인터 직접 캡처 불가)
            nint dp = depthPtr;
            nint cp = colorPtr;

            unsafe
            {
                Parallel.For(0, outHeight, outRow =>
                {
                    float* depthData = (float*)dp;
                    byte* colorData = hasColor ? (byte*)cp : null;

                    int srcRow = outRow * stride;
                    for (int outCol = 0; outCol < outWidth; outCol++)
                    {
                        int srcCol = outCol * stride;
                        int srcIdx = srcRow * srcWidth + srcCol;
                        int dstIdx = outRow * outWidth + outCol;

                        float z = depthData[srcIdx];
                        positions[dstIdx] = new Vector3(srcCol, srcRow, float.IsNaN(z) ? 0f : z);

                        if (hasColor && colorData != null)
                        {
                            int cCol = srcCol * colorWidth / srcWidth;
                            int cRow = srcRow * colorHeight / srcHeight;
                            int ci = (cRow * colorWidth + cCol) * 3;
                            colors[dstIdx] = WpfColor.FromRgb(
                                colorData[ci],
                                colorData[ci + 1],
                                colorData[ci + 2]);
                        }
                        else
                        {
                            float safeZ = float.IsNaN(z) ? 0f : z;
                            float t = Math.Clamp(safeZ / 1000f, 0f, 1f);
                            byte r = (byte)(255 * Math.Clamp(1.5f - Math.Abs(t - 0.75f) * 4f, 0f, 1f));
                            byte g = (byte)(255 * Math.Clamp(1.5f - Math.Abs(t - 0.5f) * 4f, 0f, 1f));
                            byte b = (byte)(255 * Math.Clamp(1.5f - Math.Abs(t - 0.25f) * 4f, 0f, 1f));
                            colors[dstIdx] = WpfColor.FromRgb(r, g, b);
                        }
                    }
                });
            }

            return data;
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
