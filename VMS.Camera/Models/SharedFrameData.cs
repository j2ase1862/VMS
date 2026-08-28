using OpenCvSharp;

namespace VMS.Camera.Models
{
    /// <summary>
    /// SharedFrameReader가 반환하는 프레임 데이터 POCO (deep copy)
    /// </summary>
    public class SharedFrameData
    {
        public long FrameCounter { get; set; }
        public long TimestampTicks { get; set; }
        public Mat? Image2D { get; set; }
        public PointCloudData? PointCloud { get; set; }

        /// <summary>
        /// 이 프레임을 만든 카메라의 식별자 (VisionSetup CameraInfo.Id 와 동일 체계).
        /// 빈 문자열이면 송신 측이 식별자를 싣지 않은 것 — "모름"이며, 특정 카메라를
        /// 요청한 수신 측은 이를 일치로 간주하면 안 된다.
        /// </summary>
        public string CameraId { get; set; } = string.Empty;
    }
}
