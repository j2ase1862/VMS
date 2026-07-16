using VMS.VisionSetup.Attributes;
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
        [TunableParam(
            Description = "true면 BlobTool 내부에서 이진화 수행. false면 입력 이미지가 이미 이진화되어 있다고 가정.",
            Tier = TuningTier.Semantic, DefaultHint = "true")]
        public bool UseInternalThreshold
        {
            get => _useInternalThreshold;
            set => SetProperty(ref _useInternalThreshold, value);
        }

        private double _thresholdValue = 128;
        [TunableParam(
            Description = "내부 이진화 임계값. 이미지 평균 밝기 근처가 합리적.",
            Tier = TuningTier.ImageDependent,
            Min = 0, Max = 255,
            DependsOn = "UseInternalThreshold", DefaultHint = "128")]
        public double ThresholdValue
        {
            get => _thresholdValue;
            set => SetProperty(ref _thresholdValue, Math.Clamp(value, 0, 255));
        }

        private SegmentationPolarity _segmentationPolarity = SegmentationPolarity.LightOnDark;
        [TunableParam(
            Description = "검출 객체의 밝기 극성. LightOnDark: 어두운 배경의 밝은 객체. DarkOnLight: 밝은 배경의 어두운 객체.",
            Tier = TuningTier.Semantic, DefaultHint = "LightOnDark")]
        public SegmentationPolarity SegmentationPolarity
        {
            get => _segmentationPolarity;
            set => SetProperty(ref _segmentationPolarity, value);
        }

        // 면적 필터
        private double _minArea = 100;
        [TunableParam(
            Description = "검출 객체 최소 면적(픽셀). 작은 노이즈 제거용. 작은 결함은 10~50, 일반 객체는 100~500.",
            Tier = TuningTier.DomainCommon, Min = 0, Max = 100000, DefaultHint = "100")]
        public double MinArea
        {
            get => _minArea;
            set => SetProperty(ref _minArea, Math.Max(0, value));
        }

        private double _maxArea = double.MaxValue;
        [TunableParam(
            Description = "검출 객체 최대 면적(픽셀). 너무 큰 영역(배경 등) 제외용. 무제한이면 매우 큰 값 사용.",
            Tier = TuningTier.DomainCommon, Min = 0)]
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
        [TunableParam(
            Description = "검출 객체 최소 원형도(0~1). 1에 가까울수록 원형. 원형 객체만 찾으려면 0.7 이상.",
            Tier = TuningTier.DomainCommon, Min = 0, Max = 1, DefaultHint = "0")]
        public double MinCircularity
        {
            get => _minCircularity;
            set => SetProperty(ref _minCircularity, Math.Clamp(value, 0, 1));
        }

        private double _maxCircularity = 1;
        [TunableParam(
            Description = "검출 객체 최대 원형도. 보통 1로 두고 MinCircularity로 필터.",
            Tier = TuningTier.DomainCommon, Min = 0, Max = 1, DefaultHint = "1")]
        public double MaxCircularity
        {
            get => _maxCircularity;
            set => SetProperty(ref _maxCircularity, Math.Clamp(value, MinCircularity, 1));
        }

        private double _minAspectRatio = 0;
        [TunableParam(
            Description = "검출 객체 최소 종횡비(W/H). 길쭉한 객체 필터링용. 정사각형 근처는 0.8~1.2.",
            Tier = TuningTier.DomainCommon, Min = 0, DefaultHint = "0")]
        public double MinAspectRatio
        {
            get => _minAspectRatio;
            set => SetProperty(ref _minAspectRatio, Math.Max(0, value));
        }

        private double _maxAspectRatio = double.MaxValue;
        [TunableParam(
            Description = "검출 객체 최대 종횡비. 매우 길쭉한 객체 제외용.",
            Tier = TuningTier.DomainCommon, Min = 0)]
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
        [TunableParam(
            Description = "결과로 반환할 최대 객체 수. SortBy 기준 상위 N개만 유지.",
            Tier = TuningTier.DomainCommon, Min = 1, Max = 10000, DefaultHint = "100")]
        public int MaxBlobCount
        {
            get => _maxBlobCount;
            set => SetProperty(ref _maxBlobCount, Math.Max(1, value));
        }

        // 정렬 기준
        private BlobSortBy _sortBy = BlobSortBy.Area;
        [TunableParam(
            Description = "Blob 정렬 기준. Area: 면적. Position: 위치. 기본 Area.",
            Tier = TuningTier.Semantic, DefaultHint = "Area")]
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

            // 중간 Mat — 예외/조기 경로를 포함해 finally에서 일괄 해제 (FeatureMatchTool의 using 패턴 준용)
            Mat? workImage = null;
            Mat? grayImage = null;
            Mat? binaryImage = null;
            Mat? rotationMatrixFwd = null;
            Mat? overlayImage = null;

            try
            {
                // 회전 각도 결정: 라이브 ROI shape 또는 저장된 ROIAngle 사용
                double effectiveAngle = AssociatedROIShape is RectangleAffineROI liveROI
                    ? liveROI.Angle : ROIAngle;

                bool useRotatedROI = UseROI && ROI.Width > 0 && ROI.Height > 0
                    && Math.Abs(effectiveAngle) > 0.001;

                int offsetX, offsetY;
                double roiCenterX, roiCenterY;
                int roiW, roiH;

                if (useRotatedROI)
                {
                    // ── 회전 ROI: 역회전 → 축 정렬 크롭 → 정회전(좌표 복원) ──
                    var adjustedROI = GetAdjustedROI(inputImage);
                    roiCenterX = ROICenterX != 0 ? ROICenterX : adjustedROI.X + adjustedROI.Width / 2.0;
                    roiCenterY = ROICenterY != 0 ? ROICenterY : adjustedROI.Y + adjustedROI.Height / 2.0;
                    roiW = adjustedROI.Width;
                    roiH = adjustedROI.Height;

                    var center = new Point2f((float)roiCenterX, (float)roiCenterY);

                    // 역회전 행렬로 이미지를 회전하여 ROI를 축 정렬
                    var rotMatInv = Cv2.GetRotationMatrix2D(center, -effectiveAngle, 1.0);
                    using var rotatedImage = new Mat();
                    Cv2.WarpAffine(inputImage, rotatedImage, rotMatInv, inputImage.Size(),
                        InterpolationFlags.Linear, BorderTypes.Reflect101);

                    // 축 정렬된 영역으로 크롭
                    int cropX = Math.Max(0, (int)(roiCenterX - roiW / 2.0));
                    int cropY = Math.Max(0, (int)(roiCenterY - roiH / 2.0));
                    int cropW = Math.Min(roiW, rotatedImage.Width - cropX);
                    int cropH = Math.Min(roiH, rotatedImage.Height - cropY);

                    if (cropW <= 0 || cropH <= 0)
                    {
                        workImage = inputImage.Clone();
                        offsetX = 0;
                        offsetY = 0;
                    }
                    else
                    {
                        workImage = new Mat(rotatedImage, new Rect(cropX, cropY, cropW, cropH));
                        offsetX = cropX;
                        offsetY = cropY;
                    }

                    // 정회전 행렬 (좌표 복원용)
                    rotationMatrixFwd = Cv2.GetRotationMatrix2D(center, effectiveAngle, 1.0);
                    rotMatInv.Dispose();
                }
                else
                {
                    // ── 기존 로직: 축 정렬 ROI ──
                    var adjustedROI = GetAdjustedROI(inputImage);
                    offsetX = UseROI ? adjustedROI.X : 0;
                    offsetY = UseROI ? adjustedROI.Y : 0;
                    roiCenterX = 0;
                    roiCenterY = 0;
                    roiW = 0;
                    roiH = 0;
                    workImage = GetROIImage(inputImage);
                }

                // 그레이스케일 변환
                if (workImage.Channels() > 1)
                {
                    grayImage = new Mat();
                    Cv2.CvtColor(workImage, grayImage, ColorConversionCodes.BGR2GRAY);
                }
                else
                {
                    grayImage = workImage.Clone();
                }

                // 이진화 처리
                if (UseInternalThreshold)
                {
                    binaryImage = new Mat();
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

                // 비사각형 ROI(Circle/Ellipse/Polygon) 마스크 적용: contour 검출과 결과가 도형 안에 한정됨.
                // 회전 사각형(useRotatedROI)은 위쪽 분기에서 별도 처리되므로 여기 도달하지 않음.
                if (!useRotatedROI)
                {
                    using var shapeMaskFull = GetNonRectangularShapeMask(inputImage);
                    if (shapeMaskFull != null)
                    {
                        var adjustedROIForMask = GetAdjustedROI(inputImage);
                        if (adjustedROIForMask.Width > 0 && adjustedROIForMask.Height > 0)
                        {
                            using var maskCrop = new Mat(shapeMaskFull, adjustedROIForMask);
                            if (maskCrop.Size() == binaryImage.Size())
                            {
                                Cv2.BitwiseAnd(binaryImage, maskCrop, binaryImage);
                            }
                        }
                    }
                }

                // Contour 검출 (잘라낸 이미지 기준 상대 좌표 반환, 0,0 기준)
                Cv2.FindContours(binaryImage, out Point[][] contours, out HierarchyIndex[] hierarchy,
                    RetrievalMode, ApproximationMode);

                var blobs = new List<BlobResult>();
                int blobId = 0;

                foreach (var contour in contours)
                {
                    Point[] absoluteContour;

                    if (useRotatedROI && rotationMatrixFwd != null)
                    {
                        // 로컬 좌표 → 역회전된 이미지 좌표 → 정회전하여 원본 좌표로 복원
                        absoluteContour = TransformContourToOriginal(contour, offsetX, offsetY, rotationMatrixFwd);
                    }
                    else
                    {
                        // 상대 좌표를 절대 좌표로 변환
                        absoluteContour = OffsetPoints(contour, offsetX, offsetY);
                    }

                    var blob = CalculateBlobProperties(absoluteContour, blobId);

                    if (blob.Area >= MinArea && blob.Area <= MaxArea &&
                        blob.Circularity >= MinCircularity && blob.Circularity <= MaxCircularity &&
                        blob.Convexity >= MinConvexity)
                    {
                        blobs.Add(blob);
                        blobId++;
                    }
                }

                // rotationMatrixFwd는 finally에서 해제

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
                overlayImage = GetColorOverlayBase(inputImage);

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
                if (useRotatedROI)
                {
                    // 회전된 ROI 마스크를 사용하여 결과 이미지 합성
                    var resultImage = new Mat(inputImage.Size(), binaryImage.Type(), Scalar.Black);
                    using var mask = new Mat(inputImage.Size(), MatType.CV_8UC1, Scalar.Black);
                    var roiShape = AssociatedROIShape as RectangleAffineROI;
                    if (roiShape != null)
                    {
                        var corners = roiShape.GetCorners();
                        var cvPoints = new Point[]
                        {
                            new Point((int)corners[0].X, (int)corners[0].Y),
                            new Point((int)corners[1].X, (int)corners[1].Y),
                            new Point((int)corners[2].X, (int)corners[2].Y),
                            new Point((int)corners[3].X, (int)corners[3].Y)
                        };
                        Cv2.FillConvexPoly(mask, cvPoints, Scalar.White);
                    }
                    else
                    {
                        // Fallback: ROIAngle로 직접 마스크 생성
                        var affineROI = new RectangleAffineROI(roiCenterX, roiCenterY, roiW, roiH, effectiveAngle);
                        var corners = affineROI.GetCorners();
                        var cvPoints = new Point[]
                        {
                            new Point((int)corners[0].X, (int)corners[0].Y),
                            new Point((int)corners[1].X, (int)corners[1].Y),
                            new Point((int)corners[2].X, (int)corners[2].Y),
                            new Point((int)corners[3].X, (int)corners[3].Y)
                        };
                        Cv2.FillConvexPoly(mask, cvPoints, Scalar.White);
                    }

                    // 역회전 → 이진화 결과를 정회전 → 마스크 적용
                    var center2f = new Point2f((float)roiCenterX, (float)roiCenterY);
                    using var rotMatFwd = Cv2.GetRotationMatrix2D(center2f, effectiveAngle, 1.0);
                    using var binaryFull = new Mat(inputImage.Size(), binaryImage.Type(), Scalar.Black);

                    // binaryImage를 역회전된 전체 이미지 위 올바른 위치에 복원
                    int pX = Math.Max(0, (int)(roiCenterX - roiW / 2.0));
                    int pY = Math.Max(0, (int)(roiCenterY - roiH / 2.0));
                    int pW = Math.Min(binaryImage.Width, binaryFull.Width - pX);
                    int pH = Math.Min(binaryImage.Height, binaryFull.Height - pY);
                    if (pW > 0 && pH > 0)
                    {
                        var srcRegion = binaryImage.Width == pW && binaryImage.Height == pH
                            ? binaryImage : new Mat(binaryImage, new Rect(0, 0, pW, pH));
                        srcRegion.CopyTo(new Mat(binaryFull, new Rect(pX, pY, pW, pH)));
                        if (srcRegion != binaryImage) srcRegion.Dispose();
                    }

                    // 정회전하여 원본 좌표계로 복원
                    using var binaryRotated = new Mat();
                    Cv2.WarpAffine(binaryFull, binaryRotated, rotMatFwd, inputImage.Size(),
                        InterpolationFlags.Linear, BorderTypes.Constant, Scalar.Black);

                    // 마스크 적용
                    binaryRotated.CopyTo(resultImage, mask);
                    result.OutputImage = resultImage;
                }
                else
                {
                    result.OutputImage = UseROI ? ApplyROIResult(inputImage, binaryImage) : binaryImage.Clone();
                }
                result.OverlayImage = overlayImage;
                overlayImage = null;    // 소유권이 result로 이전됨 — finally에서 해제 금지
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"Blob 분석 실패: {ex.Message}";
            }
            finally
            {
                // 중간 Mat 일괄 해제 — 예외 경로 포함, 실행마다 네이티브 버퍼가 GC에 의존하지 않도록
                rotationMatrixFwd?.Dispose();
                grayImage?.Dispose();
                binaryImage?.Dispose();
                overlayImage?.Dispose();    // 정상 경로에서는 result로 이전 후 null이므로 no-op
                if (workImage != null && !ReferenceEquals(workImage, inputImage))
                    workImage.Dispose();    // 서브매트 뷰 해제 — 원본 버퍼는 참조 카운트로 보존
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

        /// <summary>
        /// 로컬 contour 좌표를 역회전 이미지 좌표로 오프셋 후, 정회전 행렬로 원본 좌표로 복원
        /// </summary>
        private static Point[] TransformContourToOriginal(Point[] localContour, int cropX, int cropY, Mat rotationMatrixFwd)
        {
            // 행렬 값 추출 (2x3 affine matrix)
            double m00 = rotationMatrixFwd.At<double>(0, 0);
            double m01 = rotationMatrixFwd.At<double>(0, 1);
            double m02 = rotationMatrixFwd.At<double>(0, 2);
            double m10 = rotationMatrixFwd.At<double>(1, 0);
            double m11 = rotationMatrixFwd.At<double>(1, 1);
            double m12 = rotationMatrixFwd.At<double>(1, 2);

            var result = new Point[localContour.Length];
            for (int i = 0; i < localContour.Length; i++)
            {
                // 로컬 → 역회전된 이미지 좌표
                double ix = localContour[i].X + cropX;
                double iy = localContour[i].Y + cropY;

                // 정회전 행렬 적용 → 원본 좌표
                double ox = m00 * ix + m01 * iy + m02;
                double oy = m10 * ix + m11 * iy + m12;

                result[i] = new Point((int)Math.Round(ox), (int)Math.Round(oy));
            }
            return result;
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
                ROIAngle = this.ROIAngle,
                ROICenterX = this.ROICenterX,
                ROICenterY = this.ROICenterY,
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