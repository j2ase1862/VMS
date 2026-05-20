using System;
using VMS.VisionSetup.VisionTools.ImageProcessing;

namespace VMS.VisionSetup.ViewModels.ToolSettings
{
    public class ImageEnhanceToolSettingsViewModel : ToolSettingsViewModelBase
    {
        private ImageEnhanceTool TypedTool => (ImageEnhanceTool)Tool;

        public ImageEnhanceToolSettingsViewModel(ImageEnhanceTool tool) : base(tool) { }

        public ImageEnhanceMode Mode
        {
            get => TypedTool.Mode;
            set { TypedTool.Mode = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsUnsharpMask)); }
        }
        public double Amount { get => TypedTool.Amount; set { TypedTool.Amount = value; OnPropertyChanged(); } }
        public int BlurKernelSize { get => TypedTool.BlurKernelSize; set { TypedTool.BlurKernelSize = value; OnPropertyChanged(); } }
        public int Threshold { get => TypedTool.Threshold; set { TypedTool.Threshold = value; OnPropertyChanged(); } }

        public bool IsUnsharpMask => Mode == ImageEnhanceMode.UnsharpMask;
    }
}
