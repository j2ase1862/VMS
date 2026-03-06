using OpenCvSharp;

namespace VMS.Camera.Models
{
    /// <summary>
    /// 이미지 획득 결과 (2D + optional 3D)
    /// </summary>
    public class AcquisitionResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;

        /// <summary>
        /// 2D 이미지 (항상 존재)
        /// </summary>
        public Mat? Image2D { get; set; }

        /// <summary>
        /// 3D 포인트 클라우드 (Mech-Mind 등 3D 카메라일 경우)
        /// </summary>
        public PointCloudData? PointCloud { get; set; }
    }
}
