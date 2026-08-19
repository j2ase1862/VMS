using VMS.Camera.Models;

namespace VMS.Camera.Interfaces
{
    /// <summary>
    /// 카메라 연결 및 이미지 획득 인터페이스
    /// </summary>
    public interface ICameraAcquisition : IDisposable
    {
        bool IsConnected { get; }

        /// <summary>
        /// 런타임 연결 끊김 통지 (케이블 분리·하트비트 타임아웃 등) — 인자는 사유 문자열.
        /// 지원 구현체(Basler 등)만 발생시키고 IsConnected 도 함께 내린다.
        /// 기본 구현은 이벤트 미발생 (no-op) — 끊김을 감지 못하는 SDK 는 grab 실패로만 드러난다.
        /// </summary>
        event EventHandler<string>? ConnectionLost { add { } remove { } }

        Task<bool> ConnectAsync(CameraInfo camera);

        Task DisconnectAsync();

        Task<AcquisitionResult> AcquireAsync(int timeoutMs = 5000);

        /// <summary>
        /// 노출/게인을 카메라에 적용. exposureUs 는 µs 단위 (레시피 스텝 설정과 동일).
        /// 지원하는 구현체만 override — 기본은 no-op (false = 미지원/미적용).
        /// </summary>
        Task<bool> ApplySettingsAsync(double exposureUs, double gain) => Task.FromResult(false);

        /// <summary>
        /// 카메라의 현재 2D 노출(µs)/게인을 읽는다 (read-back) — UI 에 실기기 값을
        /// 표시하기 위한 용도. 지원하는 구현체만 override — 기본은 null (미지원).
        /// </summary>
        Task<CameraSettings2D?> ReadSettingsAsync() => Task.FromResult<CameraSettings2D?>(null);

        /// <summary>
        /// 3D 스캔 후처리(스무딩/노이즈 제거)·뎁스 범위를 카메라에 적용 — 촬영 시점
        /// 카메라 내부 처리라 depth map/점군/후속 도구가 모두 정제된 데이터를 받는다.
        /// 지원하는 구현체(Mech-Mind 등)만 override — 기본은 no-op.
        /// </summary>
        Task<bool> Apply3DSettingsAsync(Scan3DSettings settings) => Task.FromResult(false);

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
