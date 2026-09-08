using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using VMS.VisionSetup.Models;

namespace VMS.VisionSetup.VisionTools.DeepLearning
{
    /// <summary>
    /// 클래스별 Confidence 임계값 엔트리 (UI 바인딩용).
    /// </summary>
    public partial class ClassThresholdEntry : ObservableObject
    {
        [ObservableProperty] private int _classId;
        [ObservableProperty] private string _className = string.Empty;
        [ObservableProperty] private double _threshold = 0.25;
    }

    /// <summary>
    /// 클래스별 Dot cluster 기대값 엔트리 (UI 바인딩용).
    /// YOLO가 검출한 각 바운딩 박스 내부의 dot 개수·배치 각도 기준.
    /// </summary>
    public partial class DotClusterExpectation : ObservableObject
    {
        [ObservableProperty] private int _classId;
        [ObservableProperty] private string _className = string.Empty;
        /// <summary>기대 dot 개수 (0이면 개수 검사 생략)</summary>
        [ObservableProperty] private int _expectedCount = 3;
        /// <summary>개수 허용 편차 (±N). 0이면 정확히 일치해야 OK</summary>
        [ObservableProperty] private int _countTolerance;
        /// <summary>기준 배치 각도 (도, -90~+90). 체크 해제 시 각도 검사 생략</summary>
        [ObservableProperty] private double _referenceAngle;
        /// <summary>각도 허용 편차 (±도)</summary>
        [ObservableProperty] private double _angleTolerance = 10.0;
        /// <summary>이 클래스의 각도 검사 활성화 여부</summary>
        [ObservableProperty] private bool _checkAngle;
    }

    /// <summary>
    /// Cognex ViDi Blue Locate 대응 — ONNX 기반 객체 검출 도구.
    /// D-FINE(Apache-2.0, 기본 백본 — train_dfine.py) 또는 YOLOv8/v11 ONNX 모델을 로드하여
    /// 이미지에서 객체를 검출합니다. 모델 규약은 OnnxEngineCache 가 파일에서 자동 판별한다.
    /// </summary>
    public partial class DetectionTool : VisionToolBase
    {
        private IDetectionEngine? _engine;
        private readonly object _engineLock = new();

        // ── Parameters ──

        [ObservableProperty]
        private string _modelPath = string.Empty;

        [ObservableProperty]
        private int _inputSize = 640;

        /// <summary>모든 클래스에 대한 기본 Confidence 임계값 (클래스별 값 미지정 시 사용)</summary>
        [ObservableProperty]
        private double _confidenceThreshold = 0.25;

        [ObservableProperty]
        private double _iouThreshold = 0.45;

        [ObservableProperty]
        private string _classNamesText = string.Empty;

        [ObservableProperty]
        private bool _drawOverlay = true;

        // ── CLAHE 전처리 ──

        /// <summary>추론 전 CLAHE 대비 보정을 적용할지 여부 (조명 변동 환경에서 검출률 개선)</summary>
        [ObservableProperty]
        private bool _useClahe;

        /// <summary>CLAHE Clip Limit (2.0 권장, 값 ↑ = 대비 강화 + 노이즈 증폭)</summary>
        [ObservableProperty]
        private double _claheClipLimit = 2.0;

        /// <summary>CLAHE 타일 그리드 크기 (N×N, 8 권장)</summary>
        [ObservableProperty]
        private int _claheTileGridSize = 8;

        // ── Per-class threshold ──

        /// <summary>ONNX 메타데이터에서 로드된 클래스 목록</summary>
        [ObservableProperty]
        private ObservableCollection<string> _modelClassNames = new();

        /// <summary>클래스별 Confidence 임계값. 비어 있으면 ConfidenceThreshold를 공통으로 사용합니다.</summary>
        [ObservableProperty]
        private ObservableCollection<ClassThresholdEntry> _classThresholds = new();

        /// <summary>클래스별 임계값 UI 노출 여부</summary>
        [ObservableProperty]
        private bool _usePerClassThresholds;

        // ── SAHI Tiled Inference ──

        /// <summary>SAHI 타일링 추론 사용 여부 (고해상도 이미지에서 작은 결함 검출에 효과적)</summary>
        [ObservableProperty]
        private bool _useSahi;

        /// <summary>SAHI 타일 크기(px). InputSize의 1~2배 권장</summary>
        [ObservableProperty]
        private int _sahiTileSize = 640;

