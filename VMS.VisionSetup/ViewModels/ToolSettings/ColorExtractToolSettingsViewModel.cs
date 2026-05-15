using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using OpenCvSharp;
using System.Collections.ObjectModel;
using System.Windows.Media;
using VMS.VisionSetup.Models;
using VMS.VisionSetup.Services;
using VMS.VisionSetup.VisionTools.Color;

namespace VMS.VisionSetup.ViewModels.ToolSettings
{
    public class ColorExtractToolSettingsViewModel : ToolSettingsViewModelBase
    {
        private ColorExtractTool TypedTool => (ColorExtractTool)Tool;

        public ColorExtractToolSettingsViewModel(ColorExtractTool tool) : base(tool)
        {
            LoadAvailableParamCodes();
            TrainSelectedModelCommand = new RelayCommand(TrainSelectedModel);
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

        public ObservableCollection<ColorModel> Models => TypedTool.Models;
        public ColorModel? SelectedModel
        {
            get => TypedTool.SelectedModel;
            set
            {
                TypedTool.SelectedModel = value;
                OnPropertyChanged();
                RemoveModelCommand.NotifyCanExecuteChanged();
            }
        }

        public int HueMin { get => TypedTool.HueMin; set => TypedTool.HueMin = value; }
        public int HueMax { get => TypedTool.HueMax; set => TypedTool.HueMax = value; }
        public int SaturationMin { get => TypedTool.SaturationMin; set => TypedTool.SaturationMin = value; }
        public int SaturationMax { get => TypedTool.SaturationMax; set => TypedTool.SaturationMax = value; }
        public int ValueMin { get => TypedTool.ValueMin; set => TypedTool.ValueMin = value; }
        public int ValueMax { get => TypedTool.ValueMax; set => TypedTool.ValueMax = value; }
        public int MorphKernelSize { get => TypedTool.MorphKernelSize; set => TypedTool.MorphKernelSize = value; }
        public bool InvertMask { get => TypedTool.InvertMask; set => TypedTool.InvertMask = value; }
        public bool ShowOverlay { get => TypedTool.ShowOverlay; set => TypedTool.ShowOverlay = value; }
        public double OverlayOpacity { get => TypedTool.OverlayOpacity; set => TypedTool.OverlayOpacity = value; }

        public bool UseSearchRegion { get => TypedTool.UseSearchRegion; set => TypedTool.UseSearchRegion = value; }
        public Rect SearchRegion { get => TypedTool.SearchRegion; set => TypedTool.SearchRegion = value; }
        public int SearchRegionX { get => TypedTool.SearchRegionX; set => TypedTool.SearchRegionX = value; }
        public int SearchRegionY { get => TypedTool.SearchRegionY; set => TypedTool.SearchRegionY = value; }
        public int SearchRegionWidth { get => TypedTool.SearchRegionWidth; set => TypedTool.SearchRegionWidth = value; }
        public int SearchRegionHeight { get => TypedTool.SearchRegionHeight; set => TypedTool.SearchRegionHeight = value; }

        public IRelayCommand TrainSelectedModelCommand { get; }
        public IRelayCommand AddModelCommand { get; }
        public IRelayCommand RemoveModelCommand { get; }
        public IRelayCommand DrawSearchRegionCommand { get; }
        public IRelayCommand ClearSearchRegionCommand { get; }

        // ── 학습 컬러 시각화 (SelectedModel 기준) ──
        public int LearnedHue => ComputeAvgHue(HueMin, HueMax);
        public int LearnedSaturation => (SaturationMin + SaturationMax) / 2;
        public int LearnedValue => (ValueMin + ValueMax) / 2;

        public Brush LearnedColorBrush
        {
            get
            {
                using var hsv = new Mat(1, 1, MatType.CV_8UC3,
                    new Scalar(LearnedHue, LearnedSaturation, LearnedValue));
                using var bgr = new Mat();
                Cv2.CvtColor(hsv, bgr, ColorConversionCodes.HSV2BGR);
                var p = bgr.Get<Vec3b>(0, 0);
                return new SolidColorBrush(Color.FromRgb(p.Item2, p.Item1, p.Item0));
            }
        }
        public string LearnedColorText => $"H={LearnedHue}, S={LearnedSaturation}, V={LearnedValue}";

        private static int ComputeAvgHue(int hMin, int hMax)
        {
            if (hMin <= hMax) return (hMin + hMax) / 2;
            return ((hMin + hMax + 180) / 2) % 180;
        }

        private string _trainStatus = "모델을 선택하고 ROI를 그린 후 'Train Selected Model'을 클릭하세요.";
        public string TrainStatus
        {
            get => _trainStatus;
            private set => SetProperty(ref _trainStatus, value);
        }

        /// <summary>
        /// 전문가 모드 — H/S/V 슬라이더 수동 미세조정 노출. 기본은 숨김.
        /// 대부분의 사용자는 Train Selected Model로 자동 학습만 사용하면 충분.
        /// 세션 상태 (직렬화 X).
        /// </summary>
        private bool _isExpertMode;
        public bool IsExpertMode
        {
            get => _isExpertMode;
            set => SetProperty(ref _isExpertMode, value);
        }

        protected override void OnToolPropertyChanged(string? propertyName)
        {
            base.OnToolPropertyChanged(propertyName);
            // SelectedModel 변경 또는 HSV 변경 시 시각화/슬라이더 알림
            if (propertyName is nameof(SelectedModel))
            {
                OnPropertyChanged(nameof(SelectedModel));
            }
            if (propertyName is "HueMin" or "HueMax"
                or "SaturationMin" or "SaturationMax"
                or "ValueMin" or "ValueMax" or nameof(SelectedModel))
            {
                OnPropertyChanged(nameof(LearnedHue));
                OnPropertyChanged(nameof(LearnedSaturation));
                OnPropertyChanged(nameof(LearnedValue));
                OnPropertyChanged(nameof(LearnedColorBrush));
                OnPropertyChanged(nameof(LearnedColorText));
            }
        }

        private void AddModel()
        {
            var m = new ColorModel { Name = $"Model {Models.Count + 1}" };
            Models.Add(m);
            SelectedModel = m;
            TrainStatus = $"Added '{m.Name}'. Draw ROI and click 'Train Selected Model'.";
        }

        private void RemoveModel()
        {
            if (SelectedModel == null || Models.Count <= 1) return;
            int idx = Models.IndexOf(SelectedModel);
            string removedName = SelectedModel.Name;
            Models.RemoveAt(idx);
            SelectedModel = Models[System.Math.Clamp(idx, 0, Models.Count - 1)];
            TrainStatus = $"Removed '{removedName}'.";
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

            TrainStatus = $"'{SelectedModel.Name}' 학습: H={HueMin}-{HueMax}, S={SaturationMin}-{SaturationMax}, V={ValueMin}-{ValueMax}";
        }
    }
}
