using CommunityToolkit.Mvvm.Messaging.Messages;
using VMS.PLC.Models.Sequence;

namespace VMS.VisionSetup.Models
{
    // View-level messages (handled by MainView.xaml.cs)
    public sealed class RequestDrawROIMessage
    {
        public bool UseAffine { get; }
        public bool UseCircle { get; }
        /// <summary>도넛형 Annulus ROI 그리기 (PolarUnwrapTool 등).</summary>
        public bool UseAnnulus { get; }

        public RequestDrawROIMessage(bool useAffine = false, bool useCircle = false, bool useAnnulus = false)
        {
            UseAffine = useAffine;
            UseCircle = useCircle;
            UseAnnulus = useAnnulus;
        }
    }
    public sealed class RequestClearROIMessage { }
    public sealed class RequestDrawSearchRegionMessage { }
    public sealed class RequestClearSearchRegionMessage { }
    /// <summary>FeatureMatch 학습 마스크(don't-care) 편집기 열기 요청 — 선택 모델의 템플릿 위에서 브러시/사각형으로 칠하고 재학습까지 수행 (MainViewModel 처리).</summary>
    public sealed class RequestEditTrainMaskMessage { }

    /// <summary>안정 특징 정제 — 샘플 이미지들을 골라 불안정 엣지를 자동 마스크 (FeatureMatch).</summary>
    public sealed class RequestRefineStableFeaturesMessage { }
    /// <summary>이미지에서 한 픽셀 픽 모드 활성화 요청. MainView가 ImageCanvas의 EditMode.PickPoint 전환.</summary>
    public sealed class RequestPickColorMessage { }

    // ViewModel-level messages (handled by MainViewModel)
    public sealed class RequestTrainPatternMessage { }
    public sealed class RequestAutoTuneMessage { }
    /// <summary>학습 당시 자동 저장된 전체 원본(기준) 이미지를 메인 화면으로 다시 불러오기 (FeatureMatch).</summary>
    public sealed class RequestLoadReferenceImageMessage { }

    /// <summary>
    /// Web 파라미터 캐시 갱신 알림 (ParameterSyncService.SyncCompleted/RecipeLoaded →
    /// App.xaml.cs 브리지가 발행). 열려 있는 툴 설정의 ParamCode 콤보가 이걸 받아 재구성.
    /// </summary>
    public sealed class WebParamCacheUpdatedMessage { }

    // ── 메인 화면이 쥔 카메라를 별도 창(캘리브레이션)이 빌려 쓰기 위한 요청 ──
    //
    // 산업용 카메라는 배타 점유로 열린다. 메인 화면이 연결해 둔 카메라를 별도 창이
    // 두 번째로 열면 실패해, 버튼을 눌러도 아무 일이 없는 것처럼 보인다
    // (2026-09-16 현장 보고: VisionSetup 연결 상태에서 Capture from Camera 무반응).
    // 두 번째 연결을 만들지 말고, 이미 연 쪽에 촬영을 부탁한다.

    /// <summary>메인 화면의 카메라 점유 상태.</summary>
    /// <param name="IsConnected">메인 화면이 지금 카메라에 연결되어 있는지.</param>
    /// <param name="CameraId">연결 중인 카메라 Id (미연결이면 빈 문자열).</param>
    /// <param name="CameraName">연결 중인 카메라 표시 이름 (안내 문구용).</param>
    public sealed record MainCameraOwnership(bool IsConnected, string CameraId, string CameraName)
    {
        public static MainCameraOwnership None { get; } = new(false, string.Empty, string.Empty);
    }

    /// <summary>메인 화면에 "지금 카메라를 쥐고 있나" 물어본다 (MainViewModel 응답).</summary>
    public sealed class MainCameraOwnershipRequestMessage : RequestMessage<MainCameraOwnership> { }

    /// <summary>메인 화면이 쥔 카메라로 한 장 촬영한 결과.</summary>
    /// <param name="Success">촬영 성공 여부.</param>
    /// <param name="Image">촬영 이미지 (성공 시에만, 소유권은 요청자에게 넘어간다).</param>
    /// <param name="Message">실패 사유 또는 상태 문구.</param>
    public sealed record MainCameraCaptureReply(bool Success, OpenCvSharp.Mat? Image, string Message);

    /// <summary>메인 화면이 쥔 카메라로 한 장 찍어 달라는 요청.</summary>
    public sealed class MainCameraCaptureRequestMessage
        : AsyncRequestMessage<MainCameraCaptureReply>
    {
        public MainCameraCaptureRequestMessage(string cameraId) => CameraId = cameraId;

        /// <summary>촬영을 원하는 카메라 Id. 메인 화면이 쥔 카메라와 다르면 거절된다.</summary>
        public string CameraId { get; }
    }

    // Sequence editor messages
    public sealed class SequenceConfigChangedMessage
    {
        public SequenceConfig Config { get; }

        public SequenceConfigChangedMessage(SequenceConfig config)
        {
            Config = config;
        }
    }
}
