using CommunityToolkit.Mvvm.ComponentModel;
using OpenCvSharp;
using System;
using System.Text.Json.Serialization;
using System.Windows.Media;

namespace VMS.VisionSetup.VisionTools.Color
{
    /// <summary>
    /// ColorExtractTool의 단일 컬러 모델 — 한 HSV 범위 패치.
    /// 여러 모델을 등록해 같은 색의 미묘한 변종(조명·그라데이션·질감)을 견고하게 커버.
    /// 각 모델의 inRange 결과는 Tool의 Execute에서 OR로 합산됨.
    /// </summary>
    public partial class ColorModel : ObservableObject
    {
        [ObservableProperty] private string _name = "Model";
        [ObservableProperty] private bool _isEnabled = true;

        // HSV 범위 (OpenCV: H=0-179, S/V=0-255). 변경 시 AverageBrush도 자동 갱신.
        private int _hueMin = 0;
        public int HueMin
        {
            get => _hueMin;
            set { if (SetProperty(ref _hueMin, Math.Clamp(value, 0, 179))) OnAverageChanged(); }
        }

        private int _hueMax = 179;
        public int HueMax
        {
            get => _hueMax;
            set { if (SetProperty(ref _hueMax, Math.Clamp(value, 0, 179))) OnAverageChanged(); }
        }

        private int _saturationMin = 50;
        public int SaturationMin
        {
            get => _saturationMin;
            set { if (SetProperty(ref _saturationMin, Math.Clamp(value, 0, 255))) OnAverageChanged(); }
        }

        private int _saturationMax = 255;
        public int SaturationMax
        {
            get => _saturationMax;
            set { if (SetProperty(ref _saturationMax, Math.Clamp(value, 0, 255))) OnAverageChanged(); }
        }

        private int _valueMin = 50;
        public int ValueMin
        {
            get => _valueMin;
            set { if (SetProperty(ref _valueMin, Math.Clamp(value, 0, 255))) OnAverageChanged(); }
        }

        private int _valueMax = 255;
        public int ValueMax
        {
            get => _valueMax;
            set { if (SetProperty(ref _valueMax, Math.Clamp(value, 0, 255))) OnAverageChanged(); }
        }

        /// <summary>마지막 Execute에서 이 모델이 매칭한 픽셀 수 (UI 인라인 표시용).</summary>
        [ObservableProperty] private long _lastPixelCount;

        private void OnAverageChanged()
        {
            OnPropertyChanged(nameof(AverageBrush));
            OnPropertyChanged(nameof(AverageHsvText));
        }

        /// <summary>HSV 평균을 BGR로 변환한 WPF Brush. ListBox 미리보기용.</summary>
        [JsonIgnore]
        public Brush AverageBrush
        {
            get
            {
                int avgH = AvgHue();
                int avgS = (SaturationMin + SaturationMax) / 2;
                int avgV = (ValueMin + ValueMax) / 2;
                using var hsv = new Mat(1, 1, MatType.CV_8UC3, new Scalar(avgH, avgS, avgV));
                using var bgr = new Mat();
                Cv2.CvtColor(hsv, bgr, ColorConversionCodes.HSV2BGR);
                var p = bgr.Get<Vec3b>(0, 0);
                return new SolidColorBrush(System.Windows.Media.Color.FromRgb(p.Item2, p.Item1, p.Item0));
            }
        }

        [JsonIgnore]
        public string AverageHsvText
        {
            get
            {
                int avgH = AvgHue();
                int avgS = (SaturationMin + SaturationMax) / 2;
                int avgV = (ValueMin + ValueMax) / 2;
                return $"H={avgH} S={avgS} V={avgV}";
            }
        }

        private int AvgHue() =>
            HueMin <= HueMax ? (HueMin + HueMax) / 2 : ((HueMin + HueMax + 180) / 2) % 180;

        public ColorModel Clone() => new()
        {
            Name = Name,
            IsEnabled = IsEnabled,
            HueMin = HueMin,
            HueMax = HueMax,
            SaturationMin = SaturationMin,
            SaturationMax = SaturationMax,
            ValueMin = ValueMin,
            ValueMax = ValueMax
        };
    }
}
