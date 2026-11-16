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

        public void RefreshSourceResults()
        {
            OnPropertyChanged(nameof(SourceResults));
        }

        // 연결 입력(SourceResults)은 실행 직전에 채워진다 — 실행 결과가 바뀔 때 목록도 다시 읽는다.
        protected override void OnToolPropertyChanged(string? propertyName)
        {
            base.OnToolPropertyChanged(propertyName);
            if (propertyName == nameof(LastResult))
                RefreshSourceResults();
        }
    }
}
