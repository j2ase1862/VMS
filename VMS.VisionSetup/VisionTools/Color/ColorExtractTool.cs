using CommunityToolkit.Mvvm.ComponentModel;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using VMS.VisionSetup.Models;

namespace VMS.VisionSetup.VisionTools.Color
{
    /// <summary>
    /// HSV 범위 기반 컬러 추출 도구 (Cognex CogColorExtractorTool 대응).
    /// 다중 모델 지원: 같은 색의 미묘한 변종(조명·그라데이션·질감)을 별도 모델로 등록.
    /// Execute는 활성 모델들의 inRange 결과를 OR로 합산.
    /// Training Region(UseROI)에서 SelectedModel을 학습. Search Region(UseSearchRegion)에서 매칭 수행.
    /// </summary>
    public class ColorExtractTool : VisionToolBase, ISearchRegionTool
    {
        // ── 모델 컬렉션 ──
        public ObservableCollection<ColorModel> Models { get; } = new();

        private ColorModel? _selectedModel;
        public ColorModel? SelectedModel
        {
            get => _selectedModel;
            set
            {
                if (_selectedModel != null)
                    _selectedModel.PropertyChanged -= OnSelectedModelPropertyChanged;
                if (SetProperty(ref _selectedModel, value))
                {
                    if (_selectedModel != null)
                        _selectedModel.PropertyChanged += OnSelectedModelPropertyChanged;
                    NotifyHsvForwardingChanged();
                }
            }
        }

        private void OnSelectedModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            // SelectedModel.* → Tool.* 알림 전파 (UI 슬라이더가 즉시 갱신)
            switch (e.PropertyName)
            {
                case nameof(ColorModel.HueMin): OnPropertyChanged(nameof(HueMin)); break;
                case nameof(ColorModel.HueMax): OnPropertyChanged(nameof(HueMax)); break;
                case nameof(ColorModel.SaturationMin): OnPropertyChanged(nameof(SaturationMin)); break;
                case nameof(ColorModel.SaturationMax): OnPropertyChanged(nameof(SaturationMax)); break;
                case nameof(ColorModel.ValueMin): OnPropertyChanged(nameof(ValueMin)); break;
                case nameof(ColorModel.ValueMax): OnPropertyChanged(nameof(ValueMax)); break;
            }
        }

        private void NotifyHsvForwardingChanged()
        {
            OnPropertyChanged(nameof(HueMin));
            OnPropertyChanged(nameof(HueMax));
            OnPropertyChanged(nameof(SaturationMin));
            OnPropertyChanged(nameof(SaturationMax));
            OnPropertyChanged(nameof(ValueMin));
            OnPropertyChanged(nameof(ValueMax));
        }

        // ── HSV 프로퍼티: SelectedModel 포워딩 (기존 호환) ──
        public int HueMin
        {
            get => SelectedModel?.HueMin ?? 0;
            set { if (SelectedModel != null) SelectedModel.HueMin = value; }
        }
        public int HueMax
        {
            get => SelectedModel?.HueMax ?? 179;
            set { if (SelectedModel != null) SelectedModel.HueMax = value; }
        }
        public int SaturationMin
        {
            get => SelectedModel?.SaturationMin ?? 50;
            set { if (SelectedModel != null) SelectedModel.SaturationMin = value; }
        }
        public int SaturationMax
        {
            get => SelectedModel?.SaturationMax ?? 255;
            set { if (SelectedModel != null) SelectedModel.SaturationMax = value; }
        }
        public int ValueMin
        {
            get => SelectedModel?.ValueMin ?? 50;
            set { if (SelectedModel != null) SelectedModel.ValueMin = value; }
        }
        public int ValueMax
        {
            get => SelectedModel?.ValueMax ?? 255;
            set { if (SelectedModel != null) SelectedModel.ValueMax = value; }
        }

        // ── 후처리/표시 ──
        private int _morphKernelSize = 3;
        public int MorphKernelSize
        {
            get => _morphKernelSize;
            set => SetProperty(ref _morphKernelSize, Math.Clamp(value, 0, 31));
        }

        private bool _invertMask;
        public bool InvertMask { get => _invertMask; set => SetProperty(ref _invertMask, value); }

        private bool _showOverlay = true;
        public bool ShowOverlay { get => _showOverlay; set => SetProperty(ref _showOverlay, value); }

        private double _overlayOpacity = 0.4;
        public double OverlayOpacity
        {
            get => _overlayOpacity;
            set => SetProperty(ref _overlayOpacity, Math.Clamp(value, 0.0, 1.0));
        }

