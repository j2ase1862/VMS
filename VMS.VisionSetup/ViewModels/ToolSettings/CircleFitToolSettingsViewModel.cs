using OpenCvSharp;
using VMS.VisionSetup.VisionTools.Measurement;

namespace VMS.VisionSetup.ViewModels.ToolSettings
{
    public class CircleFitToolSettingsViewModel : ToolSettingsViewModelBase
    {
        private CircleFitTool TypedTool => (CircleFitTool)Tool;

        public CircleFitToolSettingsViewModel(CircleFitTool tool) : base(tool) { }

        public Point2d CenterPoint { get => TypedTool.CenterPoint; set => TypedTool.CenterPoint = value; }
        public double ExpectedRadius { get => TypedTool.ExpectedRadius; set => TypedTool.ExpectedRadius = value; }
        public int NumCalipers { get => TypedTool.NumCalipers; set => TypedTool.NumCalipers = value; }
        public double SearchLength { get => TypedTool.SearchLength; set => TypedTool.SearchLength = value; }
        public double SearchWidth { get => TypedTool.SearchWidth; set => TypedTool.SearchWidth = value; }
        public double StartAngle { get => TypedTool.StartAngle; set => TypedTool.StartAngle = value; }
        public double EndAngle { get => TypedTool.EndAngle; set => TypedTool.EndAngle = value; }
        public EdgePolarity Polarity { get => TypedTool.Polarity; set => TypedTool.Polarity = value; }
        public double EdgeThreshold { get => TypedTool.EdgeThreshold; set => TypedTool.EdgeThreshold = value; }
        public CircleFitMethod FitMethod { get => TypedTool.FitMethod; set => TypedTool.FitMethod = value; }
        public double RansacThreshold { get => TypedTool.RansacThreshold; set => TypedTool.RansacThreshold = value; }
        public int MinFoundCalipers { get => TypedTool.MinFoundCalipers; set => TypedTool.MinFoundCalipers = value; }
    }
}
