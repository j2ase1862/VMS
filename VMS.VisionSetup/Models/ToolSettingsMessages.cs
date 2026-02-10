using CommunityToolkit.Mvvm.Messaging.Messages;

namespace VMS.VisionSetup.Models
{
    // View-level messages (handled by MainView.xaml.cs)
    public sealed class RequestDrawROIMessage { }
    public sealed class RequestClearROIMessage { }
    public sealed class RequestDrawSearchRegionMessage { }
    public sealed class RequestClearSearchRegionMessage { }

    // ViewModel-level messages (handled by MainViewModel)
    public sealed class RequestTrainPatternMessage { }
    public sealed class RequestAutoTuneMessage { }
}
