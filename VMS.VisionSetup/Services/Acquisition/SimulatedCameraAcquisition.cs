using OpenCvSharp;
using System.Numerics;
using VMS.VisionSetup.Interfaces;
using VMS.VisionSetup.Models;

namespace VMS.VisionSetup.Services.Acquisition
{
    /// <summary>
    /// 시뮬레이션 카메라 - 개발/테스트용 테스트 패턴 이미지 및 샘플 포인트 클라우드 생성
    /// </summary>
    public class SimulatedCameraAcquisition : ICameraAcquisition
    {
        private CameraInfo? _camera;
        private bool _disposed;

        public bool IsConnected { get; private set; }

        public Task<bool> ConnectAsync(CameraInfo camera)
        {
            _camera = camera;
            IsConnected = true;
            return Task.FromResult(true);
        }

        public Task DisconnectAsync()
        {
            IsConnected = false;
            return Task.CompletedTask;
        }

        public Task<AcquisitionResult> AcquireAsync()
        {
            if (!IsConnected || _camera == null)
            {
                return Task.FromResult(new AcquisitionResult
                {
                    Success = false,
                    Message = "카메라가 연결되지 않았습니다."
                });
            }

            var result = new AcquisitionResult();

            // 2D 테스트 패턴 이미지 생성
            result.Image2D = GenerateTestPattern(_camera.Width, _camera.Height);

            // 3D 카메라인 경우 포인트 클라우드도 생성
            if (Is3DCamera(_camera))
            {
                result.PointCloud = GenerateSamplePointCloud();
            }

            result.Success = true;
            result.Message = Is3DCamera(_camera)
                ? "시뮬레이션 2D+3D 획득 완료"
                : "시뮬레이션 2D 획득 완료";

            return Task.FromResult(result);
        }

        private static bool Is3DCamera(CameraInfo camera)
        {
            return camera.Manufacturer.Contains("Mech", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 테스트 패턴 이미지 생성 (컬러 그리드 + 텍스트)
        /// </summary>
        private static Mat GenerateTestPattern(int width, int height)
        {
            var image = new Mat(height, width, MatType.CV_8UC3, Scalar.All(40));

            // 그리드 패턴
            int gridSize = 80;
            for (int y = 0; y < height; y += gridSize)
            {
                Cv2.Line(image, new Point(0, y), new Point(width, y), new Scalar(80, 80, 80), 1);
            }
            for (int x = 0; x < width; x += gridSize)
            {
                Cv2.Line(image, new Point(x, 0), new Point(x, height), new Scalar(80, 80, 80), 1);
            }

            // 컬러 사각형
            int rectSize = Math.Min(width, height) / 6;
            int cx = width / 2, cy = height / 2;

            Cv2.Rectangle(image,
                new Point(cx - rectSize * 2, cy - rectSize),
                new Point(cx - rectSize, cy + rectSize),
                new Scalar(0, 0, 255), -1); // Red

            Cv2.Rectangle(image,
                new Point(cx - rectSize / 2, cy - rectSize),
                new Point(cx + rectSize / 2, cy + rectSize),
                new Scalar(0, 255, 0), -1); // Green

            Cv2.Rectangle(image,
                new Point(cx + rectSize, cy - rectSize),
                new Point(cx + rectSize * 2, cy + rectSize),
                new Scalar(255, 0, 0), -1); // Blue

            // 원형
            Cv2.Circle(image, new Point(cx, cy - rectSize * 2), rectSize / 2,
                new Scalar(0, 255, 255), 2); // Yellow circle

            // 텍스트
            Cv2.PutText(image, "SIMULATED", new Point(20, 40),
                HersheyFonts.HersheySimplex, 1.0, new Scalar(200, 200, 200), 2);
            Cv2.PutText(image, $"{width}x{height}", new Point(20, 80),
                HersheyFonts.HersheySimplex, 0.7, new Scalar(150, 150, 150), 1);
            Cv2.PutText(image, DateTime.Now.ToString("HH:mm:ss.fff"), new Point(20, height - 20),
                HersheyFonts.HersheySimplex, 0.6, new Scalar(100, 200, 100), 1);

            return image;
        }

        /// <summary>
        /// 샘플 3D 포인트 클라우드 생성 (평면 + 돌출부)
        /// </summary>
        private static PointCloudData GenerateSamplePointCloud()
        {
            const int count = 30000;
            var rng = new Random(DateTime.Now.Millisecond);
            var positions = new Vector3[count];
            var colors = new System.Windows.Media.Color[count];

            for (int i = 0; i < count; i++)
            {
                float x = (float)(rng.NextDouble() * 200 - 100);
                float z = (float)(rng.NextDouble() * 200 - 100);
                float y;

                // 중앙에 돌출된 가우시안 형태
                float dist = MathF.Sqrt(x * x + z * z);
                if (dist < 40)
                {
                    y = 30f * MathF.Exp(-dist * dist / 800f);
                }
                else
                {
                    y = (float)(rng.NextDouble() * 2 - 1); // 평면 노이즈
                }

                positions[i] = new Vector3(x, y, z);

                // 높이 기반 색상
                float t = Math.Clamp(y / 30f, 0f, 1f);
                byte red = (byte)(255 * t);
                byte green = (byte)(255 * (1 - Math.Abs(t - 0.5f) * 2));
                byte blue = (byte)(255 * (1 - t));
                colors[i] = System.Windows.Media.Color.FromRgb(red, green, blue);
            }

            return new PointCloudData
            {
                Name = "Simulated 3D Scan",
                Positions = positions,
                Colors = colors
            };
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                IsConnected = false;
            }
        }
    }
}
