using OpenCvSharp;
using System;
using VMS.VisionSetup.VisionTools.BlobAnalysis;

namespace VMS.VisionSetup.ViewModels.ToolSettings
{
    public class BlobToolSettingsViewModel : ToolSettingsViewModelBase
    {
        private BlobTool TypedTool => (BlobTool)Tool;

        public BlobToolSettingsViewModel(BlobTool tool) : base(tool) { }

        // Segmentation
        public bool UseInternalThreshold { get => TypedTool.UseInternalThreshold; set => TypedTool.UseInternalThreshold = value; }
        public double ThresholdValue { get => TypedTool.ThresholdValue; set => TypedTool.ThresholdValue = value; }
        public SegmentationPolarity SegmentationPolarity { get => TypedTool.SegmentationPolarity; set => TypedTool.SegmentationPolarity = value; }

        // Area Filter
        public double MinArea { get => TypedTool.MinArea; set => TypedTool.MinArea = value; }
        public double MaxArea { get => TypedTool.MaxArea; set => TypedTool.MaxArea = value; }

        // Shape Filter
        public double MinCircularity { get => TypedTool.MinCircularity; set => TypedTool.MinCircularity = value; }
        public double MaxCircularity { get => TypedTool.MaxCircularity; set => TypedTool.MaxCircularity = value; }
        public double MinPerimeter { get => TypedTool.MinPerimeter; set => TypedTool.MinPerimeter = value; }
        public double MaxPerimeter { get => TypedTool.MaxPerimeter; set => TypedTool.MaxPerimeter = value; }
        public double MinAspectRatio { get => TypedTool.MinAspectRatio; set => TypedTool.MinAspectRatio = value; }
        public double MaxAspectRatio { get => TypedTool.MaxAspectRatio; set => TypedTool.MaxAspectRatio = value; }
        public double MinConvexity { get => TypedTool.MinConvexity; set => TypedTool.MinConvexity = value; }

        // Sort & Limit
        public BlobSortBy SortBy { get => TypedTool.SortBy; set => TypedTool.SortBy = value; }
        public bool SortDescending { get => TypedTool.SortDescending; set => TypedTool.SortDescending = value; }
        public int MaxBlobCount { get => TypedTool.MaxBlobCount; set => TypedTool.MaxBlobCount = value; }

        // Advanced
        public RetrievalModes RetrievalMode { get => TypedTool.RetrievalMode; set => TypedTool.RetrievalMode = value; }
        public ContourApproximationModes ApproximationMode { get => TypedTool.ApproximationMode; set => TypedTool.ApproximationMode = value; }

        // Display
        public bool DrawContours { get => TypedTool.DrawContours; set => TypedTool.DrawContours = value; }
        public bool DrawBoundingBox { get => TypedTool.DrawBoundingBox; set => TypedTool.DrawBoundingBox = value; }
        public bool DrawCenterPoint { get => TypedTool.DrawCenterPoint; set => TypedTool.DrawCenterPoint = value; }
        public bool DrawLabels { get => TypedTool.DrawLabels; set => TypedTool.DrawLabels = value; }

        // Judgment
        public bool EnableJudgment { get => TypedTool.EnableJudgment; set => TypedTool.EnableJudgment = value; }
        public bool UseAreaJudgment { get => TypedTool.UseAreaJudgment; set => TypedTool.UseAreaJudgment = value; }
        public double ExpectedArea { get => TypedTool.ExpectedArea; set => TypedTool.ExpectedArea = value; }
        public double AreaTolerancePlus { get => TypedTool.AreaTolerancePlus; set => TypedTool.AreaTolerancePlus = value; }
        public double AreaToleranceMinus { get => TypedTool.AreaToleranceMinus; set => TypedTool.AreaToleranceMinus = value; }
        public bool UseCountJudgment { get => TypedTool.UseCountJudgment; set => TypedTool.UseCountJudgment = value; }
        public CountJudgmentMode CountMode { get => TypedTool.CountMode; set => TypedTool.CountMode = value; }
        public int ExpectedCount { get => TypedTool.ExpectedCount; set => TypedTool.ExpectedCount = value; }
        public int ExpectedCountMax { get => TypedTool.ExpectedCountMax; set => TypedTool.ExpectedCountMax = value; }

        // Enum value arrays for ComboBox binding
        public Array SegmentationPolarities => Enum.GetValues(typeof(SegmentationPolarity));
        public Array SortByValues => Enum.GetValues(typeof(BlobSortBy));
        public Array CountModes => Enum.GetValues(typeof(CountJudgmentMode));
    }
}