        // ── Search Region ──
        private Rect _searchRegion;
        public Rect SearchRegion
        {
            get => _searchRegion;
            set
            {
                if (SetProperty(ref _searchRegion, value))
                {
                    if (!IsFixtureTransformActive)
                        HasFixtureBaseSearchRegion = false;
                    OnPropertyChanged(nameof(SearchRegionX));
                    OnPropertyChanged(nameof(SearchRegionY));
                    OnPropertyChanged(nameof(SearchRegionWidth));
                    OnPropertyChanged(nameof(SearchRegionHeight));
                }
            }
        }
        public int SearchRegionX
        {
            get => _searchRegion.X;
            set { SearchRegion = new Rect(value, _searchRegion.Y, _searchRegion.Width, _searchRegion.Height); }
        }
        public int SearchRegionY
        {
            get => _searchRegion.Y;
            set { SearchRegion = new Rect(_searchRegion.X, value, _searchRegion.Width, _searchRegion.Height); }
        }
        public int SearchRegionWidth
        {
            get => _searchRegion.Width;
            set { SearchRegion = new Rect(_searchRegion.X, _searchRegion.Y, value, _searchRegion.Height); }
        }
        public int SearchRegionHeight
        {
            get => _searchRegion.Height;
            set { SearchRegion = new Rect(_searchRegion.X, _searchRegion.Y, _searchRegion.Width, value); }
        }

        private bool _useSearchRegion;
        public bool UseSearchRegion { get => _useSearchRegion; set => SetProperty(ref _useSearchRegion, value); }

        private ROIShape? _associatedSearchRegionShape;
        public ROIShape? AssociatedSearchRegionShape
        {
            get => _associatedSearchRegionShape;
            set => SetProperty(ref _associatedSearchRegionShape, value);
        }

        public ColorExtractTool()
        {
            Name = "Color Extract";
            ToolType = "ColorExtractTool";

            // 기본 모델 1개로 시작 (단일 모델 사용자 호환)
            var first = new ColorModel { Name = "Model 1" };
            Models.Add(first);
            SelectedModel = first;
        }

        /// <summary>
        /// SelectedModel을 ROI 영역에서 학습 (평균 HSV ± 2.5σ).
        /// Hue 원형 처리, 최소 spread 보장.
        /// </summary>
        public bool TrainFromImage(Mat src)
        {
            if (src == null || src.Empty()) return false;
            if (SelectedModel == null) return false;

            Mat region;
            if (UseROI)
            {
                var adj = GetAdjustedROI(src);
                if (adj.Width < 4 || adj.Height < 4) return false;
                region = new Mat(src, adj);
            }
            else
            {
                region = src.Clone();
            }

            try
            {
                using var bgr = region.Channels() >= 3
                    ? region.Clone()
                    : region.CvtColor(ColorConversionCodes.GRAY2BGR);
                using var hsv = new Mat();
                Cv2.CvtColor(bgr, hsv, ColorConversionCodes.BGR2HSV);

                Cv2.MeanStdDev(hsv, out var mean, out var stddev);
                const double k = 2.5;
                int hMean = (int)Math.Round(mean.Val0);
                int sMean = (int)Math.Round(mean.Val1);
                int vMean = (int)Math.Round(mean.Val2);
                int hSpread = Math.Max(5, (int)Math.Round(stddev.Val0 * k));
                int sSpread = Math.Max(20, (int)Math.Round(stddev.Val1 * k));
                int vSpread = Math.Max(20, (int)Math.Round(stddev.Val2 * k));

                if (hSpread >= 90)
                {
                    SelectedModel.HueMin = 0;
                    SelectedModel.HueMax = 179;
                }
                else
                {
                    SelectedModel.HueMin = ((hMean - hSpread) % 180 + 180) % 180;
                    SelectedModel.HueMax = ((hMean + hSpread) % 180 + 180) % 180;
                }

                SelectedModel.SaturationMin = Math.Clamp(sMean - sSpread, 0, 255);
                SelectedModel.SaturationMax = Math.Clamp(sMean + sSpread, 0, 255);
                SelectedModel.ValueMin = Math.Clamp(vMean - vSpread, 0, 255);
                SelectedModel.ValueMax = Math.Clamp(vMean + vSpread, 0, 255);
                return true;
            }
            catch
            {
                return false;
            }
            finally
            {
                if (region != src) region.Dispose();
            }
        }

