using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.Input;
using VMS.VisionSetup.Interfaces;
using VMS.VisionSetup.Models;
using VMS.VisionSetup.VisionTools.PatternMatching;

namespace VMS.VisionSetup.ViewModels.ToolSettings
{
    public class MultiStepAlignToolSettingsViewModel : ToolSettingsViewModelBase
    {
        /// <summary>포즈(CenterX/Y)를 출력하는 소스 후보 툴 타입.</summary>
        private static readonly HashSet<string> PoseCapableToolTypes = new()
        {
            "FeatureMatchTool", "ShapeMatchTool", "BlobTool", "CircleFitTool"
        };

        public sealed class PickItem
        {
            public string Id { get; init; } = string.Empty;
            public string Label { get; init; } = string.Empty;
            public override string ToString() => Label;
        }

        private MultiStepAlignTool TypedTool => (MultiStepAlignTool)Tool;
        private readonly IRecipeService? _recipeService;

        public MultiStepAlignToolSettingsViewModel(MultiStepAlignTool tool, IRecipeService? recipeService = null)
            : base(tool)
        {
            _recipeService = recipeService;
            CaptureReferenceCommand = new RelayCommand(CaptureReference);
            RefreshSteps();
        }

        // ── 소스 선택 ──

        public ObservableCollection<PickItem> Steps { get; } = new();
        public ObservableCollection<PickItem> ToolsA { get; } = new();
        public ObservableCollection<PickItem> ToolsB { get; } = new();

        public void RefreshSteps()
        {
            Steps.Clear();
            var recipe = _recipeService?.CurrentRecipe;
            if (recipe != null)
            {
                foreach (var step in recipe.Steps)
                    Steps.Add(new PickItem { Id = step.Id, Label = step.DisplayName });
            }
            RefreshTools(ToolsA, TypedTool.SourceStepIdA);
            RefreshTools(ToolsB, TypedTool.SourceStepIdB);
            OnPropertyChanged(nameof(SelectedStepA));
            OnPropertyChanged(nameof(SelectedStepB));
            OnPropertyChanged(nameof(SelectedToolA));
            OnPropertyChanged(nameof(SelectedToolB));
        }

        private void RefreshTools(ObservableCollection<PickItem> target, string stepId)
        {
            target.Clear();
            var step = _recipeService?.CurrentRecipe?.Steps.FirstOrDefault(s => s.Id == stepId);
            if (step == null) return;
            foreach (var cfg in step.Tools.Where(t => PoseCapableToolTypes.Contains(t.ToolType)))
                target.Add(new PickItem { Id = cfg.Id, Label = $"{cfg.Name} ({cfg.ToolType})" });
        }

        public PickItem? SelectedStepA
        {
            get => Steps.FirstOrDefault(s => s.Id == TypedTool.SourceStepIdA);
            set
            {
                TypedTool.SourceStepIdA = value?.Id ?? string.Empty;
                RefreshTools(ToolsA, TypedTool.SourceStepIdA);
                OnPropertyChanged();
                OnPropertyChanged(nameof(SelectedToolA));
            }
        }

        public PickItem? SelectedToolA
        {
            get => ToolsA.FirstOrDefault(t => t.Id == TypedTool.SourceToolIdA);
            set { TypedTool.SourceToolIdA = value?.Id ?? string.Empty; OnPropertyChanged(); }
        }

        public PickItem? SelectedStepB
        {
            get => Steps.FirstOrDefault(s => s.Id == TypedTool.SourceStepIdB);
            set
            {
                TypedTool.SourceStepIdB = value?.Id ?? string.Empty;
                RefreshTools(ToolsB, TypedTool.SourceStepIdB);
                OnPropertyChanged();
                OnPropertyChanged(nameof(SelectedToolB));
            }
        }

        public PickItem? SelectedToolB
        {
            get => ToolsB.FirstOrDefault(t => t.Id == TypedTool.SourceToolIdB);
            set { TypedTool.SourceToolIdB = value?.Id ?? string.Empty; OnPropertyChanged(); }
        }

        // ── 파라미터 래퍼 (래퍼 누락 = 바인딩 허공 → 저장 불가) ──

        public double BaselineX
        {
            get => TypedTool.BaselineX;
            set { TypedTool.BaselineX = value; OnPropertyChanged(); }
        }

        public double BaselineY
        {
            get => TypedTool.BaselineY;
            set { TypedTool.BaselineY = value; OnPropertyChanged(); }
        }

        public double RefAX
        {
            get => TypedTool.RefAX;
            set { TypedTool.RefAX = value; OnPropertyChanged(); }
        }

        public double RefAY
        {
            get => TypedTool.RefAY;
            set { TypedTool.RefAY = value; OnPropertyChanged(); }
        }

        public double RefBX
        {
            get => TypedTool.RefBX;
            set { TypedTool.RefBX = value; OnPropertyChanged(); }
        }

        public double RefBY
        {
            get => TypedTool.RefBY;
            set { TypedTool.RefBY = value; OnPropertyChanged(); }
        }

        public bool HasReference
        {
            get => TypedTool.HasReference;
            set { TypedTool.HasReference = value; OnPropertyChanged(); }
        }

        public bool RequireSameCycle
        {
            get => TypedTool.RequireSameCycle;
            set { TypedTool.RequireSameCycle = value; OnPropertyChanged(); }
        }

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

        public bool EnableJudgment
        {
            get => TypedTool.EnableJudgment;
            set { TypedTool.EnableJudgment = value; OnPropertyChanged(); }
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

        // ── 기준 등록 ──

        public IRelayCommand CaptureReferenceCommand { get; }

        private string _captureStatus = string.Empty;
        public string CaptureStatus
        {
            get => _captureStatus;
            private set { _captureStatus = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// 기준 부품으로 두 스텝을 Run 한 뒤 현재 두 점(합성 좌표계)을 기준으로 등록.
        /// Baseline 을 먼저 입력해 둘 것 — 기준·현재가 같은 합성 규칙을 쓴다.
        /// </summary>
        private void CaptureReference()
        {
            if (!TypedTool.TryComputeCurrentPoints(
                    out var ax, out var ay, out var bx, out var by, out var unit, out var error))
            {
                CaptureStatus = error;
                return;
            }

            RefAX = ax;
            RefAY = ay;
            RefBX = bx;
            RefBY = by;
            HasReference = true;
            CaptureStatus = $"기준 등록({unit}): A({ax:F3}, {ay:F3}) · B({bx:F3}, {by:F3})";
        }
    }
}