        /// <summary>타일 간 겹침 비율 (0.0~0.5). 0.2 = 20% 겹침</summary>
        [ObservableProperty]
        private double _sahiOverlapRatio = 0.2;

        // ── Dot Cluster Analysis ──

        /// <summary>YOLO 검출 이후 각 바운딩 박스 내부의 dot 군락을 추가 분석할지 여부.</summary>
        [ObservableProperty]
        private bool _useDotAnalysis;

        [ObservableProperty]
        private DotDetectionMethod _dotDetectionMethod = DotDetectionMethod.Blob;

        [ObservableProperty]
        private int _minDotArea = 10;

        [ObservableProperty]
        private int _maxDotArea = 500;

        /// <summary>dot 원형도 최소값 (0~1, 1=완벽한 원). Blob 모드에서만 적용.</summary>
        [ObservableProperty]
        private double _dotCircularityThreshold = 0.7;

        /// <summary>이 거리 이하로 가까운 dot은 하나로 병합한다 (px).</summary>
        [ObservableProperty]
        private int _minDotDistance = 10;

        [ObservableProperty]
        private DotPatternMetric _dotPatternMetric = DotPatternMetric.MainAxisAngle;

        // ── 저대비 이미지 보완 옵션 ──

        /// <summary>ROI 전처리 방식 (저대비 dot 검출 개선용).</summary>
        [ObservableProperty]
        private DotPreprocessMode _dotPreprocessMode = DotPreprocessMode.None;

        /// <summary>CLAHE clip limit (DotPreprocessMode=Clahe일 때). 값↑ = 대비 강화.</summary>
        [ObservableProperty]
        private double _dotClaheClipLimit = 3.0;

        /// <summary>Top-hat/Bottom-hat 커널 크기 (px, 홀수). dot보다 크고 주변 구조보다 작아야 효과적.</summary>
        [ObservableProperty]
        private int _dotMorphKernelSize = 15;

        /// <summary>이진화 방식 (Otsu vs Adaptive).</summary>
        [ObservableProperty]
        private DotThresholdMode _dotThresholdMode = DotThresholdMode.Otsu;

        /// <summary>Adaptive threshold block size (홀수, 픽셀). 크면 부드럽게, 작으면 민감하게.</summary>
        [ObservableProperty]
        private int _dotAdaptiveBlockSize = 25;

        /// <summary>Adaptive threshold C (국소 평균에서 뺄 값). 값↑ = 전경 기준 엄격.</summary>
        [ObservableProperty]
        private double _dotAdaptiveC = 5.0;

        /// <summary>클래스별 dot 기대값. 모델 메타데이터에서 클래스가 로드될 때 자동으로 채워진다.</summary>
        [ObservableProperty]
        private ObservableCollection<DotClusterExpectation> _dotExpectations = new();

        public DetectionTool()
        {
            Name = "Detection";
            ToolType = "DetectionTool";
        }

        partial void OnModelPathChanged(string value)
        {
            // 캐시가 엔진 생명주기를 관리하므로 이 도구 인스턴스는 엔진을 Dispose하지 않는다
            // (같은 ModelPath를 쓰는 다른 도구가 공유할 수 있음).
            lock (_engineLock)
            {
                _engine = null;
            }
            LoadModelMetadata(value);
            // Recipe Load에서 이미 프리페치됐다면 NoOp. 새로 고른 경우엔 지금 워밍업 시작.
            OnnxEngineCache.PrefetchDetector(value, InputSize);
        }

        // Execute 진입 시 호출. 캐시가 완료된 엔진을 반환하거나, 진행 중이면 완료를 대기한다.
        // 캐시 경로가 실패하면 (파일 없음 등) 기존 방식으로 폴백.
        private IDetectionEngine EnsureEngine()
        {
            lock (_engineLock)
            {
                if (_engine != null) return _engine;
            }

            var engine = OnnxEngineCache.GetDetector(ModelPath, InputSize);

            lock (_engineLock)
            {
                _engine = engine;
                return engine;
            }
        }

        partial void OnConfidenceThresholdChanged(double value)
        {
            // 클래스별 임계값이 비어 있으면 전역값이 그대로 사용되므로 아무 작업 없음.
            // 사용자가 Per-Class를 켠 상태에서 이미 개별값이 존재하면 유지한다.
        }

