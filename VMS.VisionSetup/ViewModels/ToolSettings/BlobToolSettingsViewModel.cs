using OpenCvSharp;
using VMS.VisionSetup.VisionTools.BlobAnalysis;

namespace VMS.VisionSetup.ViewModels.ToolSettings
{
    public class BlobToolSettingsViewModel : ToolSettingsViewModelBase
    {
        private BlobTool TypedTool => (BlobTool)Tool;

        public BlobToolSettingsViewModel(BlobTool tool) : base(tool) { }

        public bool UseInternalThreshold { get => TypedTool.UseInternalThreshold; set => TypedTool.UseInternalThreshold = value; }
        public double ThresholdValue { get => TypedTool.ThresholdValue; set => TypedTool.ThresholdValue = value; }
        public bool InvertPolarity { get => TypedTool.InvertPolarity; set => TypedTool.InvertPolarity = value; }
        public double MinArea { get => TypedTool.MinArea; set => TypedTool.MinArea = value; }
        public double MaxArea { get => TypedTool.MaxArea; set => TypedTool.MaxArea = value; }
        public double MinPerimeter { get => TypedTool.MinPerimeter; set => TypedTool.MinPerimeter = value; }
        public double MaxPerimeter { get => TypedTool.MaxPerimeter; set => TypedTool.MaxPerimeter = value; }
        public double MinCircularity { get => TypedTool.MinCircularity; set => TypedTool.MinCircularity = value; }
        public double MaxCircularity { get => TypedTool.MaxCircularity; set => TypedTool.MaxCircularity = value; }
        public double MinAspectRatio { get => TypedTool.MinAspectRatio; set => TypedTool.MinAspectRatio = value; }
        public double MaxAspectRatio { get => TypedTool.MaxAspectRatio; set => TypedTool.MaxAspectRatio = value; }
        public double MinConvexity { get => TypedTool.MinConvexity; set => TypedTool.MinConvexity = value; }
        public int MaxBlobCount { get => TypedTool.MaxBlobCount; set => TypedTool.MaxBlobCount = value; }
        public BlobSortBy SortBy { get => TypedTool.SortBy; set => TypedTool.SortBy = value; }
        public bool SortDescending { get => TypedTool.SortDescending; set => TypedTool.SortDescending = value; }
        public RetrievalModes RetrievalMode { get => TypedTool.RetrievalMode; set => TypedTool.RetrievalMode = value; }
        public ContourApproximationModes ApproximationMode { get => TypedTool.ApproximationMode; set => TypedTool.ApproximationMode = value; }
        public bool DrawContours { get => TypedTool.DrawContours; set => TypedTool.DrawContours = value; }
        public bool DrawBoundingBox { get => TypedTool.DrawBoundingBox; set => TypedTool.DrawBoundingBox = value; }
        public bool DrawCenterPoint { get => TypedTool.DrawCenterPoint; set => TypedTool.DrawCenterPoint = value; }
        public bool DrawLabels { get => TypedTool.DrawLabels; set => TypedTool.DrawLabels = value; }
    }
}
