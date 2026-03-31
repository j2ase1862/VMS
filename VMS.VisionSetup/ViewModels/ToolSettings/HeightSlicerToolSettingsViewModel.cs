using VMS.VisionSetup.Models;
using VMS.VisionSetup.VisionTools.ImageProcessing;

namespace VMS.VisionSetup.ViewModels.ToolSettings
{
    public class HeightSlicerToolSettingsViewModel : ToolSettingsViewModelBase
    {
        private HeightSlicerTool TypedTool => (HeightSlicerTool)Tool;

        public HeightSlicerToolSettingsViewModel(HeightSlicerTool tool) : base(tool)
        {
            LoadAvailableParamCodes();
        }

        // Web ParamCode Links
        public ParamCodeItem? SelectedMinZCode
        {
            get => GetLinkedParamCodeItem(nameof(MinZ));
            set { SetLinkedParamCode(nameof(MinZ), value); OnPropertyChanged(); }
        }
        public ParamCodeItem? SelectedMaxZCode
        {
            get => GetLinkedParamCodeItem(nameof(MaxZ));
            set { SetLinkedParamCode(nameof(MaxZ), value); OnPropertyChanged(); }
        }

        public float MinZ { get => TypedTool.MinZ; set => TypedTool.MinZ = value; }
        public float MaxZ { get => TypedTool.MaxZ; set => TypedTool.MaxZ = value; }
    }
}
