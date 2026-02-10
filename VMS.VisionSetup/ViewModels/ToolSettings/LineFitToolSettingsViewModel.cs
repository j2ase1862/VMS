using OpenCvSharp;
using VMS.VisionSetup.VisionTools.Measurement;

namespace VMS.VisionSetup.ViewModels.ToolSettings
{
    public class LineFitToolSettingsViewModel : ToolSettingsViewModelBase
    {
        private LineFitTool TypedTool => (LineFitTool)Tool;

        public LineFitToolSettingsViewModel(LineFitTool tool) : base(tool) { }

        public Point2d StartPoint { get => TypedTool.StartPoint; set => TypedTool.StartPoint = value; }
        public Point2d EndPoint { get => TypedTool.EndPoint; set => TypedTool.EndPoint = value; }
        public int NumCalipers { get => TypedTool.NumCalipers; set => TypedTool.NumCalipers = value; }
        public double SearchLength { get => TypedTool.SearchLength; set => TypedTool.SearchLength = value; }
        public double SearchWidth { get => TypedTool.SearchWidth; set => TypedTool.SearchWidth = value; }
        public EdgePolarity Polarity { get => TypedTool.Polarity; set => TypedTool.Polarity = value; }
        public double EdgeThreshold { get => TypedTool.EdgeThreshold; set => TypedTool.EdgeThreshold = value; }
        public LineFitMethod FitMethod { get => TypedTool.FitMethod; set => TypedTool.FitMethod = value; }
        public double RansacThreshold { get => TypedTool.RansacThreshold; set => TypedTool.RansacThreshold = value; }
        public int MinFoundCalipers { get => TypedTool.MinFoundCalipers; set => TypedTool.MinFoundCalipers = value; }
    }
}
