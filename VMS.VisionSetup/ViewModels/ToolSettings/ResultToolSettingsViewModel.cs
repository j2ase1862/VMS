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

        // PLC Output 섹션 노출 — 단, 최종 OK/NG(Success) 출력은 시퀀스 에디터의
        // Branch→OutputAction 이 핸드셰이크(BUSY→결과 비트→COMPLETE→클리어)와 함께
        // 담당하므로 여기서는 PassCount/FailCount/TotalCount 등 통계 키만 매핑 대상
        // (GetAvailableResultKeys 가 Success 를 제외).

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
