using System;
using CommunityToolkit.Mvvm.Input;
using VMS.VisionSetup.VisionTools.Measurement;
using VMS.VisionSetup.VisionTools.PatternMatching;

namespace VMS.VisionSetup.ViewModels.ToolSettings
{
    public class MatchAlignToolSettingsViewModel : ToolSettingsViewModelBase
    {
        private MatchAlignTool TypedTool => (MatchAlignTool)Tool;

        public MatchAlignToolSettingsViewModel(MatchAlignTool tool) : base(tool)
        {
            CaptureReferenceCommand = new RelayCommand(CaptureReference);
        }

        // ── 기준 포즈 — 설정 UI 는 이 VM 에 바인딩되므로 툴 속성마다 래퍼 필수.
        // (래퍼가 빠지면 바인딩이 허공 → 입력값이 툴에 도달하지 않아 저장도 안 된다)

        public MatchAlignMode Mode
        {
            get => TypedTool.Mode;
            set
            {
                TypedTool.Mode = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsTwoPoint));
            }
        }

        public bool IsTwoPoint => TypedTool.Mode == MatchAlignMode.TwoPoint;

        public bool UseTrainedReference
        {
            get => TypedTool.UseTrainedReference;
            set
            {
                TypedTool.UseTrainedReference = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsManualReference));
            }
        }

        public bool IsManualReference => !TypedTool.UseTrainedReference;

        public double RefX
        {
            get => TypedTool.RefX;
            set { TypedTool.RefX = value; OnPropertyChanged(); }
        }

        public double RefY
        {
            get => TypedTool.RefY;
            set { TypedTool.RefY = value; OnPropertyChanged(); }
        }

        public double RefTheta
        {
            get => TypedTool.RefTheta;
            set { TypedTool.RefTheta = value; OnPropertyChanged(); }
        }

        public double RefX2
        {
            get => TypedTool.RefX2;
            set { TypedTool.RefX2 = value; OnPropertyChanged(); }
        }

        public double RefY2
        {
            get => TypedTool.RefY2;
            set { TypedTool.RefY2 = value; OnPropertyChanged(); }
        }

        public IRelayCommand CaptureReferenceCommand { get; }

        private string _captureStatus = string.Empty;
        public string CaptureStatus
        {
            get => _captureStatus;
            private set { _captureStatus = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// 마지막 실행에서 주입된 소스 매칭의 현재 포즈를 수동 기준으로 캡처.
        /// (기준 부품을 올려 두고 Run 한 뒤 이 버튼으로 Origin 등록 — 2점 모드는 두 점 모두)
        /// </summary>
        private void CaptureReference()
        {
            var data = TypedTool.SourceMatchResult?.Data;
            if (data == null
                || !data.TryGetValue("CenterX", out var cx)
                || !data.TryGetValue("CenterY", out var cy))
            {
                CaptureStatus = "캡처할 매칭 결과가 없습니다 — 기준 부품으로 Run(F5/F6)을 먼저 실행하세요.";
                return;
            }

            if (IsTwoPoint)
            {
                var data2 = TypedTool.SourceMatchResult2?.Data;
                if (data2 == null
                    || !data2.TryGetValue("CenterX", out var cx2)
                    || !data2.TryGetValue("CenterY", out var cy2))
                {
                    CaptureStatus = "2번 포인트 매칭 결과가 없습니다 — Feature Match 2개를 연결하고 Run 을 먼저 실행하세요.";
                    return;
                }
                RefX = Convert.ToDouble(cx);
                RefY = Convert.ToDouble(cy);
                RefX2 = Convert.ToDouble(cx2);
                RefY2 = Convert.ToDouble(cy2);
                UseTrainedReference = false;
                CaptureStatus = $"기준 등록: P1({RefX:F1}, {RefY:F1}) · P2({RefX2:F1}, {RefY2:F1})";
                return;
            }

            RefX = Convert.ToDouble(cx);
            RefY = Convert.ToDouble(cy);
            RefTheta = data.TryGetValue("Angle", out var ang) ? Convert.ToDouble(ang) : 0;
            UseTrainedReference = false;
            CaptureStatus = $"기준 등록: ({RefX:F1}, {RefY:F1}), θ={RefTheta:F2}°";
        }

        // ── 로봇/스테이지 변환 ──

        public bool EnableRobotTransform
        {
            get => TypedTool.EnableRobotTransform;
            set { TypedTool.EnableRobotTransform = value; OnPropertyChanged(); }
        }

        public double RobotM11
        {
            get => TypedTool.RobotM11;
            set { TypedTool.RobotM11 = value; OnPropertyChanged(); }
        }

        public double RobotM12
        {
            get => TypedTool.RobotM12;
            set { TypedTool.RobotM12 = value; OnPropertyChanged(); }
        }

        public double RobotM21
        {
            get => TypedTool.RobotM21;
            set { TypedTool.RobotM21 = value; OnPropertyChanged(); }
        }

        public double RobotM22
        {
            get => TypedTool.RobotM22;
            set { TypedTool.RobotM22 = value; OnPropertyChanged(); }
        }

        public double RobotThetaSign
        {
            get => TypedTool.RobotThetaSign;
            set { TypedTool.RobotThetaSign = value; OnPropertyChanged(); }
        }

        // ── 판정 ──

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

        public double MaxDeltaXY
        {
            get => TypedTool.MaxDeltaXY;
            set { TypedTool.MaxDeltaXY = value; OnPropertyChanged(); }
        }

        public double MaxDeltaTheta
        {
            get => TypedTool.MaxDeltaTheta;
            set { TypedTool.MaxDeltaTheta = value; OnPropertyChanged(); }
        }

        public bool DrawOverlay
        {
            get => TypedTool.DrawOverlay;
            set { TypedTool.DrawOverlay = value; OnPropertyChanged(); }
        }
    }
}
