using VMS.VisionSetup.VisionTools.ImageProcessing;

namespace VMS.VisionSetup.ViewModels.ToolSettings
{
    public class BlurToolSettingsViewModel : ToolSettingsViewModelBase
    {
        private BlurTool TypedTool => (BlurTool)Tool;

        public BlurToolSettingsViewModel(BlurTool tool) : base(tool) { }

        public BlurType BlurType { get => TypedTool.BlurType; set => TypedTool.BlurType = value; }
        public int KernelSize { get => TypedTool.KernelSize; set => TypedTool.KernelSize = value; }
        public double SigmaX { get => TypedTool.SigmaX; set => TypedTool.SigmaX = value; }
        public double SigmaY { get => TypedTool.SigmaY; set => TypedTool.SigmaY = value; }
        public double SigmaColor { get => TypedTool.SigmaColor; set => TypedTool.SigmaColor = value; }
        public double SigmaSpace { get => TypedTool.SigmaSpace; set => TypedTool.SigmaSpace = value; }
    }
}
