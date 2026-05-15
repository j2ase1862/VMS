using CommunityToolkit.Mvvm.ComponentModel;
using OpenCvSharp;
using System;
using System.Text.Json.Serialization;
using System.Windows.Media;

namespace VMS.VisionSetup.VisionTools.Color
{
    /// <summary>
    /// ColorMatchTool의 단일 학습 컬러 — OpenCV 8-bit Lab 공간의 평균 (L, a, b).
    /// Execute는 픽셀 Lab과 각 모델의 (L, a, b) 사이 ΔE76 거리를 계산하여 Tolerance 이내인 픽셀을 매칭.
    /// HSV inRange보다 조명 변화에 강건 (Lab는 인지 균일 색공간).
    /// </summary>
    public partial class ColorMatchModel : ObservableObject
    {
        [ObservableProperty] private string _name = "Model";
        [ObservableProperty] private bool _isEnabled = true;

        // OpenCV 8-bit Lab (L: 0-255, a/b: 0-255 with offset 128 — 표준 Lab 스케일과 다르나 거리 의미 유지)
        private int _meanL;
        public int MeanL
        {
            get => _meanL;
            set { if (SetProperty(ref _meanL, Math.Clamp(value, 0, 255))) OnAverageChanged(); }
        }

        private int _meanA = 128;
        public int MeanA
        {
            get => _meanA;
            set { if (SetProperty(ref _meanA, Math.Clamp(value, 0, 255))) OnAverageChanged(); }
        }

        private int _meanB = 128;
        public int MeanB
        {
            get => _meanB;
            set { if (SetProperty(ref _meanB, Math.Clamp(value, 0, 255))) OnAverageChanged(); }
        }

        /// <summary>마지막 Execute에서 이 모델이 매칭한 픽셀 수 (UI 인라인 표시).</summary>
        [ObservableProperty] private long _lastPixelCount;

        private void OnAverageChanged()
        {
            OnPropertyChanged(nameof(AverageBrush));
            OnPropertyChanged(nameof(AverageLabText));
        }

        /// <summary>학습 평균 Lab를 BGR로 변환한 WPF Brush. ListBox 미리보기용.</summary>
        [JsonIgnore]
        public Brush AverageBrush
        {
            get
            {
                using var lab = new Mat(1, 1, MatType.CV_8UC3, new Scalar(MeanL, MeanA, MeanB));
                using var bgr = new Mat();
                Cv2.CvtColor(lab, bgr, ColorConversionCodes.Lab2BGR);
                var p = bgr.Get<Vec3b>(0, 0);
                return new SolidColorBrush(System.Windows.Media.Color.FromRgb(p.Item2, p.Item1, p.Item0));
            }
        }

        [JsonIgnore]
        public string AverageLabText => $"L={MeanL} a={MeanA} b={MeanB}";

        public ColorMatchModel Clone() => new()
        {
            Name = Name,
            IsEnabled = IsEnabled,
            MeanL = MeanL,
            MeanA = MeanA,
            MeanB = MeanB
        };
    }
}
