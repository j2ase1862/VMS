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
    /// <summary>이미지에서 한 픽셀 픽 모드 활성화 요청. MainView가 ImageCanvas의 EditMode.PickPoint 전환.</summary>
    public sealed class RequestPickColorMessage { }

    // ViewModel-level messages (handled by MainViewModel)
    public sealed class RequestTrainPatternMessage { }
    public sealed class RequestAutoTuneMessage { }

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