        private void LoadModelMetadata(string modelPath)
        {
            // 기존 DotExpectations 값은 사용자 설정이므로 가능한 한 보존한다.
            var previousDotExpectations = DotExpectations.ToDictionary(e => e.ClassName, e => e);

            ModelClassNames.Clear();
            ClassThresholds.Clear();
            DotExpectations.Clear();

            if (string.IsNullOrEmpty(modelPath) || !System.IO.File.Exists(modelPath))
                return;

            try
            {
                // InferenceSession 생성 없이 ONNX 파일에서 메타데이터만 직접 파싱한다.
                // (이전 구현은 매 Step Load마다 전체 세션을 만들어 1~5초 지연을 유발했음)
                var names = OnnxModelBase.ReadClassNamesFromFile(modelPath);
                if (names.Length > 0)
                {
                    for (int i = 0; i < names.Length; i++)
                    {
                        ModelClassNames.Add(names[i]);
                        ClassThresholds.Add(new ClassThresholdEntry
                        {
                            ClassId = i,
                            ClassName = names[i],
                            Threshold = ConfidenceThreshold
                        });

                        // Dot 기대값: 같은 클래스명의 기존 설정이 있으면 재사용.
                        if (previousDotExpectations.TryGetValue(names[i], out var prev))
                        {
                            prev.ClassId = i;
                            DotExpectations.Add(prev);
                        }
                        else
                        {
                            DotExpectations.Add(new DotClusterExpectation
                            {
                                ClassId = i,
                                ClassName = names[i],
                                ExpectedCount = 3,
                                CountTolerance = 0,
                                ReferenceAngle = 0,
                                AngleTolerance = 10,
                                CheckAngle = false
                            });
                        }
                    }
                    ClassNamesText = string.Join(", ", names);
                }
            }
            catch { /* 모델 로드 실패 시 무시 */ }
        }

