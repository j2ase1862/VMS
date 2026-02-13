using OpenCvSharp;
using VMS.VisionSetup.Models;

namespace VMS.VisionSetup.Interfaces
{
    /// <summary>
    /// 카메라 연결 및 이미지 획득 인터페이스
    /// </summary>
    public interface ICameraAcquisition : IDisposable
    {
        bool IsConnected { get; }

        Task<bool> ConnectAsync(CameraInfo camera);

        Task DisconnectAsync();

        Task<AcquisitionResult> AcquireAsync();
    }

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
