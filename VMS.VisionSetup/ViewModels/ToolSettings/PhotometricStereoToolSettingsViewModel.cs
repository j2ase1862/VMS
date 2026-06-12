using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using VMS.VisionSetup.VisionTools.SurfaceAnalysis;

namespace VMS.VisionSetup.ViewModels.ToolSettings
{
    public class PhotometricStereoToolSettingsViewModel : ToolSettingsViewModelBase
    {
        private PhotometricStereoTool TypedTool => (PhotometricStereoTool)Tool;

        public PhotometricStereoToolSettingsViewModel(PhotometricStereoTool tool) : base(tool)
        {
            AddLightCommand = new RelayCommand(AddLight);
            RemoveLightCommand = new RelayCommand<LightSample>(RemoveLight);
        }

        public ObservableCollection<LightSample> Lights => TypedTool.Lights;

        public PsOutputType OutputType { get => TypedTool.OutputType; set => TypedTool.OutputType = value; }
        public double CurvatureGain { get => TypedTool.CurvatureGain; set => TypedTool.CurvatureGain = value; }
        public int ShadowThreshold { get => TypedTool.ShadowThreshold; set => TypedTool.ShadowThreshold = value; }
        public int HighlightThreshold { get => TypedTool.HighlightThreshold; set => TypedTool.HighlightThreshold = value; }

        public IRelayCommand AddLightCommand { get; }
        public IRelayCommand<LightSample> RemoveLightCommand { get; }

        // 파일 탐색은 XAML의 TextBoxParameter(BrowseFilter) 컨트롤이 UI 영역에서 처리.
        private void AddLight() => Lights.Add(new LightSample { Lx = 0, Ly = 0, Lz = 1 });

        private void RemoveLight(LightSample? light)
        {
            if (light != null) Lights.Remove(light);
        }
    }
}
