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
    }
}
