using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using VMS.VisionSetup.VisionTools.Result;

namespace VMS.VisionSetup.ViewModels.ToolSettings
{
    public class ResultToolSettingsViewModel : ToolSettingsViewModelBase
    {
        private ResultTool TypedTool => (ResultTool)Tool;

        public ResultToolSettingsViewModel(ResultTool tool) : base(tool) { }

        public override bool HidePlcSection => true;

        public ResultJudgmentMode JudgmentMode
        {
            get => TypedTool.JudgmentMode;
            set
            {
                TypedTool.JudgmentMode = value;
                OnPropertyChanged();
            }
        }

        public Array JudgmentModes => Enum.GetValues(typeof(ResultJudgmentMode));

        public ObservableCollection<SourceToolResult> SourceResults => new(TypedTool.SourceResults);

        public void RefreshSourceResults()
        {
            OnPropertyChanged(nameof(SourceResults));
        }
    }
}
