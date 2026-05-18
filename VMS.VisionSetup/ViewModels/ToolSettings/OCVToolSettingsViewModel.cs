using CommunityToolkit.Mvvm.Input;
using VMS.VisionSetup.Services;
using VMS.VisionSetup.VisionTools.Identification;

namespace VMS.VisionSetup.ViewModels.ToolSettings
{
    public class OCVToolSettingsViewModel : ToolSettingsViewModelBase
    {
        private OCVTool TypedTool => (OCVTool)Tool;

        public OCVToolSettingsViewModel(OCVTool tool) : base(tool)
        {
            TrainCommand = new RelayCommand(Train);
            ClearLibraryCommand = new RelayCommand(ClearLibrary);
            RemoveCharCommand = new RelayCommand<string>(RemoveChar);
        }

        // Segmentation
        public int MinCharHeight { get => TypedTool.MinCharHeight; set { TypedTool.MinCharHeight = value; OnPropertyChanged(); } }
        public int MaxCharHeight { get => TypedTool.MaxCharHeight; set { TypedTool.MaxCharHeight = value; OnPropertyChanged(); } }
        public int MinCharWidth { get => TypedTool.MinCharWidth; set { TypedTool.MinCharWidth = value; OnPropertyChanged(); } }
        public bool InvertImage { get => TypedTool.InvertImage; set { TypedTool.InvertImage = value; OnPropertyChanged(); } }

        // Matching
        public double MatchThreshold { get => TypedTool.MatchThreshold; set { TypedTool.MatchThreshold = value; OnPropertyChanged(); } }
        public string ExpectedText { get => TypedTool.ExpectedText; set { TypedTool.ExpectedText = value; OnPropertyChanged(); } }

        // Display
        public bool DrawOverlay { get => TypedTool.DrawOverlay; set { TypedTool.DrawOverlay = value; OnPropertyChanged(); } }

        // ── Training ──

        private string _knownString = string.Empty;
        /// <summary>Train 시 ROI 내부 분할 segments에 1:1 대응될 정답 문자열.</summary>
        public string KnownString
        {
            get => _knownString;
            set => SetProperty(ref _knownString, value ?? string.Empty);
        }

        public int LibraryCount => TypedTool.FontLibrary.Count;
        public string LibraryChars => string.Join(" ", TypedTool.FontLibrary.UniqueChars);

        private string _trainStatus = "Not trained.";
        public string TrainStatus
        {
            get => _trainStatus;
            private set => SetProperty(ref _trainStatus, value);
        }

        public IRelayCommand TrainCommand { get; }
        public IRelayCommand ClearLibraryCommand { get; }
        public IRelayCommand<string> RemoveCharCommand { get; }

        private void Train()
        {
            var src = VisionService.Instance.CurrentImage;
            if (src == null || src.Empty()) { TrainStatus = "Load an image first."; return; }
            if (string.IsNullOrEmpty(KnownString)) { TrainStatus = "Known String을 입력하세요."; return; }

            // Training Region = ROI (없으면 전체 이미지)
            OpenCvSharp.Mat? roi = null;
            try
            {
                if (TypedTool.UseROI)
                {
                    int x1 = System.Math.Min(TypedTool.ROIX, TypedTool.ROIX + TypedTool.ROIWidth);
                    int y1 = System.Math.Min(TypedTool.ROIY, TypedTool.ROIY + TypedTool.ROIHeight);
                    int w = System.Math.Abs(TypedTool.ROIWidth);
                    int h = System.Math.Abs(TypedTool.ROIHeight);
                    x1 = System.Math.Clamp(x1, 0, src.Width);
                    y1 = System.Math.Clamp(y1, 0, src.Height);
                    w = System.Math.Min(w, src.Width - x1);
                    h = System.Math.Min(h, src.Height - y1);
                    if (w <= 0 || h <= 0) { TrainStatus = "ROI가 유효하지 않습니다."; return; }
                    roi = new OpenCvSharp.Mat(src, new OpenCvSharp.Rect(x1, y1, w, h));
                }
                else
                {
                    roi = src.Clone();
                }

                bool ok = TypedTool.TrainFromImage(roi, KnownString, out string error);
                TrainStatus = ok
                    ? $"학습 완료 — 총 {LibraryCount}개 템플릿"
                    : $"학습 실패 — {error}";
                OnPropertyChanged(nameof(LibraryCount));
                OnPropertyChanged(nameof(LibraryChars));
            }
            finally { roi?.Dispose(); }
        }

        private void ClearLibrary()
        {
            TypedTool.FontLibrary.Clear();
            TrainStatus = "라이브러리를 비웠습니다.";
            OnPropertyChanged(nameof(LibraryCount));
            OnPropertyChanged(nameof(LibraryChars));
        }

        private void RemoveChar(string? ch)
        {
            if (string.IsNullOrEmpty(ch)) return;
            TypedTool.FontLibrary.RemoveByChar(ch);
            TrainStatus = $"'{ch}' 템플릿을 삭제했습니다.";
            OnPropertyChanged(nameof(LibraryCount));
            OnPropertyChanged(nameof(LibraryChars));
        }
    }
}
