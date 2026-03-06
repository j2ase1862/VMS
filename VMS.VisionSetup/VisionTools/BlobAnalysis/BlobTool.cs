using VMS.VisionSetup.Models;
using VMS.VisionSetup.Services;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace VMS.VisionSetup.VisionTools.BlobAnalysis
{
    /// <summary>
    /// Blob 분석 도구 (Cognex VisionPro CogBlobTool 대체)
    /// 이진화된 이미지에서 객체(Blob)를 검출하고 분석
    /// </summary>
    public class BlobTool : VisionToolBase
    {
        // Threshold 설정 (내부 이진화용)
        private bool _useInternalThreshold = true;
        public bool UseInternalThreshold
        {
            get => _useInternalThreshold;
            set => SetProperty(ref _useInternalThreshold, value);
        }

        private double _thresholdValue = 128;
        public double ThresholdValue
        {
            get => _thresholdValue;
            set => SetProperty(ref _thresholdValue, Math.Clamp(value, 0, 255));
        }

        private SegmentationPolarity _segmentationPolarity = SegmentationPolarity.LightOnDark;
        public SegmentationPolarity SegmentationPolarity
        {
            get => _segmentationPolarity;
            set => SetProperty(ref _segmentationPolarity, value);
        }

        // 면적 필터
        private double _minArea = 100;
        public double MinArea
        {
            get => _minArea;
            set => SetProperty(ref _minArea, Math.Max(0, value));
        }

        private double _maxArea = double.MaxValue;
        public double MaxArea
        {
            get => _maxArea;
            set => SetProperty(ref _maxArea, Math.Max(MinArea, value));
        }

        // 둘레 필터
        private double _minPerimeter = 0;
        public double MinPerimeter
        {
            get => _minPerimeter;
            set => SetProperty(ref _minPerimeter, Math.Max(0, value));
        }

        private double _maxPerimeter = double.MaxValue;
        public double MaxPerimeter
        {
            get => _maxPerimeter;
            set => SetProperty(ref _maxPerimeter, Math.Max(MinPerimeter, value));
        }

        // 형상 필터
        private double _minCircularity = 0;
        public double MinCircularity
        {
            get => _minCircularity;
            set => SetProperty(ref _minCircularity, Math.Clamp(value, 0, 1));
        }

        private double _maxCircularity = 1;
        public double MaxCircularity
        {
            get => _maxCircularity;
            set => SetProperty(ref _maxCircularity, Math.Clamp(value, MinCircularity, 1));
        }

        private double _minAspectRatio = 0;
        public double MinAspectRatio
        {
            get => _minAspectRatio;
            set => SetProperty(ref _minAspectRatio, Math.Max(0, value));
        }

        private double _maxAspectRatio = double.MaxValue;
        public double MaxAspectRatio
        {
            get => _maxAspectRatio;
            set => SetProperty(ref _maxAspectRatio, Math.Max(MinAspectRatio, value));
        }

        // Convexity 필터
        private double _minConvexity = 0;
        public double MinConvexity
        {
            get => _minConvexity;
            set => SetProperty(ref _minConvexity, Math.Clamp(value, 0, 1));
        }

        // 최대 Blob 수
        private int _maxBlobCount = 100;
        public int MaxBlobCount
        {
            get => _maxBlobCount;
            set => SetProperty(ref _maxBlobCount, Math.Max(1, value));
        }

        // 정렬 기준
        private BlobSortBy _sortBy = BlobSortBy.Area;
        public BlobSortBy SortBy
        {
            get => _sortBy;
            set => SetProperty(ref _sortBy, value);
        }

        private bool _sortDescending = true;
        public bool SortDescending
        {
            get => _sortDescending;
            set => SetProperty(ref _sortDescending, value);
        }

        // Contour 검출 모드
        private RetrievalModes _retrievalMode = RetrievalModes.External;
        public RetrievalModes RetrievalMode
        {
            get => _retrievalMode;
            set => SetProperty(ref _retrievalMode, value);
        }

        private ContourApproximationModes _approximationMode = ContourApproximationModes.ApproxSimple;
        public ContourApproximationModes ApproximationMode
        {
            get => _approximationMode;
            set => SetProperty(ref _approximationMode, value);
        }

        // 표시 옵션
        private bool _drawContours = true;
        public bool DrawContours
        {
            get => _drawContours;
            set => SetProperty(ref _drawContours, value);
        }

        private bool _drawBoundingBox = true;
        public bool DrawBoundingBox
        {
            get => _drawBoundingBox;
            set => SetProperty(ref _drawBoundingBox, value);
        }

        private bool _drawCenterPoint = true;
        public bool DrawCenterPoint
        {
            get => _drawCenterPoint;
            set => SetProperty(ref _drawCenterPoint, value);
        }

        private bool _drawLabels = true;
        public bool DrawLabels
        {
            get => _drawLabels;
            set => SetProperty(ref _drawLabels, value);
        }

        // ── 판정 (Judgment) ──

        private bool _enableJudgment = false;
        public bool EnableJudgment
        {
            get => _enableJudgment;
            set => SetProperty(ref _enableJudgment, value);
        }

        // 면적 판정: 기준 면적 ± 허용 오차
        private bool _useAreaJudgment = false;
        public bool UseAreaJudgment
        {
            get => _useAreaJudgment;
            set => SetProperty(ref _useAreaJudgment, value);
        }

        private double _expectedArea = 1000;
        public double ExpectedArea
        {
            get => _expectedArea;
            set => SetProperty(ref _expectedArea, Math.Max(0, value));
        }

        private double _areaTolerancePlus = 200;
        public double AreaTolerancePlus
        {
            get => _areaTolerancePlus;
            set => SetProperty(ref _areaTolerancePlus, Math.Max(0, value));
        }

        private double _areaToleranceMinus = 200;
        public double AreaToleranceMinus
        {
            get => _areaToleranceMinus;
            set => SetProperty(ref _areaToleranceMinus, Math.Max(0, value));
        }

        // 개수 판정
        private bool _useCountJudgment = false;
        public bool UseCountJudgment
        {
            get => _useCountJudgment;
            set => SetProperty(ref _useCountJudgment, value);
        }

        private CountJudgmentMode _countMode = CountJudgmentMode.Equal;
        public CountJudgmentMode CountMode
        {
            get => _countMode;
            set => SetProperty(ref _countMode, value);
        }

        private int _expectedCount = 1;
        public int ExpectedCount
        {
            get => _expectedCount;
            set => SetProperty(ref _expectedCount, Math.Max(0, value));
        }

        private int _expectedCountMax = 10;
        public int ExpectedCountMax
        {
            get => _expectedCountMax;
            set => SetProperty(ref _expectedCountMax, Math.Max(ExpectedCount, value));
        }

        public BlobTool()
        {
            Name = "Blob";
            ToolType = "BlobTool";
        }

        public override VisionResult Execute(Mat inputImage)
        {
            var result = new VisionResult();
            var sw = Stopwatch.StartNew();

            try
            {
                // ROI 오프셋 계산 (ROI 좌표계 → 원본 이미지 좌표계 변환)
                // FindContours는 ROI 잘라낸 이미지 기준 좌표(0,0)를 반환하므로
                // 절대 좌표 변환 시 ROI 시작점만큼 오프셋을 더해야 함
                var adjustedROI = GetAdjustedROI(inputImage);
                int offsetX = UseROI ? adjustedROI.X : 0;
                int offsetY = UseROI ? adjustedROI.Y : 0;

                // ROI 영역만 잘라내어 작업 이미지 생성
                Mat workImage = GetROIImage(inputImage);
                Mat binaryImage = new Mat();

                // 그레이스케일 변환
                Mat grayImage = new Mat();
                if (workImage.Channels() > 1)
                    Cv2.CvtColor(workImage, grayImage, ColorConversionCodes.BGR2GRAY);
                else
                    grayImage = workImage.Clone();

                // 이진화 처리
                if (UseInternalThreshold)
                {
                    var threshType = SegmentationPolarity == SegmentationPolarity.DarkOnLight
                        ? ThresholdTypes.BinaryInv : ThresholdTypes.Binary;
                    Cv2.Threshold(grayImage, binaryImage, ThresholdValue, 255, threshType);
                }
                else
                {
                    binaryImage = grayImage.Clone();
                    if (SegmentationPolarity == SegmentationPolarity.DarkOnLight)
                        Cv2.BitwiseNot(binaryImage, binaryImage);
                }

                // Contour 검출 (잘라낸 이미지 기준 상대 좌표 반환, 0,0 기준)
                Cv2.FindContours(binaryImage, out Point[][] contours, out HierarchyIndex[] hierarchy,
                    RetrievalMode, ApproximationMode);

                var blobs = new List<BlobResult>();
                int blobId = 0;

                foreach (var contour in contours)
                {
                    // 상대 좌표(ROI 기준)를 절대 좌표(원본 이미지 기준)로 즉시 변환
                    // 이후 CalculateBlobProperties가 반환하는 모든 속성
                    // (CenterX, CenterY, BoundingRect 등)이 절대 좌표로 저장됨
                    Point[] absoluteContour = OffsetPoints(contour, offsetX, offsetY);
                    var blob = CalculateBlobProperties(absoluteContour, blobId);

                    if (blob.Area >= MinArea && blob.Area <= MaxArea &&
                        blob.Circularity >= MinCircularity && blob.Circularity <= MaxCircularity &&
                        blob.Convexity >= MinConvexity)
                    {
                        blobs.Add(blob);
                        blobId++;
                    }
                }

                // 결과 정렬 및 최대 개수 제한
                blobs = SortBlobs(blobs);
                if (blobs.Count > MaxBlobCount)
                    blobs = blobs.Take(MaxBlobCount).ToList();

                // result.Data 채우기 — PLC 전송 및 Tool 간 데이터 전달용
                double totalArea = blobs.Sum(b => b.Area);
                result.Data["BlobCount"] = blobs.Count;
                result.Data["TotalArea"] = totalArea;
                if (blobs.Count > 0)
                {
                    var first = blobs[0];
                    result.Data["CenterX"] = first.CenterX;
                    result.Data["CenterY"] = first.CenterY;
                    result.Data["Area"] = first.Area;
                    result.Data["Perimeter"] = first.Perimeter;
                    result.Data["Circularity"] = first.Circularity;
                    result.Data["Angle"] = first.Angle;
                    result.Data["BoundingRect"] = first.BoundingRect;
                }
                result.Data["Blobs"] = blobs;

                // 결과 오버레이 이미지 생성 (원본 컬러 이미지 기반)
                Mat overlayImage = GetColorOverlayBase(inputImage);

                // Blob 그래픽 그리기
                // blob 데이터는 이미 절대 좌표이므로 오프셋을 다시 적용하지 않음
                for (int i = 0; i < blobs.Count; i++)
                {
                    var blob = blobs[i];
                    var color = GetBlobColor(i);

                    if (DrawContours)
                    {
                        Cv2.DrawContours(overlayImage, new[] { blob.Contour }, 0, color, 2);
                    }

                    if (DrawBoundingBox)
                    {
                        Cv2.Rectangle(overlayImage, blob.BoundingRect, new Scalar(255, 255, 0), 1);
                    }

                    if (DrawCenterPoint)
                    {
                        Cv2.DrawMarker(overlayImage,
                            new Point((int)blob.CenterX, (int)blob.CenterY),
                            new Scalar(0, 0, 255), MarkerTypes.Cross, 10, 2);
                    }

                    if (DrawLabels)
                    {
                        Cv2.PutText(overlayImage, $"#{i}",
                            new Point((int)blob.CenterX + 5, (int)blob.CenterY - 5),
                            HersheyFonts.HersheySimplex, 0.4, new Scalar(255, 255, 255), 1);
                    }

                    result.Graphics.Add(new GraphicOverlay
                    {
                        Type = GraphicType.Polygon,
                        Points = blob.Contour.ToList(),
                        Color = color
                    });
                }

                // 판정 (Judgment)
                bool judgmentPass = true;
                var judgmentDetails = new List<string>();

                if (EnableJudgment || UseAreaJudgment || UseCountJudgment)
                {
                    // 면적 판정
                    if (UseAreaJudgment && blobs.Count > 0)
                    {
                        double areaLow = ExpectedArea - AreaToleranceMinus;
                        double areaHigh = ExpectedArea + AreaTolerancePlus;
                        bool areaPass = totalArea >= areaLow && totalArea <= areaHigh;
                        if (!areaPass)
                        {
                            judgmentPass = false;
                            judgmentDetails.Add($"Area NG: {totalArea:F1} (기준 {areaLow:F1}~{areaHigh:F1})");
                        }
                        else
                        {
                            judgmentDetails.Add($"Area OK: {totalArea:F1}");
                        }
                        result.Data["AreaJudgment"] = areaPass;
                    }

                    // 개수 판정
                    if (UseCountJudgment)
                    {
                        bool countPass = CountMode switch
                        {
                            CountJudgmentMode.Equal => blobs.Count == ExpectedCount,
                            CountJudgmentMode.GreaterOrEqual => blobs.Count >= ExpectedCount,
                            CountJudgmentMode.LessOrEqual => blobs.Count <= ExpectedCount,
                            CountJudgmentMode.Range => blobs.Count >= ExpectedCount && blobs.Count <= ExpectedCountMax,
                            _ => true
                        };
                        if (!countPass)
                        {
                            judgmentPass = false;
                            string expected = CountMode switch
                            {
                                CountJudgmentMode.Equal => $"={ExpectedCount}",
                                CountJudgmentMode.GreaterOrEqual => $">={ExpectedCount}",
                                CountJudgmentMode.LessOrEqual => $"<={ExpectedCount}",
                                CountJudgmentMode.Range => $"{ExpectedCount}~{ExpectedCountMax}",
                                _ => ""
                            };
                            judgmentDetails.Add($"Count NG: {blobs.Count} (기준 {expected})");
                        }
                        else
                        {
                            judgmentDetails.Add($"Count OK: {blobs.Count}");
                        }
                        result.Data["CountJudgment"] = countPass;
                    }

                    result.Data["JudgmentPass"] = judgmentPass;
                    result.Success = judgmentPass;
                    string status = judgmentPass ? "PASS" : "FAIL";
                    string detail = judgmentDetails.Count > 0 ? $" [{string.Join(", ", judgmentDetails)}]" : "";
                    result.Message = $"Blob {status}: {blobs.Count}개 검출{detail}";
                }
                else
                {
                    result.Success = blobs.Count > 0;
                    result.Message = $"Blob 검출 완료: {blobs.Count}개";
                }

                // 최종 결과 이미지 구성
                result.OutputImage = UseROI ? ApplyROIResult(inputImage, binaryImage) : binaryImage.Clone();
                result.OverlayImage = overlayImage;
                if (workImage != inputImage)
                    workImage.Dispose();
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"Blob 분석 실패: {ex.Message}";
            }

            sw.Stop();
            ExecutionTime = sw.Elapsed.TotalMilliseconds;
            LastResult = result;
            return result;
        }

        private BlobResult CalculateBlobProperties(Point[] contour, int id)
        {
            var blob = new BlobResult
            {
                Id = id,
                Contour = contour
            };

            // 면적
            blob.Area = Cv2.ContourArea(contour);

            // 둘레
            blob.Perimeter = Cv2.ArcLength(contour, true);

            // Bounding Rectangle
            blob.BoundingRect = Cv2.BoundingRect(contour);

            // Minimum Area Rectangle (회전된 사각형)
            if (contour.Length >= 5)
            {
                blob.MinAreaRect = Cv2.MinAreaRect(contour);
                blob.Angle = blob.MinAreaRect.Angle;
            }

            // 모멘트 계산
            var moments = Cv2.Moments(contour);
            if (moments.M00 > 0)
            {
                blob.CenterX = moments.M10 / moments.M00;
                blob.CenterY = moments.M01 / moments.M00;
            }
            else
            {
                blob.CenterX = blob.BoundingRect.X + blob.BoundingRect.Width / 2.0;
                blob.CenterY = blob.BoundingRect.Y + blob.BoundingRect.Height / 2.0;
            }

            // Circularity (4π × Area / Perimeter²)
            if (blob.Perimeter > 0)
                blob.Circularity = 4 * Math.PI * blob.Area / (blob.Perimeter * blob.Perimeter);

            // Aspect Ratio
            if (blob.BoundingRect.Height > 0)
                blob.AspectRatio = (double)blob.BoundingRect.Width / blob.BoundingRect.Height;

            // Convex Hull
            var hull = Cv2.ConvexHull(contour);
            double hullArea = Cv2.ContourArea(hull);
            blob.Convexity = hullArea > 0 ? blob.Area / hullArea : 1;

            // Equivalent Diameter
            blob.EquivalentDiameter = Math.Sqrt(4 * blob.Area / Math.PI);

            // Extent (Area / Bounding Rect Area)
            double rectArea = blob.BoundingRect.Width * blob.BoundingRect.Height;
            blob.Extent = rectArea > 0 ? blob.Area / rectArea : 0;

            // Solidity (Area / Convex Hull Area)
            blob.Solidity = hullArea > 0 ? blob.Area / hullArea : 1;

            // Fit Ellipse (5개 이상의 점 필요)
            if (contour.Length >= 5)
            {
                blob.FitEllipse = Cv2.FitEllipse(contour);
            }

            return blob;
        }

        private List<BlobResult> SortBlobs(List<BlobResult> blobs)
        {
            IEnumerable<BlobResult> sorted = SortBy switch
            {
                BlobSortBy.Area => blobs.OrderBy(b => b.Area),
                BlobSortBy.Perimeter => blobs.OrderBy(b => b.Perimeter),
                BlobSortBy.CenterX => blobs.OrderBy(b => b.CenterX),
                BlobSortBy.CenterY => blobs.OrderBy(b => b.CenterY),
                BlobSortBy.Circularity => blobs.OrderBy(b => b.Circularity),
                BlobSortBy.AspectRatio => blobs.OrderBy(b => b.AspectRatio),
                _ => blobs.OrderBy(b => b.Area)
            };

            if (SortDescending)
                sorted = sorted.Reverse();

            return sorted.ToList();
        }

        private static Point[] OffsetPoints(Point[] points, int offsetX, int offsetY)
        {
            if (offsetX == 0 && offsetY == 0)
                return points;

            var result = new Point[points.Length];
            for (int i = 0; i < points.Length; i++)
            {
                result[i] = new Point(points[i].X + offsetX, points[i].Y + offsetY);
            }
            return result;
        }

        private Mat GetColorOverlayBase(Mat inputImage)
        {
            var orig = OverlayBaseImage ?? VisionService.Instance.CurrentImage;
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

        private Scalar GetBlobColor(int index)
        {
            // 다양한 색상으로 Blob 구분
            var colors = new Scalar[]
            {
                new Scalar(0, 255, 0),     // Green
                new Scalar(255, 0, 0),     // Blue
                new Scalar(0, 255, 255),   // Yellow
                new Scalar(255, 0, 255),   // Magenta
                new Scalar(255, 255, 0),   // Cyan
                new Scalar(0, 128, 255),   // Orange
                new Scalar(128, 0, 255),   // Pink
                new Scalar(0, 255, 128),   // Spring Green
            };

            return colors[index % colors.Length];
        }

        public override List<string> GetAvailableResultKeys()
        {
            return new List<string>
            {
                "Success", "BlobCount", "TotalArea", "CenterX", "CenterY",
                "Area", "Perimeter", "Circularity", "Angle",
                "AreaJudgment", "CountJudgment", "JudgmentPass"
            };
        }

        public override VisionToolBase Clone()
        {
            var clone = new BlobTool
            {
                Name = this.Name,
                ToolType = this.ToolType,
                IsEnabled = this.IsEnabled,
                ROI = this.ROI,
                UseROI = this.UseROI,
                UseInternalThreshold = this.UseInternalThreshold,
                ThresholdValue = this.ThresholdValue,
                SegmentationPolarity = this.SegmentationPolarity,
                MinArea = this.MinArea,
                MaxArea = this.MaxArea,
                MinPerimeter = this.MinPerimeter,
                MaxPerimeter = this.MaxPerimeter,
                MinCircularity = this.MinCircularity,
                MaxCircularity = this.MaxCircularity,
                MinAspectRatio = this.MinAspectRatio,
                MaxAspectRatio = this.MaxAspectRatio,
                MinConvexity = this.MinConvexity,
                MaxBlobCount = this.MaxBlobCount,
                SortBy = this.SortBy,
                SortDescending = this.SortDescending,
                RetrievalMode = this.RetrievalMode,
                ApproximationMode = this.ApproximationMode,
                DrawContours = this.DrawContours,
                DrawBoundingBox = this.DrawBoundingBox,
                DrawCenterPoint = this.DrawCenterPoint,
                DrawLabels = this.DrawLabels,
                EnableJudgment = this.EnableJudgment,
                UseAreaJudgment = this.UseAreaJudgment,
                ExpectedArea = this.ExpectedArea,
                AreaTolerancePlus = this.AreaTolerancePlus,
                AreaToleranceMinus = this.AreaToleranceMinus,
                UseCountJudgment = this.UseCountJudgment,
                CountMode = this.CountMode,
                ExpectedCount = this.ExpectedCount,
                ExpectedCountMax = this.ExpectedCountMax
            };
            CopyPlcMappingsTo(clone);
            return clone;
        }
    }

    /// <summary>
    /// Blob 분석 결과
    /// </summary>
    public class BlobResult
    {
        public int Id { get; set; }
        public Point[] Contour { get; set; } = Array.Empty<Point>();

        // 위치
        public double CenterX { get; set; }
        public double CenterY { get; set; }

        // 크기
        public double Area { get; set; }
        public double Perimeter { get; set; }
        public double EquivalentDiameter { get; set; }

        // 형상
        public double Circularity { get; set; }
        public double AspectRatio { get; set; }
        public double Convexity { get; set; }
        public double Solidity { get; set; }
        public double Extent { get; set; }
        public double Angle { get; set; }

        // Bounding Box
        public Rect BoundingRect { get; set; }
        public RotatedRect MinAreaRect { get; set; }
        public RotatedRect FitEllipse { get; set; }
    }

    public enum BlobSortBy
    {
        Area,
        Perimeter,
        CenterX,
        CenterY,
        Circularity,
        AspectRatio
    }

    public enum SegmentationPolarity
    {
        LightOnDark,
        DarkOnLight
    }

    /// <summary>
    /// Blob 개수 판정 모드
    /// </summary>
    public enum CountJudgmentMode
    {
        /// <summary>정확히 N개</summary>
        Equal,
        /// <summary>N개 이상</summary>
        GreaterOrEqual,
        /// <summary>N개 이하</summary>
        LessOrEqual,
        /// <summary>Min ~ Max 범위</summary>
        Range
    }
}