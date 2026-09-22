using VMS.VisionSetup.Attributes;
using VMS.VisionSetup.Models;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace VMS.VisionSetup.VisionTools.Measurement
{
    /// <summary>
    /// 직선 검출 및 피팅 도구 (Cognex VisionPro CogFindLineTool 대체)
    /// 여러 Caliper를 사용하여 직선을 검출하고 피팅
    /// </summary>
    public class LineFitTool : VisionToolBase
    {
        // 검색 영역 설정
        private Point2d _startPoint = new Point2d(0, 100);
        public Point2d StartPoint
        {
            get => _startPoint;
            set => SetProperty(ref _startPoint, value);
        }

        private Point2d _endPoint = new Point2d(200, 100);
        public Point2d EndPoint
        {
            get => _endPoint;
            set => SetProperty(ref _endPoint, value);
        }

        private int _numCalipers = 10;
        public int NumCalipers
        {
            get => _numCalipers;
            set => SetProperty(ref _numCalipers, Math.Max(2, value));
        }

        private double _searchLength = 50;
        public double SearchLength
        {
            get => _searchLength;
            set => SetProperty(ref _searchLength, Math.Max(10, value));
        }

        private double _searchWidth = 5;
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

        // 피팅 설정
        private LineFitMethod _fitMethod = LineFitMethod.LeastSquares;
        public LineFitMethod FitMethod
        {
            get => _fitMethod;
            set => SetProperty(ref _fitMethod, value);
        }

        private double _ransacThreshold = 5.0;
        public double RansacThreshold
        {
            get => _ransacThreshold;
            set => SetProperty(ref _ransacThreshold, Math.Max(0.1, value));
        }

        private int _minFoundCalipers = 3;
        public int MinFoundCalipers
        {
            get => _minFoundCalipers;
            set => SetProperty(ref _minFoundCalipers, Math.Max(2, value));
        }

        // 탐색 방향 — 화살표가 가리키는 쪽이 곧 훑는 방향이다 (CaliperTool 과 같은 규약).
        // 캘리퍼들은 이 방향과 수직으로 늘어서고, 찾은 점들로 직선을 맞춘다.
        private LineSearchAxis _searchAxis = LineSearchAxis.AlongWidth;
        [TunableParam(
            Description = "엣지 탐색 방향. AlongWidth: ROI 가로 방향으로 훑음. AlongHeight: 세로 방향. " +
                          "LongerSide: 긴 변을 기준선으로 삼고 짧은 변 쪽으로 훑음(구버전 동작).",
            Tier = TuningTier.Semantic, DefaultHint = "AlongWidth")]
        public LineSearchAxis SearchAxis
        {
            get => _searchAxis;
            set => SetProperty(ref _searchAxis, value);
        }

        // 캘리퍼 하나에서 엣지가 여러 개 잡힐 때 무엇을 채택할지.
        private LineEdgeSelectionMode _selectionMode = LineEdgeSelectionMode.First;
        [TunableParam(
            Description = "여러 엣지 중 채택 기준. First: 탐색 시작점에 가장 가까운 엣지. " +
                          "Last: 가장 먼 엣지. Best: 대비가 가장 큰 엣지. " +
                          "ClosestToCenter: 검색선 중심에 가장 가까운 엣지(구버전 동작).",
            Tier = TuningTier.Semantic, DefaultHint = "First")]
        public LineEdgeSelectionMode SelectionMode
        {
            get => _selectionMode;
            set => SetProperty(ref _selectionMode, value);
        }

        public LineFitTool()
        {
            Name = "Line Fit";
            ToolType = "LineFitTool";
        }

        public override VisionResult Execute(Mat inputImage)
        {
            var result = new VisionResult();
            var sw = Stopwatch.StartNew();

            try
            {
                // CaliperTool과 동일한 패턴: GetROIImage() 사용하지 않고 전체 이미지에서 작업
                Mat grayImage = new Mat();

                if (inputImage.Channels() > 1)
                    Cv2.CvtColor(inputImage, grayImage, ColorConversionCodes.BGR2GRAY);
                else
                    grayImage = inputImage.Clone();

                // ROI 사용 시: ROI 사각형에서 기준선과 검색 파라미터를 직접 도출
                // ROI 미사용 시: StartPoint/EndPoint/SearchLength 사용
                Point2d baselineStart, baselineEnd;
                double searchLength;

                // 회전 각도 결정: 라이브 ROI shape 또는 저장된 ROIAngle 사용
                double effectiveAngle = EffectiveROIAngle;

                if (UseROI && ROI.Width > 0 && ROI.Height > 0 &&
                    Math.Abs(effectiveAngle) > 0.001)
                {
                    // 회전된 ROI: ROI rect 중심 + 회전 각도로 기준선 계산
                    // ROI rect 중심을 사용해야 Fixture Transform 후에도 올바른 위치 반영
                    double cx = ROI.X + ROI.Width / 2.0;
                    double cy = ROI.Y + ROI.Height / 2.0;
                    double w = ROI.Width;
                    double h = ROI.Height;
                    double rad = effectiveAngle * Math.PI / 180.0;
                    double cosA = Math.Cos(rad);
                    double sinA = Math.Sin(rad);

                    // 탐색 방향(화살표)과 수직인 축이 기준선이 된다.
                    //   AlongWidth  → Width 축으로 훑음 → 기준선은 Height 축
                    //   AlongHeight → Height 축으로 훑음 → 기준선은 Width 축
                    //   LongerSide  → 구버전: 긴 변이 기준선 (화살표와 어긋날 수 있음)
                    bool searchAlongWidth = SearchAxis switch
                    {
                        LineSearchAxis.AlongWidth => true,
                        LineSearchAxis.AlongHeight => false,
                        _ => w < h,          // LongerSide: 긴 변이 기준선 = 짧은 변 쪽으로 훑음
                    };

                    if (searchAlongWidth)
                    {
                        // Width 방향으로 훑음 → 기준선 = Height 축 (-sinθ, cosθ), 검색 길이 = Width
                        baselineStart = new Point2d(cx - (h / 2) * (-sinA), cy - (h / 2) * cosA);
                        baselineEnd = new Point2d(cx + (h / 2) * (-sinA), cy + (h / 2) * cosA);
                        searchLength = w;
                    }
                    else
                    {
                        // Height 방향으로 훑음 → 기준선 = Width 축 (cosθ, sinθ), 검색 길이 = Height
                        baselineStart = new Point2d(cx - (w / 2) * cosA, cy - (w / 2) * sinA);
                        baselineEnd = new Point2d(cx + (w / 2) * cosA, cy + (w / 2) * sinA);
                        searchLength = h;
                    }
                }
                else if (UseROI && ROI.Width > 0 && ROI.Height > 0)
                {
                    // Fallback: 기존 축 정렬 RectangleROI
                    var roi = GetAdjustedROI(inputImage);
                    bool searchAlongWidth = SearchAxis switch
                    {
                        LineSearchAxis.AlongWidth => true,
                        LineSearchAxis.AlongHeight => false,
                        _ => roi.Width < roi.Height,
                    };

                    if (searchAlongWidth)
                    {
                        // 가로로 훑음 → 기준선은 세로
                        double cxr = roi.X + roi.Width / 2.0;
                        baselineStart = new Point2d(cxr, roi.Y);
                        baselineEnd = new Point2d(cxr, roi.Y + roi.Height);
                        searchLength = roi.Width;
                    }
                    else
                    {
                        // 세로로 훑음 → 기준선은 가로
                        double cyr = roi.Y + roi.Height / 2.0;
                        baselineStart = new Point2d(roi.X, cyr);
                        baselineEnd = new Point2d(roi.X + roi.Width, cyr);
                        searchLength = roi.Height;
                    }
                }
                else
                {
                    baselineStart = StartPoint;
                    baselineEnd = EndPoint;
                    searchLength = SearchLength;
                }

                // 기준선 벡터 계산
                double dx = baselineEnd.X - baselineStart.X;
                double dy = baselineEnd.Y - baselineStart.Y;
                double baseLength = Math.Sqrt(dx * dx + dy * dy);

                if (baseLength < 1)
                {
                    result.Success = false;
                    result.Message = "기준선이 너무 짧습니다.";
                    return result;
                }

                // 단위 벡터 (기준선 방향)
                double ux = dx / baseLength;
                double uy = dy / baseLength;

                // 수직 벡터 (검색 방향)
                double vx = -uy;
                double vy = ux;

                // 검색 방향을 직관적으로 정규화: 왼쪽→오른쪽, 위→아래
                // (vx < 0이면 오른쪽→왼쪽으로 검색하게 되므로 반전)
                if (vx < 0 || (Math.Abs(vx) < 1e-6 && vy < 0))
                {
                    vx = -vx;
                    vy = -vy;
                }

                // 결과 이미지 생성 (원본 컬러 이미지 기반)
                Mat overlayImage = GetColorOverlayBase(inputImage);

                // Caliper 위치에서 Edge 검출 (모든 좌표는 절대 좌표)
                var foundEdges = new List<(Point2d Point, double Score)>();
                var caliperResults = new List<CaliperResult>();

                for (int i = 0; i < NumCalipers; i++)
                {
                    // Caliper 중심 위치 (절대 좌표)
                    double t = (double)i / (NumCalipers - 1);
                    double cx = baselineStart.X + dx * t;
                    double cy = baselineStart.Y + dy * t;

                    // Caliper 검색 영역 (수직 방향, 절대 좌표)
                    var searchStart = new Point2d(cx - vx * searchLength / 2, cy - vy * searchLength / 2);
                    var searchEnd = new Point2d(cx + vx * searchLength / 2, cy + vy * searchLength / 2);

                    // Edge 검출 (전체 이미지에서 절대 좌표로 검색)
                    var edge = FindEdgeAlongLine(grayImage, searchStart, searchEnd, SearchWidth);

                    var caliperResult = new CaliperResult
                    {
                        Index = i,
                        SearchStart = searchStart,
                        SearchEnd = searchEnd,
                        Found = edge.HasValue
                    };

                    // Caliper 검색 영역 표시 (이미 절대 좌표)
                    Cv2.Line(overlayImage,
                        new Point((int)searchStart.X, (int)searchStart.Y),
                        new Point((int)searchEnd.X, (int)searchEnd.Y),
                        new Scalar(128, 128, 128), 1);

                    if (edge.HasValue)
                    {
                        foundEdges.Add((edge.Value.Point, edge.Value.Score));
                        caliperResult.EdgePoint = edge.Value.Point;
                        caliperResult.EdgeScore = edge.Value.Score;
                    }
                    else
                    {
                        // 미검출 표시
                        Cv2.Circle(overlayImage,
                            new Point((int)cx, (int)cy),
                            3, new Scalar(0, 0, 255), -1);
                    }

                    caliperResults.Add(caliperResult);
                }

                // 저점수 에지 필터링: 최대 점수의 30% 미만인 에지를 제외
                double maxEdgeScore = foundEdges.Count > 0 ? foundEdges.Max(e => e.Score) : 0;
                double scoreThreshold = maxEdgeScore * 0.3;
                var filteredEdges = foundEdges.Where(e => e.Score >= scoreThreshold).ToList();

                // 오버레이 그리기: 필터된 에지(녹색) vs 제외된 에지(노란색)
                foreach (var edge in foundEdges)
                {
                    bool accepted = edge.Score >= scoreThreshold;
                    var color = accepted ? new Scalar(0, 255, 0) : new Scalar(0, 255, 255);
                    Cv2.Circle(overlayImage,
                        new Point((int)edge.Point.X, (int)edge.Point.Y),
                        3, color, -1);
                }

                var foundPoints = filteredEdges.Select(e => e.Point).ToList();

                result.Data["CaliperResults"] = caliperResults;
                result.Data["FoundCount"] = foundPoints.Count;
                result.Data["TotalCalipers"] = NumCalipers;

                // 직선 피팅 (모든 점이 이미 절대 좌표)
                if (foundPoints.Count >= MinFoundCalipers)
                {
                    LineFitResult lineResult;

                    if (FitMethod == LineFitMethod.RANSAC)
                    {
                        lineResult = FitLineRANSAC(foundPoints);
                    }
                    else
                    {
                        lineResult = FitLineLeastSquares(foundPoints);
                    }

                    if (lineResult.Success)
                    {
                        // 좌표 변환 불필요 — 이미 절대 좌표

                        // 피팅된 직선 그리기
                        var linePoints = GetLineEndPoints(lineResult, inputImage.Width, inputImage.Height);
                        Cv2.Line(overlayImage,
                            new Point((int)linePoints.Item1.X, (int)linePoints.Item1.Y),
                            new Point((int)linePoints.Item2.X, (int)linePoints.Item2.Y),
                            new Scalar(0, 255, 255), 2);

                        // 결과 데이터 (절대 좌표)
                        result.Data["LineAngle"] = lineResult.Angle;
                        result.Data["LinePointX"] = lineResult.PointOnLine.X;
                        result.Data["LinePointY"] = lineResult.PointOnLine.Y;
                        result.Data["DirectionX"] = lineResult.Direction.X;
                        result.Data["DirectionY"] = lineResult.Direction.Y;
                        result.Data["FitError"] = lineResult.FitError;
                        result.Data["InlierCount"] = lineResult.InlierCount;

                        // Graphics 추가
                        result.Graphics.Add(new GraphicOverlay
                        {
                            Type = GraphicType.Line,
                            Position = linePoints.Item1,
                            EndPosition = linePoints.Item2,
                            Color = new Scalar(0, 255, 255)
                        });

                        result.Success = true;
                        result.Message = $"직선 검출 완료: 각도 {lineResult.Angle:F2}°, {foundPoints.Count}/{NumCalipers} 점 사용";
                    }
                    else
                    {
                        result.Success = false;
                        result.Message = "직선 피팅 실패";
                    }
                }
                else
                {
                    result.Success = false;
                    result.Message = $"검출된 점 부족: {foundPoints.Count}/{MinFoundCalipers} 필요";
                }

                result.OutputImage = grayImage;
                result.OverlayImage = overlayImage;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"Line Fit 실행 실패: {ex.Message}";
            }

            sw.Stop();
            ExecutionTime = sw.Elapsed.TotalMilliseconds;
            LastResult = result;
            return result;
        }

        /// <summary>
        /// CaliperTool과 동일한 Affine Warp 기반 프로파일 추출
        /// </summary>
        private double[] ExtractProfile(Mat image, Point2d start, double ux, double uy, double vx, double vy, double length, double searchWidth)
        {
            int profileLength = (int)length;
            int stripHeight = Math.Max(1, (int)searchWidth);

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

            using var reduced = new Mat();
            Cv2.Reduce(strip, reduced, ReduceDimension.Row, ReduceTypes.Avg, MatType.CV_64F);

            var profile = new double[profileLength];
            for (int i = 0; i < profileLength; i++)
                profile[i] = reduced.At<double>(0, i);

            return profile;
        }

        /// <summary>
        /// 엣지 검출: Affine Warp 프로파일 → 미분 커널 → 서브픽셀 보간
        /// 전략: "충분히 강한 에지 중 검색선 중심에 가장 가까운 것" 선택
        /// (Cognex CogCaliperTool의 Closest + Contrast 조합과 동일한 접근)
        /// </summary>
        private (Point2d Point, double Score)? FindEdgeAlongLine(Mat image, Point2d start, Point2d end, double width)
        {
            double dx = end.X - start.X;
            double dy = end.Y - start.Y;
            double length = Math.Sqrt(dx * dx + dy * dy);

            if (length < 1)
                return null;

            double ux = dx / length;
            double uy = dy / length;
            double vx = -uy;
            double vy = ux;

            // 1단계: Affine Warp 프로파일 추출 (SearchWidth 방향 평균화로 1차 노이즈 제거)
            var profile = ExtractProfile(image, start, ux, uy, vx, vy, length, width);
            int profileLength = profile.Length;

            // 2단계: FilterHalfWidth 미분 커널 (스무딩 내장 — 별도 가우시안 불필요)
            var gradient = new double[profileLength];
            for (int i = FilterHalfWidth; i < profileLength - FilterHalfWidth; i++)
            {
                double sum = 0;
                for (int j = -FilterHalfWidth; j <= FilterHalfWidth; j++)
                {
                    sum += profile[i + j] * j;
                }
                gradient[i] = sum / (FilterHalfWidth * 2 + 1);
            }

            // 절대 그래디언트 (서브픽셀 보간용)
            var absGradient = new double[profileLength];
            for (int i = 0; i < profileLength; i++)
                absGradient[i] = Math.Abs(gradient[i]);

            // 3단계: 에지 후보 수집 (Local Maxima + Polarity 필터링)
            //
            // 봉우리 비교에서 한쪽만 엄격(>)하고 반대쪽은 같음을 허용(>=)한다.
            // 양쪽 다 엄격하면 그래디언트가 두 표본에 걸쳐 같은 값이 될 때
            // (대칭 엣지가 두 화소 사이에 놓이는 흔한 경우) 두 표본 모두 탈락해
            // 엣지를 통째로 놓친다 — 실제로 그런 프로파일에서 검출이 0 이었다
            // (2026-09-22). 한쪽만 허용하면 평탄부의 첫 표본 하나만 채택되므로
            // 중복 후보도 생기지 않는다.
            var candidates = new List<(int Index, double Contrast)>();

            for (int i = FilterHalfWidth + 1; i < profileLength - FilterHalfWidth - 1; i++)
            {
                double g = gradient[i];
                bool isLocalExtreme = false;
                EdgePolarity polarity = EdgePolarity.DarkToLight;

                if (g > 0 && g > EdgeThreshold)
                {
                    if (g > gradient[i - 1] && g >= gradient[i + 1])
                    {
                        isLocalExtreme = true;
                        polarity = EdgePolarity.DarkToLight;
                    }
                }
                else if (g < 0 && -g > EdgeThreshold)
                {
                    if (g < gradient[i - 1] && g <= gradient[i + 1])
                    {
                        isLocalExtreme = true;
                        polarity = EdgePolarity.LightToDark;
                    }
                }

                if (!isLocalExtreme)
                    continue;

                bool polarityMatch = Polarity == EdgePolarity.Any ||
                    (Polarity == EdgePolarity.DarkToLight && polarity == EdgePolarity.DarkToLight) ||
                    (Polarity == EdgePolarity.LightToDark && polarity == EdgePolarity.LightToDark);

                if (!polarityMatch)
                    continue;

                candidates.Add((i, Math.Abs(g)));
            }

            if (candidates.Count == 0)
                return null;

            // 4단계: 에지 선택 — 기준은 CaliperTool 과 같은 말을 쓴다.
            // First/Last 는 "탐색 시작점에서" 가깝고 먼 것이므로 화살표 방향이 곧 기준이다.
            //
            // ⚠ 대비 하한(최대의 50%)은 ClosestToCenter 에만 건다.
            //   First/Last 에 걸면 "같은 탐색선 안의 다른 강한 엣지"가 기준을 끌어올려
            //   정작 찾으려던 약한 엣지가 탈락한다. 실제로 반사광이 걸린 두 캘리퍼만
            //   다른 경계를 잡아 맞춰진 직선이 기울었다 (현장 보고 2026-09-22).
            //   약한 엣지를 걸러 내는 손잡이는 EdgeThreshold 이고 그건 사용자 것이다 —
            //   CaliperTool 도 EdgeThreshold + 극성만으로 First/Last 를 고른다.
            double centerPos = length / 2.0;

            var best = SelectionMode switch
            {
                LineEdgeSelectionMode.First => candidates.OrderBy(c => c.Index).First(),
                LineEdgeSelectionMode.Last => candidates.OrderByDescending(c => c.Index).First(),
                LineEdgeSelectionMode.Best => candidates.OrderByDescending(c => c.Contrast).First(),
                _ => SelectClosestToCenter(candidates, centerPos),
            };

            double subPixelPos = ParabolicSubPixel(absGradient, best.Index);
            var edgePoint = new Point2d(
                start.X + ux * subPixelPos,
                start.Y + uy * subPixelPos);
            return (edgePoint, best.Contrast);
        }

        /// <summary>
        /// 구버전 동작 — 최대 대비의 50% 이상인 후보 중 검색선 중심에 가장 가까운 엣지.
        /// ClosestToCenter 전용이다 (이 설정이 없던 레시피의 호환 경로).
        /// </summary>
        private static (int Index, double Contrast) SelectClosestToCenter(
            List<(int Index, double Contrast)> candidates, double centerPos)
        {
            double contrastFloor = candidates.Max(c => c.Contrast) * 0.5;
            var qualified = candidates.Where(c => c.Contrast >= contrastFloor).ToList();
            if (qualified.Count == 0)
                qualified = candidates;
            return qualified.OrderBy(c => Math.Abs(c.Index - centerPos)).First();
        }

        /// <summary>
        /// 1D 가우시안 스무딩: FilterHalfWidth를 sigma로 사용하여 프로파일 노이즈 제거
        /// </summary>
        private static double[] GaussianSmooth1D(double[] profile, int halfWidth)
        {
            int kernelSize = halfWidth * 2 + 1;
            double sigma = halfWidth;
            var kernel = new double[kernelSize];
            double kernelSum = 0;

            for (int i = 0; i < kernelSize; i++)
            {
                double x = i - halfWidth;
                kernel[i] = Math.Exp(-(x * x) / (2.0 * sigma * sigma));
                kernelSum += kernel[i];
            }
            for (int i = 0; i < kernelSize; i++)
                kernel[i] /= kernelSum;

            var smoothed = new double[profile.Length];
            for (int i = 0; i < profile.Length; i++)
            {
                double sum = 0;
                double wSum = 0;
                for (int j = -halfWidth; j <= halfWidth; j++)
                {
                    int idx = i + j;
                    if (idx >= 0 && idx < profile.Length)
                    {
                        double w = kernel[j + halfWidth];
                        sum += profile[idx] * w;
                        wSum += w;
                    }
                }
                smoothed[i] = sum / wSum;
            }
            return smoothed;
        }

        /// <summary>
        /// CaliperTool과 동일한 포물선 서브픽셀 보간
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

        private LineFitResult FitLineLeastSquares(List<Point2d> points)
        {
            var result = new LineFitResult();

            if (points.Count < 2)
            {
                result.Success = false;
                return result;
            }

            // OpenCV FitLine 사용
            var pointsArray = points.Select(p => new Point2f((float)p.X, (float)p.Y)).ToArray();
            using var line = new Mat();

            Cv2.FitLine(
                InputArray.Create(pointsArray),
                line,
                DistanceTypes.L2, 0, 0.01, 0.01);

            // Mat에서 결과 추출
            line.GetArray(out float[] lineParams);

            result.Direction = new Point2d(lineParams[0], lineParams[1]);
            result.PointOnLine = new Point2d(lineParams[2], lineParams[3]);
            result.Angle = Math.Atan2(lineParams[1], lineParams[0]) * 180 / Math.PI;

            // 피팅 오차 계산
            double totalError = 0;
            foreach (var p in points)
            {
                double dist = DistanceToLine(p, result.PointOnLine, result.Direction);
                totalError += dist * dist;
            }
            result.FitError = Math.Sqrt(totalError / points.Count);
            result.InlierCount = points.Count;
            result.Success = true;

            return result;
        }

        private LineFitResult FitLineRANSAC(List<Point2d> points)
        {
            var result = new LineFitResult();

            if (points.Count < 2)
            {
                result.Success = false;
                return result;
            }

            int maxIterations = 100;
            int bestInlierCount = 0;
            List<Point2d> bestInliers = new List<Point2d>();

            var random = new Random();

            for (int iter = 0; iter < maxIterations; iter++)
            {
                // 랜덤으로 2점 선택
                int i1 = random.Next(points.Count);
                int i2 = random.Next(points.Count);
                while (i2 == i1)
                    i2 = random.Next(points.Count);

                var p1 = points[i1];
                var p2 = points[i2];

                // 직선 파라미터 계산
                double dx = p2.X - p1.X;
                double dy = p2.Y - p1.Y;
                double len = Math.Sqrt(dx * dx + dy * dy);
                if (len < 0.001) continue;

                var dir = new Point2d(dx / len, dy / len);

                // Inlier 계산
                var inliers = new List<Point2d>();
                foreach (var p in points)
                {
                    double dist = DistanceToLine(p, p1, dir);
                    if (dist < RansacThreshold)
                        inliers.Add(p);
                }

                if (inliers.Count > bestInlierCount)
                {
                    bestInlierCount = inliers.Count;
                    bestInliers = inliers;
                }
            }

            if (bestInliers.Count >= MinFoundCalipers)
            {
                // Inlier로 최종 피팅
                result = FitLineLeastSquares(bestInliers);
                result.InlierCount = bestInlierCount;
            }
            else
            {
                result.Success = false;
            }

            return result;
        }

        private double DistanceToLine(Point2d point, Point2d linePoint, Point2d lineDir)
        {
            double dx = point.X - linePoint.X;
            double dy = point.Y - linePoint.Y;

            // 직선까지의 수직 거리
            double cross = Math.Abs(dx * lineDir.Y - dy * lineDir.X);
            return cross;
        }

        private (Point2d, Point2d) GetLineEndPoints(LineFitResult line, int imageWidth, int imageHeight)
        {
            // 이미지 경계와의 교차점 계산
            double t1 = -1000;
            double t2 = 1000;

            var p1 = new Point2d(
                line.PointOnLine.X + line.Direction.X * t1,
                line.PointOnLine.Y + line.Direction.Y * t1);
            var p2 = new Point2d(
                line.PointOnLine.X + line.Direction.X * t2,
                line.PointOnLine.Y + line.Direction.Y * t2);

            return (p1, p2);
        }

        public override List<string> GetAvailableResultKeys()
        {
            return new List<string>
            {
                "Success", "FoundCount", "LineAngle", "LinePointX", "LinePointY",
                "FitError", "InlierCount"
            };
        }

        public override VisionToolBase Clone()
        {
            var clone = new LineFitTool
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
                NumCalipers = this.NumCalipers,
                SearchLength = this.SearchLength,
                SearchWidth = this.SearchWidth,
                Polarity = this.Polarity,
                EdgeThreshold = this.EdgeThreshold,
                FilterHalfWidth = this.FilterHalfWidth,
                FitMethod = this.FitMethod,
                RansacThreshold = this.RansacThreshold,
                MinFoundCalipers = this.MinFoundCalipers
            };
            CopyPlcMappingsTo(clone);
            return clone;
        }
    }

    public class CaliperResult
    {
        public int Index { get; set; }
        public Point2d SearchStart { get; set; }
        public Point2d SearchEnd { get; set; }
        public bool Found { get; set; }
        public Point2d EdgePoint { get; set; }
        public double EdgeScore { get; set; }
    }

    public class LineFitResult
    {
        public bool Success { get; set; }
        public Point2d PointOnLine { get; set; }
        public Point2d Direction { get; set; }
        public double Angle { get; set; }
        public double FitError { get; set; }
        public int InlierCount { get; set; }
    }

    public enum LineFitMethod
    {
        LeastSquares,
        RANSAC
    }

    /// <summary>
    /// 직선 맞춤의 엣지 탐색 방향. ROI 화살표가 가리키는 쪽이 곧 훑는 방향이고,
    /// 캘리퍼들은 그와 수직으로 늘어선다 (CaliperTool 과 같은 규약).
    /// </summary>
    public enum LineSearchAxis
    {
        /// <summary>ROI 가로 방향으로 훑는다 (기준선은 세로 축).</summary>
        AlongWidth,
        /// <summary>ROI 세로 방향으로 훑는다 (기준선은 가로 축).</summary>
        AlongHeight,
        /// <summary>
        /// 구버전 동작 — 긴 변을 기준선으로 삼고 짧은 변 쪽으로 훑는다.
        /// 화살표와 어긋날 수 있다. 이 설정이 없던 레시피의 호환 값.
        /// </summary>
        LongerSide
    }

    /// <summary>
    /// 캘리퍼 하나에서 엣지가 여러 개 잡힐 때의 채택 기준.
    /// First/Last 는 탐색 시작점(화살표 꼬리) 기준이다.
    /// </summary>
    public enum LineEdgeSelectionMode
    {
        /// <summary>탐색 시작점에 가장 가까운 엣지</summary>
        First,
        /// <summary>탐색 시작점에서 가장 먼 엣지</summary>
        Last,
        /// <summary>대비(contrast)가 가장 큰 엣지</summary>
        Best,
        /// <summary>검색선 중심에 가장 가까운 엣지 — 구버전 동작.</summary>
        ClosestToCenter
    }
}
