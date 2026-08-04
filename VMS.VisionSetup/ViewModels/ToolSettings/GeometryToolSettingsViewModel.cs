using System;
using System.Collections.ObjectModel;
using VMS.VisionSetup.VisionTools.Measurement;

namespace VMS.VisionSetup.ViewModels.ToolSettings
{
    public class GeometryToolSettingsViewModel : ToolSettingsViewModelBase
    {
        private GeometryTool TypedTool => (GeometryTool)Tool;

        public GeometryToolSettingsViewModel(GeometryTool tool) : base(tool) { }

        public override bool HasCustomROISection => true;

        public GeometryOperation Operation
        {
            get => TypedTool.Operation;
            set
            {
                TypedTool.Operation = value;
                OnPropertyChanged();
            }
        }

        public Array Operations => Enum.GetValues(typeof(GeometryOperation));

        // ── 판정 (Judgment) — 설정 UI 는 이 VM 에 바인딩되므로 툴 속성마다 래퍼 필수.
        // (래퍼가 빠지면 바인딩이 허공 → 입력값이 툴에 도달하지 않아 저장도 안 된다)

        public bool EnableJudgment
        {
            get => TypedTool.EnableJudgment;
            set { TypedTool.EnableJudgment = value; OnPropertyChanged(); }
        }

        public GeometryJudgmentUnit JudgmentUnit
        {
            get => TypedTool.JudgmentUnit;
            set { TypedTool.JudgmentUnit = value; OnPropertyChanged(); }
        }

        public double ExpectedValue
        {
            get => TypedTool.ExpectedValue;
            set { TypedTool.ExpectedValue = value; OnPropertyChanged(); }
        }

        public double ToleranceMinus
        {
            get => TypedTool.ToleranceMinus;
            set { TypedTool.ToleranceMinus = value; OnPropertyChanged(); }
        }

        public double TolerancePlus
        {
            get => TypedTool.TolerancePlus;
            set { TypedTool.TolerancePlus = value; OnPropertyChanged(); }
        }

        public ObservableCollection<SourceGeometry> SourceGeometries => new(TypedTool.SourceGeometries);

        public void RefreshSourceGeometries()
        {
            OnPropertyChanged(nameof(SourceGeometries));
        }
    }
}
