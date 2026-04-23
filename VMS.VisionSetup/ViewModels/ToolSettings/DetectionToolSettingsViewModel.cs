using System.Collections.ObjectModel;
using VMS.VisionSetup.VisionTools.DeepLearning;

namespace VMS.VisionSetup.ViewModels.ToolSettings
{
    public class DetectionToolSettingsViewModel : ToolSettingsViewModelBase
    {
        private DetectionTool TypedTool => (DetectionTool)Tool;

        public DetectionToolSettingsViewModel(DetectionTool tool) : base(tool) { }

        // Model
        public string ModelPath { get => TypedTool.ModelPath; set => TypedTool.ModelPath = value; }
        public int InputSize { get => TypedTool.InputSize; set => TypedTool.InputSize = value; }

        // Detection
        public double ConfidenceThreshold { get => TypedTool.ConfidenceThreshold; set => TypedTool.ConfidenceThreshold = value; }
        public double IouThreshold { get => TypedTool.IouThreshold; set => TypedTool.IouThreshold = value; }
        public string ClassNamesText { get => TypedTool.ClassNamesText; set => TypedTool.ClassNamesText = value; }
        public ObservableCollection<string> ModelClassNames => TypedTool.ModelClassNames;

        // Per-class thresholds
        public bool UsePerClassThresholds { get => TypedTool.UsePerClassThresholds; set => TypedTool.UsePerClassThresholds = value; }
        public ObservableCollection<ClassThresholdEntry> ClassThresholds => TypedTool.ClassThresholds;

        // CLAHE 전처리
        public bool UseClahe { get => TypedTool.UseClahe; set => TypedTool.UseClahe = value; }
        public double ClaheClipLimit { get => TypedTool.ClaheClipLimit; set => TypedTool.ClaheClipLimit = value; }
        public int ClaheTileGridSize { get => TypedTool.ClaheTileGridSize; set => TypedTool.ClaheTileGridSize = value; }

        // SAHI (Slicing Aided Hyper Inference)
        public bool UseSahi { get => TypedTool.UseSahi; set => TypedTool.UseSahi = value; }
        public int SahiTileSize { get => TypedTool.SahiTileSize; set => TypedTool.SahiTileSize = value; }
        public double SahiOverlapRatio { get => TypedTool.SahiOverlapRatio; set => TypedTool.SahiOverlapRatio = value; }

        // Dot Cluster Analysis
        public bool UseDotAnalysis { get => TypedTool.UseDotAnalysis; set => TypedTool.UseDotAnalysis = value; }
        public DotDetectionMethod DotDetectionMethod { get => TypedTool.DotDetectionMethod; set => TypedTool.DotDetectionMethod = value; }
        public int MinDotArea { get => TypedTool.MinDotArea; set => TypedTool.MinDotArea = value; }
        public int MaxDotArea { get => TypedTool.MaxDotArea; set => TypedTool.MaxDotArea = value; }
        public double DotCircularityThreshold { get => TypedTool.DotCircularityThreshold; set => TypedTool.DotCircularityThreshold = value; }
        public int MinDotDistance { get => TypedTool.MinDotDistance; set => TypedTool.MinDotDistance = value; }
        public DotPatternMetric DotPatternMetric { get => TypedTool.DotPatternMetric; set => TypedTool.DotPatternMetric = value; }
        public DotPreprocessMode DotPreprocessMode { get => TypedTool.DotPreprocessMode; set => TypedTool.DotPreprocessMode = value; }
        public double DotClaheClipLimit { get => TypedTool.DotClaheClipLimit; set => TypedTool.DotClaheClipLimit = value; }
        public int DotMorphKernelSize { get => TypedTool.DotMorphKernelSize; set => TypedTool.DotMorphKernelSize = value; }
        public DotThresholdMode DotThresholdMode { get => TypedTool.DotThresholdMode; set => TypedTool.DotThresholdMode = value; }
        public int DotAdaptiveBlockSize { get => TypedTool.DotAdaptiveBlockSize; set => TypedTool.DotAdaptiveBlockSize = value; }
        public double DotAdaptiveC { get => TypedTool.DotAdaptiveC; set => TypedTool.DotAdaptiveC = value; }
        public ObservableCollection<DotClusterExpectation> DotExpectations => TypedTool.DotExpectations;
        public System.Array DotDetectionMethodValues => System.Enum.GetValues(typeof(DotDetectionMethod));
        public System.Array DotPatternMetricValues => System.Enum.GetValues(typeof(DotPatternMetric));
        public System.Array DotPreprocessModeValues => System.Enum.GetValues(typeof(DotPreprocessMode));
        public System.Array DotThresholdModeValues => System.Enum.GetValues(typeof(DotThresholdMode));

        // Display
        public bool DrawOverlay { get => TypedTool.DrawOverlay; set => TypedTool.DrawOverlay = value; }
    }
}
