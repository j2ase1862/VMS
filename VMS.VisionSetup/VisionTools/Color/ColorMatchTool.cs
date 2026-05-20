using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using VMS.VisionSetup.Models;

namespace VMS.VisionSetup.VisionTools.Color
{
    /// <summary>
    /// Lab 공간 ΔE 거리 기반 컬러 매칭 도구 (Cognex CogColorMatchTool 대응).
    /// 다중 모델 지원: 학습된 컬러 패치 여러 개와 픽셀 ΔE76 거리 비교 → Tolerance 이내면 매칭.
    /// HSV inRange보다 조명 변화에 강건. 색상 유사도 점수 기반.
    /// Training Region(UseROI)에서 SelectedModel 학습. Search Region(UseSearchRegion)에서 매칭.
    /// </summary>
    public class ColorMatchTool : VisionToolBase, ISearchRegionTool
    {
        // ── 모델 컬렉션 ──
        public ObservableCollection<ColorMatchModel> Models { get; } = new();

        private ColorMatchModel? _selectedModel;
        public ColorMatchModel? SelectedModel
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
                    NotifyLabForwardingChanged();
                }
            }
        }

        private void OnSelectedModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(ColorMatchModel.MeanL): OnPropertyChanged(nameof(MeanL)); break;
                case nameof(ColorMatchModel.MeanA): OnPropertyChanged(nameof(MeanA)); break;
                case nameof(ColorMatchModel.MeanB): OnPropertyChanged(nameof(MeanB)); break;
            }
        }

        private void NotifyLabForwardingChanged()
        {
            OnPropertyChanged(nameof(MeanL));
            OnPropertyChanged(nameof(MeanA));
            OnPropertyChanged(nameof(MeanB));
        }

        // ── SelectedModel 포워딩 (전문가 모드 UI용) ──
        public int MeanL
        {
            get => SelectedModel?.MeanL ?? 0;
            set { if (SelectedModel != null) SelectedModel.MeanL = value; }
        }
        public int MeanA
        {
            get => SelectedModel?.MeanA ?? 128;
            set { if (SelectedModel != null) SelectedModel.MeanA = value; }
        }
        public int MeanB
        {
            get => SelectedModel?.MeanB ?? 128;
            set { if (SelectedModel != null) SelectedModel.MeanB = value; }
        }

        // ── 매칭 임계값 ──
        private double _colorTolerance = 25.0;
        /// <summary>ΔE76 임계값 (Lab 거리). 이 값 이하인 픽셀이 매칭.</summary>
        public double ColorTolerance
        {
            get => _colorTolerance;
            set => SetProperty(ref _colorTolerance, Math.Clamp(value, 0.0, 200.0));
        }

        // ── 후처리 / 표시 ──
        private int _morphKernelSize = 3;
        public int MorphKernelSize
        {
            get => _morphKernelSize;
            set => SetProperty(ref _morphKernelSize, Math.Clamp(value, 0, 31));
        }

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

        public ColorMatchTool()
        {
            Name = "Color Match";
            ToolType = "ColorMatchTool";

            var first = new ColorMatchModel { Name = "Model 1" };
            Models.Add(first);
            SelectedModel = first;
        }

        /// <summary>
        /// 단일 픽셀 BGR을 Lab으로 변환해 SelectedModel에 적용.
        /// 호출자가 이미지 클릭 좌표에서 BGR을 추출해 전달.
        /// </summary>
        public bool PickFromPixel(byte b, byte g, byte r)
        {
            if (SelectedModel == null) return false;
            try
            {
                using var bgrMat = new Mat(1, 1, MatType.CV_8UC3, new Scalar(b, g, r));
                using var labMat = new Mat();
                Cv2.CvtColor(bgrMat, labMat, ColorConversionCodes.BGR2Lab);
                var p = labMat.Get<Vec3b>(0, 0);
                SelectedModel.MeanL = p.Item0;
                SelectedModel.MeanA = p.Item1;
                SelectedModel.MeanB = p.Item2;
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>SelectedModel을 Training Region에서 학습 — 평균 Lab.</summary>
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
                using var lab = new Mat();
                Cv2.CvtColor(bgr, lab, ColorConversionCodes.BGR2Lab);
                var mean = Cv2.Mean(lab);
                SelectedModel.MeanL = (int)Math.Round(mean.Val0);
                SelectedModel.MeanA = (int)Math.Round(mean.Val1);
                SelectedModel.MeanB = (int)Math.Round(mean.Val2);
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
                using var lab8 = new Mat();
                Cv2.CvtColor(bgr, lab8, ColorConversionCodes.BGR2Lab);
                // CV_32F로 변환해 부호 차이 처리
                using var labF = new Mat();
                lab8.ConvertTo(labF, MatType.CV_32FC3);

                using var combined = new Mat(lab8.Size(), MatType.CV_8UC1, Scalar.All(0));
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
                    using var mask = ComputeMatchMask(labF, m, ColorTolerance);
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

                // 후처리
                if (MorphKernelSize >= 3)
                {
                    var k = MorphKernelSize | 1;
                    using var kernel = Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(k, k));
                    Cv2.MorphologyEx(combined, combined, MorphTypes.Open, kernel);
                    Cv2.MorphologyEx(combined, combined, MorphTypes.Close, kernel);
                }

                // 통계
                long totalPx = combined.Width * (long)combined.Height;
                long matchedPx = Cv2.CountNonZero(combined);
                double ratio = totalPx > 0 ? (double)matchedPx / totalPx : 0;

                result.Data["MatchedPixels"] = matchedPx;
                result.Data["TotalPixels"] = totalPx;
                result.Data["AreaRatio"] = ratio;
                result.Data["EnabledModelCount"] = enabledCount;
                result.Data["ColorTolerance"] = ColorTolerance;
                for (int i = 0; i < perModel.Count; i++)
                {
                    result.Data[$"Model{i}_Name"] = perModel[i].Name;
                    result.Data[$"Model{i}_Pixels"] = perModel[i].Pixels;
                    result.Data[$"Model{i}_AreaRatio"] = totalPx > 0 ? (double)perModel[i].Pixels / totalPx : 0;
                }

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
                result.Message = $"Matched {matchedPx}/{totalPx} px ({ratio * 100:F2}%) from {enabledCount} model(s) @ ΔE≤{ColorTolerance:F1}";
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"Color match failed: {ex.Message}";
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
        /// 단일 모델의 Lab 평균과 픽셀 Lab 사이 ΔE76 거리 계산 후 임계값 이하 픽셀 마스크 생성.
        /// labF: CV_32FC3 (BGR→Lab 후 float 변환된 입력)
        /// </summary>
        private static Mat ComputeMatchMask(Mat labF, ColorMatchModel m, double tolerance)
        {
            using var diff = new Mat();
            Cv2.Subtract(labF, new Scalar(m.MeanL, m.MeanA, m.MeanB), diff);
            using var sq = new Mat();
            Cv2.Multiply(diff, diff, sq);

            // 채널 합 (L² + a² + b²)
            var channels = Cv2.Split(sq);
            try
            {
                using var sum = new Mat();
                Cv2.Add(channels[0], channels[1], sum);
                Cv2.Add(sum, channels[2], sum);

                using var deltaE = new Mat();
                Cv2.Sqrt(sum, deltaE);

                // threshold: ΔE ≤ tolerance → 255, else 0
                using var inv = new Mat();
                Cv2.Threshold(deltaE, inv, tolerance, 255, ThresholdTypes.BinaryInv);

                var mask = new Mat();
                inv.ConvertTo(mask, MatType.CV_8UC1);
                return mask;
            }
            finally
            {
                foreach (var c in channels) c.Dispose();
            }
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

            using var colorLayer = new Mat(inputImage.Size(), MatType.CV_8UC3, new Scalar(0, 255, 0));
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
                "Success", "MatchedPixels", "TotalPixels", "AreaRatio", "EnabledModelCount", "ColorTolerance"
            };
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
            var clone = new ColorMatchTool
            {
                Name = this.Name,
                ToolType = this.ToolType,
                IsEnabled = this.IsEnabled,
                ROI = this.ROI,
                UseROI = this.UseROI,
                ROIAngle = this.ROIAngle,
                ROICenterX = this.ROICenterX,
                ROICenterY = this.ROICenterY,
                ColorTolerance = this.ColorTolerance,
                MorphKernelSize = this.MorphKernelSize,
                ShowOverlay = this.ShowOverlay,
                OverlayOpacity = this.OverlayOpacity,
                SearchRegion = this.SearchRegion,
                UseSearchRegion = this.UseSearchRegion
            };
            clone.Models.Clear();
            foreach (var m in this.Models)
                clone.Models.Add(m.Clone());
            int selIdx = SelectedModel != null ? Models.IndexOf(SelectedModel) : 0;
            clone.SelectedModel = clone.Models.Count > 0
                ? clone.Models[Math.Clamp(selIdx, 0, clone.Models.Count - 1)]
                : null;
            CopyPlcMappingsTo(clone);
            return clone;
        }
    }
}
