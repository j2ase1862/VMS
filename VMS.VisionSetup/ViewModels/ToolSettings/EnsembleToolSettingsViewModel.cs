using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using VMS.VisionSetup.VisionTools.DeepLearning;

namespace VMS.VisionSetup.ViewModels.ToolSettings
{
    public class EnsembleToolSettingsViewModel : ToolSettingsViewModelBase
    {
        private EnsembleTool TypedTool => (EnsembleTool)Tool;

        public EnsembleToolSettingsViewModel(EnsembleTool tool) : base(tool) { }

        public EnsembleMode Mode
        {
            get => TypedTool.Mode;
            set { TypedTool.Mode = value; OnPropertyChanged(); }
        }
        public Array Modes => Enum.GetValues(typeof(EnsembleMode));

        public double DetectionWeight
        {
            get => TypedTool.DetectionWeight;
            set => TypedTool.DetectionWeight = value;
        }
        public double AnomalyWeight
        {
            get => TypedTool.AnomalyWeight;
            set => TypedTool.AnomalyWeight = value;
        }
        public double WeightedThreshold
        {
            get => TypedTool.WeightedThreshold;
            set => TypedTool.WeightedThreshold = value;
        }
        public bool DrawOverlay
        {
            get => TypedTool.DrawOverlay;
            set => TypedTool.DrawOverlay = value;
        }

        public ObservableCollection<SourceToolResultEx> SourceResults => new(TypedTool.SourceResults);
    }
}
