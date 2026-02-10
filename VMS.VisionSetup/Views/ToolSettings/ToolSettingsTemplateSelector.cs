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
                _ => base.SelectTemplate(item, container)
            };
        }
    }
}