        public override VisionResult Execute(Mat inputImage)
        {
            var result = new VisionResult();
            var sw = Stopwatch.StartNew();

            try
            {
                // Search Region 적용
                Rect searchRect;
                Mat workArea;
                if (UseSearchRegion && SearchRegion.Width > 0 && SearchRegion.Height > 0)
                {
                    searchRect = ClipRect(SearchRegion, inputImage.Width, inputImage.Height);
                    if (searchRect.Width <= 0 || searchRect.Height <= 0)
                    {
                        result.Success = false;
                        result.Message = "Search region is outside the image.";
                        return result;
                    }
                    workArea = new Mat(inputImage, searchRect);
                }
                else
                {
                    searchRect = new Rect(0, 0, inputImage.Width, inputImage.Height);
                    workArea = inputImage.Clone();
                }

                using var roi = workArea;
                using var bgr = roi.Channels() >= 3
                    ? roi.Clone()
                    : roi.CvtColor(ColorConversionCodes.GRAY2BGR);
                using var hsv = new Mat();
                Cv2.CvtColor(bgr, hsv, ColorConversionCodes.BGR2HSV);

                // 활성 모델별 마스크 → OR 합산. 모델별 LastPixelCount도 UI 표시용으로 set.
                using var combined = new Mat(hsv.Size(), MatType.CV_8UC1, Scalar.All(0));
                var perModel = new List<(string Name, long Pixels)>();
                int enabledCount = 0;
                for (int i = 0; i < Models.Count; i++)
                {
                    var m = Models[i];
                    if (!m.IsEnabled)
                    {
                        m.LastPixelCount = 0;
                        continue;
                    }
                    enabledCount++;
                    using var mask = ComputeMaskForModel(hsv, m);
                    long count = Cv2.CountNonZero(mask);
                    m.LastPixelCount = count;
                    perModel.Add((m.Name, count));
                    Cv2.BitwiseOr(combined, mask, combined);
                }

                if (enabledCount == 0)
                {
                    result.Success = false;
                    result.Message = "No enabled color model. Enable at least one model.";
                    return result;
                }

                try
                {
                    // 후처리
                    if (MorphKernelSize >= 3)
                    {
                        var k = MorphKernelSize | 1;
                        using var kernel = Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(k, k));
                        Cv2.MorphologyEx(combined, combined, MorphTypes.Open, kernel);
                        Cv2.MorphologyEx(combined, combined, MorphTypes.Close, kernel);
                    }
                    if (InvertMask) Cv2.BitwiseNot(combined, combined);

                    // 통계
                    long totalPx = combined.Width * (long)combined.Height;
                    long matchedPx = Cv2.CountNonZero(combined);
                    double ratio = totalPx > 0 ? (double)matchedPx / totalPx : 0;

                    result.Data["MatchedPixels"] = matchedPx;
                    result.Data["TotalPixels"] = totalPx;
                    result.Data["AreaRatio"] = ratio;
                    result.Data["EnabledModelCount"] = enabledCount;
                    for (int i = 0; i < perModel.Count; i++)
                    {
                        result.Data[$"Model{i}_Name"] = perModel[i].Name;
                        result.Data[$"Model{i}_Pixels"] = perModel[i].Pixels;
                        result.Data[$"Model{i}_AreaRatio"] = totalPx > 0 ? (double)perModel[i].Pixels / totalPx : 0;
                    }

                    // 오버레이
                    Mat overlay = ShowOverlay
                        ? BuildOverlay(inputImage, combined, searchRect)
                        : GetColorOverlayBase(inputImage);

                    var output = new Mat(inputImage.Size(), MatType.CV_8UC1, Scalar.All(0));
                    if (searchRect.Width > 0 && searchRect.Height > 0
                        && combined.Width == searchRect.Width && combined.Height == searchRect.Height)
                    {
                        using var dest = new Mat(output, searchRect);
                        combined.CopyTo(dest);
                    }
                    result.OutputImage = output;
                    result.OverlayImage = overlay;
                    result.Success = true;
                    result.Message = $"Extracted {matchedPx}/{totalPx} px ({ratio * 100:F2}%) from {enabledCount} model(s)";
                }
                finally { /* combined은 using으로 자동 dispose */ }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"Color extract failed: {ex.Message}";
            }
            finally
            {
                sw.Stop();
                ExecutionTime = sw.Elapsed.TotalMilliseconds;
                LastResult = result;
            }
            return result;
        }

        /// <summary>
        /// 단일 모델의 HSV 범위로 inRange 마스크 생성. Hue 원형(Min>Max)이면 두 구간 OR.
        /// </summary>
        private static Mat ComputeMaskForModel(Mat hsv, ColorModel m)
        {
            if (m.HueMin <= m.HueMax)
            {
                var mask = new Mat();
                Cv2.InRange(hsv,
                    new Scalar(m.HueMin, m.SaturationMin, m.ValueMin),
                    new Scalar(m.HueMax, m.SaturationMax, m.ValueMax),
                    mask);
                return mask;
            }
            using var m1 = new Mat();
            using var m2 = new Mat();
            Cv2.InRange(hsv,
                new Scalar(m.HueMin, m.SaturationMin, m.ValueMin),
                new Scalar(179, m.SaturationMax, m.ValueMax), m1);
            Cv2.InRange(hsv,
                new Scalar(0, m.SaturationMin, m.ValueMin),
                new Scalar(m.HueMax, m.SaturationMax, m.ValueMax), m2);
            var result = new Mat();
            Cv2.BitwiseOr(m1, m2, result);
            return result;
        }

        private Mat BuildOverlay(Mat inputImage, Mat mask, Rect searchRect)
        {
            var baseImg = GetColorOverlayBase(inputImage);

            using var fullMask = new Mat(inputImage.Size(), MatType.CV_8UC1, Scalar.All(0));
            if (searchRect.Width > 0 && searchRect.Height > 0
                && mask.Width == searchRect.Width && mask.Height == searchRect.Height)
            {
                using var dest = new Mat(fullMask, searchRect);
                mask.CopyTo(dest);
            }

            using var colorLayer = new Mat(inputImage.Size(), MatType.CV_8UC3, new Scalar(255, 0, 255));
            using var blended = new Mat();
            Cv2.AddWeighted(baseImg, 1.0 - OverlayOpacity, colorLayer, OverlayOpacity, 0, blended);

            using var inv = new Mat();
            Cv2.BitwiseNot(fullMask, inv);
            baseImg.CopyTo(blended, inv);

            return blended.Clone();
        }

        public override List<string> GetAvailableResultKeys()
        {
            var keys = new List<string>
            {
                "Success", "MatchedPixels", "TotalPixels", "AreaRatio", "EnabledModelCount"
            };
            // 모델별 키 (실제 모델 수만큼)
            for (int i = 0; i < Math.Max(Models.Count, 4); i++)
            {
                keys.Add($"Model{i}_Name");
                keys.Add($"Model{i}_Pixels");
                keys.Add($"Model{i}_AreaRatio");
            }
            return keys;
        }

        private static Rect ClipRect(Rect r, int imgW, int imgH)
        {
            int x1 = Math.Min(r.X, r.X + r.Width);
            int y1 = Math.Min(r.Y, r.Y + r.Height);
            int x2 = Math.Max(r.X, r.X + r.Width);
            int y2 = Math.Max(r.Y, r.Y + r.Height);
            int sx = Math.Clamp(x1, 0, imgW);
            int sy = Math.Clamp(y1, 0, imgH);
            int ex = Math.Clamp(x2, 0, imgW);
            int ey = Math.Clamp(y2, 0, imgH);
            return new Rect(sx, sy, ex - sx, ey - sy);
        }

        public override VisionToolBase Clone()
        {
            var clone = new ColorExtractTool
            {
                Name = this.Name,
                ToolType = this.ToolType,
                IsEnabled = this.IsEnabled,
                ROI = this.ROI,
                UseROI = this.UseROI,
                ROIAngle = this.ROIAngle,
                ROICenterX = this.ROICenterX,
                ROICenterY = this.ROICenterY,
                MorphKernelSize = this.MorphKernelSize,
                InvertMask = this.InvertMask,
                ShowOverlay = this.ShowOverlay,
                OverlayOpacity = this.OverlayOpacity,
                SearchRegion = this.SearchRegion,
                UseSearchRegion = this.UseSearchRegion
            };
            // Models 깊은 복사 (생성자에서 기본 1개 추가됐으니 비우고 새로)
            clone.Models.Clear();
            foreach (var m in this.Models)
                clone.Models.Add(m.Clone());
            // 선택 상태 복원 (인덱스 기반)
            int selIdx = SelectedModel != null ? Models.IndexOf(SelectedModel) : 0;
            clone.SelectedModel = clone.Models.Count > 0
                ? clone.Models[Math.Clamp(selIdx, 0, clone.Models.Count - 1)]
                : null;
            CopyPlcMappingsTo(clone);
            return clone;
        }
    }
}
