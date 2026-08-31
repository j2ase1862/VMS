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
