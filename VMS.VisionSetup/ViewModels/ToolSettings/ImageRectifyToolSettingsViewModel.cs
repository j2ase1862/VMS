using VMS.VisionSetup.VisionTools.Calibration;

namespace VMS.VisionSetup.ViewModels.ToolSettings
{
    public class ImageRectifyToolSettingsViewModel : ToolSettingsViewModelBase
    {
        private ImageRectifyTool TypedTool => (ImageRectifyTool)Tool;

        public ImageRectifyToolSettingsViewModel(ImageRectifyTool tool) : base(tool)
        {
            LoadAvailableParamCodes();
        }

        public bool Undistort
        {
            get => TypedTool.Undistort;
            set => TypedTool.Undistort = value;
        }

        public bool ApplyHomography
        {
            get => TypedTool.ApplyHomography;
            set => TypedTool.ApplyHomography = value;
        }
    }
}
