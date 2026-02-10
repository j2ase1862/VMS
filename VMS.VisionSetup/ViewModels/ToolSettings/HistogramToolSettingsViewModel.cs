using VMS.VisionSetup.VisionTools.ImageProcessing;

namespace VMS.VisionSetup.ViewModels.ToolSettings
{
    public class HistogramToolSettingsViewModel : ToolSettingsViewModelBase
    {
        private HistogramTool TypedTool => (HistogramTool)Tool;

        public HistogramToolSettingsViewModel(HistogramTool tool) : base(tool) { }

        public HistogramOperation Operation { get => TypedTool.Operation; set => TypedTool.Operation = value; }
        public double ClipLimit { get => TypedTool.ClipLimit; set => TypedTool.ClipLimit = value; }
        public int TileGridWidth { get => TypedTool.TileGridWidth; set => TypedTool.TileGridWidth = value; }
        public int TileGridHeight { get => TypedTool.TileGridHeight; set => TypedTool.TileGridHeight = value; }
    }
}
