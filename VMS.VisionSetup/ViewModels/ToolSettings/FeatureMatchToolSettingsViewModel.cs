using VMS.VisionSetup.Models;
using VMS.VisionSetup.VisionTools.PatternMatching;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using OpenCvSharp;
using System.Collections.ObjectModel;

// ParamCode support

namespace VMS.VisionSetup.ViewModels.ToolSettings
{
    public class FeatureMatchToolSettingsViewModel : ToolSettingsViewModelBase
    {
        private FeatureMatchTool TypedTool => (FeatureMatchTool)Tool;

        public FeatureMatchToolSettingsViewModel(FeatureMatchTool tool) : base(tool)
        {
            LoadAvailableParamCodes();

            RemoveModelCommand = new RelayCommand<FeatureMatchModel>(m => { if (m != null) TypedTool.RemoveModel(m); });

            ClearSearchRegionCommand = new RelayCommand(() =>
            {
                UseSearchRegion = false;
                SearchRegion = new Rect();
                AssociatedSearchRegionShape = null;
                WeakReferenceMessenger.Default.Send(new RequestClearSearchRegionMessage());
            });

            ApplySuggestionsCommand = new RelayCommand(() => TypedTool.ApplySuggestedParameters());

            AddAndTrainModelCommand = new RelayCommand(() =>
            {
                SelectedModel = null;
                WeakReferenceMessenger.Default.Send(new RequestTrainPatternMessage());
            });

            TrainSelectedModelCommand = new RelayCommand(() =>
                WeakReferenceMessenger.Default.Send(new RequestTrainPatternMessage()));

            RequestAutoTuneCommand = new RelayCommand(() =>
                WeakReferenceMessenger.Default.Send(new RequestAutoTuneMessage()));

            DrawSearchRegionCommand = new RelayCommand(() =>
                WeakReferenceMessenger.Default.Send(new RequestDrawSearchRegionMessage()));

            DeleteSelectedModelCommand = new RelayCommand(
                () => { if (SelectedModel != null) TypedTool.RemoveModel(SelectedModel); },
                () => SelectedModel != null);
        }

        public override bool HasCustomROISection => true;

        // FeatureMatch-specific commands
        public IRelayCommand<FeatureMatchModel> RemoveModelCommand { get; }
        public IRelayCommand ClearSearchRegionCommand { get; }
        public IRelayCommand ApplySuggestionsCommand { get; }
        public IRelayCommand AddAndTrainModelCommand { get; }
        public IRelayCommand TrainSelectedModelCommand { get; }
        public IRelayCommand RequestAutoTuneCommand { get; }
        public IRelayCommand DrawSearchRegionCommand { get; }
        public IRelayCommand DeleteSelectedModelCommand { get; }

        // Multi-model
        public ObservableCollection<FeatureMatchModel> Models => TypedTool.Models;
        public FeatureMatchModel? SelectedModel { get => TypedTool.SelectedModel; set => TypedTool.SelectedModel = value; }
        public FeatureMatchModel? LastMatchedModel { get => TypedTool.LastMatchedModel; set => TypedTool.LastMatchedModel = value; }

        // Model image proxies
        public Mat? SelectedModelTemplateImage => TypedTool.SelectedModelTemplateImage;
        public Mat? SelectedModelFeatureImage => TypedTool.SelectedModelFeatureImage;
        public Mat? TemplateImage => TypedTool.TemplateImage;
        public Mat? TrainedFeatureImage => TypedTool.TrainedFeatureImage;

        // Preview (computed)
        public Mat? PreviewImage => SelectedModelFeatureImage ?? SelectedModelTemplateImage;
        public bool HasPreviewImage => PreviewImage != null && !PreviewImage.Empty();
        public string PreviewPlaceholderText => SelectedModel != null ? "Not trained" : "No model selected";

