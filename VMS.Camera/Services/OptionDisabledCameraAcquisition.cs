using VMS.Camera.Interfaces;
using VMS.Camera.Models;

namespace VMS.Camera.Services
{
    /// <summary>
    /// 라이선스 확장 옵션이 꺼진 카메라 종류 — 연결을 거절만 한다.
    /// 시뮬레이션으로 대신 붙이면 "카메라가 연결됐다"로 보이므로 쓰지 않는다.
    /// </summary>
    public sealed class OptionDisabledCameraAcquisition : ICameraAcquisition
    {
        public OptionDisabledCameraAcquisition(string reason) => Reason = reason;

        /// <summary>사용자에게 보일 거절 사유.</summary>
        public string Reason { get; }

        public bool IsConnected => false;

        public Task<bool> ConnectAsync(CameraInfo camera) => Task.FromResult(false);

        public Task DisconnectAsync() => Task.CompletedTask;

        public Task<AcquisitionResult> AcquireAsync(int timeoutMs = 5000) =>
            Task.FromResult(new AcquisitionResult { Success = false, Message = Reason });

        public void Dispose() { }
    }
}
