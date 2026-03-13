using VMS.Camera.Models;

namespace VMS.Camera.Interfaces
{
    /// <summary>
    /// 카메라 연결 및 이미지 획득 인터페이스
    /// </summary>
    public interface ICameraAcquisition : IDisposable
    {
        bool IsConnected { get; }

        Task<bool> ConnectAsync(CameraInfo camera);

        Task DisconnectAsync();

        Task<AcquisitionResult> AcquireAsync(int timeoutMs = 5000);

        /// <summary>
        /// Live 모드 다운샘플링 스트라이드 (1 = 전체 해상도, 2 = 1/4)
        /// </summary>
        int DownsampleStride { get => 1; set { } }
    }
}
