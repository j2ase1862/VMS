using System;
using System.Collections.ObjectModel;
using VMS.VisionSetup.Models;
using VMS.VisionSetup.VisionTools.Measurement;

namespace VMS.VisionSetup.ViewModels.ToolSettings
{
    public class GeometryToolSettingsViewModel : ToolSettingsViewModelBase
    {
        private GeometryTool TypedTool => (GeometryTool)Tool;

        public GeometryToolSettingsViewModel(GeometryTool tool) : base(tool)
        {
            LoadAvailableParamCodes();
        }

        public override bool HasCustomROISection => true;

        // ── Web Parameter Link (ParamCode) ──
        // Web 의 Dimension Tool 프리셋(Reference Value / Upper·Lower Tolerance)과 1:1 대응.
        // 값은 숫자만 내려오므로 mm/px 해석은 JudgmentUnit 설정을 따른다.
        public ParamCodeItem? SelectedExpectedValueCode
        {
            get => GetLinkedParamCodeItem(nameof(ExpectedValue));
            set { SetLinkedParamCode(nameof(ExpectedValue), value); OnPropertyChanged(); }
        }

        public ParamCodeItem? SelectedTolerancePlusCode
        {
            get => GetLinkedParamCodeItem(nameof(TolerancePlus));
            set { SetLinkedParamCode(nameof(TolerancePlus), value); OnPropertyChanged(); }
        }

        public ParamCodeItem? SelectedToleranceMinusCode
        {
            get => GetLinkedParamCodeItem(nameof(ToleranceMinus));
            set { SetLinkedParamCode(nameof(ToleranceMinus), value); OnPropertyChanged(); }
        }

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
