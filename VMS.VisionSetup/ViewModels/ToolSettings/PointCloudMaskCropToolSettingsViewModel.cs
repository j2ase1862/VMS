using VMS.VisionSetup.VisionTools.PointCloud;

namespace VMS.VisionSetup.ViewModels.ToolSettings
{
    public class PointCloudMaskCropToolSettingsViewModel : ToolSettingsViewModelBase
    {
        private PointCloudMaskCropTool TypedTool => (PointCloudMaskCropTool)Tool;

        public PointCloudMaskCropToolSettingsViewModel(PointCloudMaskCropTool tool) : base(tool)
        {
            LoadAvailableParamCodes();
        }

        public bool InvertMask { get => TypedTool.InvertMask; set => TypedTool.InvertMask = value; }
        public int MinMaskValue { get => TypedTool.MinMaskValue; set => TypedTool.MinMaskValue = value; }
        public int DilatePixels { get => TypedTool.DilatePixels; set => TypedTool.DilatePixels = value; }
        public bool SkipInvalidZ { get => TypedTool.SkipInvalidZ; set => TypedTool.SkipInvalidZ = value; }
        public PointCloudMaskCropTool.CropCombineMode CombineMode
        {
            get => TypedTool.CombineMode;
            set => TypedTool.CombineMode = value;
        }
    }
}
