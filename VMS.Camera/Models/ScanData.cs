using OpenCvSharp;

namespace VMS.Camera.Models
{
    /// <summary>
    /// 단일 촬영 데이터: 로봇 포즈 + 포인트 클라우드 + 2D 이미지를 1:1 페어링
    /// Phase 1 로드맵의 List&lt;Tuple&lt;PointCloud, RobotPose&gt;&gt; 구조를 객체화
    /// </summary>
    public class ScanData : IDisposable
    {
        private bool _disposed;

        /// <summary>촬영 인덱스 (0부터 시작)</summary>
        public int Index { get; set; }

        /// <summary>촬영 시점 타임스탬프</summary>
        public DateTime Timestamp { get; set; } = DateTime.Now;

        /// <summary>촬영 시점의 로봇 TCP 포즈</summary>
        public RobotPose Pose { get; set; } = new();

        /// <summary>3D 포인트 클라우드 (카메라 좌표계 기준)</summary>
        public PointCloudData? PointCloud { get; set; }

        /// <summary>2D 컬러/그레이 이미지</summary>
        public Mat? Image2D { get; set; }

        /// <summary>
        /// 월드 좌표계로 변환된 포인트 클라우드
        /// Phase 3에서 T_base_tcp × T_tcp_cam × P_cam 적용 후 저장
        /// </summary>
        public PointCloudData? TransformedPointCloud { get; set; }

        /// <summary>촬영 데이터 유효성</summary>
        public bool IsValid => PointCloud != null && PointCloud.PointCount > 0;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            PointCloud?.Dispose();
            TransformedPointCloud?.Dispose();
            Image2D?.Dispose();
        }

        public override string ToString()
            => $"Scan[{Index}] {Timestamp:HH:mm:ss.fff} {Pose} Points={PointCloud?.PointCount ?? 0}";
    }
}
