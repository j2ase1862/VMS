using OpenCvSharp;
using VMS.VisionSetup.VisionTools.ImageProcessing;
using ThresholdType = VMS.VisionSetup.VisionTools.ImageProcessing.ThresholdType;

namespace VMS.VisionSetup.ViewModels.ToolSettings
{
    public class ThresholdToolSettingsViewModel : ToolSettingsViewModelBase
    {
        private ThresholdTool TypedTool => (ThresholdTool)Tool;

        public ThresholdToolSettingsViewModel(ThresholdTool tool) : base(tool) { }

        public double ThresholdValue { get => TypedTool.ThresholdValue; set => TypedTool.ThresholdValue = value; }
        public double MaxValue { get => TypedTool.MaxValue; set => TypedTool.MaxValue = value; }
        public ThresholdType ThresholdType { get => TypedTool.ThresholdType; set => TypedTool.ThresholdType = value; }
        public bool UseOtsu { get => TypedTool.UseOtsu; set => TypedTool.UseOtsu = value; }
        public bool UseAdaptive { get => TypedTool.UseAdaptive; set => TypedTool.UseAdaptive = value; }
        public AdaptiveThresholdTypes AdaptiveMethod { get => TypedTool.AdaptiveMethod; set => TypedTool.AdaptiveMethod = value; }
        public int BlockSize { get => TypedTool.BlockSize; set => TypedTool.BlockSize = value; }
        public double CValue { get => TypedTool.CValue; set => TypedTool.CValue = value; }
    }
}
