using CommunityToolkit.Mvvm.Messaging.Messages;
using VMS.PLC.Models.Sequence;

namespace VMS.VisionSetup.Models
{
    // View-level messages (handled by MainView.xaml.cs)
    public sealed class RequestDrawROIMessage
    {
        /// <summary>
        /// 회전 가능한 Affine ROI를 그릴지 여부 (측정 도구용)
        /// </summary>
        public bool UseAffine { get; }

        /// <summary>
        /// 원형 ROI를 그릴지 여부 (CircleFitTool용)
        /// </summary>
        public bool UseCircle { get; }

        public RequestDrawROIMessage(bool useAffine = false, bool useCircle = false)
        {
            UseAffine = useAffine;
            UseCircle = useCircle;
        }
    }
    public sealed class RequestClearROIMessage { }
    public sealed class RequestDrawSearchRegionMessage { }
    public sealed class RequestClearSearchRegionMessage { }

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