        public string[] ClassNames => string.IsNullOrWhiteSpace(ClassNamesText)
            ? Array.Empty<string>()
            : ClassNamesText.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        public override VisionResult Execute(Mat inputImage)
        {
            var result = new VisionResult();
            Mat? claheBuffer = null;

            try
            {
                if (string.IsNullOrEmpty(ModelPath))
                {
                    result.Success = false;
                    result.Message = "모델 경로를 지정하세요.";
                    return result;
                }

                var roiImage = UseROI ? GetROIImage(inputImage) : inputImage;

                // CLAHE 전처리 (옵션)
                Mat inferenceInput = roiImage;
                if (UseClahe)
                {
                    claheBuffer = OnnxModelBase.ApplyClahe(roiImage, ClaheClipLimit, ClaheTileGridSize);
                    inferenceInput = claheBuffer;
                }

                var engine = EnsureEngine();

                float[]? perClassConf = BuildPerClassThresholds();

                List<DetectionResult> detections;
                string mode;
                if (UseSahi)
                {
                    int tileSize = Math.Max(InputSize, SahiTileSize);
                    double overlap = Math.Clamp(SahiOverlapRatio, 0.0, 0.5);
                    detections = RunSahiDetection(engine, inferenceInput, tileSize, overlap,
                        (float)ConfidenceThreshold, (float)IouThreshold, perClassConf);
                    mode = $"SAHI {tileSize}px/{overlap:P0}";
                }
                else
                {
                    detections = engine.Detect(
                        inferenceInput, InputSize,
                        (float)ConfidenceThreshold, (float)IouThreshold,
                        perClassConf);
                    mode = "Full";
                }

                // Dot Cluster 분석 (옵션): YOLO 바운딩 박스 각각에 대해 내부의 dot 개수·배치 각도 측정.
                // 원본 이미지(inputImage) 기준으로 자름 — CLAHE 전처리 본은 추론에만 쓰고 분석은 원본으로.
                List<ClusterAnalysis>? clusters = null;
                bool allClustersOK = true;
                if (UseDotAnalysis && detections.Count > 0)
                {
                    clusters = new List<ClusterAnalysis>(detections.Count);
                    int roiX = UseROI ? ROI.X : 0;
                    int roiY = UseROI ? ROI.Y : 0;

                    foreach (var det in detections)
                    {
                        var absRect = new Rect(det.X + roiX, det.Y + roiY, det.Width, det.Height);
                        var analysis = AnalyzeCluster(inputImage, absRect, det.ClassId);
                        clusters.Add(analysis);
                        if (!analysis.ClusterOK) allClustersOK = false;
                    }
                }

                // Success 판정: Dot 분석이 켜져 있으면 모든 cluster가 OK여야 전체 성공.
                bool successBase = detections.Count > 0;
                result.Success = UseDotAnalysis ? (successBase && allClustersOK) : successBase;

                string msgSuffix = "";
                if (UseDotAnalysis && clusters != null)
                {
                    int okCount = clusters.Count(c => c.ClusterOK);
                    msgSuffix = $" | Dot분석 {okCount}/{clusters.Count} OK";
                }
                result.Message = $"{detections.Count}개 객체 검출 ({mode}, EP: {engine.ActiveProvider}){msgSuffix}";

                result.Data["DetectionCount"] = detections.Count;
                result.Data["Detections"] = detections;
                result.Data["ExecutionProvider"] = engine.ActiveProvider;
                if (UseDotAnalysis) result.Data["AllClustersOK"] = allClustersOK;

                // 오버레이
                if (DrawOverlay)
                {
                    var overlay = GetColorOverlayBase(inputImage);
                    var classNames = ClassNames;
                    int roiOffsetX = UseROI ? ROI.X : 0;
                    int roiOffsetY = UseROI ? ROI.Y : 0;

                    for (int i = 0; i < detections.Count; i++)
                    {
                        var det = detections[i];
                        var rect = new Rect(
                            det.X + roiOffsetX, det.Y + roiOffsetY,
                            det.Width, det.Height);

                        // 박스 색: Dot 분석이 켜져 있으면 pass/fail 색상, 아니면 클래스 기본 색상
                        Scalar color;
                        if (UseDotAnalysis && clusters != null)
                            color = clusters[i].ClusterOK ? new Scalar(0, 255, 0) : new Scalar(0, 0, 255);
                        else
                            color = GetDetectionColor(det.ClassId);

                        Cv2.Rectangle(overlay, rect, color, 2);

                        string baseLabel = det.ClassId < classNames.Length
                            ? $"{classNames[det.ClassId]} {det.Confidence:F2}"
                            : $"class{det.ClassId} {det.Confidence:F2}";
                        string label = baseLabel;
                        if (UseDotAnalysis && clusters != null)
                        {
                            var cl = clusters[i];
                            label = cl.AngleComputed
                                ? $"{baseLabel} [{cl.DotCount}, {cl.PatternAngle:F1}°]"
                                : $"{baseLabel} [{cl.DotCount}]";
                        }

                        Cv2.PutText(overlay, label,
                            new Point(rect.X, rect.Y - 6),
                            HersheyFonts.HersheySimplex, 0.5, color, 1);

                        // Dot 분석 시 각 dot을 원으로 표시
                        if (UseDotAnalysis && clusters != null)
                        {
                            foreach (var p in clusters[i].DotPositionsAbs)
                            {
                                Cv2.Circle(overlay, new Point((int)p.X, (int)p.Y), 4,
                                    new Scalar(0, 255, 255), 2);
                            }
                        }

                        result.Graphics.Add(new GraphicOverlay
                        {
                            Type = GraphicType.Rectangle,
                            Position = new Point2d(rect.X, rect.Y),
                            Width = rect.Width,
                            Height = rect.Height,
                            Color = color,
                            Text = label
                        });
                    }

                    result.OverlayImage = overlay;
                }

                // 개별 검출 결과를 Data에 저장
                for (int i = 0; i < detections.Count; i++)
                {
                    var det = detections[i];
                    result.Data[$"Det{i}_Class"] = det.ClassId;
                    result.Data[$"Det{i}_Confidence"] = det.Confidence;
                    result.Data[$"Det{i}_X"] = det.X;
                    result.Data[$"Det{i}_Y"] = det.Y;
                    result.Data[$"Det{i}_Width"] = det.Width;
                    result.Data[$"Det{i}_Height"] = det.Height;

                    if (UseDotAnalysis && clusters != null)
                    {
                        var cl = clusters[i];
                        result.Data[$"Det{i}_DotCount"] = cl.DotCount;
                        result.Data[$"Det{i}_DotCountOK"] = cl.CountOK;
                        result.Data[$"Det{i}_PatternAngle"] = cl.PatternAngle;
                        result.Data[$"Det{i}_AngleOK"] = cl.AngleOK;
                        result.Data[$"Det{i}_ClusterOK"] = cl.ClusterOK;
                    }
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"Detection 오류: {ex.Message}";
            }
            finally
            {
                claheBuffer?.Dispose();
            }

            return result;
        }

        /// <summary>
        /// SAHI (Slicing Aided Hyper Inference):
        /// 이미지를 tileSize × tileSize 타일로 분할(겹침 포함)하여 각각 추론한 뒤 좌표 복원 + 전역 NMS로 병합.
        /// 고해상도(예: 4K+) 영상에서 작은 결함이 InputSize 리사이즈로 사라지는 문제를 완화합니다.
        /// </summary>
        private static List<DetectionResult> RunSahiDetection(
            IDetectionEngine engine, Mat image, int tileSize, double overlapRatio,
            float confThreshold, float iouThreshold, float[]? perClassConf)
        {
            var all = new List<DetectionResult>();

            int step = Math.Max(1, (int)(tileSize * (1.0 - overlapRatio)));
            int w = image.Width;
            int h = image.Height;

            // 이미지가 타일보다 작으면 단일 추론으로 폴백
            if (w <= tileSize && h <= tileSize)
            {
                return engine.Detect(image, tileSize, confThreshold, iouThreshold, perClassConf);
            }

            for (int y = 0; y < h; y += step)
            {
                // 마지막 타일이 잘리지 않도록 경계로 스냅
                int y0 = Math.Min(y, Math.Max(0, h - tileSize));
                int tileH = Math.Min(tileSize, h - y0);

                for (int x = 0; x < w; x += step)
                {
                    int x0 = Math.Min(x, Math.Max(0, w - tileSize));
                    int tileW = Math.Min(tileSize, w - x0);

                    using var tile = new Mat(image, new Rect(x0, y0, tileW, tileH));
                    var tileDets = engine.Detect(tile, tileSize, confThreshold, iouThreshold, perClassConf);

                    // 타일 좌표 → 원본 좌표 복원
                    foreach (var d in tileDets)
                    {
                        d.X += x0;
                        d.Y += y0;
                        all.Add(d);
                    }

                    if (x0 + tileSize >= w) break;
                }
                if (y0 + tileSize >= h) break;
            }

            // 전역 NMS (타일 경계에 걸친 중복 검출 제거)
            return YoloOnnxEngine.GlobalNMS(all, iouThreshold);
        }

        /// <summary>
        /// ClassThresholds를 ClassId 인덱스 배열로 변환. UsePerClassThresholds가 false이거나 컬렉션이 비면 null 반환.
        /// </summary>
        private float[]? BuildPerClassThresholds()
        {
            if (!UsePerClassThresholds || ClassThresholds.Count == 0)
                return null;

            int maxId = ClassThresholds.Max(c => c.ClassId);
            var arr = new float[maxId + 1];
            for (int i = 0; i < arr.Length; i++) arr[i] = (float)ConfidenceThreshold;
            foreach (var e in ClassThresholds)
                arr[e.ClassId] = (float)e.Threshold;
            return arr;
        }

        // Execute 내부 전용: 한 바운딩 박스의 dot 분석 결과.
        // internal — 판정 fail-closed 회귀 테스트(DetectionToolDotJudgmentTests)에서 접근.
        internal class ClusterAnalysis
        {
            public int DotCount;
            public double PatternAngle;
            public bool AngleComputed;
            public bool CountOK;
            public bool AngleOK;
            public bool ClusterOK => CountOK && AngleOK;
            public List<Point2f> DotPositionsAbs = new();
        }

        internal ClusterAnalysis AnalyzeCluster(Mat fullImage, Rect absRect, int classId)
        {
            var outcome = new ClusterAnalysis();

            // 이미지 경계 클리핑
            var clipped = new Rect(
                Math.Max(0, absRect.X),
                Math.Max(0, absRect.Y),
                Math.Min(absRect.Width, fullImage.Width - Math.Max(0, absRect.X)),
                Math.Min(absRect.Height, fullImage.Height - Math.Max(0, absRect.Y)));
            if (clipped.Width <= 0 || clipped.Height <= 0)
            {
                outcome.CountOK = outcome.AngleOK = true; // 유효한 ROI 없음 → 판정 보류 (통과)
                return outcome;
            }

            using var roi = new Mat(fullImage, clipped);
            var dotResult = DotClusterAnalyzer.Analyze(
                roi,
                DotDetectionMethod,
                MinDotArea, MaxDotArea,
                DotCircularityThreshold,
                MinDotDistance,
                DotPatternMetric,
                DotPreprocessMode,
                DotClaheClipLimit,
                DotMorphKernelSize,
                DotThresholdMode,
                DotAdaptiveBlockSize,
                DotAdaptiveC);

            outcome.DotCount = dotResult.DotCount;
            outcome.PatternAngle = dotResult.PatternAngle;
            outcome.AngleComputed = dotResult.AngleComputed;

            // ROI 상대좌표 → 원본 절대좌표 변환
            foreach (var p in dotResult.DotPositions)
                outcome.DotPositionsAbs.Add(new Point2f(p.X + clipped.X, p.Y + clipped.Y));

            // 클래스별 기대값 조회 후 OK 판정
            var expectation = DotExpectations.FirstOrDefault(e => e.ClassId == classId);
            if (expectation == null)
            {
                // 기대값 미설정 → 검사 생략 (OK로 간주)
                outcome.CountOK = true;
                outcome.AngleOK = true;
                return outcome;
            }

            // 개수 검사
            if (expectation.ExpectedCount <= 0)
            {
                outcome.CountOK = true; // 0이면 검사 skip
            }
            else
            {
                int diff = Math.Abs(outcome.DotCount - expectation.ExpectedCount);
                outcome.CountOK = diff <= Math.Max(0, expectation.CountTolerance);
            }

            // 각도 검사
            if (!expectation.CheckAngle)
            {
                outcome.AngleOK = true;
            }
            else if (!outcome.AngleComputed)
            {
                // 각도 판정을 켰는데 dot 2개 미만으로 각도 계산 자체가 불가 —
                // 판정 불능을 통과로 삼키면 미검출 불량이 PASS 가 된다 (fail-closed).
                outcome.AngleOK = false;
            }
            else
            {
                // 각도는 -90~+90 범위. 180도 대칭성 고려해 최소 편차 계산.
                double diff = Math.Abs(outcome.PatternAngle - expectation.ReferenceAngle);
                if (diff > 90) diff = 180 - diff;
                outcome.AngleOK = diff <= Math.Max(0, expectation.AngleTolerance);
            }

            return outcome;
        }

        public override VisionToolBase Clone()
        {
            var clone = new DetectionTool
            {
                Name = this.Name,
                ModelPath = this.ModelPath,
                InputSize = this.InputSize,
                ConfidenceThreshold = this.ConfidenceThreshold,
                IouThreshold = this.IouThreshold,
                ClassNamesText = this.ClassNamesText,
                DrawOverlay = this.DrawOverlay,
                UseClahe = this.UseClahe,
                ClaheClipLimit = this.ClaheClipLimit,
                ClaheTileGridSize = this.ClaheTileGridSize,
                UsePerClassThresholds = this.UsePerClassThresholds,
                UseSahi = this.UseSahi,
                SahiTileSize = this.SahiTileSize,
                SahiOverlapRatio = this.SahiOverlapRatio,
                UseDotAnalysis = this.UseDotAnalysis,
                DotDetectionMethod = this.DotDetectionMethod,
                MinDotArea = this.MinDotArea,
                MaxDotArea = this.MaxDotArea,
                DotCircularityThreshold = this.DotCircularityThreshold,
                MinDotDistance = this.MinDotDistance,
                DotPatternMetric = this.DotPatternMetric,
                DotPreprocessMode = this.DotPreprocessMode,
                DotClaheClipLimit = this.DotClaheClipLimit,
                DotMorphKernelSize = this.DotMorphKernelSize,
                DotThresholdMode = this.DotThresholdMode,
                DotAdaptiveBlockSize = this.DotAdaptiveBlockSize,
                DotAdaptiveC = this.DotAdaptiveC,
                UseROI = this.UseROI,
                ROI = this.ROI
            };

            foreach (var entry in ClassThresholds)
            {
                clone.ClassThresholds.Add(new ClassThresholdEntry
                {
                    ClassId = entry.ClassId,
                    ClassName = entry.ClassName,
                    Threshold = entry.Threshold
                });
            }
            foreach (var entry in DotExpectations)
            {
                clone.DotExpectations.Add(new DotClusterExpectation
                {
                    ClassId = entry.ClassId,
                    ClassName = entry.ClassName,
                    ExpectedCount = entry.ExpectedCount,
                    CountTolerance = entry.CountTolerance,
                    ReferenceAngle = entry.ReferenceAngle,
                    AngleTolerance = entry.AngleTolerance,
                    CheckAngle = entry.CheckAngle
                });
            }
            return clone;
        }

        public override List<string> GetAvailableResultKeys()
        {
            return new List<string> { "Success", "DetectionCount" };
        }

        private static Scalar GetDetectionColor(int classId)
        {
            var colors = new Scalar[]
            {
                new(0, 255, 0), new(255, 0, 0), new(0, 0, 255),
                new(255, 255, 0), new(0, 255, 255), new(255, 0, 255),
                new(128, 255, 0), new(255, 128, 0), new(0, 128, 255)
            };
            return colors[classId % colors.Length];
        }
    }

