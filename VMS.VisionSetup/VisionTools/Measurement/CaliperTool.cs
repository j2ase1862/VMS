using VMS.VisionSetup.Models;
using VMS.VisionSetup.Services;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace VMS.VisionSetup.VisionTools.Measurement
{
    /// <summary>
    /// Caliper 도구 (Cognex VisionPro CogCaliperTool 대체)
    /// 에지 검출을 통한 거리 측정
    /// </summary>
    public class CaliperTool : VisionToolBase
    {
        // 검색 영역 설정
        private Point2d _startPoint = new Point2d(0, 0);
        public Point2d StartPoint
        {
            get => _startPoint;
            set => SetProperty(ref _startPoint, value);
        }

        private Point2d _endPoint = new Point2d(100, 0);
        public Point2d EndPoint
        {
            get => _endPoint;
            set => SetProperty(ref _endPoint, value);
        }

        private double _searchWidth = 20;
        public double SearchWidth
        {
            get => _searchWidth;
            set => SetProperty(ref _searchWidth, Math.Max(1, value));
        }

        // Edge 검출 설정
        private EdgePolarity _polarity = EdgePolarity.DarkToLight;
        public EdgePolarity Polarity
        {
            get => _polarity;
            set => SetProperty(ref _polarity, value);
        }

        private double _edgeThreshold = 30;
        public double EdgeThreshold
        {
            get => _edgeThreshold;
            set => SetProperty(ref _edgeThreshold, Math.Max(1, value));
        }

        private int _filterHalfWidth = 2;
        public int FilterHalfWidth
        {
            get => _filterHalfWidth;
            set => SetProperty(ref _filterHalfWidth, Math.Max(1, value));
        }

        // 측정 모드
        private CaliperMode _mode = CaliperMode.SingleEdge;
        public CaliperMode Mode
        {
            get => _mode;
            set => SetProperty(ref _mode, value);
        }

        // Edge Pair 설정 (Width 측정용)
        private double _expectedWidth = 50;
        public double ExpectedWidth
        {
            get => _expectedWidth;
            set => SetProperty(ref _expectedWidth, Math.Max(1, value));
        }

        private double _widthTolerance = 20;
        public double WidthTolerance
        {
            get => _widthTolerance;
            set => SetProperty(ref _widthTolerance, Math.Max(0, value));
        }

        private int _maxEdges = 10;
        public int MaxEdges
        {
            get => _maxEdges;
            set => SetProperty(ref _maxEdges, Math.Max(1, value));
        }

        // Scorer 설정
        private double _expectedPosition = -1;
        public double ExpectedPosition
        {
            get => _expectedPosition;
            set => SetProperty(ref _expectedPosition, value);
        }

        private double _contrastWeight = 1.0;
        public double ContrastWeight
        {
            get => _contrastWeight;
            set => SetProperty(ref _contrastWeight, Math.Max(0, value));
        }

        private double _positionWeight = 0.0;
        public double PositionWeight
        {
            get => _positionWeight;
            set => SetProperty(ref _positionWeight, Math.Max(0, value));
        }

        private double _positionSigma = 50.0;
        public double PositionSigma
        {
            get => _positionSigma;
            set => SetProperty(ref _positionSigma, Math.Max(1, value));
        }

        private double _polarityWeight = 0.0;
        public double PolarityWeight
        {
            get => _polarityWeight;
            set => SetProperty(ref _polarityWeight, Math.Max(0, value));
        }

        // Scorer 프리셋 모드
        private ScorerMode _scorerMode = ScorerMode.MaxContrast;
        public ScorerMode ScorerMode
        {
            get => _scorerMode;
            set
            {
                if (SetProperty(ref _scorerMode, value))
                    ApplyScorerPreset(value);
            }
        }

        // Profile 시각화 데이터
        public double[]? LastProfile { get; private set; }
        public double[]? LastGradient { get; private set; }

        public CaliperTool()
        {
            Name = "Caliper";
            ToolType = "CaliperTool";
        }

        private void ApplyScorerPreset(ScorerMode mode)
        {
            switch (mode)
            {
                case ScorerMode.MaxContrast:
                    ContrastWeight = 1.0;
                    PositionWeight = 0.0;
                    PolarityWeight = 0.0;
                    break;
                case ScorerMode.Closest:
                    ContrastWeight = 0.0;
                    PositionWeight = 1.0;
                    PolarityWeight = 0.0;
                    break;
                case ScorerMode.BestOverall:
                    ContrastWeight = 1.0;
                    PositionWeight = 1.0;
                    PolarityWeight = 1.0;
                    break;
                case ScorerMode.Custom:
                    break;
            }
        }

        private double CalculateFinalScore(double contrastScore, double positionScore, double polarityScore)
        {
            double totalWeight = ContrastWeight + PositionWeight + PolarityWeight;
            if (totalWeight < 1e-12) totalWeight = 1.0;
            return (ContrastWeight * contrastScore
                  + PositionWeight * positionScore
                  + PolarityWeight * polarityScore) / totalWeight;
        }

        public override VisionResult Execute(Mat inputImage)
        {
            var result = new VisionResult();
            var sw = Stopwatch.StartNew();

            try
            {
                Mat grayImage = new Mat();

                if (inputImage.Channels() > 1)
                    Cv2.CvtColor(inputImage, grayImage, ColorConversionCodes.BGR2GRAY);
                else
                    grayImage = inputImage.Clone();

                // When UseROI is true, derive search line from the ROI rectangle:
                //   longer axis → search direction, shorter axis → search width
                Point2d searchStart, searchEnd;
                double searchWidth;

                if (UseROI && ROI.Width > 0 && ROI.Height > 0)
                {
                    var roi = GetAdjustedROI(inputImage);
                    if (roi.Width >= roi.Height)
                    {
                        // Horizontal search through ROI center
                        double cy = roi.Y + roi.Height / 2.0;
                        searchStart = new Point2d(roi.X, cy);
                        searchEnd = new Point2d(roi.X + roi.Width, cy);
                        searchWidth = roi.Height;
                    }
                    else
                    {
                        // Vertical search through ROI center
                        double cx = roi.X + roi.Width / 2.0;
                        searchStart = new Point2d(cx, roi.Y);
                        searchEnd = new Point2d(cx, roi.Y + roi.Height);
                        searchWidth = roi.Width;
                    }
                }
                else
                {
                    searchStart = StartPoint;
                    searchEnd = EndPoint;
                    searchWidth = SearchWidth;
                }

                // 검색 라인의 방향 계산
                double dx = searchEnd.X - searchStart.X;
                double dy = searchEnd.Y - searchStart.Y;
                double length = Math.Sqrt(dx * dx + dy * dy);

                if (length < 1)
                {
                    result.Success = false;
                    result.Message = "검색 라인이 너무 짧습니다.";
                    return result;
                }

                // 단위 벡터
                double ux = dx / length;
                double uy = dy / length;

                // 수직 벡터
                double vx = -uy;
                double vy = ux;

                // 검색 라인을 따라 프로파일 추출
                var profile = ExtractProfile(grayImage, searchStart, ux, uy, vx, vy, length, searchWidth);

                // Edge 검출
                var edges = DetectEdges(profile, length, out var gradient);

                // Store for visualization
                LastProfile = profile;
                LastGradient = gradient;

                // 결과 이미지 생성 — draw on original color image
                Mat overlayImage = GetColorOverlayBase(inputImage);

                // 검색 영역 표시
                DrawSearchRegion(overlayImage, searchStart, searchEnd, searchWidth, vx, vy);

                if (Mode == CaliperMode.SingleEdge)
                {
                    // 단일 Edge 결과
                    if (edges.Count > 0)
                    {
                        var edge = edges[0];
                        var edgePoint = new Point2d(
                            searchStart.X + ux * edge.SubPixelPosition,
                            searchStart.Y + uy * edge.SubPixelPosition);

                        DrawEdge(overlayImage, edgePoint, vx, vy, searchWidth);

                        result.Data["EdgeX"] = edgePoint.X;
                        result.Data["EdgeY"] = edgePoint.Y;
                        result.Data["EdgeScore"] = edge.Score;
                        result.Data["EdgePolarity"] = edge.Polarity.ToString();

                        result.Graphics.Add(new GraphicOverlay
                        {
                            Type = GraphicType.Line,
                            Position = new Point2d(edgePoint.X + vx * searchWidth / 2, edgePoint.Y + vy * searchWidth / 2),
                            EndPosition = new Point2d(edgePoint.X - vx * searchWidth / 2, edgePoint.Y - vy * searchWidth / 2),
                            Color = new Scalar(0, 255, 0)
                        });

                        result.Success = true;
                        result.Message = $"Edge 검출 완료: ({edgePoint.X:F1}, {edgePoint.Y:F1})";
                    }
                    else
                    {
                        result.Success = false;
                        result.Message = "Edge를 찾을 수 없습니다.";
                    }
                }
                else if (Mode == CaliperMode.EdgePair)
                {
                    // Edge Pair (Width) 측정
                    var pairs = FindEdgePairs(edges, searchStart, ux, uy);

                    if (pairs.Count > 0)
                    {
                        var pair = pairs[0];

                        DrawEdge(overlayImage, pair.Edge1Point, vx, vy, searchWidth, new Scalar(0, 255, 0));
                        DrawEdge(overlayImage, pair.Edge2Point, vx, vy, searchWidth, new Scalar(0, 255, 255));

                        // Width 라인
                        Cv2.Line(overlayImage,
                            new Point((int)pair.Edge1Point.X, (int)pair.Edge1Point.Y),
                            new Point((int)pair.Edge2Point.X, (int)pair.Edge2Point.Y),
                            new Scalar(255, 0, 255), 2);

                        // Width 값 표시
                        var midPoint = new Point(
                            (int)((pair.Edge1Point.X + pair.Edge2Point.X) / 2),
                            (int)((pair.Edge1Point.Y + pair.Edge2Point.Y) / 2));
                        Cv2.PutText(overlayImage, $"{pair.Width:F2}px",
                            new Point(midPoint.X + 5, midPoint.Y - 5),
                            HersheyFonts.HersheySimplex, 0.5, new Scalar(255, 255, 255), 1);

                        result.Data["Width"] = pair.Width;
                        result.Data["Edge1X"] = pair.Edge1Point.X;
                        result.Data["Edge1Y"] = pair.Edge1Point.Y;
                        result.Data["Edge2X"] = pair.Edge2Point.X;
                        result.Data["Edge2Y"] = pair.Edge2Point.Y;
                        result.Data["CenterX"] = (pair.Edge1Point.X + pair.Edge2Point.X) / 2;
                        result.Data["CenterY"] = (pair.Edge1Point.Y + pair.Edge2Point.Y) / 2;

                        result.Success = true;
                        result.Message = $"Width 측정 완료: {pair.Width:F2}px";
                    }
                    else
                    {
                        result.Success = false;
                        result.Message = "Edge Pair를 찾을 수 없습니다.";
                    }
                }

                result.Data["EdgeCount"] = edges.Count;
                result.Data["Edges"] = edges;
                result.OutputImage = grayImage;
                result.OverlayImage = overlayImage;

            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"Caliper 실행 실패: {ex.Message}";
            }

            sw.Stop();
            ExecutionTime = sw.Elapsed.TotalMilliseconds;
            LastResult = result;
            OnPropertyChanged(nameof(LastProfile));
            OnPropertyChanged(nameof(LastGradient));
            return result;
        }

        private Mat GetColorOverlayBase(Mat inputImage)
        {
            var orig = VisionService.Instance.CurrentImage;
            if (orig != null && !orig.Empty()
                && orig.Width == inputImage.Width && orig.Height == inputImage.Height)
            {
                return orig.Channels() >= 3
                    ? orig.Clone()
                    : orig.CvtColor(ColorConversionCodes.GRAY2BGR);
            }
            return inputImage.Channels() >= 3
                ? inputImage.Clone()
                : inputImage.CvtColor(ColorConversionCodes.GRAY2BGR);
        }

        private double[] ExtractProfile(Mat image, Point2d start, double ux, double uy, double vx, double vy, double length, double searchWidth)
        {
            int profileLength = (int)length;
            int stripHeight = Math.Max(1, (int)searchWidth);

            // 3-point affine: map search rectangle corners to rectified strip
            // Source: top-left corner of search rectangle along the search line
            double halfW = searchWidth / 2.0;
            var srcPts = new Point2f[]
            {
                new Point2f((float)(start.X - vx * halfW), (float)(start.Y - vy * halfW)),
                new Point2f((float)(start.X + ux * length - vx * halfW), (float)(start.Y + uy * length - vy * halfW)),
                new Point2f((float)(start.X - vx * halfW + vx * searchWidth), (float)(start.Y - vy * halfW + vy * searchWidth))
            };
            var dstPts = new Point2f[]
            {
                new Point2f(0, 0),
                new Point2f(profileLength, 0),
                new Point2f(0, stripHeight)
            };

            var affine = Cv2.GetAffineTransform(srcPts, dstPts);

            using var strip = new Mat();
            Cv2.WarpAffine(image, strip, affine, new Size(profileLength, stripHeight),
                InterpolationFlags.Linear, BorderTypes.Reflect101);

            // Column average: collapse strip into 1D profile
            using var reduced = new Mat();
            Cv2.Reduce(strip, reduced, ReduceDimension.Row, ReduceTypes.Avg, MatType.CV_64F);

            var profile = new double[profileLength];
            for (int i = 0; i < profileLength; i++)
                profile[i] = reduced.At<double>(0, i);

            return profile;
        }

        private List<EdgeResult> DetectEdges(double[] profile, double length, out double[] gradient)
        {
            var edges = new List<EdgeResult>();

            // Sobel 필터로 그래디언트 계산
            gradient = new double[profile.Length];
            for (int i = FilterHalfWidth; i < profile.Length - FilterHalfWidth; i++)
            {
                double sum = 0;
                for (int j = -FilterHalfWidth; j <= FilterHalfWidth; j++)
                {
                    sum += profile[i + j] * j;
                }
                gradient[i] = sum / (FilterHalfWidth * 2 + 1);
            }

            // Use absolute gradient values for finding max
            var absGradient = new double[gradient.Length];
            for (int i = 0; i < gradient.Length; i++)
                absGradient[i] = Math.Abs(gradient[i]);

            // When PolarityWeight > 0, relax strict polarity filtering to let scorer rank
            bool useScorerPolarity = PolarityWeight > 0;

            // Edge 검출 (Local Maxima/Minima)
            var candidates = new List<EdgeResult>();
            for (int i = FilterHalfWidth + 1; i < gradient.Length - FilterHalfWidth - 1; i++)
            {
                double g = gradient[i];
                bool isLocalExtreme = false;
                EdgePolarity polarity = EdgePolarity.DarkToLight;

                // Dark to Light (positive gradient peak)
                if (g > 0 && Math.Abs(g) > EdgeThreshold)
                {
                    if (g > gradient[i - 1] && g > gradient[i + 1])
                    {
                        isLocalExtreme = true;
                        polarity = EdgePolarity.DarkToLight;
                    }
                }
                // Light to Dark (negative gradient peak)
                else if (g < 0 && Math.Abs(g) > EdgeThreshold)
                {
                    if (g < gradient[i - 1] && g < gradient[i + 1])
                    {
                        isLocalExtreme = true;
                        polarity = EdgePolarity.LightToDark;
                    }
                }

                if (isLocalExtreme)
                {
                    // When scorer handles polarity, accept all; otherwise strict filter
                    bool polarityMatch = Polarity == EdgePolarity.Any ||
                        (Polarity == EdgePolarity.DarkToLight && polarity == EdgePolarity.DarkToLight) ||
                        (Polarity == EdgePolarity.LightToDark && polarity == EdgePolarity.LightToDark);

                    if (useScorerPolarity || polarityMatch)
                    {
                        // Sub-pixel interpolation
                        double subPixelPos = ParabolicSubPixel(absGradient, i);

                        candidates.Add(new EdgeResult
                        {
                            Position = i,
                            SubPixelPosition = subPixelPos,
                            Score = Math.Abs(g),
                            Polarity = polarity,
                            PolarityScore = polarityMatch ? 1.0 : 0.0
                        });
                    }
                }
            }

            // Multi-scorer system
            double maxGrad = candidates.Count > 0 ? candidates.Max(e => e.Score) : 1.0;
            if (maxGrad < 1e-12) maxGrad = 1.0;

            double expectedPos = ExpectedPosition >= 0 ? ExpectedPosition : length / 2.0;

            foreach (var edge in candidates)
            {
                // Contrast Score: normalized 0..1
                edge.ContrastScore = edge.Score / maxGrad;

                // Position Score: Gaussian centered on expected position
                double posDiff = edge.SubPixelPosition - expectedPos;
                edge.PositionScore = Math.Exp(-(posDiff * posDiff) / (2.0 * PositionSigma * PositionSigma));

                // PolarityScore already set above

                // Final weighted score
                edge.Score = CalculateFinalScore(edge.ContrastScore, edge.PositionScore, edge.PolarityScore);
            }

            // 점수순 정렬
            edges = candidates.OrderByDescending(e => e.Score).Take(MaxEdges).ToList();

            return edges;
        }

        private List<EdgePairResult> FindEdgePairs(List<EdgeResult> edges, Point2d origin, double ux, double uy)
        {
            var pairs = new List<EdgePairResult>();

            for (int i = 0; i < edges.Count; i++)
            {
                for (int j = i + 1; j < edges.Count; j++)
                {
                    var e1 = edges[i];
                    var e2 = edges[j];

                    // Polarity가 반대인지 확인
                    if ((e1.Polarity == EdgePolarity.DarkToLight && e2.Polarity == EdgePolarity.LightToDark) ||
                        (e1.Polarity == EdgePolarity.LightToDark && e2.Polarity == EdgePolarity.DarkToLight))
                    {
                        double width = Math.Abs(e2.SubPixelPosition - e1.SubPixelPosition);

                        // Width 허용 범위 확인
                        if (Math.Abs(width - ExpectedWidth) <= WidthTolerance)
                        {
                            var first = e1.SubPixelPosition < e2.SubPixelPosition ? e1 : e2;
                            var second = e1.SubPixelPosition < e2.SubPixelPosition ? e2 : e1;

                            pairs.Add(new EdgePairResult
                            {
                                Edge1Point = new Point2d(
                                    origin.X + ux * first.SubPixelPosition,
                                    origin.Y + uy * first.SubPixelPosition),
                                Edge2Point = new Point2d(
                                    origin.X + ux * second.SubPixelPosition,
                                    origin.Y + uy * second.SubPixelPosition),
                                Width = width,
                                Score = (first.Score + second.Score) / 2
                            });
                        }
                    }
                }
            }

            // Width가 ExpectedWidth에 가까운 순으로 정렬
            return pairs.OrderBy(p => Math.Abs(p.Width - ExpectedWidth)).ToList();
        }

        private void DrawSearchRegion(Mat image, Point2d start, Point2d end, double width, double vx, double vy)
        {
            var corners = new Point[]
            {
                new Point((int)(start.X + vx * width / 2), (int)(start.Y + vy * width / 2)),
                new Point((int)(start.X - vx * width / 2), (int)(start.Y - vy * width / 2)),
                new Point((int)(end.X - vx * width / 2), (int)(end.Y - vy * width / 2)),
                new Point((int)(end.X + vx * width / 2), (int)(end.Y + vy * width / 2))
            };

            Cv2.Polylines(image, new[] { corners }, true, new Scalar(128, 128, 128), 3);

            // 중심선
            Cv2.Line(image,
                new Point((int)start.X, (int)start.Y),
                new Point((int)end.X, (int)end.Y),
                new Scalar(0, 128, 255), 1);
        }

        private static double ParabolicSubPixel(double[] data, int index)
        {
            if (index <= 0 || index >= data.Length - 1)
                return index;

            double fPrev = data[index - 1];
            double fCurr = data[index];
            double fNext = data[index + 1];

            double denom = 2.0 * fPrev - 4.0 * fCurr + 2.0 * fNext;
            if (Math.Abs(denom) < 1e-12)
                return index;

            double offset = (fPrev - fNext) / denom;
            offset = Math.Clamp(offset, -0.5, 0.5);
            return index + offset;
        }

        private void DrawEdge(Mat image, Point2d point, double vx, double vy, double width, Scalar? color = null)
        {
            var c = color ?? new Scalar(0, 255, 0);
            Cv2.Line(image,
                new Point((int)(point.X + vx * width / 2), (int)(point.Y + vy * width / 2)),
                new Point((int)(point.X - vx * width / 2), (int)(point.Y - vy * width / 2)),
                c, 3);
        }

        public override VisionToolBase Clone()
        {
            return new CaliperTool
            {
                Name = this.Name,
                ToolType = this.ToolType,
                IsEnabled = this.IsEnabled,
                ROI = this.ROI,
                UseROI = this.UseROI,
                StartPoint = this.StartPoint,
                EndPoint = this.EndPoint,
                SearchWidth = this.SearchWidth,
                Polarity = this.Polarity,
                EdgeThreshold = this.EdgeThreshold,
                FilterHalfWidth = this.FilterHalfWidth,
                Mode = this.Mode,
                ExpectedWidth = this.ExpectedWidth,
                WidthTolerance = this.WidthTolerance,
                MaxEdges = this.MaxEdges,
                ScorerMode = this.ScorerMode,
                ExpectedPosition = this.ExpectedPosition,
                ContrastWeight = this.ContrastWeight,
                PositionWeight = this.PositionWeight,
                PositionSigma = this.PositionSigma,
                PolarityWeight = this.PolarityWeight
            };
        }
    }

    public class EdgeResult
    {
        public double Position { get; set; }
        public double SubPixelPosition { get; set; }
        public double Score { get; set; }
        public double ContrastScore { get; set; }
        public double PositionScore { get; set; }
        public double PolarityScore { get; set; }
        public EdgePolarity Polarity { get; set; }
    }

    public class EdgePairResult
    {
        public Point2d Edge1Point { get; set; }
        public Point2d Edge2Point { get; set; }
        public double Width { get; set; }
        public double Score { get; set; }
    }

    public enum EdgePolarity
    {
        DarkToLight,
        LightToDark,
        Any
    }

    public enum CaliperMode
    {
        SingleEdge,
        EdgePair
    }

    public enum ScorerMode
    {
        MaxContrast,
        Closest,
        BestOverall,
        Custom
    }
}
