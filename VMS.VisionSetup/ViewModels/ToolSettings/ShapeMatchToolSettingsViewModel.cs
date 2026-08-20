using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using OpenCvSharp;
using VMS.VisionSetup.Models;
using VMS.VisionSetup.Services;
using VMS.VisionSetup.VisionTools.PatternMatching;

namespace VMS.VisionSetup.ViewModels.ToolSettings
{
    public class ShapeMatchToolSettingsViewModel : ToolSettingsViewModelBase
    {
        private ShapeMatchTool TypedTool => (ShapeMatchTool)Tool;

        public ShapeMatchToolSettingsViewModel(ShapeMatchTool tool) : base(tool)
        {
            LoadAvailableParamCodes();
            TrainTemplateCommand = new RelayCommand(TrainTemplate);
            ClearTemplateCommand = new RelayCommand(ClearTemplate);

            DrawSearchRegionCommand = new RelayCommand(() =>
                WeakReferenceMessenger.Default.Send(new RequestDrawSearchRegionMessage()));

            ClearSearchRegionCommand = new RelayCommand(() =>
            {
                UseSearchRegion = false;
                SearchRegion = new Rect();
                WeakReferenceMessenger.Default.Send(new RequestClearSearchRegionMessage());
            });
        }

        /// <summary>Training Region(UseROI)과 Search Region(UseSearchRegion)을 자체 UI로 분리.</summary>
        public override bool HasCustomROISection => true;

        public double AngleStep { get => TypedTool.AngleStep; set => TypedTool.AngleStep = value; }
        public double MinScale { get => TypedTool.MinScale; set => TypedTool.MinScale = value; }
        public double MaxScale { get => TypedTool.MaxScale; set => TypedTool.MaxScale = value; }
        public double ScaleStep { get => TypedTool.ScaleStep; set => TypedTool.ScaleStep = value; }
        public double ScoreThreshold { get => TypedTool.ScoreThreshold; set => TypedTool.ScoreThreshold = value; }
        public int NumPyramidLevels { get => TypedTool.NumPyramidLevels; set => TypedTool.NumPyramidLevels = value; }
        public int TopCandidates { get => TypedTool.TopCandidates; set => TypedTool.TopCandidates = value; }
        public int MaxInstances { get => TypedTool.MaxInstances; set => TypedTool.MaxInstances = value; }
        public double NmsDistanceFactor { get => TypedTool.NmsDistanceFactor; set => TypedTool.NmsDistanceFactor = value; }

        // IsExpertMode 는 ToolSettingsViewModelBase 에서 도구 타입별 + 영구 저장으로 제공.

        public bool IsTrained => TypedTool.IsTrained;
        public int TemplateWidth => TypedTool.TemplateWidth;
        public int TemplateHeight => TypedTool.TemplateHeight;

        // Search Region 포워딩 (FeatureMatchTool과 동일 패턴)
        public bool UseSearchRegion { get => TypedTool.UseSearchRegion; set => TypedTool.UseSearchRegion = value; }
        public Rect SearchRegion { get => TypedTool.SearchRegion; set => TypedTool.SearchRegion = value; }
        public int SearchRegionX { get => TypedTool.SearchRegionX; set => TypedTool.SearchRegionX = value; }
        public int SearchRegionY { get => TypedTool.SearchRegionY; set => TypedTool.SearchRegionY = value; }
        public int SearchRegionWidth { get => TypedTool.SearchRegionWidth; set => TypedTool.SearchRegionWidth = value; }
        public int SearchRegionHeight { get => TypedTool.SearchRegionHeight; set => TypedTool.SearchRegionHeight = value; }

        public ParamCodeItem? SelectedScoreThresholdCode
        {
            get => GetLinkedParamCodeItem(nameof(ScoreThreshold));
            set { SetLinkedParamCode(nameof(ScoreThreshold), value); OnPropertyChanged(); }
        }

        // ── 각도/스케일 판정 (Web ParamCode 연동) ──
        public bool UseAngleJudgment { get => TypedTool.UseAngleJudgment; set => TypedTool.UseAngleJudgment = value; }
        public double AngleLowerLimit { get => TypedTool.AngleLowerLimit; set => TypedTool.AngleLowerLimit = value; }
        public double AngleUpperLimit { get => TypedTool.AngleUpperLimit; set => TypedTool.AngleUpperLimit = value; }
        public bool UseScaleJudgment { get => TypedTool.UseScaleJudgment; set => TypedTool.UseScaleJudgment = value; }
        public double ScaleLowerLimit { get => TypedTool.ScaleLowerLimit; set => TypedTool.ScaleLowerLimit = value; }
        public double ScaleUpperLimit { get => TypedTool.ScaleUpperLimit; set => TypedTool.ScaleUpperLimit = value; }

        public ParamCodeItem? SelectedAngleLowerLimitCode
        {
            get => GetLinkedParamCodeItem(nameof(AngleLowerLimit));
            set { SetLinkedParamCode(nameof(AngleLowerLimit), value); OnPropertyChanged(); }
        }
        public ParamCodeItem? SelectedAngleUpperLimitCode
        {
            get => GetLinkedParamCodeItem(nameof(AngleUpperLimit));
            set { SetLinkedParamCode(nameof(AngleUpperLimit), value); OnPropertyChanged(); }
        }
        public ParamCodeItem? SelectedScaleLowerLimitCode
        {
            get => GetLinkedParamCodeItem(nameof(ScaleLowerLimit));
            set { SetLinkedParamCode(nameof(ScaleLowerLimit), value); OnPropertyChanged(); }
        }
        public ParamCodeItem? SelectedScaleUpperLimitCode
        {
            get => GetLinkedParamCodeItem(nameof(ScaleUpperLimit));
            set { SetLinkedParamCode(nameof(ScaleUpperLimit), value); OnPropertyChanged(); }
        }

        public IRelayCommand TrainTemplateCommand { get; }
        public IRelayCommand ClearTemplateCommand { get; }
        public IRelayCommand DrawSearchRegionCommand { get; }
        public IRelayCommand ClearSearchRegionCommand { get; }

        private string _trainStatus = "Not trained.";
        public string TrainStatus
        {
            get => _trainStatus;
            private set => SetProperty(ref _trainStatus, value);
        }

        private void TrainTemplate()
        {
            var src = VisionService.Instance.CurrentImage;
            if (src == null || src.Empty())
            {
                TrainStatus = "Load an image first.";
                return;
            }
            bool ok = TypedTool.TrainFromImage(src);
            OnPropertyChanged(nameof(IsTrained));
            OnPropertyChanged(nameof(TemplateWidth));
            OnPropertyChanged(nameof(TemplateHeight));
            TrainStatus = ok
                ? $"Trained ({TemplateWidth}x{TemplateHeight})."
                : "Train failed (check Training Region size).";
        }

        private void ClearTemplate()
        {
            TypedTool.TemplatePngBytes = null;
            OnPropertyChanged(nameof(IsTrained));
            OnPropertyChanged(nameof(TemplateWidth));
            OnPropertyChanged(nameof(TemplateHeight));
            TrainStatus = "Template cleared.";
        }
    }
}
