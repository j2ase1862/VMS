using VMS.VisionSetup.ViewModels.ToolSettings;
using System.Windows;
using System.Windows.Controls;

namespace VMS.VisionSetup.Views.ToolSettings
{
    public class ToolSettingsTemplateSelector : DataTemplateSelector
    {
        public DataTemplate? GrayscaleTemplate { get; set; }
        public DataTemplate? BlurTemplate { get; set; }
        public DataTemplate? ThresholdTemplate { get; set; }
        public DataTemplate? EdgeDetectionTemplate { get; set; }
        public DataTemplate? MorphologyTemplate { get; set; }
        public DataTemplate? HistogramTemplate { get; set; }
        public DataTemplate? FeatureMatchTemplate { get; set; }
        public DataTemplate? BlobTemplate { get; set; }
        public DataTemplate? CaliperTemplate { get; set; }
        public DataTemplate? LineFitTemplate { get; set; }
        public DataTemplate? CircleFitTemplate { get; set; }
        public DataTemplate? HeightSlicerTemplate { get; set; }
        public DataTemplate? CodeReaderTemplate { get; set; }
        public DataTemplate? GeometryTemplate { get; set; }
        public DataTemplate? OCRTemplate { get; set; }
        public DataTemplate? OCVTemplate { get; set; }
        public DataTemplate? DetectionTemplate { get; set; }
        public DataTemplate? ClassifyTemplate { get; set; }
        public DataTemplate? AnomalyTemplate { get; set; }
        public DataTemplate? EnsembleTemplate { get; set; }
        public DataTemplate? PlaneFitTemplate { get; set; }
        public DataTemplate? Geometry3DTemplate { get; set; }
        public DataTemplate? ResultTemplate { get; set; }
        public DataTemplate? ImageRectifyTemplate { get; set; }
        public DataTemplate? SegmentationTemplate { get; set; }
        public DataTemplate? YoloSegTemplate { get; set; }
        public DataTemplate? ShapeMatchTemplate { get; set; }
        public DataTemplate? ColorExtractTemplate { get; set; }
        public DataTemplate? ColorMatchTemplate { get; set; }
        public DataTemplate? PointCloudFilterTemplate { get; set; }
        public DataTemplate? PointCloudRegistrationTemplate { get; set; }
        public DataTemplate? PointCloudClusterTemplate { get; set; }

        public override DataTemplate? SelectTemplate(object? item, DependencyObject container)
        {
            return item switch
            {
                GrayscaleToolSettingsViewModel => GrayscaleTemplate,
                BlurToolSettingsViewModel => BlurTemplate,
                ThresholdToolSettingsViewModel => ThresholdTemplate,
                EdgeDetectionToolSettingsViewModel => EdgeDetectionTemplate,
                MorphologyToolSettingsViewModel => MorphologyTemplate,
                HistogramToolSettingsViewModel => HistogramTemplate,
                FeatureMatchToolSettingsViewModel => FeatureMatchTemplate,
                BlobToolSettingsViewModel => BlobTemplate,
                CaliperToolSettingsViewModel => CaliperTemplate,
                LineFitToolSettingsViewModel => LineFitTemplate,
                CircleFitToolSettingsViewModel => CircleFitTemplate,
                HeightSlicerToolSettingsViewModel => HeightSlicerTemplate,
                CodeReaderToolSettingsViewModel => CodeReaderTemplate,
                GeometryToolSettingsViewModel => GeometryTemplate,
                PlaneFitToolSettingsViewModel => PlaneFitTemplate,
                Geometry3DToolSettingsViewModel => Geometry3DTemplate,
                OCRToolSettingsViewModel => OCRTemplate,
                OCVToolSettingsViewModel => OCVTemplate,
                DetectionToolSettingsViewModel => DetectionTemplate,
                ClassifyToolSettingsViewModel => ClassifyTemplate,
                AnomalyToolSettingsViewModel => AnomalyTemplate,
                EnsembleToolSettingsViewModel => EnsembleTemplate,
                ResultToolSettingsViewModel => ResultTemplate,
                ImageRectifyToolSettingsViewModel => ImageRectifyTemplate,
                SegmentationToolSettingsViewModel => SegmentationTemplate,
                YoloSegToolSettingsViewModel => YoloSegTemplate,
                ShapeMatchToolSettingsViewModel => ShapeMatchTemplate,
                ColorExtractToolSettingsViewModel => ColorExtractTemplate,
                ColorMatchToolSettingsViewModel => ColorMatchTemplate,
                PointCloudFilterToolSettingsViewModel => PointCloudFilterTemplate,
                PointCloudRegistrationToolSettingsViewModel => PointCloudRegistrationTemplate,
                PointCloudClusterToolSettingsViewModel => PointCloudClusterTemplate,
                _ => base.SelectTemplate(item, container)
            };
        }
    }
}
