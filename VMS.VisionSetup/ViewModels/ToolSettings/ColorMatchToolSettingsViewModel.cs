using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using OpenCvSharp;
using System.Collections.ObjectModel;
using VMS.VisionSetup.Models;
using VMS.VisionSetup.Services;
using VMS.VisionSetup.VisionTools.Color;

namespace VMS.VisionSetup.ViewModels.ToolSettings
{
    public class ColorMatchToolSettingsViewModel : ToolSettingsViewModelBase
    {
        private ColorMatchTool TypedTool => (ColorMatchTool)Tool;

        public ColorMatchToolSettingsViewModel(ColorMatchTool tool) : base(tool)
        {
            LoadAvailableParamCodes();
            TrainSelectedModelCommand = new RelayCommand(TrainSelectedModel);
            PickColorCommand = new RelayCommand(() =>
                WeakReferenceMessenger.Default.Send(new RequestPickColorMessage()));
            AddModelCommand = new RelayCommand(AddModel);
            RemoveModelCommand = new RelayCommand(RemoveModel, () => Models.Count > 1 && SelectedModel != null);

            DrawSearchRegionCommand = new RelayCommand(() =>
                WeakReferenceMessenger.Default.Send(new RequestDrawSearchRegionMessage()));

            ClearSearchRegionCommand = new RelayCommand(() =>
            {
                UseSearchRegion = false;
                SearchRegion = new Rect();
                WeakReferenceMessenger.Default.Send(new RequestClearSearchRegionMessage());
            });

            Models.CollectionChanged += (_, _) => RemoveModelCommand.NotifyCanExecuteChanged();
        }

        public override bool HasCustomROISection => true;

        public ObservableCollection<ColorMatchModel> Models => TypedTool.Models;
        public ColorMatchModel? SelectedModel
        {
            get => TypedTool.SelectedModel;
            set
            {
                TypedTool.SelectedModel = value;
                OnPropertyChanged();
                RemoveModelCommand.NotifyCanExecuteChanged();
            }
        }

        public int MeanL { get => TypedTool.MeanL; set => TypedTool.MeanL = value; }
        public int MeanA { get => TypedTool.MeanA; set => TypedTool.MeanA = value; }
        public int MeanB { get => TypedTool.MeanB; set => TypedTool.MeanB = value; }

        public double ColorTolerance { get => TypedTool.ColorTolerance; set => TypedTool.ColorTolerance = value; }
        public int MorphKernelSize { get => TypedTool.MorphKernelSize; set => TypedTool.MorphKernelSize = value; }
        public bool ShowOverlay { get => TypedTool.ShowOverlay; set => TypedTool.ShowOverlay = value; }
        public double OverlayOpacity { get => TypedTool.OverlayOpacity; set => TypedTool.OverlayOpacity = value; }

        public bool UseSearchRegion { get => TypedTool.UseSearchRegion; set => TypedTool.UseSearchRegion = value; }
        public Rect SearchRegion { get => TypedTool.SearchRegion; set => TypedTool.SearchRegion = value; }
        public int SearchRegionX { get => TypedTool.SearchRegionX; set => TypedTool.SearchRegionX = value; }
        public int SearchRegionY { get => TypedTool.SearchRegionY; set => TypedTool.SearchRegionY = value; }
        public int SearchRegionWidth { get => TypedTool.SearchRegionWidth; set => TypedTool.SearchRegionWidth = value; }
        public int SearchRegionHeight { get => TypedTool.SearchRegionHeight; set => TypedTool.SearchRegionHeight = value; }

        public IRelayCommand TrainSelectedModelCommand { get; }
        public IRelayCommand PickColorCommand { get; }
        public IRelayCommand AddModelCommand { get; }
        public IRelayCommand RemoveModelCommand { get; }
        public IRelayCommand DrawSearchRegionCommand { get; }
        public IRelayCommand ClearSearchRegionCommand { get; }

        public ParamCodeItem? SelectedColorToleranceCode
        {
            get => GetLinkedParamCodeItem(nameof(ColorTolerance));
            set { SetLinkedParamCode(nameof(ColorTolerance), value); OnPropertyChanged(); }
        }

        private bool _isExpertMode;
        public bool IsExpertMode
        {
            get => _isExpertMode;
            set => SetProperty(ref _isExpertMode, value);
        }

        private string _trainStatus = "모델 선택 후 ROI를 그리고 'Train Selected Model'을 클릭하세요.";
        public string TrainStatus
        {
            get => _trainStatus;
            private set => SetProperty(ref _trainStatus, value);
        }

        private void AddModel()
        {
            var m = new ColorMatchModel { Name = $"Model {Models.Count + 1}" };
            Models.Add(m);
            SelectedModel = m;
            TrainStatus = $"Added '{m.Name}'. Draw ROI and click 'Train Selected Model'.";
        }

        private void RemoveModel()
        {
            if (SelectedModel == null || Models.Count <= 1) return;
            int idx = Models.IndexOf(SelectedModel);
            string removed = SelectedModel.Name;
            Models.RemoveAt(idx);
            SelectedModel = Models[System.Math.Clamp(idx, 0, Models.Count - 1)];
            TrainStatus = $"Removed '{removed}'.";
        }

        private void TrainSelectedModel()
        {
            var src = VisionService.Instance.CurrentImage;
            if (src == null || src.Empty())
            {
                TrainStatus = "이미지를 먼저 로드하세요.";
                return;
            }
            if (SelectedModel == null)
            {
                TrainStatus = "선택된 모델이 없습니다.";
                return;
            }
            if (!TypedTool.UseROI || TypedTool.ROIWidth < 4 || TypedTool.ROIHeight < 4)
            {
                TrainStatus = "'Use Training Region' 체크 후 색 영역에 ROI를 그리세요.";
                return;
            }
            bool ok = TypedTool.TrainFromImage(src);
            if (!ok)
            {
                TrainStatus = "학습 실패 (ROI가 너무 작거나 비어 있음).";
                return;
            }
            TrainStatus = $"'{SelectedModel.Name}' 학습: L={MeanL}, a={MeanA}, b={MeanB}";
        }
    }
}
