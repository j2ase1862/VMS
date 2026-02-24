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

        Task<AcquisitionResult> AcquireAsync();
    }
}