        // Parameters
        public double CannyLow { get => TypedTool.CannyLow; set => TypedTool.CannyLow = value; }
        public double CannyHigh { get => TypedTool.CannyHigh; set => TypedTool.CannyHigh = value; }
        public double AngleStart { get => TypedTool.AngleStart; set => TypedTool.AngleStart = value; }
        public double AngleExtent { get => TypedTool.AngleExtent; set => TypedTool.AngleExtent = value; }
        public double AngleStep { get => TypedTool.AngleStep; set => TypedTool.AngleStep = value; }
        public double MinScale { get => TypedTool.MinScale; set => TypedTool.MinScale = value; }
        public double MaxScale { get => TypedTool.MaxScale; set => TypedTool.MaxScale = value; }
        public double ScaleStep { get => TypedTool.ScaleStep; set => TypedTool.ScaleStep = value; }
        public double ScoreThreshold { get => TypedTool.ScoreThreshold; set => TypedTool.ScoreThreshold = value; }

        // Web ParamCode Link
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
        public int NumLevels { get => TypedTool.NumLevels; set => TypedTool.NumLevels = value; }
        public double Greediness { get => TypedTool.Greediness; set => TypedTool.Greediness = value; }
        public int MaxModelPoints { get => TypedTool.MaxModelPoints; set => TypedTool.MaxModelPoints = value; }
        public bool UseContrastInvariant { get => TypedTool.UseContrastInvariant; set => TypedTool.UseContrastInvariant = value; }
        public double CurvatureWeight { get => TypedTool.CurvatureWeight; set => TypedTool.CurvatureWeight = value; }

        // Auto-tune
        public bool IsAutoTuneEnabled { get => TypedTool.IsAutoTuneEnabled; set => TypedTool.IsAutoTuneEnabled = value; }
        public double SuggestedCannyLow { get => TypedTool.SuggestedCannyLow; set => TypedTool.SuggestedCannyLow = value; }
        public double SuggestedCannyHigh { get => TypedTool.SuggestedCannyHigh; set => TypedTool.SuggestedCannyHigh = value; }
        public int SuggestedNumLevels { get => TypedTool.SuggestedNumLevels; set => TypedTool.SuggestedNumLevels = value; }
        public int SuggestedMaxModelPoints { get => TypedTool.SuggestedMaxModelPoints; set => TypedTool.SuggestedMaxModelPoints = value; }
        public bool HasSuggestions { get => TypedTool.HasSuggestions; set => TypedTool.HasSuggestions = value; }

        // Search region
        public Rect SearchRegion { get => TypedTool.SearchRegion; set => TypedTool.SearchRegion = value; }
        public int SearchRegionX { get => TypedTool.SearchRegionX; set => TypedTool.SearchRegionX = value; }
        public int SearchRegionY { get => TypedTool.SearchRegionY; set => TypedTool.SearchRegionY = value; }
        public int SearchRegionWidth { get => TypedTool.SearchRegionWidth; set => TypedTool.SearchRegionWidth = value; }
        public int SearchRegionHeight { get => TypedTool.SearchRegionHeight; set => TypedTool.SearchRegionHeight = value; }
        public bool UseSearchRegion { get => TypedTool.UseSearchRegion; set => TypedTool.UseSearchRegion = value; }
        public ROIShape? AssociatedSearchRegionShape { get => TypedTool.AssociatedSearchRegionShape; set => TypedTool.AssociatedSearchRegionShape = value; }

        protected override void OnToolPropertyChanged(string? propertyName)
        {
            base.OnToolPropertyChanged(propertyName);

            // When selected model or its images change, raise preview computed properties
            if (propertyName is nameof(SelectedModel)
                or nameof(SelectedModelFeatureImage)
                or nameof(SelectedModelTemplateImage))
            {
                OnPropertyChanged(nameof(PreviewImage));
                OnPropertyChanged(nameof(HasPreviewImage));
                OnPropertyChanged(nameof(PreviewPlaceholderText));
                DeleteSelectedModelCommand.NotifyCanExecuteChanged();
            }
        }
    }
}