    /// <summary>
    /// 검출 결과 1건
    /// </summary>
    public class DetectionResult
    {
        public int ClassId { get; set; }
        public float Confidence { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
    }

    /// <summary>
    /// YOLOv8/v11 ONNX 추론 엔진
    /// </summary>
    public class YoloOnnxEngine : OnnxModelBase, IDetectionEngine
    {
        public YoloOnnxEngine(string modelPath)
        {
            LoadModel(modelPath);
        }

        /// <summary>ONNX 메타데이터에서 클래스 이름 배열을 반환합니다.</summary>
        public string[] GetClassNames() => ReadClassNamesFromMetadata();

        public List<DetectionResult> Detect(
            Mat image, int inputSize,
            float confThreshold, float iouThreshold,
            float[]? perClassConfThresholds = null)
        {
            if (_session == null) return new List<DetectionResult>();

            // Letterbox 전처리: 비율 유지 + 패딩 (Ultralytics 학습 방식과 동일)
            float scale = Math.Min((float)inputSize / image.Width, (float)inputSize / image.Height);
            int newW = (int)(image.Width * scale);
            int newH = (int)(image.Height * scale);
            int padX = (inputSize - newW) / 2;
            int padY = (inputSize - newH) / 2;

            using var resized = new Mat();
            Cv2.Resize(image, resized, new Size(newW, newH));

            using var letterbox = new Mat(inputSize, inputSize, image.Type(), new Scalar(114, 114, 114));
            resized.CopyTo(letterbox[new Rect(padX, padY, newW, newH)]);

            var tensor = PreprocessImageSimple(letterbox, inputSize, inputSize);

            var inputs = CreateInput(GetInputName(), tensor);

            using var outputs = _session.Run(inputs);
            var output = (DenseTensor<float>)outputs.First().AsTensor<float>();

            // letterbox 좌표 → 원본 이미지 좌표 변환을 위한 스케일
            return ParseYoloOutput(output, scale, padX, padY, confThreshold, iouThreshold,
                image.Width, image.Height, perClassConfThresholds);
        }

