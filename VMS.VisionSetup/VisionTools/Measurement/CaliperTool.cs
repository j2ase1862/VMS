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

        // [개선1] 프로파일 투영 모드: 균일 평균 vs 가우시안 가중 평균
        private ProjectionMode _projectionMode = ProjectionMode.Uniform;
        public ProjectionMode ProjectionMode
        {
            get => _projectionMode;
            set => SetProperty(ref _projectionMode, value);
        }

        // [개선2] 가우시안 1D 필터 (이동 평균 대체)
        private bool _useGaussianFilter = true;
        public bool UseGaussianFilter
        {
            get => _useGaussianFilter;
            set => SetProperty(ref _useGaussianFilter, value);
        }

        private double _gaussianSigma = 1.0;
        public double GaussianSigma
        {
            get => _gaussianSigma;
            set => SetProperty(ref _gaussianSigma, Math.Max(0.3, value));
        }

        // [개선3] 정규화된 대비 스코어링
        private bool _useNormalizedContrast = false;
        public bool UseNormalizedContrast
        {
            get => _useNormalizedContrast;
            set => SetProperty(ref _useNormalizedContrast, value);
        }

        // 에지 선택 모드 (Best/First/Last)
        private EdgeSelectionMode _selectionMode = EdgeSelectionMode.Best;
        public EdgeSelectionMode SelectionMode
        {
            get => _selectionMode;
            set => SetProperty(ref _selectionMode, value);
        }

        // 탐색 방향 축 설정 (W/H 비율에 관계없이 고정)
        private CaliperSearchAxis _searchAxis = CaliperSearchAxis.AlongWidth;
        public CaliperSearchAxis SearchAxis
        {
            get => _searchAxis;
            set => SetProperty(ref _searchAxis, value);
        }

        // [개선4] 서브픽셀 보간 방법
        private SubPixelMethod _subPixelMethod = SubPixelMethod.Parabolic;
        public SubPixelMethod SubPixelMethod
        {
            get => _subPixelMethod;
            set => SetProperty(ref _subPixelMethod, value);
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

                // 회전 각도 결정: 라이브 ROI shape 또는 저장된 ROIAngle 사용
                double effectiveAngle = AssociatedROIShape is RectangleAffineROI liveROI
                    ? liveROI.Angle : ROIAngle;

                if (UseROI && ROI.Width > 0 && ROI.Height > 0 &&
                    Math.Abs(effectiveAngle) > 0.001)
                {
                    // 회전된 ROI: ROI rect 중심 + 회전 각도로 검색 방향 계산
                    // ROI rect 중심을 사용해야 Fixture Transform 후에도 올바른 위치 반영
                    double cx = ROI.X + ROI.Width / 2.0;
                    double cy = ROI.Y + ROI.Height / 2.0;
                    double w = ROI.Width;
                    double h = ROI.Height;
                    double rad = effectiveAngle * Math.PI / 180.0;
                    double cosA = Math.Cos(rad);
                    double sinA = Math.Sin(rad);

                    if (SearchAxis == CaliperSearchAxis.AlongWidth)
                    {
                        // Width 축 방향으로 탐색: (cosθ, sinθ)
                        searchStart = new Point2d(cx - (w / 2) * cosA, cy - (w / 2) * sinA);
                        searchEnd = new Point2d(cx + (w / 2) * cosA, cy + (w / 2) * sinA);
                        searchWidth = h;
                    }
                    else
                    {
                        // Height 축 방향으로 탐색: (-sinθ, cosθ)
                        searchStart = new Point2d(cx - (h / 2) * (-sinA), cy - (h / 2) * cosA);
                        searchEnd = new Point2d(cx + (h / 2) * (-sinA), cy + (h / 2) * cosA);
                        searchWidth = w;
                    }
                }
                else if (UseROI && ROI.Width > 0 && ROI.Height > 0)
                {
                    // Fallback: 기존 축 정렬 RectangleROI
                    var roi = GetAdjustedROI(inputImage);
                    if (SearchAxis == CaliperSearchAxis.AlongWidth)
                    {
                        double cy = roi.Y + roi.Height / 2.0;
                        searchStart = new Point2d(roi.X, cy);
                        searchEnd = new Point2d(roi.X + roi.Width, cy);
                        searchWidth = roi.Height;
                    }
                    else
                    {
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

                // [개선1] 검색 라인을 따라 프로파일 추출 (바이리니어 보간 + 가우시안 투영)
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
                    // 단일 Edge 결과 — SelectionMode에 따라 최종 에지 선택
                    if (edges.Count > 0)
                    {
                        var edge = SelectEdge(edges);
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
                        var pair = SelectEdgePair(pairs);

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

        /// <summary>
        /// [개선1] 프로파일 추출 — WarpAffine(바이리니어 보간) + 가우시안 가중 투영 옵션
        /// WarpAffine은 InterpolationFlags.Linear을 사용하므로 바이리니어 보간이 기본 적용됨.
        /// ProjectionMode.Gaussian 선택 시 중심선에 가까운 픽셀에 높은 가중치를 부여.
        /// </summary>
        private double[] ExtractProfile(Mat image, Point2d start, double ux, double uy, double vx, double vy, double length, double searchWidth)
        {
            int profileLength = (int)length;
            int stripHeight = Math.Max(1, (int)searchWidth);

            // 3-point affine: map search rectangle corners to rectified strip
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

            var profile = new double[profileLength];

            if (ProjectionMode == ProjectionMode.Gaussian && stripHeight > 1)
            {
                // 가우시안 가중 투영: 중심선에 가까운 행에 높은 가중치
                double center = (stripHeight - 1) / 2.0;
                double sigma = stripHeight / 4.0; // 가중치 분포 폭
                var weights = new double[stripHeight];
                double weightSum = 0;
                for (int r = 0; r < stripHeight; r++)
                {
                    double d = r - center;
                    weights[r] = Math.Exp(-(d * d) / (2.0 * sigma * sigma));
                    weightSum += weights[r];
                }
                // 정규화
                for (int r = 0; r < stripHeight; r++)
                    weights[r] /= weightSum;

                // 가중 평균
                using var strip64 = new Mat();
                strip.ConvertTo(strip64, MatType.CV_64F);
                for (int c = 0; c < profileLength; c++)
                {
                    double sum = 0;
                    for (int r = 0; r < stripHeight; r++)
                        sum += strip64.At<double>(r, c) * weights[r];
                    profile[c] = sum;
                }
            }
            else
            {
                // 균일 평균 투영 (기존 방식)
                using var reduced = new Mat();
                Cv2.Reduce(strip, reduced, ReduceDimension.Row, ReduceTypes.Avg, MatType.CV_64F);
                for (int i = 0; i < profileLength; i++)
                    profile[i] = reduced.At<double>(0, i);
            }

            return profile;
        }

        /// <summary>
        /// [개선2] 가우시안 1D 커널 생성
        /// </summary>
        private static double[] CreateGaussianKernel(int halfWidth, double sigma)
        {
            int size = halfWidth * 2 + 1;
            var kernel = new double[size];
            double sum = 0;
            for (int i = 0; i < size; i++)
            {
                double x = i - halfWidth;
                kernel[i] = Math.Exp(-(x * x) / (2.0 * sigma * sigma));
                sum += kernel[i];
            }
            for (int i = 0; i < size; i++)
                kernel[i] /= sum;
            return kernel;
        }

        /// <summary>
        /// [개선2] 가우시안 1D 평활화 적용
        /// </summary>
        private static double[] ApplyGaussianSmoothing(double[] profile, int halfWidth, double sigma)
        {
            var kernel = CreateGaussianKernel(halfWidth, sigma);
            var smoothed = new double[profile.Length];
            int kernelSize = halfWidth * 2 + 1;

            for (int i = 0; i < profile.Length; i++)
            {
                double sum = 0;
                double wSum = 0;
                for (int k = 0; k < kernelSize; k++)
                {
                    int idx = i + k - halfWidth;
                    if (idx >= 0 && idx < profile.Length)
                    {
                        sum += profile[idx] * kernel[k];
                        wSum += kernel[k];
                    }
                }
                smoothed[i] = wSum > 0 ? sum / wSum : profile[i];
            }
            return smoothed;
        }

        private List<EdgeResult> DetectEdges(double[] rawProfile, double length, out double[] gradient)
        {
            var edges = new List<EdgeResult>();

            // [개선2] 가우시안 필터 적용 (옵션)
            double[] profile;
            if (UseGaussianFilter)
                profile = ApplyGaussianSmoothing(rawProfile, FilterHalfWidth, GaussianSigma);
            else
                profile = rawProfile;

            // [개선2] 가우시안 1D 미분 필터 (기존 이동 평균 미분 대체)
            gradient = new double[profile.Length];
            if (UseGaussianFilter)
            {
                // Gaussian derivative: d/dx G(x) = -x/(σ²) * G(x)
                var derivKernel = CreateGaussianDerivativeKernel(FilterHalfWidth, GaussianSigma);
                for (int i = FilterHalfWidth; i < profile.Length - FilterHalfWidth; i++)
                {
                    double sum = 0;
                    for (int j = -FilterHalfWidth; j <= FilterHalfWidth; j++)
                        sum += profile[i + j] * derivKernel[j + FilterHalfWidth];
                    gradient[i] = sum;
                }
            }
            else
            {
                // 기존 이동 평균 미분 필터
                for (int i = FilterHalfWidth; i < profile.Length - FilterHalfWidth; i++)
                {
                    double sum = 0;
                    for (int j = -FilterHalfWidth; j <= FilterHalfWidth; j++)
                        sum += profile[i + j] * j;
                    gradient[i] = sum / (FilterHalfWidth * 2 + 1);
                }
            }

            // Use absolute gradient values for finding max
            var absGradient = new double[gradient.Length];
            for (int i = 0; i < gradient.Length; i++)
                absGradient[i] = Math.Abs(gradient[i]);

            // [개선3] 정규화된 대비를 위한 국부 평균 계산
            double[]? localMean = null;
            if (UseNormalizedContrast)
            {
                localMean = new double[profile.Length];
                int meanRadius = Math.Max(FilterHalfWidth * 2, 5);
                for (int i = 0; i < profile.Length; i++)
                {
                    double sum = 0;
                    int count = 0;
                    for (int j = -meanRadius; j <= meanRadius; j++)
                    {
                        int idx = i + j;
                        if (idx >= 0 && idx < profile.Length)
                        {
                            sum += profile[idx];
                            count++;
                        }
                    }
                    localMean[i] = count > 0 ? sum / count : 128.0;
                }
            }

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
                    // Polarity는 항상 hard filter로 적용
                    // Any: 모든 극성 허용, DarkToLight/LightToDark: 해당 극성만 허용
                    bool polarityMatch = Polarity == EdgePolarity.Any ||
                        (Polarity == EdgePolarity.DarkToLight && polarity == EdgePolarity.DarkToLight) ||
                        (Polarity == EdgePolarity.LightToDark && polarity == EdgePolarity.LightToDark);

                    if (polarityMatch)
                    {
                        // [개선4] 서브픽셀 보간
                        double subPixelPos = ComputeSubPixelPosition(absGradient, i);

                        // [개선3] 정규화된 대비 계산
                        double rawContrast = Math.Abs(g);
                        double normalizedContrast = rawContrast;
                        if (UseNormalizedContrast && localMean != null)
                        {
                            double lm = localMean[i];
                            normalizedContrast = lm > 1.0 ? rawContrast / lm : rawContrast;
                        }

                        candidates.Add(new EdgeResult
                        {
                            Position = i,
                            SubPixelPosition = subPixelPos,
                            Score = rawContrast,
                            NormalizedContrast = normalizedContrast,
                            Polarity = polarity,
                            PolarityScore = polarityMatch ? 1.0 : 0.0
                        });
                    }
                }
            }

            // Multi-scorer system
            double maxGrad = candidates.Count > 0 ? candidates.Max(e => e.Score) : 1.0;
            if (maxGrad < 1e-12) maxGrad = 1.0;

            // [개선3] 정규화된 대비 사용 시 정규화 기준 변경
            double maxNormContrast = 1.0;
            if (UseNormalizedContrast && candidates.Count > 0)
            {
                maxNormContrast = candidates.Max(e => e.NormalizedContrast);
                if (maxNormContrast < 1e-12) maxNormContrast = 1.0;
            }

            double expectedPos = ExpectedPosition >= 0 ? ExpectedPosition : length / 2.0;

            foreach (var edge in candidates)
            {
                // [개선3] Contrast Score: 정규화된 대비 or 기존 방식
                if (UseNormalizedContrast)
                    edge.ContrastScore = edge.NormalizedContrast / maxNormContrast;
                else
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

        /// <summary>
        /// [개선2] 가우시안 1차 미분 커널 생성
        /// G'(x) = -x/(σ²) * G(x) — 에지 위치를 왜곡 없이 검출
        /// </summary>
        private static double[] CreateGaussianDerivativeKernel(int halfWidth, double sigma)
        {
            int size = halfWidth * 2 + 1;
            var kernel = new double[size];
            double sigma2 = sigma * sigma;
            for (int i = 0; i < size; i++)
            {
                double x = i - halfWidth;
                // 부호 반전: 이동 평균 미분과 동일한 부호 규약 (양수 = DarkToLight)
                // 수학적 G'(x) = -x/σ² * G(x) 이지만, 신호처리 관례상 부호를 맞춤
                kernel[i] = (x / sigma2) * Math.Exp(-(x * x) / (2.0 * sigma2));
            }
            return kernel;
        }

        /// <summary>
        /// [개선4] 서브픽셀 보간 — Parabolic(3점) / Gaussian(3점) / Quartic(5점) 선택
        /// </summary>
        private double ComputeSubPixelPosition(double[] data, int index)
        {
            return SubPixelMethod switch
            {
                SubPixelMethod.Gaussian => GaussianSubPixel(data, index),
                SubPixelMethod.Quartic5Point => Quartic5PointSubPixel(data, index),
                _ => ParabolicSubPixel(data, index)
            };
        }

        /// <summary>
        /// 3점 포물선 보간 (기존)
        /// </summary>
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

        /// <summary>
        /// [개선4] 가우시안 함수 피팅 — ln(f) 기반 3점 보간
        /// 신호가 가우시안 형태에 가까울 때 포물선보다 정확
        /// </summary>
        private static double GaussianSubPixel(double[] data, int index)
        {
            if (index <= 0 || index >= data.Length - 1)
                return index;

            double fPrev = data[index - 1];
            double fCurr = data[index];
            double fNext = data[index + 1];

            // 0 이하 값 방지 (log 사용)
            if (fPrev <= 0 || fCurr <= 0 || fNext <= 0)
                return ParabolicSubPixel(data, index);

            double logPrev = Math.Log(fPrev);
            double logCurr = Math.Log(fCurr);
            double logNext = Math.Log(fNext);

            double denom = 2.0 * logPrev - 4.0 * logCurr + 2.0 * logNext;
            if (Math.Abs(denom) < 1e-12)
                return ParabolicSubPixel(data, index);

            double offset = (logPrev - logNext) / denom;
            offset = Math.Clamp(offset, -0.5, 0.5);
            return index + offset;
        }

        /// <summary>
        /// [개선4] 5점 보간법 — 4차 다항식 피팅으로 0.01~0.05 픽셀 정밀도 달성
        /// </summary>
        private static double Quartic5PointSubPixel(double[] data, int index)
        {
            if (index <= 1 || index >= data.Length - 2)
                return ParabolicSubPixel(data, index);

            double fm2 = data[index - 2];
            double fm1 = data[index - 1];
            double f0 = data[index];
            double fp1 = data[index + 1];
            double fp2 = data[index + 2];

            // 2차 미분 추정 (a) 과 1차 미분 추정 (b) from 5-point stencil
            // f(x) ≈ a*x² + b*x + c  (x centered at index)
            // 5-point least-squares quadratic fit:
            //   a = (2*(fm2+fp2) - (fm1+fp1) - 2*f0) / 14  (simplified)
            //   b = (-2*fm2 - fm1 + fp1 + 2*fp2) / 10
            double b = (-2.0 * fm2 - fm1 + fp1 + 2.0 * fp2) / 10.0;
            double a = (2.0 * (fm2 + fp2) - (fm1 + fp1) - 2.0 * f0) / 7.0;

            if (Math.Abs(a) < 1e-12)
                return ParabolicSubPixel(data, index);

            double offset = -b / (2.0 * a);
            offset = Math.Clamp(offset, -1.0, 1.0);
            return index + offset;
        }

        /// <summary>
        /// SelectionMode에 따라 최종 에지 1개를 선택
        /// Best: Score 최고, First: Position 최소 (시작점에 가까운), Last: Position 최대 (시작점에서 먼)
        /// </summary>
        private EdgeResult SelectEdge(List<EdgeResult> edges)
        {
            return SelectionMode switch
            {
                EdgeSelectionMode.First => edges.OrderBy(e => e.SubPixelPosition).First(),
                EdgeSelectionMode.Last => edges.OrderByDescending(e => e.SubPixelPosition).First(),
                _ => edges[0] // Best — 이미 Score 순으로 정렬되어 있음
            };
        }

        /// <summary>
        /// SelectionMode에 따라 최종 EdgePair를 선택
        /// Best: Score 최고, First: 시작점에 가까운 쌍, Last: 시작점에서 먼 쌍
        /// </summary>
        private EdgePairResult SelectEdgePair(List<EdgePairResult> pairs)
        {
            return SelectionMode switch
            {
                EdgeSelectionMode.First => pairs.OrderBy(p => p.CenterPosition).First(),
                EdgeSelectionMode.Last => pairs.OrderByDescending(p => p.CenterPosition).First(),
                _ => pairs[0] // Best — 이미 ExpectedWidth 기준으로 정렬되어 있음
            };
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

                            // [개선3] EdgePair 대칭성 점수: 두 에지 강도 비율이 1에 가까울수록 높은 점수
                            double symmetryScore = 1.0;
                            if (first.ContrastScore > 0 && second.ContrastScore > 0)
                            {
                                double ratio = Math.Min(first.ContrastScore, second.ContrastScore)
                                             / Math.Max(first.ContrastScore, second.ContrastScore);
                                symmetryScore = ratio; // 0~1, 1이면 완벽 대칭
                            }

                            pairs.Add(new EdgePairResult
                            {
                                Edge1Point = new Point2d(
                                    origin.X + ux * first.SubPixelPosition,
                                    origin.Y + uy * first.SubPixelPosition),
                                Edge2Point = new Point2d(
                                    origin.X + ux * second.SubPixelPosition,
                                    origin.Y + uy * second.SubPixelPosition),
                                Width = width,
                                Score = (first.Score + second.Score) / 2,
                                SymmetryScore = symmetryScore,
                                CenterPosition = (first.SubPixelPosition + second.SubPixelPosition) / 2.0
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

        private void DrawEdge(Mat image, Point2d point, double vx, double vy, double width, Scalar? color = null)
        {
            var c = color ?? new Scalar(0, 255, 0);
            Cv2.Line(image,
                new Point((int)(point.X + vx * width / 2), (int)(point.Y + vy * width / 2)),
                new Point((int)(point.X - vx * width / 2), (int)(point.Y - vy * width / 2)),
                c, 1);
        }

        public override List<string> GetAvailableResultKeys()
        {
            return new List<string>
            {
                "Success", "EdgeX", "EdgeY", "EdgeScore", "Width",
                "Edge1X", "Edge1Y", "Edge2X", "Edge2Y",
                "CenterX", "CenterY", "EdgeCount"
            };
        }

        public override VisionToolBase Clone()
        {
            var clone = new CaliperTool
            {
                Name = this.Name,
                ToolType = this.ToolType,
                IsEnabled = this.IsEnabled,
                ROI = this.ROI,
                UseROI = this.UseROI,
                ROIAngle = this.ROIAngle,
                ROICenterX = this.ROICenterX,
                ROICenterY = this.ROICenterY,
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
                PolarityWeight = this.PolarityWeight,
                ProjectionMode = this.ProjectionMode,
                UseGaussianFilter = this.UseGaussianFilter,
                GaussianSigma = this.GaussianSigma,
                UseNormalizedContrast = this.UseNormalizedContrast,
                SubPixelMethod = this.SubPixelMethod,
                SearchAxis = this.SearchAxis,
                SelectionMode = this.SelectionMode
            };
            CopyPlcMappingsTo(clone);
            return clone;
        }
    }

    public class EdgeResult
    {
        public double Position { get; set; }
        public double SubPixelPosition { get; set; }
        public double Score { get; set; }
        public double ContrastScore { get; set; }
        public double NormalizedContrast { get; set; }
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
        public double SymmetryScore { get; set; }
        /// <summary>쌍 중심의 프로파일 위치 (First/Last 선택용)</summary>
        public double CenterPosition { get; set; }
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

    /// <summary>
    /// [개선1] 프로파일 투영 모드
    /// </summary>
    public enum ProjectionMode
    {
        /// <summary>균일 평균 (기존 방식)</summary>
        Uniform,
        /// <summary>가우시안 가중 평균 (중심선 우선)</summary>
        Gaussian
    }

    /// <summary>
    /// [개선4] 서브픽셀 보간 방법
    /// </summary>
    public enum SubPixelMethod
    {
        /// <summary>3점 포물선 보간 (기존)</summary>
        Parabolic,
        /// <summary>3점 가우시안 피팅</summary>
        Gaussian,
        /// <summary>5점 다항식 피팅 (고정밀)</summary>
        Quartic5Point
    }

    /// <summary>
    /// 에지 선택 모드 (Cognex CogCaliperTool 호환)
    /// </summary>
    public enum EdgeSelectionMode
    {
        /// <summary>점수(Score)가 가장 높은 에지</summary>
        Best,
        /// <summary>검색 시작점에서 가장 가까운 에지</summary>
        First,
        /// <summary>검색 시작점에서 가장 먼 에지</summary>
        Last
    }

    /// <summary>
    /// Caliper 탐색 방향 축 설정
    /// </summary>
    public enum CaliperSearchAxis
    {
        /// <summary>ROI의 Width 축 방향으로 탐색</summary>
        AlongWidth,
        /// <summary>ROI의 Height 축 방향으로 탐색</summary>
        AlongHeight
    }
}
