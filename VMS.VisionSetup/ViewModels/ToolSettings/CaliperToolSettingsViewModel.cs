using OpenCvSharp;
using VMS.VisionSetup.VisionTools.Measurement;

namespace VMS.VisionSetup.ViewModels.ToolSettings
{
    public class CaliperToolSettingsViewModel : ToolSettingsViewModelBase
    {
        private CaliperTool TypedTool => (CaliperTool)Tool;

        public CaliperToolSettingsViewModel(CaliperTool tool) : base(tool) { }

        public Point2d StartPoint { get => TypedTool.StartPoint; set => TypedTool.StartPoint = value; }
        public Point2d EndPoint { get => TypedTool.EndPoint; set => TypedTool.EndPoint = value; }
        public double SearchWidth { get => TypedTool.SearchWidth; set => TypedTool.SearchWidth = value; }
        public EdgePolarity Polarity { get => TypedTool.Polarity; set => TypedTool.Polarity = value; }
        public double EdgeThreshold { get => TypedTool.EdgeThreshold; set => TypedTool.EdgeThreshold = value; }
        public int FilterHalfWidth { get => TypedTool.FilterHalfWidth; set => TypedTool.FilterHalfWidth = value; }
        public CaliperMode Mode { get => TypedTool.Mode; set => TypedTool.Mode = value; }
        public double ExpectedWidth { get => TypedTool.ExpectedWidth; set => TypedTool.ExpectedWidth = value; }
        public double WidthTolerance { get => TypedTool.WidthTolerance; set => TypedTool.WidthTolerance = value; }
        public int MaxEdges { get => TypedTool.MaxEdges; set => TypedTool.MaxEdges = value; }
        public double ExpectedPosition { get => TypedTool.ExpectedPosition; set => TypedTool.ExpectedPosition = value; }
        public double ContrastWeight { get => TypedTool.ContrastWeight; set => TypedTool.ContrastWeight = value; }
        public double PositionWeight { get => TypedTool.PositionWeight; set => TypedTool.PositionWeight = value; }
        public double PositionSigma { get => TypedTool.PositionSigma; set => TypedTool.PositionSigma = value; }
        public double PolarityWeight { get => TypedTool.PolarityWeight; set => TypedTool.PolarityWeight = value; }
        public ScorerMode ScorerMode { get => TypedTool.ScorerMode; set => TypedTool.ScorerMode = value; }
        public double[]? LastProfile => TypedTool.LastProfile;
        public double[]? LastGradient => TypedTool.LastGradient;
    }
}