        /// <summary>
        /// YOLOv8/v11 출력을 파싱한다.
        /// 성능 주의: Tensor의 3D 인덱서를 쓰면 8400×84 ≈ 700K 호출에서 bounds check/stride 계산이
        /// 누적되어 수백 ms가 소요된다. Buffer.Span에서 직접 읽어 오버헤드를 제거한다.
        /// </summary>
        private static List<DetectionResult> ParseYoloOutput(
            DenseTensor<float> output, float scale, int padX, int padY,
            float confThreshold, float iouThreshold,
            int imgWidth, int imgHeight,
            float[]? perClassConf)
        {
            var results = new List<DetectionResult>();
            var dims = output.Dimensions;

            // YOLOv8 output: [1, 84, 8400] (transposed) or [1, 8400, 84]
            int numDetections, numChannels;
            bool transposed;

            if (dims.Length == 3)
            {
                if (dims[1] < dims[2])
                {
                    numChannels = dims[1];
                    numDetections = dims[2];
                    transposed = true;
                }
                else
                {
                    numDetections = dims[1];
                    numChannels = dims[2];
                    transposed = false;
                }
            }
            else
            {
                return results;
            }

            int numClasses = numChannels - 4; // first 4 = cx, cy, w, h
            if (numClasses <= 0) return results;

            ReadOnlySpan<float> buf = output.Buffer.Span;

            // 텐서 레이아웃별 인덱싱 공식 (row-major).
            //   transposed [1,C,N]: (c, i) → c*N + i
            //   contiguous [1,N,C]: (i, c) → i*C + c
            var candidates = new List<(DetectionResult det, float score)>();

            // 진단용: 전체 예측 중 최고 confidence 추적. 검출 0개일 때 원인 파악에 사용.
            float diagMaxScore = 0f;
            int diagMaxClassId = 0;
            int diagPassedThreshold = 0;

            for (int i = 0; i < numDetections; i++)
            {
                float cx, cy, w, h;
                int classBase;
                int classStride;

                if (transposed)
                {
                    cx = buf[0 * numDetections + i];
                    cy = buf[1 * numDetections + i];
                    w  = buf[2 * numDetections + i];
                    h  = buf[3 * numDetections + i];
                    classBase = 4 * numDetections + i;
                    classStride = numDetections;
                }
                else
                {
                    int rowBase = i * numChannels;
                    cx = buf[rowBase];
                    cy = buf[rowBase + 1];
                    w  = buf[rowBase + 2];
                    h  = buf[rowBase + 3];
                    classBase = rowBase + 4;
                    classStride = 1;
                }

                // 최대 클래스 점수 찾기
                float maxScore = 0;
                int maxClassId = 0;
                int idx = classBase;
                for (int c = 0; c < numClasses; c++)
                {
                    float score = buf[idx];
                    if (score > maxScore)
                    {
                        maxScore = score;
                        maxClassId = c;
                    }
                    idx += classStride;
                }

                if (maxScore > diagMaxScore)
                {
                    diagMaxScore = maxScore;
                    diagMaxClassId = maxClassId;
                }

                // 클래스별 임계값 우선 적용, 없으면 전역값
                float effectiveThreshold = confThreshold;
                if (perClassConf != null && maxClassId < perClassConf.Length)
                    effectiveThreshold = perClassConf[maxClassId];

                if (maxScore < effectiveThreshold) continue;
                diagPassedThreshold++;

                // letterbox 좌표 → 원본 이미지 좌표
                int x1 = Math.Clamp((int)((cx - w / 2 - padX) / scale), 0, imgWidth);
                int y1 = Math.Clamp((int)((cy - h / 2 - padY) / scale), 0, imgHeight);
                int x2 = Math.Clamp((int)((cx + w / 2 - padX) / scale), 0, imgWidth);
                int y2 = Math.Clamp((int)((cy + h / 2 - padY) / scale), 0, imgHeight);

                candidates.Add((new DetectionResult
                {
                    ClassId = maxClassId,
                    Confidence = maxScore,
                    X = x1,
                    Y = y1,
                    Width = x2 - x1,
                    Height = y2 - y1
                }, maxScore));
            }

            var finalResults = ApplyNMS(candidates, iouThreshold);
            System.Diagnostics.Debug.WriteLine(
                $"[YOLO-Diag] MaxScore={diagMaxScore:F3} (class={diagMaxClassId}), " +
                $"PassedThresh={diagPassedThreshold}/{numDetections}, AfterNMS={finalResults.Count}");
            return finalResults;
        }

