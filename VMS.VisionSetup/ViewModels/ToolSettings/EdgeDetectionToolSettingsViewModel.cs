using VMS.VisionSetup.VisionTools.ImageProcessing;

namespace VMS.VisionSetup.ViewModels.ToolSettings
{
    public class EdgeDetectionToolSettingsViewModel : ToolSettingsViewModelBase
    {
        private EdgeDetectionTool TypedTool => (EdgeDetectionTool)Tool;

        public EdgeDetectionToolSettingsViewModel(EdgeDetectionTool tool) : base(tool) { }

        public EdgeDetectionMethod Method { get => TypedTool.Method; set => TypedTool.Method = value; }
        public double CannyThreshold1 { get => TypedTool.CannyThreshold1; set => TypedTool.CannyThreshold1 = value; }
        public double CannyThreshold2 { get => TypedTool.CannyThreshold2; set => TypedTool.CannyThreshold2 = value; }
        public int CannyApertureSize { get => TypedTool.CannyApertureSize; set => TypedTool.CannyApertureSize = value; }
        public bool L2Gradient { get => TypedTool.L2Gradient; set => TypedTool.L2Gradient = value; }
        public int SobelKernelSize { get => TypedTool.SobelKernelSize; set => TypedTool.SobelKernelSize = value; }
        public int Dx { get => TypedTool.Dx; set => TypedTool.Dx = value; }
        public int Dy { get => TypedTool.Dy; set => TypedTool.Dy = value; }
    }
}
