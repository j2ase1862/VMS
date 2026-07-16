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
        /// 노출/게인을 카메라에 적용. exposureUs 는 µs 단위 (레시피 스텝 설정과 동일).
        /// 지원하는 구현체만 override — 기본은 no-op (false = 미지원/미적용).
        /// </summary>
        Task<bool> ApplySettingsAsync(double exposureUs, double gain) => Task.FromResult(false);

        /// <summary>
        /// Live 모드 다운샘플링 스트라이드 (1 = 전체 해상도, 2 = 1/4)
        /// </summary>
        int DownsampleStride { get => 1; set { } }

        /// <summary>
        /// 연속 획득 시작 (MdigProcess 등 하드웨어 연속 그랩)
        /// 지원하지 않는 구현체는 기본 no-op
        /// </summary>
        void StartProcessing() { }

        /// <summary>
        /// 연속 획득 중지
        /// </summary>
        void StopProcessing() { }
    }
}