        /// <summary>
        /// 타일 추론 결과 병합용 전역 NMS. 클래스 ID별로 그룹핑하여 IoU 중복을 제거합니다.
        /// </summary>
        public static List<DetectionResult> GlobalNMS(List<DetectionResult> detections, float iouThreshold)
        {
            var candidates = detections.Select(d => (d, d.Confidence)).ToList();
            return ApplyNMS(candidates, iouThreshold);
        }

        private static List<DetectionResult> ApplyNMS(
            List<(DetectionResult det, float score)> candidates, float iouThreshold)
        {
            var sorted = candidates.OrderByDescending(c => c.score).ToList();
            var results = new List<DetectionResult>();
            var used = new bool[sorted.Count];

            for (int i = 0; i < sorted.Count; i++)
            {
                if (used[i]) continue;
                results.Add(sorted[i].det);

                for (int j = i + 1; j < sorted.Count; j++)
                {
                    if (used[j]) continue;
                    if (sorted[i].det.ClassId != sorted[j].det.ClassId) continue;
                    if (ComputeIoU(sorted[i].det, sorted[j].det) > iouThreshold)
                        used[j] = true;
                }
            }

            return results;
        }

        private static float ComputeIoU(DetectionResult a, DetectionResult b)
        {
            int x1 = Math.Max(a.X, b.X);
            int y1 = Math.Max(a.Y, b.Y);
            int x2 = Math.Min(a.X + a.Width, b.X + b.Width);
            int y2 = Math.Min(a.Y + a.Height, b.Y + b.Height);

            int interW = Math.Max(0, x2 - x1);
            int interH = Math.Max(0, y2 - y1);
            float inter = interW * interH;

            float areaA = a.Width * a.Height;
            float areaB = b.Width * b.Height;
            float union = areaA + areaB - inter;

            return union > 0 ? inter / union : 0;
        }
    }
}
