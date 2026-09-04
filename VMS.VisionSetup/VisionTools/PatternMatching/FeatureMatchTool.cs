using VMS.VisionSetup.Models;
using VMS.VisionSetup.Services;
using OpenCvSharp;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using System.Threading.Tasks;

namespace VMS.VisionSetup.VisionTools.PatternMatching
{
    /// <summary>기존 모델 재학습 시 원점(학습 중심·기준 각도·기준 이미지) 처리 방식 (2026-09-04).</summary>
    public enum RetrainOriginMode
    {
        /// <summary>현재 이미지 기준 — 원점을 현재 ROI 중심·각도로 재계산하고 기준 이미지도 교체 (기본, 종전 동작).</summary>
        UseCurrentImage,
        /// <summary>기준 이미지 유지 — 원 학습의 원점·기준 각도·기준 이미지를 그대로 두고 템플릿 특징만 갱신.</summary>
        KeepReference
    }

    /// <summary>
    /// Edge-based geometric pattern matching: Generalized Hough Voting + gradient refinement.
    /// Supports multiple trained pattern models — best match is selected at runtime.
    /// Optimized: ArrayPool (zero GC in hot path), binned accumulator, direct Mat pointers,
    /// AVX2 SIMD scoring with Reciprocal approximation.
    /// </summary>
    public unsafe class FeatureMatchTool : VisionToolBase
    {
        #region Structs

        internal struct EdgePoint
        {
            public float X, Y;   // relative to pattern center
            public float Dx, Dy; // normalized gradient direction
            public float Magnitude; // gradient magnitude (for weighted selection)
            public float CurvatureScore; // Harris corner response (0..1)
        }

        #endregion

        #region Native Interop

        private static class NativeVision
        {
            private const string DllName = "NativeVision.dll";

            [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
            public static extern void ComputeGradientNative(
                byte* gray,
                int width, int height, int stride,
                float* outDx, float* outDy, float* outMag);

            [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
            public static extern double EvaluateBatchNative(
                int baseCx, int baseCy, int refRadius,
                int* rx, int* ry, float* rdx, float* rdy,
                float* dxImg, float* dyImg, float* magImg,
                int imgW, int imgH, int N, int margin,
                float thresh, float greedy,
                int* outDx, int* outDy,
                int contrastInvariant);

            [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
            public static extern double EvaluateAllPosesNative(
                int baseCx, int baseCy, int refRadius,
                int* allRx, int* allRy,
                float* allRdx, float* allRdy,
                int* margins,
                int poseCount, int N,
                float* dxImg, float* dyImg, float* magImg,
                int imgW, int imgH,
                float thresh, float greedy,
                int* outBestDx, int* outBestDy, int* outBestPoseIdx,
                int contrastInvariant);

            [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
            public static extern void HoughVotingNative(
                float* modelX, float* modelY, int modelCount,
                int* binOffsets, int* binIndices, int numGradBins,
                int* searchX, int* searchY, int* searchBin, int searchEdgeCount,
                int voteWidth, int voteHeight,
                double angleStart, double angleExtent,
                double coarseAngleStep, double fineAngleStep, int topK,
                double invScale, int binShiftBits,
                double* outBestCx, double* outBestCy, double* outBestAngle, int* outBestVotes);

            [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
            public static extern int HoughVotingTopKNative(
                float* modelX, float* modelY, int modelCount,
                int* binOffsets, int* binIndices, int numGradBins,
                int* searchX, int* searchY, int* searchBin, int searchEdgeCount,
                int voteWidth, int voteHeight,
                double angleStart, double angleExtent,
                double coarseAngleStep, double fineAngleStep, int topK,
                double invScale, int binShiftBits,
                double minSeparation, int outK,
                double* outCx, double* outCy, double* outAngle, int* outVotes);

            [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
            public static extern int HoughVotingTopKMultiNative(
                float* modelX, float* modelY, int modelCount,
                int* binOffsets, int* binIndices, int numGradBins,
                int* searchX, int* searchY, int* searchBin, int searchEdgeCount,
                int voteWidth, int voteHeight,
                double angleStart, double angleExtent,
                double coarseAngleStep, double fineAngleStep, int topK,
                double invScale, int binShiftBits,
                int peaksPerAngle,
                double minSeparation, int outK,
                double* outCx, double* outCy, double* outAngle, int* outVotes);

            private static readonly bool _isAvailable = ProbeNative();
            private static readonly bool _hasTopKExport = ProbeExport("HoughVotingTopKNative");
            private static readonly bool _hasTopKMultiExport = ProbeExport("HoughVotingTopKMultiNative");

            public static bool IsAvailable => _isAvailable;

            /// <summary>구버전 DLL(단일 피크 export만)과의 호환 — 없으면 단일 경로 폴백.</summary>
            public static bool HasTopKExport => _hasTopKExport;

            /// <summary>각도당 다중 피크 export — 없으면(구 DLL) 다중 인스턴스는 managed 투표로 폴백.</summary>
            public static bool HasTopKMultiExport => _hasTopKMultiExport;

            private static bool ProbeNative()
            {
                try
                {
                    return NativeLibrary.TryLoad(DllName, typeof(NativeVision).Assembly, null, out _);
                }
                catch
                {
                    return false;
                }
            }

            private static bool ProbeExport(string name)
            {
                try
                {
                    return NativeLibrary.TryLoad(DllName, typeof(NativeVision).Assembly, null, out var handle)
                        && NativeLibrary.TryGetExport(handle, name, out _);
                }
                catch
                {
                    return false;
                }
            }
        }

        #endregion

        #region Multi-Model Data

        private const int NUM_GRAD_BINS = 36;
        private const double BIN_WIDTH_DEG = 360.0 / NUM_GRAD_BINS;

        /// <summary>
        /// Collection of trained pattern models.
        /// </summary>
        public ObservableCollection<FeatureMatchModel> Models { get; } = new();

        private FeatureMatchModel? _selectedModel;
        /// <summary>
        /// Currently selected model in UI (for preview, editing).
        /// </summary>
        public FeatureMatchModel? SelectedModel
        {
            get => _selectedModel;
            set
            {
                if (SetProperty(ref _selectedModel, value))
                {
                    OnPropertyChanged(nameof(SelectedModelTemplateImage));
                    OnPropertyChanged(nameof(SelectedModelFeatureImage));
                }
            }
        }

        public Mat? SelectedModelTemplateImage => SelectedModel?.TemplateImage;
        public Mat? SelectedModelFeatureImage => SelectedModel?.TrainedFeatureImage;

        private FeatureMatchModel? _lastMatchedModel;
        /// <summary>
        /// The model that matched best in the last Execute call.
        /// </summary>
        public FeatureMatchModel? LastMatchedModel
        {
            get => _lastMatchedModel;
            set => SetProperty(ref _lastMatchedModel, value);
        }

        // Legacy compatibility — returns first model's images for UI preview listeners
        public Mat? TemplateImage => SelectedModel?.TemplateImage;
        public Mat? TrainedFeatureImage => SelectedModel?.TrainedFeatureImage;

        #endregion

        #region Parameters

        private double _cannyLow = 50;
        public double CannyLow { get => _cannyLow; set => SetProperty(ref _cannyLow, value); }

        private double _cannyHigh = 150;
        public double CannyHigh { get => _cannyHigh; set => SetProperty(ref _cannyHigh, value); }

        private double _angleStart = -45;
        public double AngleStart { get => _angleStart; set => SetProperty(ref _angleStart, value); }

        private double _angleExtent = 90;
        public double AngleExtent { get => _angleExtent; set => SetProperty(ref _angleExtent, value); }

        private double _angleStep = 1;
        public double AngleStep { get => _angleStep; set => SetProperty(ref _angleStep, value); }

        private double _minScale = 0.9;
        public double MinScale { get => _minScale; set => SetProperty(ref _minScale, value); }

        private double _maxScale = 1.1;
        public double MaxScale { get => _maxScale; set => SetProperty(ref _maxScale, value); }

        private double _scaleStep = 0.05;
        public double ScaleStep { get => _scaleStep; set => SetProperty(ref _scaleStep, value); }

        private double _scoreThreshold = 0.5;
        public double ScoreThreshold { get => _scoreThreshold; set => SetProperty(ref _scoreThreshold, value); }

        // ── 다중 인스턴스 검출 (ShapeMatchTool과 동일 파라미터/결과 키 체계) ──
        private int _maxInstances = 1;
        /// <summary>찾을 최대 인스턴스 수. 1이면 단일 매칭(기존 동작), 2+면 투표 다중 피크 + NMS로 상위 N개.</summary>
        public int MaxInstances
        {
            get => _maxInstances;
            set => SetProperty(ref _maxInstances, Math.Clamp(value, 1, 50));
        }

        private double _nmsDistanceFactor = 0.5;
        /// <summary>인스턴스 분리 거리 = 템플릿 짧은 변 × 이 값. 이보다 가까운 매칭은
        /// 같은 인스턴스로 보고 낮은 점수를 억제한다 (투표 후보 NMS·최종 NMS 공용).</summary>
        public double NmsDistanceFactor
        {
            get => _nmsDistanceFactor;
            set => SetProperty(ref _nmsDistanceFactor, Math.Clamp(value, 0.1, 3.0));
        }

        // ── 커버리지 판정 — 부분(반쪽) 매칭 기각 ──
        // 스코어(그래디언트 내적 평균)는 모델 절반만 정합해도 ~0.5가 나와, 임계가
        // 낮거나 특징점이 적으면 중심이 어긋난 반쪽 매칭이 통과한다(실증 PC 보고).
        // 커버리지 = "점별로 실제 일치한 모델 점의 비율"이라 반쪽 매칭은 ~0.5에 묶인다.
        private double _minCoverage;
        /// <summary>최소 커버리지(0~1). 0이면 비활성. 매칭 커버리지가 이 값 미만이면 기각. 권장 0.6~0.75.</summary>
        public double MinCoverage
        {
            get => _minCoverage;
            set => SetProperty(ref _minCoverage, Math.Clamp(value, 0, 1));
        }

        // ── 각도/스케일 판정 (Web ParamCode 연동 가능) ──
        // 검색 범위(AngleStart/Extent, Min/MaxScale)와 별개로, 찾은 매칭의 자세가
        // 허용 범위를 벗어나면 NG 처리하는 판정 기준. 기본 비활성 (기존 동작 유지).
        private bool _useAngleJudgment;
        public bool UseAngleJudgment { get => _useAngleJudgment; set => SetProperty(ref _useAngleJudgment, value); }

        private double _angleLowerLimit = -180;
        public double AngleLowerLimit { get => _angleLowerLimit; set => SetProperty(ref _angleLowerLimit, value); }

        private double _angleUpperLimit = 180;
        public double AngleUpperLimit { get => _angleUpperLimit; set => SetProperty(ref _angleUpperLimit, value); }

        private bool _useScaleJudgment;
        public bool UseScaleJudgment { get => _useScaleJudgment; set => SetProperty(ref _useScaleJudgment, value); }

        private double _scaleLowerLimit = 0.9;
        public double ScaleLowerLimit { get => _scaleLowerLimit; set => SetProperty(ref _scaleLowerLimit, value); }

        private double _scaleUpperLimit = 1.1;
        public double ScaleUpperLimit { get => _scaleUpperLimit; set => SetProperty(ref _scaleUpperLimit, value); }

        private int _numLevels = 3;
        public int NumLevels { get => _numLevels; set => SetProperty(ref _numLevels, value); }

        private double _greediness = 0.8;
        public double Greediness { get => _greediness; set => SetProperty(ref _greediness, value); }

        private int _maxModelPoints = 200;
        public int MaxModelPoints { get => _maxModelPoints; set => SetProperty(ref _maxModelPoints, value); }

        private bool _useContrastInvariant;
        public bool UseContrastInvariant { get => _useContrastInvariant; set => SetProperty(ref _useContrastInvariant, value); }

        private double _curvatureWeight = 0.4;
        public double CurvatureWeight
        {
            get => _curvatureWeight;
            set => SetProperty(ref _curvatureWeight, Math.Clamp(value, 0, 1));
        }

        private bool _isAutoTuneEnabled = true;
        public bool IsAutoTuneEnabled { get => _isAutoTuneEnabled; set => SetProperty(ref _isAutoTuneEnabled, value); }

        /// <summary>
        /// 학습(Train) 당시 전체 원본 이미지가 저장된 PNG 경로.
        /// 템플릿은 ROI 크롭만 레시피에 들어가므로, 재학습·ROI 조정 시 원래 장면을
        /// 다시 불러올 수 있도록 Train 성공 시 MainViewModel 이 자동 기록한다.
        /// </summary>
        private string? _referenceImagePath;
        public string? ReferenceImagePath { get => _referenceImagePath; set => SetProperty(ref _referenceImagePath, value); }

        private RetrainOriginMode _retrainOriginMode = RetrainOriginMode.UseCurrentImage;
        /// <summary>
        /// 기존 모델 재학습([Train Selected]) 시 원점(학습 중심·기준 각도·기준 이미지) 처리 (2026-09-04).
        /// UseCurrentImage(기본): 현재 ROI 기준으로 재계산 + 기준 이미지 교체. KeepReference: 원 학습의
        /// 원점·각도·기준 이미지를 유지하고 템플릿 특징만 갱신 — 얼라인 마스터 포즈 보존용.
        /// </summary>
        public RetrainOriginMode RetrainOriginMode
        {
            get => _retrainOriginMode;
            set => SetProperty(ref _retrainOriginMode, value);
        }

        private double _suggestedCannyLow;
        public double SuggestedCannyLow { get => _suggestedCannyLow; set => SetProperty(ref _suggestedCannyLow, value); }

        private double _suggestedCannyHigh;
        public double SuggestedCannyHigh { get => _suggestedCannyHigh; set => SetProperty(ref _suggestedCannyHigh, value); }

        private int _suggestedNumLevels;
        public int SuggestedNumLevels { get => _suggestedNumLevels; set => SetProperty(ref _suggestedNumLevels, value); }

        private int _suggestedMaxModelPoints;
        public int SuggestedMaxModelPoints { get => _suggestedMaxModelPoints; set => SetProperty(ref _suggestedMaxModelPoints, value); }

        private bool _hasSuggestions;
        public bool HasSuggestions { get => _hasSuggestions; set => SetProperty(ref _hasSuggestions, value); }

        private string? _lastTrainWarning;
        /// <summary>직전 학습의 경고(특징점 부족 등). 없으면 null — UI 상태 표시용.</summary>
        public string? LastTrainWarning { get => _lastTrainWarning; set => SetProperty(ref _lastTrainWarning, value); }

        #endregion

        #region Search Region Properties

        private Rect _searchRegion;
        public Rect SearchRegion
        {
            get => _searchRegion;
            set
            {
                if (SetProperty(ref _searchRegion, value))
                {
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

        #endregion

        public FeatureMatchTool()
        {
            Name = "Feature Match";
            ToolType = "FeatureMatchTool";
        }

        #region Train

        /// <summary>학습에 적용되는 ROI 각도 — GetAlignedROIImage 와 같은 판단(라이브 도형 우선, 미세 각도 무시).</summary>
        private double CurrentTrainAngle()
        {
            double angle = AssociatedROIShape is RectangleAffineROI live ? live.Angle : ROIAngle;
            return Math.Abs(angle) <= 0.001 ? 0 : angle;
        }

        /// <summary>
        /// 구 레시피 복원 보정 (2026-09-04): 학습 원점이 저장되지 않은 모델은 역직렬화 재학습 시점에 ROI 가
        /// 아직 없어 원점이 템플릿 중심으로 남는다 — 기본 속성(ROI) 적용 후 이 메서드로 종전 규약
        /// (ROI 좌상단 + 템플릿 반폭, 각도 = ROI 각도)대로 재계산한다. 명시 저장된 모델은 건드리지 않는다.
        /// </summary>
        internal void FixLegacyTrainedOrigins()
        {
            foreach (var model in Models)
            {
                if (!model.NeedsLegacyOriginFix) continue;
                model.NeedsLegacyOriginFix = false;
                if (UseROI && ROI.Width > 0 && ROI.Height > 0)
                {
                    model.TrainedCenterX = ROI.X + model.TemplateWidth / 2.0;
                    model.TrainedCenterY = ROI.Y + model.TemplateHeight / 2.0;
                    model.TrainedAngle = CurrentTrainAngle();
                }
                else
                {
                    model.TrainedCenterX = model.TemplateWidth / 2.0;
                    model.TrainedCenterY = model.TemplateHeight / 2.0;
                    model.TrainedAngle = 0;
                }
            }
        }

        /// <summary>
        /// Train a pattern model. If targetModel is provided, retrain that model.
        /// If null, create a new model and add to Models collection.
        /// trainMask: 학습 마스크(8UC1, 255=제외) — 지정 시 모델에 저장, 미지정 시 모델의
        /// 기존 마스크 유지 (재학습·역직렬화 경로 공용). 템플릿과 크기가 다르면 무시.
        /// preserveTrainedCenter: true 면 기존 모델의 학습 중심(TrainedCenterX/Y)을 유지 —
        /// 마스크 적용/해제처럼 "저장된 옛 템플릿"으로 재학습하는 경로용. 기본(재계산)은
        /// 현재 ROI 위치를 쓰므로, 원 학습 후 ROI 를 옮긴 상태에서 옛 템플릿을 재학습하면
        /// 중심이 ROI 이동량만큼 어긋난다 (Match Align 기준·TrainedCenter 출력 오염).
        /// </summary>
        public bool TrainPattern(Mat patternImage, FeatureMatchModel? targetModel = null, Mat? trainMask = null,
            bool preserveTrainedCenter = false)
        {
            try
            {
                var model = targetModel;
                bool isNew = model == null;
                double prevCenterX = model?.TrainedCenterX ?? 0;
                double prevCenterY = model?.TrainedCenterY ?? 0;
                double prevAngle = model?.TrainedAngle ?? 0;

                if (isNew)
                {
                    model = new FeatureMatchModel
                    {
                        Name = $"Model {Models.Count + 1}"
                    };
                }
                else
                {
                    // Clear old data for retrain
                    model!.FreeNativePoseBuffers();
                    model.TemplateImage?.Dispose();
                    model.TemplateImage = null;
                    model.TrainedFeatureImage?.Dispose();
                    model.TrainedFeatureImage = null;
                    model.ModelEdges.Clear();
                }

                if (trainMask != null)
                    model!.TrainMask = trainMask.Clone();

                model!.TemplateImage = patternImage.Clone();
                model.TemplateWidth = patternImage.Width;
                model.TemplateHeight = patternImage.Height;

                if (preserveTrainedCenter && !isNew)
                {
                    model.TrainedCenterX = prevCenterX;
                    model.TrainedCenterY = prevCenterY;
                    model.TrainedAngle = prevAngle;
                }
                else if (UseROI && ROI.Width > 0 && ROI.Height > 0)
                {
                    model.TrainedCenterX = ROI.X + patternImage.Width / 2.0;
                    model.TrainedCenterY = ROI.Y + patternImage.Height / 2.0;
                    // 회전 ROI 는 GetAlignedROIImage 가 정렬 워프해 넘기므로 매칭 각도 = ROI 각도.
                    // 기준 각도를 0 으로 두면 같은 장면에서도 Δθ = ROI 각도가 되어 얼라인이 어긋난다.
                    model.TrainedAngle = CurrentTrainAngle();
                }
                else
                {
                    model.TrainedCenterX = patternImage.Width / 2.0;
                    model.TrainedCenterY = patternImage.Height / 2.0;
                    model.TrainedAngle = 0;
                }

                using var gray = patternImage.Channels() > 1
                    ? patternImage.CvtColor(ColorConversionCodes.BGR2GRAY)
                    : patternImage.Clone();

                using var sobelX = new Mat();
                using var sobelY = new Mat();
                Cv2.Sobel(gray, sobelX, MatType.CV_32F, 1, 0, 3);
                Cv2.Sobel(gray, sobelY, MatType.CV_32F, 0, 1, 3);
                using var edges = gray.Canny(CannyLow, CannyHigh);

                // Compute Harris corner response for curvature scoring
                using var harris = new Mat();
                Cv2.CornerHarris(gray, harris, 3, 3, 0.04);
                double harrisMin, harrisMax;
                Cv2.MinMaxLoc(harris, out harrisMin, out harrisMax);
                double harrisRange = harrisMax - harrisMin;
                if (harrisRange < 1e-12) harrisRange = 1.0;

                var allEdges = new List<EdgePoint>();
                float cx = model.TemplateWidth / 2.0f;
                float cy = model.TemplateHeight / 2.0f;

                // 학습 마스크 — don't-care 영역(그림자·가변 각인·반사 등)의 에지는 모델에서 제외.
                // 크기가 템플릿과 다르면(ROI 변경 후 재학습 등) 적용하지 않는다.
                var mask = model.TrainMask;
                bool useMask = mask != null && !mask.Empty()
                    && mask.Width == gray.Width && mask.Height == gray.Height;

                for (int y = 1; y < gray.Height - 1; y++)
                    for (int x = 1; x < gray.Width - 1; x++)
                    {
                        if (edges.At<byte>(y, x) == 0) continue;
                        if (useMask && mask!.At<byte>(y, x) > 0) continue;
                        float gx = sobelX.At<float>(y, x);
                        float gy = sobelY.At<float>(y, x);
                        float mag = MathF.Sqrt(gx * gx + gy * gy);
                        if (mag < 1e-6f) continue;
                        float cv = (float)((harris.At<float>(y, x) - harrisMin) / harrisRange);
                        allEdges.Add(new EdgePoint
                        {
                            X = x - cx, Y = y - cy,
                            Dx = gx / mag, Dy = gy / mag,
                            Magnitude = mag,
                            CurvatureScore = Math.Clamp(cv, 0f, 1f)
                        });
                    }

                if (allEdges.Count > MaxModelPoints)
                {
                    model.ModelEdges = SpatialSample(allEdges, MaxModelPoints, model.TemplateWidth, model.TemplateHeight, CurvatureWeight);
                }
                else
                    model.ModelEdges = allEdges;

                // Build gradient-direction bin table
                model.GradBinTable = new List<int>[NUM_GRAD_BINS];
                for (int b = 0; b < NUM_GRAD_BINS; b++) model.GradBinTable[b] = new List<int>();
                for (int i = 0; i < model.ModelEdges.Count; i++)
                {
                    double deg = Math.Atan2(model.ModelEdges[i].Dy, model.ModelEdges[i].Dx) * (180.0 / Math.PI);
                    if (deg < 0) deg += 360.0;
                    int bin = (int)(deg / BIN_WIDTH_DEG) % NUM_GRAD_BINS;
                    model.GradBinTable[bin].Add(i);
                }

                // Flatten bin table
                model.BinOffsets = new int[NUM_GRAD_BINS + 1];
                int total = 0;
                for (int b = 0; b < NUM_GRAD_BINS; b++)
                {
                    model.BinOffsets[b] = total;
                    total += model.GradBinTable[b].Count;
                }
                model.BinOffsets[NUM_GRAD_BINS] = total;
                model.BinIndices = new int[total];
                for (int b = 0; b < NUM_GRAD_BINS; b++)
                {
                    var list = model.GradBinTable[b];
                    for (int i = 0; i < list.Count; i++)
                        model.BinIndices[model.BinOffsets[b] + i] = list[i];
                }

                // Cache model X/Y as float arrays for native Hough voting
                model.ModelXArray = new float[model.ModelEdges.Count];
                model.ModelYArray = new float[model.ModelEdges.Count];
                for (int i = 0; i < model.ModelEdges.Count; i++)
                {
                    model.ModelXArray[i] = model.ModelEdges[i].X;
                    model.ModelYArray[i] = model.ModelEdges[i].Y;
                }

                // Generate training feature visualization
                BuildTrainedFeatureImage(model, patternImage);

                // Pre-allocate native pose buffers based on worst-case pose count
                {
                    // 정련 각도 창 = 코스 스텝 + 그래디언트 빈 폭 (MatchSingleModel 과 동일식)
                    double coarseA = Math.Max(AngleStep, 4.0) + BIN_WIDTH_DEG;
                    double fineA = Math.Max(0.1, AngleStep / 2.0);
                    double sRange = (MaxScale - MinScale) / 2.0;
                    double sStep = Math.Max(0.001, ScaleStep);
                    int maxAnglePoses = (int)(2.0 * coarseA / fineA) + 2;
                    int maxScalePoses = (int)(2.0 * sRange / sStep) + 2;
                    model.EnsurePoseBufferCapacity(maxAnglePoses * maxScalePoses, model.ModelEdges.Count);
                }

                if (isNew)
                    Models.Add(model);

                SelectedModel = model;

                // Notify legacy property listeners
                OnPropertyChanged(nameof(TemplateImage));
                OnPropertyChanged(nameof(TrainedFeatureImage));
                OnPropertyChanged(nameof(SelectedModelTemplateImage));
                OnPropertyChanged(nameof(SelectedModelFeatureImage));

                // 특징점이 적으면 스코어 분산이 커져 부분(반쪽) 매칭·중심 이탈이 빈발한다
                // (실증 PC 보고, 2026-08-31) — 학습 시점에 바로 경고해 원인 파악을 돕는다.
                LastTrainWarning = model.ModelEdges.Count is >= 10 and < 60
                    ? $"학습 특징점이 {model.ModelEdges.Count}개로 적습니다 — 부분 매칭으로 중심이 어긋날 수 있습니다. " +
                      "Canny 임계를 낮추거나 학습 영역을 넓히고, Min Coverage 판정 사용을 권장합니다."
                    : null;

                return model.ModelEdges.Count >= 10;
            }
            catch { return false; }
        }

        /// <summary>
        /// Remove a model from the collection and dispose its resources.
        /// </summary>
        public void RemoveModel(FeatureMatchModel model)
        {
            model.Dispose();
            Models.Remove(model);
            if (SelectedModel == model)
                SelectedModel = Models.FirstOrDefault();
            if (LastMatchedModel == model)
                LastMatchedModel = null;
        }

        private static List<EdgePoint> SpatialSample(List<EdgePoint> allEdges, int maxPoints, int imgW, int imgH, double curvatureWeight = 0.0)
        {
            float cx = imgW / 2.0f;
            float cy = imgH / 2.0f;
            double aspect = (double)imgW / imgH;
            int gridRows = Math.Max(1, (int)Math.Sqrt(maxPoints / aspect));
            int gridCols = Math.Max(1, (int)(gridRows * aspect));
            float cellW = imgW / (float)gridCols;
            float cellH = imgH / (float)gridCols;
            if (cellH * gridRows < imgH) gridRows = (int)Math.Ceiling(imgH / cellH);

            var buckets = new Dictionary<int, List<EdgePoint>>();
            foreach (var e in allEdges)
            {
                int col = Math.Clamp((int)((e.X + cx) / cellW), 0, gridCols - 1);
                int row = Math.Clamp((int)((e.Y + cy) / cellH), 0, gridRows - 1);
                int key = row * gridCols + col;
                if (!buckets.TryGetValue(key, out var list))
                {
                    list = new List<EdgePoint>();
                    buckets[key] = list;
                }
                list.Add(e);
            }

            float maxMag = 0;
            foreach (var e in allEdges)
                if (e.Magnitude > maxMag) maxMag = e.Magnitude;
            if (maxMag < 1e-6f) maxMag = 1f;

            float cw = (float)Math.Clamp(curvatureWeight, 0, 1);

            foreach (var list in buckets.Values)
                list.Sort((a, b) =>
                {
                    float scoreA = (1f - cw) * (a.Magnitude / maxMag) + cw * a.CurvatureScore;
                    float scoreB = (1f - cw) * (b.Magnitude / maxMag) + cw * b.CurvatureScore;
                    return scoreB.CompareTo(scoreA);
                });

            var result = new List<EdgePoint>(maxPoints);
            int round = 0;
            while (result.Count < maxPoints)
            {
                bool added = false;
                foreach (var list in buckets.Values)
                {
                    if (round < list.Count)
                    {
                        result.Add(list[round]);
                        added = true;
                        if (result.Count >= maxPoints) break;
                    }
                }
                if (!added) break;
                round++;
            }

            return result;
        }

        public void AutoTuneParameters(Mat patternImage)
        {
            using var gray = patternImage.Channels() > 1
                ? patternImage.CvtColor(ColorConversionCodes.BGR2GRAY)
                : patternImage.Clone();

            Cv2.MeanStdDev(gray, out var mean, out var stddev);
            double mu = mean[0];
            double sigma = stddev[0];

            double cannyLow = Math.Clamp(mu - sigma, 10, 200);
            double cannyHigh = Math.Clamp(mu + sigma, cannyLow + 20, 400);

            int maxDim = Math.Max(gray.Width, gray.Height);
            int numLevels = maxDim <= 100 ? 2 : maxDim <= 500 ? 3 : 4;

            using var autoEdges = gray.Canny(cannyLow, cannyHigh);
            int edgePixels = Cv2.CountNonZero(autoEdges);

            // 특징점 상한은 엣지 "밀도"가 아니라 실제 엣지 픽셀 수 기준 — 밀도 기준은
            // 대형 템플릿 + 가는 윤곽(면적 대비 윤곽 비율이 낮음)에서 항상 최저 등급이
            // 나와, 수천 픽셀 윤곽을 100점으로 솎아내 인식이 불안정해진다 (실증 T-피팅
            // 483×355: 엣지 3,311px·밀도 0.019 → 100점 배정). 윤곽 8px당 1점이면
            // 투표·스코어링에 충분히 조밀하고, 상한 500은 AVX2 스코어링에 부담 없다.
            int maxModelPoints = Math.Clamp(edgePixels / 8, 100, 500);

            if (IsAutoTuneEnabled)
            {
                CannyLow = cannyLow;
                CannyHigh = cannyHigh;
                NumLevels = numLevels;
                MaxModelPoints = maxModelPoints;
                HasSuggestions = false;
            }
            else
            {
                SuggestedCannyLow = cannyLow;
                SuggestedCannyHigh = cannyHigh;
                SuggestedNumLevels = numLevels;
                SuggestedMaxModelPoints = maxModelPoints;
                HasSuggestions = true;
            }
        }

        public void ApplySuggestedParameters()
        {
            CannyLow = SuggestedCannyLow;
            CannyHigh = SuggestedCannyHigh;
            NumLevels = SuggestedNumLevels;
            MaxModelPoints = SuggestedMaxModelPoints;
            HasSuggestions = false;
        }

        private static void BuildTrainedFeatureImage(FeatureMatchModel model, Mat patternImage)
        {
            model.TrainedFeatureImage?.Dispose();

            var vis = patternImage.Channels() == 1
                ? patternImage.CvtColor(ColorConversionCodes.GRAY2BGR)
                : patternImage.Clone();

            // 마스크 영역을 반투명 빨강으로 표기 — 학습 직후 제외 여부를 눈으로 검증
            var mask = model.TrainMask;
            if (mask != null && !mask.Empty()
                && mask.Width == vis.Width && mask.Height == vis.Height)
            {
                using var redLayer = vis.Clone();
                redLayer.SetTo(new Scalar(0, 0, 255), mask);
                Cv2.AddWeighted(vis, 0.55, redLayer, 0.45, 0, vis);
            }

            float cx = model.TemplateWidth / 2.0f;
            float cy = model.TemplateHeight / 2.0f;

            float maxMag = 0;
            foreach (var edge in model.ModelEdges)
                if (edge.Magnitude > maxMag) maxMag = edge.Magnitude;
            if (maxMag < 1e-6f) maxMag = 1f;

            foreach (var edge in model.ModelEdges)
            {
                int px = (int)(edge.X + cx);
                int py = (int)(edge.Y + cy);

                float norm = edge.Magnitude / maxMag;
                int radius = Math.Max(1, (int)(1 + norm * 3));

                Cv2.Circle(vis, new Point(px, py), radius, new Scalar(0, 255, 0), -1);

                int ex = (int)(px + edge.Dx * 6);
                int ey = (int)(py + edge.Dy * 6);
                Cv2.Line(vis, new Point(px, py), new Point(ex, ey), new Scalar(255, 200, 0), 1);
            }

            model.TrainedFeatureImage = vis;
        }

        #endregion

        #region Stability Refinement

        /// <summary>안정 특징 정제 결과 요약 (UI 상태 표시용).</summary>
        public readonly record struct StableRefineResult(
            bool Success, string Message, int ImagesMatched, int ImagesTotal, int EdgesTotal, int EdgesMasked);

        /// <summary>
        /// 다중 샘플 이미지로 학습 특징의 안정성을 검증해, 이미지마다 흔들리는 특징
        /// (그림자 외곽·정반사·가변 각인 등)을 TrainMask 에 자동 반영하고 재학습한다 —
        /// 수작업 마스킹 없이 "매칭된 샘플 중 minStableRatio 이상에서 일관된" 엣지만 남긴다.
        /// 각 샘플에서 현재 모델로 자세를 찾고, 템플릿의 학습 후보 엣지 픽셀 전부
        /// (MaxModelPoints 샘플링 이전 전체 집합)를 그 자세로 투영해 그래디언트 방향
        /// 일치(minPointScore)를 판정한다. 불안정 픽셀은 기존 TrainMask 와의 합집합으로
        /// 칠해 재학습하므로 직렬화(레시피 저장→재학습)·Clone·마스크 편집기 경로와
        /// 그대로 호환된다. 객체 자신의 실루엣(예: 그림자와 맞닿은 윤곽)은 자세와 함께
        /// 움직여 매 샘플 일치하므로 남고, 그림자 외곽·반사는 흔들려서 탈락한다.
        /// </summary>
        public StableRefineResult RefineStableFeatures(FeatureMatchModel model, IReadOnlyList<Mat> sampleImages,
            double minPointScore = 0.5, double minStableRatio = 0.8)
        {
            if (model.TemplateImage == null || model.TemplateImage.Empty() || !model.IsTrained
                || model.BinOffsets == null || model.BinIndices == null)
                return new StableRefineResult(false, "학습된 모델이 아닙니다 — 먼저 [Train]으로 학습하세요.", 0, sampleImages.Count, 0, 0);
            if (sampleImages.Count == 0)
                return new StableRefineResult(false, "샘플 이미지가 없습니다.", 0, 0, 0, 0);

            try
            {
                // 1) 템플릿의 학습 후보 엣지 전체 — 기존 마스크로 제외된 픽셀은 대상 밖
                using var tGray = model.TemplateImage.Channels() > 1
                    ? model.TemplateImage.CvtColor(ColorConversionCodes.BGR2GRAY)
                    : model.TemplateImage.Clone();
                using var tSobelX = new Mat();
                using var tSobelY = new Mat();
                Cv2.Sobel(tGray, tSobelX, MatType.CV_32F, 1, 0, 3);
                Cv2.Sobel(tGray, tSobelY, MatType.CV_32F, 0, 1, 3);
                using var tEdges = tGray.Canny(CannyLow, CannyHigh);

                var mask = model.TrainMask;
                bool useMask = mask != null && !mask.Empty()
                    && mask.Width == tGray.Width && mask.Height == tGray.Height;

                var candX = new List<int>();
                var candY = new List<int>();
                var candDx = new List<float>();
                var candDy = new List<float>();
                for (int y = 1; y < tGray.Height - 1; y++)
                    for (int x = 1; x < tGray.Width - 1; x++)
                    {
                        if (tEdges.At<byte>(y, x) == 0) continue;
                        if (useMask && mask!.At<byte>(y, x) > 0) continue;
                        float gx = tSobelX.At<float>(y, x);
                        float gy = tSobelY.At<float>(y, x);
                        float mag = MathF.Sqrt(gx * gx + gy * gy);
                        if (mag < 1e-6f) continue;
                        candX.Add(x); candY.Add(y);
                        candDx.Add(gx / mag); candDy.Add(gy / mag);
                    }

                int total = candX.Count;
                if (total == 0)
                    return new StableRefineResult(false, "템플릿에서 학습 후보 엣지를 찾지 못했습니다.", 0, sampleImages.Count, 0, 0);

                double cxT = model.TemplateWidth / 2.0;
                double cyT = model.TemplateHeight / 2.0;

                // 2) 샘플별 자세 추정 + 후보 픽셀별 그래디언트 방향 일치 판정
                var okCount = new int[total];
                int matched = 0;
                foreach (var img in sampleImages)
                {
                    if (img == null || img.Empty()) continue;
                    using var gray = img.Channels() > 1
                        ? img.CvtColor(ColorConversionCodes.BGR2GRAY)
                        : img.Clone();

                    if (!TryLocateModel(model, gray, out double px, out double py,
                        out double ang, out double sc, out double score)
                        || score < ScoreThreshold)
                        continue;
                    matched++;

                    using var sx = new Mat();
                    using var sy = new Mat();
                    Cv2.Sobel(gray, sx, MatType.CV_32F, 1, 0, 3);
                    Cv2.Sobel(gray, sy, MatType.CV_32F, 0, 1, 3);

                    double rad = ang * Math.PI / 180.0;
                    double cosA = Math.Cos(rad), sinA = Math.Sin(rad);
                    int W = gray.Width, H = gray.Height;
                    bool ci = UseContrastInvariant;

                    for (int i = 0; i < total; i++)
                    {
                        double rx = candX[i] - cxT, ry = candY[i] - cyT;
                        int ix = (int)Math.Round((rx * cosA - ry * sinA) * sc + px);
                        int iy = (int)Math.Round((rx * sinA + ry * cosA) * sc + py);
                        if (ix < 1 || iy < 1 || ix >= W - 1 || iy >= H - 1) continue;

                        double rdx = candDx[i] * cosA - candDy[i] * sinA;
                        double rdy = candDx[i] * sinA + candDy[i] * cosA;

                        // 자세 추정의 서브픽셀 오차(반올림 ±1px)에 관대하도록 3×3 이웃의
                        // 최대 일치를 취한다 — 이동한 그림자는 평탄 영역(그래디언트 없음)
                        // 이라 여전히 탈락하고, 실제 안정 엣지만 구제된다.
                        double best = double.MinValue;
                        for (int wy = -1; wy <= 1; wy++)
                            for (int wx = -1; wx <= 1; wx++)
                            {
                                float gx2 = sx.At<float>(iy + wy, ix + wx);
                                float gy2 = sy.At<float>(iy + wy, ix + wx);
                                double m = Math.Sqrt(gx2 * gx2 + gy2 * gy2);
                                if (m < 1e-3) continue;
                                double contrib = (rdx * gx2 + rdy * gy2) / m;
                                if (ci) contrib = Math.Abs(contrib);
                                if (contrib > best) best = contrib;
                            }
                        if (best >= minPointScore) okCount[i]++;
                    }
                }

                if (matched == 0)
                    return new StableRefineResult(false,
                        $"샘플 {sampleImages.Count}장 모두 매칭 실패 (Score < {ScoreThreshold:F2}) — 정제를 적용하지 않았습니다.",
                        0, sampleImages.Count, total, 0);

                // 3) 불안정 픽셀 → 마스크 합집합 (단일 픽셀 단위 — 이웃 안정 엣지 보존)
                int required = (int)Math.Ceiling(minStableRatio * matched);
                var newMask = useMask ? mask!.Clone()
                    : new Mat(tGray.Height, tGray.Width, MatType.CV_8UC1, Scalar.Black);
                int maskedCnt = 0;
                for (int i = 0; i < total; i++)
                {
                    if (okCount[i] >= required) continue;
                    newMask.Set(candY[i], candX[i], (byte)255);
                    maskedCnt++;
                }

                if (maskedCnt == 0)
                {
                    newMask.Dispose();
                    return new StableRefineResult(true,
                        $"모든 후보 엣지가 안정적입니다 — 변경 없음 (샘플 {matched}/{sampleImages.Count}장 매칭).",
                        matched, sampleImages.Count, total, 0);
                }

                // 4) 합집합 마스크로 재학습 — 저장된 옛 템플릿이므로 학습 중심 유지
                using var template = model.TemplateImage.Clone();
                bool retrained = TrainPattern(template, model, newMask, preserveTrainedCenter: true);
                newMask.Dispose();

                return retrained
                    ? new StableRefineResult(true,
                        $"안정 특징 정제 완료 — 불안정 엣지 {maskedCnt}/{total}px 자동 마스크 (샘플 {matched}/{sampleImages.Count}장 매칭).",
                        matched, sampleImages.Count, total, maskedCnt)
                    : new StableRefineResult(false,
                        "정제 후 남은 특징이 너무 적어 재학습에 실패했습니다 — 샘플 품질/ScoreThreshold 를 확인하세요.",
                        matched, sampleImages.Count, total, maskedCnt);
            }
            catch (Exception ex)
            {
                return new StableRefineResult(false, $"안정 특징 정제 오류: {ex.Message}", 0, sampleImages.Count, 0, 0);
            }
        }

        /// <summary>
        /// 단일 모델을 그레이 이미지 전체에서 탐색 (Execute 의 모델별 매칭 경로를 검색
        /// 영역 없이 축약한 내부용 — 오버레이/판정 없음).
        /// </summary>
        private bool TryLocateModel(FeatureMatchModel model, Mat gray,
            out double x, out double y, out double angle, out double scale, out double score)
        {
            x = y = angle = 0; scale = 1.0; score = 0;
            int W = gray.Cols, H = gray.Rows;
            if (W < 8 || H < 8) return false;

            using var sSobelX = new Mat(H, W, MatType.CV_32F);
            using var sSobelY = new Mat(H, W, MatType.CV_32F);
            using var sMag = new Mat(H, W, MatType.CV_32F);
            float* dxPtr = (float*)sSobelX.Data;
            float* dyPtr = (float*)sSobelY.Data;
            float* magPtr = (float*)sMag.Data;

            if (NativeVision.IsAvailable)
            {
                NativeVision.ComputeGradientNative((byte*)gray.Data, W, H, (int)gray.Step(), dxPtr, dyPtr, magPtr);
            }
            else
            {
                Cv2.Sobel(gray, sSobelX, MatType.CV_32F, 1, 0, 3);
                Cv2.Sobel(gray, sSobelY, MatType.CV_32F, 0, 1, 3);
                Cv2.Magnitude(sSobelX, sSobelY, sMag);
            }

            int actualLevels = Math.Max(1, Math.Min(NumLevels, 5));
            Mat? coarseImg = null;
            double pyramidScale = 1.0;
            if (actualLevels > 1)
            {
                coarseImg = gray;
                for (int lvl = 0; lvl < actualLevels - 1; lvl++)
                {
                    var temp = new Mat();
                    Cv2.PyrDown(coarseImg, temp);
                    if (coarseImg != gray) coarseImg.Dispose();
                    coarseImg = temp;
                }
                pyramidScale = Math.Pow(2, actualLevels - 1);
            }

            var voteImg = coarseImg ?? gray;
            int vW = voteImg.Cols, vH = voteImg.Rows;

            // Execute 와 동일한 피라미드 임계 보정
            double voteCannyLow = Math.Max(1, CannyLow / pyramidScale);
            double voteCannyHigh = Math.Max(2, CannyHigh / pyramidScale);
            using var voteEdges = voteImg.Canny(voteCannyLow, voteCannyHigh);
            using var votePhase = new Mat();
            {
                using var vsx = new Mat();
                using var vsy = new Mat();
                Cv2.Sobel(voteImg, vsx, MatType.CV_32F, 1, 0, 3);
                Cv2.Sobel(voteImg, vsy, MatType.CV_32F, 0, 1, 3);
                Cv2.Phase(vsx, vsy, votePhase, true);
            }

            byte* vEdgePtr = (byte*)voteEdges.Data;
            float* vPhasePtr = (float*)votePhase.Data;
            int vtotalPx = vW * vH;
            int vEdgeCount = 0;
            for (int i = 0; i < vtotalPx; i++)
                if (vEdgePtr[i] > 0) vEdgeCount++;

            var pool = ArrayPool<int>.Shared;
            int[] seX = pool.Rent(Math.Max(1, vEdgeCount));
            int[] seY = pool.Rent(Math.Max(1, vEdgeCount));
            int[] seBin = pool.Rent(Math.Max(1, vEdgeCount));
            int sei = 0;
            for (int idx = 0; idx < vtotalPx; idx++)
            {
                if (vEdgePtr[idx] > 0)
                {
                    seX[sei] = idx % vW;
                    seY[sei] = idx / vW;
                    int b = (int)(vPhasePtr[idx] / BIN_WIDTH_DEG);
                    if (b < 0) b += NUM_GRAD_BINS;
                    if (b >= NUM_GRAD_BINS) b = NUM_GRAD_BINS - 1;
                    seBin[sei] = b;
                    sei++;
                }
            }

            var (locMatches, _) = MatchSingleModel(model, gray, dxPtr, dyPtr, magPtr,
                W, H, 0, 0, pyramidScale, actualLevels, vW, vH, seX, seY, seBin, sei, pool);

            pool.Return(seX);
            pool.Return(seY);
            pool.Return(seBin);
            if (coarseImg != null && coarseImg != gray) coarseImg.Dispose();

            var best = (score: 0.0, x: 0.0, y: 0.0, angle: 0.0, scale: 1.0);
            foreach (var m in locMatches)
                if (m.score > best.score) best = (m.score, m.x, m.y, m.angle, m.scale);

            score = best.score; x = best.x; y = best.y;
            angle = NormalizeAngle(best.angle); scale = best.scale;
            return best.score > 0;
        }

        #endregion

        #region Execute

        public override VisionResult Execute(Mat inputImage)
        {
            var result = new VisionResult();

            if (inputImage.Channels() != 1)
            {
                result.Success = false;
                result.Message = "FeatureMatchTool은 8-bit Gray 이미지만 입력으로 허용됩니다. 파이프라인에 GrayscaleTool을 추가하세요.";
                return result;
            }

            var sw = Stopwatch.StartNew();

            try
            {
                // Check if any enabled, trained models exist
                var enabledModels = Models.Where(m => m.IsEnabled && m.IsTrained
                    && m.BinOffsets != null && m.BinIndices != null).ToList();

                if (enabledModels.Count == 0)
                {
                    result.Success = false;
                    result.Message = Models.Count == 0
                        ? "패턴이 학습되지 않았습니다."
                        : "활성화된 학습 모델이 없습니다.";
                    return result;
                }

                // ── 1. Shared pre-processing (once for all models) ──
                int offsetX, offsetY;
                using var searchGray = PrepareSearchImage(inputImage, out offsetX, out offsetY);
                int W = searchGray.Cols, H = searchGray.Rows;

                // ── 2. Gradient computation (shared across all models) ──
                using var sSobelX = new Mat(H, W, MatType.CV_32F);
                using var sSobelY = new Mat(H, W, MatType.CV_32F);
                using var sMag = new Mat(H, W, MatType.CV_32F);

                float* dxPtr = (float*)sSobelX.Data;
                float* dyPtr = (float*)sSobelY.Data;
                float* magPtr = (float*)sMag.Data;

                if (NativeVision.IsAvailable)
                {
                    NativeVision.ComputeGradientNative(
                        (byte*)searchGray.Data,
                        W, H, (int)searchGray.Step(),
                        dxPtr, dyPtr, magPtr);
                }
                else
                {
                    Cv2.Sobel(searchGray, sSobelX, MatType.CV_32F, 1, 0, 3);
                    Cv2.Sobel(searchGray, sSobelY, MatType.CV_32F, 0, 1, 3);
                    Cv2.Magnitude(sSobelX, sSobelY, sMag);
                }

                // ── 3. Shared pyramid voting image ──
                int actualLevels = Math.Max(1, Math.Min(NumLevels, 5));
                Mat? coarseImg = null;
                double pyramidScale = 1.0;
                if (actualLevels > 1)
                {
                    coarseImg = searchGray;
                    for (int lvl = 0; lvl < actualLevels - 1; lvl++)
                    {
                        var temp = new Mat();
                        Cv2.PyrDown(coarseImg, temp);
                        if (coarseImg != searchGray) coarseImg.Dispose();
                        coarseImg = temp;
                    }
                    pyramidScale = Math.Pow(2, actualLevels - 1);
                }

                var voteImg = coarseImg ?? searchGray;
                int vW = voteImg.Cols, vH = voteImg.Rows;

                // 피라미드 축소는 블러+데시메이션이라 그래디언트가 레벨마다 절반 수준으로
                // 약해진다 — 풀해상도 기준 Canny 임계를 그대로 쓰면 저대비 윤곽(그림자 경계
                // 등)이 투표 단계에서 통째로 소실. 임계를 배율만큼 낮춰 보정한다.
                double voteCannyLow = Math.Max(1, CannyLow / pyramidScale);
                double voteCannyHigh = Math.Max(2, CannyHigh / pyramidScale);
                using var voteEdges = voteImg.Canny(voteCannyLow, voteCannyHigh);
                using var votePhase = new Mat();
                {
                    using var vsx = new Mat();
                    using var vsy = new Mat();
                    Cv2.Sobel(voteImg, vsx, MatType.CV_32F, 1, 0, 3);
                    Cv2.Sobel(voteImg, vsy, MatType.CV_32F, 0, 1, 3);
                    Cv2.Phase(vsx, vsy, votePhase, true);
                }

                byte* vEdgePtr = (byte*)voteEdges.Data;
                float* vPhasePtr = (float*)votePhase.Data;

                int vtotalPx = vW * vH;
                int vEdgeCount = 0;
                for (int i = 0; i < vtotalPx; i++)
                    if (vEdgePtr[i] > 0) vEdgeCount++;

                var pool = ArrayPool<int>.Shared;
                int[] seX = pool.Rent(vEdgeCount);
                int[] seY = pool.Rent(vEdgeCount);
                int[] seBin = pool.Rent(vEdgeCount);
                int sei = 0;
                for (int idx = 0; idx < vtotalPx; idx++)
                {
                    if (vEdgePtr[idx] > 0)
                    {
                        seX[sei] = idx % vW;
                        seY[sei] = idx / vW;
                        int b = (int)(vPhasePtr[idx] / BIN_WIDTH_DEG);
                        if (b < 0) b += NUM_GRAD_BINS;
                        if (b >= NUM_GRAD_BINS) b = NUM_GRAD_BINS - 1;
                        seBin[sei] = b;
                        sei++;
                    }
                }
                int searchEdgeCount = sei;

                // ── 4. Iterate all enabled models — 모델별 정련 후보 전부 수집 ──
                var allMatches = new List<(double score, double x, double y, double angle, double scale,
                    double coverage, FeatureMatchModel model)>();
                double globalBestScore = 0;
                double globalBestVoteVal = 0;

                foreach (var model in enabledModels)
                {
                    var (modelMatches, modelVoteVal) =
                        MatchSingleModel(model, searchGray, dxPtr, dyPtr, magPtr,
                            W, H, offsetX, offsetY, pyramidScale, actualLevels,
                            vW, vH, seX, seY, seBin, searchEdgeCount, pool);

                    if (modelVoteVal > globalBestVoteVal) globalBestVoteVal = modelVoteVal;
                    foreach (var m in modelMatches)
                    {
                        if (m.score > globalBestScore) globalBestScore = m.score;
                        allMatches.Add((m.score, m.x, m.y, m.angle, m.scale, m.coverage, model));
                    }
                }

                pool.Return(seX);
                pool.Return(seY);
                pool.Return(seBin);
                if (coarseImg != null && coarseImg != searchGray)
                    coarseImg.Dispose();

                // ── 5. Build result — 임계/커버리지 통과 → 점수순 → 전역 NMS → 상위 N ──
                sw.Stop();
                ExecutionTime = sw.Elapsed.TotalMilliseconds;

                var accepted = allMatches
                    .Where(m => m.score >= ScoreThreshold && (MinCoverage <= 0 || m.coverage >= MinCoverage))
                    .OrderByDescending(m => m.score)
                    .ToList();

                // 전역 NMS — 모델이 여럿이면 서로 다른 모델이 같은 위치를 두 번 잡는
                // 것도 여기서 걸러진다. 분리 거리는 템플릿 짧은 변 × NmsDistanceFactor.
                var kept = new List<(double score, double x, double y, double angle, double scale,
                    double coverage, FeatureMatchModel model)>();
                foreach (var c in accepted)
                {
                    if (kept.Count >= MaxInstances) break;
                    double minSide = Math.Min(c.model.TemplateWidth, c.model.TemplateHeight)
                        * c.scale * NmsDistanceFactor;
                    bool dup = false;
                    foreach (var kInst in kept)
                    {
                        double dx = c.x - kInst.x, dy = c.y - kInst.y;
                        if (dx * dx + dy * dy < minSide * minSide) { dup = true; break; }
                    }
                    if (!dup) kept.Add(c);
                }

                LastMatchedModel = kept.Count > 0 ? kept[0].model : null;
                result.Data["MatchCount"] = kept.Count;

                if (kept.Count > 0)
                {
                    var rep = kept[0];
                    double finalX = rep.x + offsetX;
                    double finalY = rep.y + offsetY;

                    // 정련이 코스 최적각 ±코스스텝을 탐색하므로 360° 검색(Start -180, Extent 360)
                    // 경계에서 ±180 밖 각도(예: 183°)가 나올 수 있다 — 보고/판정 전 정규화.
                    // 미정규화 시 각도 판정(허용 -180~180)이 경계 근처 정상품을 오탐 NG 처리.
                    double repAngle = NormalizeAngle(rep.angle);

                    result.Success = true;
                    string modelInfo = Models.Count > 1 ? $", Model={rep.model.Name}" : "";
                    string countInfo = kept.Count > 1 ? $", Instances={kept.Count}" : "";
                    result.Message = $"Score={rep.score:F3}, Pos=({finalX:F1},{finalY:F1}), Angle={repAngle:F2}, Scale={rep.scale:F3}, Coverage={rep.coverage:F2}{countInfo}{modelInfo}";
                    result.Data["Score"] = rep.score;
                    result.Data["CenterX"] = finalX;
                    result.Data["CenterY"] = finalY;
                    result.Data["Angle"] = repAngle;
                    result.Data["Scale"] = rep.scale;
                    result.Data["Coverage"] = rep.coverage;
                    result.Data["MatchedModel"] = rep.model.Name;
                    result.Data["TrainedCenterX"] = rep.model.TrainedCenterX;
                    result.Data["TrainedCenterY"] = rep.model.TrainedCenterY;
                    result.Data["TrainedAngle"] = rep.model.TrainedAngle;

                    // 다중 인스턴스 키 — ShapeMatchTool 과 동일 체계. 대표(최고 점수)는
                    // 위의 기존 키에 그대로 실려 Fixture/Align 등 단일 소비처와 호환.
                    if (MaxInstances > 1)
                    {
                        for (int i = 0; i < kept.Count; i++)
                        {
                            var m = kept[i];
                            result.Data[$"Match{i}_Score"] = m.score;
                            result.Data[$"Match{i}_CenterX"] = m.x + offsetX;
                            result.Data[$"Match{i}_CenterY"] = m.y + offsetY;
                            result.Data[$"Match{i}_Angle"] = NormalizeAngle(m.angle);
                            result.Data[$"Match{i}_Scale"] = m.scale;
                            result.Data[$"Match{i}_Coverage"] = m.coverage;
                        }
                    }

                    var overlayInstances = new List<(double x, double y, double angle, double scale,
                        double score, FeatureMatchModel model)>(kept.Count);
                    foreach (var m in kept)
                        overlayInstances.Add((m.x + offsetX, m.y + offsetY, NormalizeAngle(m.angle), m.scale, m.score, m.model));
                    result.OverlayImage = DrawOverlay(inputImage, overlayInstances);

                    // 각도/스케일 판정 — 매칭은 찾았으나 자세가 허용 범위를 벗어나면 NG.
                    // Data/오버레이는 그대로 남겨 측정값 확인·Web 업로드가 가능하게 한다.
                    // 다중 인스턴스에서도 대표(최고 점수) 기준 (ShapeMatch 와 동일).
                    var poseNg = EvaluatePoseJudgment(repAngle, rep.scale);
                    if (poseNg != null)
                    {
                        result.Success = false;
                        result.Message += " — " + poseNg;
                    }
                }
                else
                {
                    result.Success = false;

                    // 임계는 넘었으나 커버리지 판정으로 전량 기각된 경우 — 부분(반쪽)
                    // 매칭이 의심되는 상황이므로 진단 메시지를 구체적으로 남긴다.
                    double covRejScore = 0, covRejVal = 0;
                    if (MinCoverage > 0)
                        foreach (var m in allMatches)
                            if (m.score >= ScoreThreshold && m.coverage < MinCoverage && m.score > covRejScore)
                            {
                                covRejScore = m.score;
                                covRejVal = m.coverage;
                            }

                    result.Message = covRejScore > 0
                        ? $"커버리지 부족으로 기각: Score={covRejScore:F3}, Coverage={covRejVal:F2} < {MinCoverage:F2} — 부분(반쪽) 매칭 의심. 학습 특징점 수와 Canny 임계를 확인하세요."
                        : $"패턴을 찾지 못했습니다. (최대 Score={globalBestScore:F3}, Votes={globalBestVoteVal})";
                    if (UseSearchRegion && SearchRegion.Width > 0 && SearchRegion.Height > 0)
                    {
                        var overlay = GetColorOverlayBase(inputImage);
                        Cv2.Rectangle(overlay,
                            new Point(SearchRegion.X, SearchRegion.Y),
                            new Point(SearchRegion.X + SearchRegion.Width, SearchRegion.Y + SearchRegion.Height),
                            new Scalar(0, 255, 255), 2);
                        result.OverlayImage = overlay;
                    }
                }

                // 위치 검출 도구는 이미지를 변형하지 않으므로 입력을 그대로 다음 도구로 전달 (passthrough).
                // 미설정 시 이 도구를 Image 연결 소스로 쓰면 하류가 조용히 원본 이미지로 폴백했었음
                // (VisionService.GetConnectedInputImage). Clone인 이유: 입력 Mat은 VisionService 소유
                // (원본 Clone) 또는 업스트림 결과와의 공유 참조라서, 소유권이 VisionResult로 이전되는
                // OutputImage(재실행 시 ReleaseMats로 Dispose됨)에 그대로 담으면 이중 해제/사후 변조 위험.
                result.OutputImage = inputImage.Clone();
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"실행 오류: {ex.Message}";
            }

            return result;
        }

        /// <summary>
        /// 투표 결과에서 공간적으로 분리된 상위 K개 후보 선별 (그리디 NMS).
        /// minSeparation(투표 이미지 px) 안의 피크는 같은 인스턴스의 다른 각도 응답으로
        /// 보고 최고 득표만 남긴다. votes ≤ 0 항목은 무시.
        /// </summary>
        internal static List<(double cx, double cy, double angle, int votes)> SelectTopKCandidates(
            List<(double cx, double cy, double angle, int votes)> all, int k, double minSeparation)
        {
            var result = new List<(double cx, double cy, double angle, int votes)>(k);
            double sepSq = minSeparation * minSeparation;
            foreach (var c in all.Where(c => c.votes > 0).OrderByDescending(c => c.votes))
            {
                if (result.Count >= k) break;
                bool dup = false;
                foreach (var r in result)
                {
                    double dx = r.cx - c.cx, dy = r.cy - c.cy;
                    if (dx * dx + dy * dy < sepSq) { dup = true; break; }
                }
                if (!dup) result.Add(c);
            }
            return result;
        }

        /// <summary>각도를 [-180, 180) 로 정규화 — 검색·정련은 주기 함수라 값 자체는 등가.</summary>
        internal static double NormalizeAngle(double deg)
        {
            var norm = (deg + 180.0) % 360.0;
            if (norm < 0) norm += 360.0;
            return norm - 180.0;
        }

        /// <summary>
        /// 매칭 자세(각도/스케일) 판정. 통과·비활성이면 null, NG면 사유 문자열.
        /// 각도는 호출 전 정규화된 값을 받는다 (Execute 경로 보장).
        /// </summary>
        internal string? EvaluatePoseJudgment(double angle, double scale)
        {
            if (UseAngleJudgment && (angle < AngleLowerLimit || angle > AngleUpperLimit))
                return $"각도 판정 NG: {angle:F2}° (허용 {AngleLowerLimit:F2}~{AngleUpperLimit:F2}°)";
            if (UseScaleJudgment && (scale < ScaleLowerLimit || scale > ScaleUpperLimit))
                return $"스케일 판정 NG: {scale:F3} (허용 {ScaleLowerLimit:F3}~{ScaleUpperLimit:F3})";
            return null;
        }

        /// <summary>
        /// Run matching for a single model. 정련까지 마친 후보 목록(점수 미필터)과
        /// 최대 득표수를 반환한다 — 채택(임계/커버리지/NMS/상위 N)은 Execute 가 수행.
        /// </summary>
        private (List<(double score, double x, double y, double angle, double scale, double coverage)> matches, double voteVal)
            MatchSingleModel(
                FeatureMatchModel model, Mat searchGray,
                float* dxPtr, float* dyPtr, float* magPtr,
                int W, int H, int offsetX, int offsetY,
                double pyramidScale, int actualLevels,
                int vW, int vH,
                int[] seX, int[] seY, int[] seBin, int searchEdgeCount,
                ArrayPool<int> pool)
        {
            int N = model.ModelEdges.Count;
            double coarseAngleStep = Math.Max(AngleStep, 4.0);
            double fineVoteAngleStep = Math.Max(AngleStep, 1.0);

            int[] binOffsets = model.BinOffsets!;
            int[] binIndices = model.BinIndices!;

            const int BIN_SHIFT = 1;
            double invScale = 1.0 / pyramidScale;

            // 투표 피크를 1개가 아니라 공간적으로 분리된 상위 K개로 받는다 — 배경
            // 구조물·이웃 부품·그림자 덩어리가 최다 득표해도 진짜 위치가 후보에 남아
            // 정련(점수) 단계에서 판가름 난다. 다중 인스턴스면 후보 수와 각도당 피크
            // 수를 함께 늘려, 같은 각도로 놓인 동일 객체 여러 개도 후보에 들게 한다.
            int topCandidates = Math.Max(3, Math.Min(MaxInstances * 2, 60));
            int peaksPerAngle = MaxInstances > 1 ? Math.Min(MaxInstances, 16) : 1;
            int coarseTopK = Math.Max(5, Math.Min(MaxInstances + 2, 12));
            double minSeparation = Math.Max(4.0,
                Math.Min(model.TemplateWidth, model.TemplateHeight) * NmsDistanceFactor * invScale);

            // 투표 스케일 축 — 투표는 원래 학습 스케일 고정이라, 실물 스케일이 중심에서
            // 멀면(넓은 Min/MaxScale 설정) 투표 피크가 번져 진짜 위치가 후보에 못 든다.
            // 스케일 범위가 ±0.15 를 넘으면 코스 스케일 몇 개로 나눠 투표하고 후보를
            // 합친다 (기본 0.9~1.1 은 중심 1회 → 기존과 동일 비용).
            double scaleCenter = (MinScale + MaxScale) / 2.0;
            double scaleRange = (MaxScale - MinScale) / 2.0;
            const double VOTE_SCALE_STEP = 0.15;
            var voteScales = new List<double> { scaleCenter };
            for (int k = 1; voteScales.Count < 5 && k * VOTE_SCALE_STEP <= scaleRange + 1e-9; k++)
            {
                voteScales.Add(scaleCenter - k * VOTE_SCALE_STEP);
                if (voteScales.Count < 5) voteScales.Add(scaleCenter + k * VOTE_SCALE_STEP);
            }

            var rawCands = new List<(double cx, double cy, double angle, int votes)>();

            // 각도당 다중 피크가 필요한데 구 DLL(Multi export 없음)이면 managed 투표로
            // 폴백 — 네이티브 TopK 는 각도당 피크 1개라 같은 각도의 인스턴스를 못 나눈다.
            bool nativeVoting = NativeVision.IsAvailable && model.ModelXArray != null && model.ModelYArray != null
                && (peaksPerAngle == 1 || NativeVision.HasTopKMultiExport);

            if (nativeVoting)
            {
                double* oCx = stackalloc double[topCandidates];
                double* oCy = stackalloc double[topCandidates];
                double* oAng = stackalloc double[topCandidates];
                int* oVotes = stackalloc int[topCandidates];

                fixed (float* pModelX = model.ModelXArray, pModelY = model.ModelYArray)
                fixed (int* pBinOffsets = binOffsets, pBinIndices = binIndices)
                fixed (int* pSeX = seX, pSeY = seY, pSeBin = seBin)
                {
                    foreach (var voteScale in voteScales)
                    {
                        double effInvScale = invScale * voteScale;

                        if (NativeVision.HasTopKMultiExport)
                        {
                            int filled = NativeVision.HoughVotingTopKMultiNative(
                                pModelX, pModelY, N,
                                pBinOffsets, pBinIndices, NUM_GRAD_BINS,
                                pSeX, pSeY, pSeBin, searchEdgeCount,
                                vW, vH,
                                AngleStart, AngleExtent,
                                coarseAngleStep, fineVoteAngleStep, coarseTopK,
                                effInvScale, BIN_SHIFT,
                                peaksPerAngle,
                                minSeparation, topCandidates,
                                oCx, oCy, oAng, oVotes);
                            for (int i = 0; i < filled; i++)
                                rawCands.Add((oCx[i], oCy[i], oAng[i], oVotes[i]));
                        }
                        else if (NativeVision.HasTopKExport)
                        {
                            int filled = NativeVision.HoughVotingTopKNative(
                                pModelX, pModelY, N,
                                pBinOffsets, pBinIndices, NUM_GRAD_BINS,
                                pSeX, pSeY, pSeBin, searchEdgeCount,
                                vW, vH,
                                AngleStart, AngleExtent,
                                coarseAngleStep, fineVoteAngleStep, coarseTopK,
                                effInvScale, BIN_SHIFT,
                                minSeparation, topCandidates,
                                oCx, oCy, oAng, oVotes);
                            for (int i = 0; i < filled; i++)
                                rawCands.Add((oCx[i], oCy[i], oAng[i], oVotes[i]));
                        }
                        else
                        {
                            // 구버전 DLL — 단일 최고 피크 경로 유지
                            double outCx, outCy, outAngle;
                            int outVotes;
                            NativeVision.HoughVotingNative(
                                pModelX, pModelY, N,
                                pBinOffsets, pBinIndices, NUM_GRAD_BINS,
                                pSeX, pSeY, pSeBin, searchEdgeCount,
                                vW, vH,
                                AngleStart, AngleExtent,
                                coarseAngleStep, fineVoteAngleStep, 5,
                                effInvScale, BIN_SHIFT,
                                &outCx, &outCy, &outAngle, &outVotes);
                            rawCands.Add((outCx, outCy, outAngle, outVotes));
                        }
                    }
                }
            }
            else
            {
                // C# fallback with multi-resolution angle search (+ 다중 피크·다중 스케일)
                int bW = (vW >> BIN_SHIFT) + 1;
                int bH = (vH >> BIN_SHIFT) + 1;
                int accLen = bW * bH;
                int numCoarseAngles = Math.Max(1, (int)(AngleExtent / coarseAngleStep) + 1);
                int sepBins = Math.Max(1, (int)(minSeparation / (1 << BIN_SHIFT)));

                object lockObj = new();
                var modelEdges = model.ModelEdges;
                var fineAll = new List<(double cx, double cy, double angle, int votes)>();
                (double angle, double cx, double cy, int votes) coarseFallback = default;

                foreach (var voteScale in voteScales)
                {
                    double effInvScale = invScale * voteScale;
                    var coarseCandidates = new (double angle, double cx, double cy, int votes)[coarseTopK];

                    Parallel.For(0, numCoarseAngles, ai =>
                    {
                        double angle = AngleStart + ai * coarseAngleStep;
                        double rad = angle * (Math.PI / 180.0);
                        double cosA = Math.Cos(rad);
                        double sinA = Math.Sin(rad);

                        int[] rotX = pool.Rent(N);
                        int[] rotY = pool.Rent(N);
                        int[] acc = pool.Rent(accLen);
                        Array.Clear(acc, 0, accLen);

                        for (int i = 0; i < N; i++)
                        {
                            var mp = modelEdges[i];
                            rotX[i] = (int)Math.Round((mp.X * cosA - mp.Y * sinA) * effInvScale);
                            rotY[i] = (int)Math.Round((mp.X * sinA + mp.Y * cosA) * effInvScale);
                        }

                        int binShift = (int)Math.Round(angle / BIN_WIDTH_DEG);

                        for (int si = 0; si < searchEdgeCount; si++)
                        {
                            int ex = seX[si], ey = seY[si], sb = seBin[si];
                            for (int db = -1; db <= 1; db++)
                            {
                                int modelBin = ((sb - binShift + db) % NUM_GRAD_BINS + NUM_GRAD_BINS) % NUM_GRAD_BINS;
                                int bStart = binOffsets[modelBin];
                                int bEnd = binOffsets[modelBin + 1];
                                for (int bi = bStart; bi < bEnd; bi++)
                                {
                                    int j = binIndices[bi];
                                    int cx = (ex - rotX[j]) >> BIN_SHIFT;
                                    int cy = (ey - rotY[j]) >> BIN_SHIFT;
                                    if ((uint)cx < (uint)bW && (uint)cy < (uint)bH)
                                        acc[cy * bW + cx]++;
                                }
                            }
                        }

                        var peaks = ExtractAccumulatorPeaks(acc, bW, bH, accLen, peaksPerAngle, sepBins);

                        pool.Return(rotX);
                        pool.Return(rotY);
                        pool.Return(acc);

                        lock (lockObj)
                        {
                            foreach (var (votes, idx) in peaks)
                            {
                                if (votes <= coarseCandidates[coarseTopK - 1].votes) continue;
                                double peakCx = (idx % bW) * (1 << BIN_SHIFT) + (1 << BIN_SHIFT) / 2;
                                double peakCy = (idx / bW) * (1 << BIN_SHIFT) + (1 << BIN_SHIFT) / 2;
                                coarseCandidates[coarseTopK - 1] = (angle, peakCx, peakCy, votes);
                                for (int k = coarseTopK - 1; k > 0 && coarseCandidates[k].votes > coarseCandidates[k - 1].votes; k--)
                                    (coarseCandidates[k], coarseCandidates[k - 1]) = (coarseCandidates[k - 1], coarseCandidates[k]);
                            }
                        }
                    });

                    if (coarseCandidates[0].votes >= coarseFallback.votes)
                        coarseFallback = coarseCandidates[0];

                    // Fine pass — 모든 파인 결과를 모아 두었다가 NMS 로 상위 K개 선별.
                    // 다중 피크에서는 같은 각도의 코스 후보가 위치만 달리해 여러 개
                    // 들어오는데, 파인 스윕은 위치 무관이므로 각도당 1회면 충분하다.
                    var sweptAngles = new List<double>();
                    foreach (var cand in coarseCandidates)
                    {
                        if (cand.votes == 0) continue;
                        if (sweptAngles.Contains(cand.angle)) continue;
                        sweptAngles.Add(cand.angle);

                        double fineStart = cand.angle - coarseAngleStep;
                        double fineEnd = cand.angle + coarseAngleStep;
                        int numFine = Math.Max(1, (int)((fineEnd - fineStart) / fineVoteAngleStep) + 1);

                        Parallel.For(0, numFine, fi =>
                        {
                            double angle = fineStart + fi * fineVoteAngleStep;
                            if (angle < AngleStart || angle > AngleStart + AngleExtent) return;

                            double rad = angle * (Math.PI / 180.0);
                            double cosA = Math.Cos(rad);
                            double sinA = Math.Sin(rad);

                            int[] rotX = pool.Rent(N);
                            int[] rotY = pool.Rent(N);
                            int[] acc = pool.Rent(accLen);
                            Array.Clear(acc, 0, accLen);

                            for (int i = 0; i < N; i++)
                            {
                                var mp = modelEdges[i];
                                rotX[i] = (int)Math.Round((mp.X * cosA - mp.Y * sinA) * effInvScale);
                                rotY[i] = (int)Math.Round((mp.X * sinA + mp.Y * cosA) * effInvScale);
                            }

                            int binShift = (int)Math.Round(angle / BIN_WIDTH_DEG);

                            for (int si = 0; si < searchEdgeCount; si++)
                            {
                                int ex = seX[si], ey = seY[si], sb = seBin[si];
                                for (int db = -1; db <= 1; db++)
                                {
                                    int modelBin = ((sb - binShift + db) % NUM_GRAD_BINS + NUM_GRAD_BINS) % NUM_GRAD_BINS;
                                    int bStart = binOffsets[modelBin];
                                    int bEnd = binOffsets[modelBin + 1];
                                    for (int bi = bStart; bi < bEnd; bi++)
                                    {
                                        int j = binIndices[bi];
                                        int cx = (ex - rotX[j]) >> BIN_SHIFT;
                                        int cy = (ey - rotY[j]) >> BIN_SHIFT;
                                        if ((uint)cx < (uint)bW && (uint)cy < (uint)bH)
                                            acc[cy * bW + cx]++;
                                    }
                                }
                            }

                            var peaks = ExtractAccumulatorPeaks(acc, bW, bH, accLen, peaksPerAngle, sepBins);

                            pool.Return(rotX);
                            pool.Return(rotY);
                            pool.Return(acc);

                            lock (lockObj)
                            {
                                foreach (var (votes, idx) in peaks)
                                {
                                    double peakCx = (idx % bW) * (1 << BIN_SHIFT) + (1 << BIN_SHIFT) / 2;
                                    double peakCy = (idx / bW) * (1 << BIN_SHIFT) + (1 << BIN_SHIFT) / 2;
                                    fineAll.Add((peakCx, peakCy, angle, votes));
                                }
                            }
                        });
                    }
                }

                rawCands.AddRange(fineAll);
                if (rawCands.Count == 0)
                    rawCands.Add((coarseFallback.cx, coarseFallback.cy,
                        coarseFallback.angle, coarseFallback.votes));
            }

            var voteCands = SelectTopKCandidates(rawCands, topCandidates, minSeparation);
            if (voteCands.Count == 0 && rawCands.Count > 0)
                voteCands.Add(rawCands[0]);

            double bestVoteVal = 0;
            foreach (var vc in voteCands)
                if (vc.votes > bestVoteVal) bestVoteVal = vc.votes;

            // ── Phase 2: 후보별 SIMD gradient dot-product refinement — 후보 전부 보존 ──
            double fineAngleStep = Math.Max(0.1, AngleStep / 2.0);
            double fineScaleStep = Math.Max(0.001, ScaleStep);

            // 투표는 코스 레벨에서 (1<<BIN_SHIFT)px 빈으로 양자화되므로 풀해상도 환산
            // 위치 오차가 최대 pyramidScale×(1<<BIN_SHIFT)px — 정련 반경이 이보다 작으면
            // 정답이 탐색 창 밖에 있어 점수가 깎이거나 미검출된다 (레벨 3에서 8px 오차
            // vs 기존 반경 6px).
            int refRadius = actualLevels > 1
                ? Math.Max(4, (int)(pyramidScale * (1 << BIN_SHIFT)))
                : 4;
            float thresh = (float)ScoreThreshold;
            float greedy = (float)Greediness;
            bool ciFlag = UseContrastInvariant;

            // 다중 인스턴스에서는 2·3번째 인스턴스의 득표가 원래 낮으므로 컷 완화
            double voteCut = MaxInstances > 1 ? 0.10 : 0.25;

            // 정련 각도 창 — 투표는 그래디언트 방향 빈 ±1칸(±10°)을 허용하므로 직선
            // 위주 형상은 ±15° 안에서 득표가 거의 같고, 최다 득표 각도가 진짜 각도에서
            // 빈 폭만큼 어긋날 수 있다. 창을 코스 스텝만 잡으면 진짜 각도가 정련 밖에
            // 남아 저점수 미검출·오검출이 된다 (다중 인스턴스 진단에서 실측 -12° 오프).
            double refineAngleRange = coarseAngleStep + BIN_WIDTH_DEG;

            var matches = new List<(double score, double x, double y, double angle, double scale, double coverage)>();

            foreach (var vc in voteCands)
            {
                // 최고 득표 대비 미미한 후보는 정련 생략 (비용 절약)
                if (vc.votes < bestVoteVal * voteCut) continue;

                double candCx = vc.cx * pyramidScale;
                double candCy = vc.cy * pyramidScale;

                double cScore = 0, cX = candCx, cY = candCy, cAngle = vc.angle, cScale = 1.0;

                int poseCount;
                PrecomputeFinePosesNative(
                    model,
                    vc.angle, refineAngleRange, fineAngleStep,
                    scaleCenter, scaleRange, fineScaleStep,
                    out poseCount);

                if (NativeVision.IsAvailable && poseCount > 0)
                {
                    int bestDx, bestDy, bestPoseIdx;
                    double score = NativeVision.EvaluateAllPosesNative(
                        (int)candCx, (int)candCy, refRadius,
                        model.NativeRxBuf, model.NativeRyBuf,
                        model.NativeRdxBuf, model.NativeRdyBuf,
                        model.NativeMarginBuf,
                        poseCount, N,
                        dxPtr, dyPtr, magPtr,
                        W, H, thresh, greedy,
                        &bestDx, &bestDy, &bestPoseIdx,
                        ciFlag ? 1 : 0);
                    if (score > cScore)
                    {
                        cScore = score;
                        cX = (int)candCx + bestDx;
                        cY = (int)candCy + bestDy;
                        cAngle = model.NativeAngleBuf[bestPoseIdx];
                        cScale = model.NativeScaleBuf[bestPoseIdx];
                    }
                }
                else if (poseCount > 0)
                {
                    for (int pi = 0; pi < poseCount; pi++)
                    {
                        int fm = model.NativeMarginBuf[pi];
                        int* pRx = model.NativeRxBuf + pi * N;
                        int* pRy = model.NativeRyBuf + pi * N;
                        float* pRdx = model.NativeRdxBuf + pi * N;
                        float* pRdy = model.NativeRdyBuf + pi * N;
                        for (int dy = -refRadius; dy <= refRadius; dy++)
                        {
                            int py = (int)candCy + dy;
                            if (py < fm || py >= H - fm) continue;
                            for (int dx = -refRadius; dx <= refRadius; dx++)
                            {
                                int px = (int)candCx + dx;
                                if (px < fm || px >= W - fm) continue;

                                double score = EvaluateSimd(
                                    px, py, pRx, pRy, pRdx, pRdy,
                                    dxPtr, dyPtr, magPtr, W, N, thresh, greedy, ciFlag);
                                if (score > cScore)
                                {
                                    cScore = score;
                                    cX = px; cY = py;
                                    cAngle = model.NativeAngleBuf[pi];
                                    cScale = model.NativeScaleBuf[pi];
                                }
                            }
                        }
                    }
                }

                if (cScore <= 0) continue;

                // Sub-pixel parabolic refinement (인스턴스별)
                if (cScore >= ScoreThreshold)
                {
                    int bxi = (int)cX, byi = (int)cY;

                    double sxm = EvaluateSinglePose(model.ModelEdges, cAngle, cScale, bxi - 1, byi, dxPtr, dyPtr, magPtr, W, N, thresh, greedy, ciFlag);
                    double sxp = EvaluateSinglePose(model.ModelEdges, cAngle, cScale, bxi + 1, byi, dxPtr, dyPtr, magPtr, W, N, thresh, greedy, ciFlag);
                    cX = bxi + ParabolicPeak(sxm, cScore, sxp);

                    double sym = EvaluateSinglePose(model.ModelEdges, cAngle, cScale, bxi, byi - 1, dxPtr, dyPtr, magPtr, W, N, thresh, greedy, ciFlag);
                    double syp = EvaluateSinglePose(model.ModelEdges, cAngle, cScale, bxi, byi + 1, dxPtr, dyPtr, magPtr, W, N, thresh, greedy, ciFlag);
                    cY = byi + ParabolicPeak(sym, cScore, syp);

                    double sam = EvaluateSinglePose(model.ModelEdges, cAngle - fineAngleStep, cScale, bxi, byi, dxPtr, dyPtr, magPtr, W, N, thresh, greedy, ciFlag);
                    double sap = EvaluateSinglePose(model.ModelEdges, cAngle + fineAngleStep, cScale, bxi, byi, dxPtr, dyPtr, magPtr, W, N, thresh, greedy, ciFlag);
                    cAngle += ParabolicPeak(sam, cScore, sap) * fineAngleStep;

                    double ssm = EvaluateSinglePose(model.ModelEdges, cAngle, cScale - fineScaleStep, bxi, byi, dxPtr, dyPtr, magPtr, W, N, thresh, greedy, ciFlag);
                    double ssp = EvaluateSinglePose(model.ModelEdges, cAngle, cScale + fineScaleStep, bxi, byi, dxPtr, dyPtr, magPtr, W, N, thresh, greedy, ciFlag);
                    cScale += ParabolicPeak(ssm, cScore, ssp) * fineScaleStep;
                }

                // 커버리지 — 점별 실제 일치 비율. 부분(반쪽) 매칭은 정합된 절반의
                // 점만 통과해 ~0.5 에 묶인다 (스코어 평균과 달리 부분 정합을 식별).
                double coverage = ComputeCoverage(model, cX, cY, cAngle, cScale,
                    dxPtr, dyPtr, magPtr, W, H, ciFlag);

                matches.Add((cScore, cX, cY, cAngle, cScale, coverage));
            }

            return (matches, bestVoteVal);
        }

        /// <summary>
        /// Accumulator 에서 공간 분리된 상위 피크를 최대 maxPeaks 개 추출 — 피크 채택 후
        /// 주변 (2·sepBins+1)² 빈을 지워 같은 인스턴스의 중복 채택을 막는다.
        /// acc 를 파괴하므로 호출 후 재사용 금지 (각도별로 새로 클리어됨).
        /// </summary>
        internal static List<(int votes, int idx)> ExtractAccumulatorPeaks(
            int[] acc, int bW, int bH, int accLen, int maxPeaks, int sepBins)
        {
            var peaks = new List<(int votes, int idx)>(maxPeaks);
            while (peaks.Count < maxPeaks)
            {
                int maxVote = 0, maxIdx = 0;
                for (int i = 0; i < accLen; i++)
                    if (acc[i] > maxVote) { maxVote = acc[i]; maxIdx = i; }
                if (maxVote <= 0) break;
                peaks.Add((maxVote, maxIdx));
                if (peaks.Count >= maxPeaks) break;

                int px = maxIdx % bW, py = maxIdx / bW;
                int x0 = Math.Max(0, px - sepBins), y0 = Math.Max(0, py - sepBins);
                int x1 = Math.Min(bW - 1, px + sepBins), y1 = Math.Min(bH - 1, py + sepBins);
                for (int yy = y0; yy <= y1; yy++)
                    Array.Clear(acc, yy * bW + x0, x1 - x0 + 1);
            }
            return peaks;
        }

        /// <summary>
        /// 최종 자세에서 모델 점별로 그래디언트 방향 일치(내적 ≥ 0.5)를 판정해 일치
        /// 비율을 반환한다. 분모는 전체 모델 점 — 이미지 밖·평탄 영역에 떨어진 점은
        /// 불일치로 세어, 모델 절반만 정합한 반쪽 매칭이 높은 커버리지를 받지 못한다.
        /// 서브픽셀 자세의 반올림 오차(±1px)에 관대하도록 3×3 이웃의 최대 일치를
        /// 취한다 (RefineStableFeatures 와 동일 근거) — 객체가 없는 평탄 영역은
        /// 이웃에도 그래디언트가 없어 여전히 탈락한다.
        /// </summary>
        private static double ComputeCoverage(FeatureMatchModel model,
            double cx, double cy, double angle, double scale,
            float* dxPtr, float* dyPtr, float* magPtr, int W, int H, bool contrastInvariant)
        {
            var edges = model.ModelEdges;
            int n = edges.Count;
            if (n == 0) return 0;

            double rad = angle * Math.PI / 180.0;
            double cosA = Math.Cos(rad), sinA = Math.Sin(rad);
            const double POINT_PASS = 0.5;

            int ok = 0;
            for (int i = 0; i < n; i++)
            {
                var p = edges[i];
                int px = (int)Math.Round((p.X * cosA - p.Y * sinA) * scale + cx);
                int py = (int)Math.Round((p.X * sinA + p.Y * cosA) * scale + cy);
                if (px < 1 || py < 1 || px >= W - 1 || py >= H - 1) continue;

                double rdx = p.Dx * cosA - p.Dy * sinA;
                double rdy = p.Dx * sinA + p.Dy * cosA;

                double best = double.MinValue;
                for (int wy = -1; wy <= 1; wy++)
                    for (int wx = -1; wx <= 1; wx++)
                    {
                        int idx = (py + wy) * W + (px + wx);
                        float m = magPtr[idx];
                        if (m < 1e-3f) continue;
                        double contrib = (rdx * dxPtr[idx] + rdy * dyPtr[idx]) / m;
                        if (contrastInvariant) contrib = Math.Abs(contrib);
                        if (contrib > best) best = contrib;
                    }
                if (best >= POINT_PASS) ok++;
            }
            return (double)ok / n;
        }

        #endregion

        #region SIMD Evaluation

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static double EvaluateSimd(
            int px, int py,
            int* rx, int* ry, float* rdx, float* rdy,
            float* dxImg, float* dyImg, float* magImg,
            int imgW, int N,
            float thresh, float greedy,
            bool contrastInvariant = false)
        {
            float sum = 0;
            int earlyN = N / 5;
            float earlyThresh = thresh * (1.0f - greedy);

            if (Avx.IsSupported && N >= 8)
            {
                var vsum = Vector256<float>.Zero;
                var veps = Vector256.Create(0.001f);
                var absMask = Vector256.Create(0x7FFFFFFF).AsSingle();
                float* gDx = stackalloc float[8];
                float* gDy = stackalloc float[8];
                float* gMag = stackalloc float[8];

                int vecN = N & ~7;
                for (int i = 0; i < vecN; i += 8)
                {
                    for (int k = 0; k < 8; k++)
                    {
                        int idx = (py + ry[i + k]) * imgW + (px + rx[i + k]);
                        gDx[k] = dxImg[idx];
                        gDy[k] = dyImg[idx];
                        gMag[k] = magImg[idx];
                    }

                    var vdx = Avx.LoadVector256(gDx);
                    var vdy = Avx.LoadVector256(gDy);
                    var vmag = Avx.LoadVector256(gMag);
                    var vrdx = Avx.LoadVector256(rdx + i);
                    var vrdy = Avx.LoadVector256(rdy + i);

                    var dot = Avx.Add(
                        Avx.Multiply(vrdx, vdx),
                        Avx.Multiply(vrdy, vdy));

                    var mask = Avx.Compare(veps, vmag,
                        FloatComparisonMode.OrderedLessThanSignaling);

                    var invMag = Avx.And(Avx.Reciprocal(vmag), mask);
                    var val = Avx.Multiply(dot, invMag);

                    if (contrastInvariant)
                        val = Avx.And(val, absMask);

                    vsum = Avx.Add(vsum, val);

                    if (i + 8 >= earlyN && i + 8 < vecN)
                    {
                        float partial = HorizontalSum(vsum);
                        if (partial / (i + 8) < earlyThresh) return 0;
                    }
                }

                sum = HorizontalSum(vsum);

                for (int i = vecN; i < N; i++)
                {
                    int idx = (py + ry[i]) * imgW + (px + rx[i]);
                    float m = magImg[idx];
                    if (m > 0.001f)
                    {
                        float contrib = (rdx[i] * dxImg[idx] + rdy[i] * dyImg[idx]) / m;
                        sum += contrastInvariant ? MathF.Abs(contrib) : contrib;
                    }
                }
            }
            else
            {
                for (int i = 0; i < N; i++)
                {
                    int idx = (py + ry[i]) * imgW + (px + rx[i]);
                    float m = magImg[idx];
                    if (m > 0.001f)
                    {
                        float contrib = (rdx[i] * dxImg[idx] + rdy[i] * dyImg[idx]) / m;
                        sum += contrastInvariant ? MathF.Abs(contrib) : contrib;
                    }

                    if (i == earlyN && sum / (i + 1) < earlyThresh)
                        return 0;
                }
            }

            return sum / N;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float HorizontalSum(Vector256<float> v)
        {
            var lo = v.GetLower();
            var hi = v.GetUpper();
            var s = Sse.Add(lo, hi);
            var shuf1 = Sse.MoveHighToLow(s, s);
            s = Sse.Add(s, shuf1);
            var shuf2 = Sse.Shuffle(s, s, 0b_00_00_00_01);
            s = Sse.AddScalar(s, shuf2);
            return s.ToScalar();
        }

        #endregion

        #region LUT Helpers

        private static void PrecomputeFinePosesNative(
            FeatureMatchModel model,
            double centerAngle, double angleRange, double angleStep,
            double centerScale, double scaleRange, double scaleStep,
            out int poseCount)
        {
            var edges = model.ModelEdges;
            int n = edges.Count;

            int count = 0;
            for (double da = -angleRange; da <= angleRange + 0.001; da += angleStep)
                for (double ds = -scaleRange; ds <= scaleRange + 0.001; ds += scaleStep)
                    if (centerScale + ds >= 0.1) count++;

            model.EnsurePoseBufferCapacity(count, n);
            poseCount = count;

            int pi = 0;
            for (double da = -angleRange; da <= angleRange + 0.001; da += angleStep)
            {
                double angle = centerAngle + da;
                double rad = angle * (Math.PI / 180.0);
                double cosA = Math.Cos(rad);
                double sinA = Math.Sin(rad);

                for (double ds = -scaleRange; ds <= scaleRange + 0.001; ds += scaleStep)
                {
                    double scale = centerScale + ds;
                    if (scale < 0.1) continue;

                    int* prx = model.NativeRxBuf + pi * n;
                    int* pry = model.NativeRyBuf + pi * n;
                    float* prdx = model.NativeRdxBuf + pi * n;
                    float* prdy = model.NativeRdyBuf + pi * n;
                    int maxOff = 0;

                    for (int i = 0; i < n; i++)
                    {
                        var p = edges[i];
                        prx[i] = (int)Math.Round((p.X * cosA - p.Y * sinA) * scale);
                        pry[i] = (int)Math.Round((p.X * sinA + p.Y * cosA) * scale);
                        prdx[i] = (float)(p.Dx * cosA - p.Dy * sinA);
                        prdy[i] = (float)(p.Dx * sinA + p.Dy * cosA);

                        int ax = Math.Abs(prx[i]);
                        int ay = Math.Abs(pry[i]);
                        if (ax > maxOff) maxOff = ax;
                        if (ay > maxOff) maxOff = ay;
                    }

                    model.NativeMarginBuf[pi] = maxOff + 1;
                    model.NativeAngleBuf[pi] = angle;
                    model.NativeScaleBuf[pi] = scale;
                    pi++;
                }
            }
        }

        private static double ParabolicPeak(double sMinus, double sCenter, double sPlus)
        {
            double denom = 2.0 * (2.0 * sCenter - sMinus - sPlus);
            if (Math.Abs(denom) < 1e-12) return 0;
            double offset = (sMinus - sPlus) / denom;
            return Math.Clamp(offset, -0.5, 0.5);
        }

        private static double EvaluateSinglePose(
            List<EdgePoint> modelEdges,
            double angle, double scale, int px, int py,
            float* dxPtr, float* dyPtr, float* magPtr,
            int W, int N, float thresh, float greedy, bool ciFlag)
        {
            double rad = angle * (Math.PI / 180.0);
            double cosA = Math.Cos(rad);
            double sinA = Math.Sin(rad);

            int* prx = stackalloc int[N];
            int* pry = stackalloc int[N];
            float* prdx = stackalloc float[N];
            float* prdy = stackalloc float[N];
            int maxOff = 0;

            for (int i = 0; i < N; i++)
            {
                var p = modelEdges[i];
                prx[i] = (int)Math.Round((p.X * cosA - p.Y * sinA) * scale);
                pry[i] = (int)Math.Round((p.X * sinA + p.Y * cosA) * scale);
                prdx[i] = (float)(p.Dx * cosA - p.Dy * sinA);
                prdy[i] = (float)(p.Dx * sinA + p.Dy * cosA);

                int ax = Math.Abs(prx[i]);
                int ay = Math.Abs(pry[i]);
                if (ax > maxOff) maxOff = ax;
                if (ay > maxOff) maxOff = ay;
            }

            if (px < maxOff + 1 || px >= W - maxOff - 1) return 0;

            return EvaluateSimd(px, py, prx, pry, prdx, prdy,
                dxPtr, dyPtr, magPtr, W, N, thresh, greedy, ciFlag);
        }

        #endregion

        #region Helpers

        private Mat PrepareSearchImage(Mat input, out int ox, out int oy)
        {
            if (UseSearchRegion && SearchRegion.Width > 0 && SearchRegion.Height > 0)
            {
                ox = Math.Max(0, SearchRegion.X);
                oy = Math.Max(0, SearchRegion.Y);
                int w = Math.Min(SearchRegion.Width, input.Width - ox);
                int h = Math.Min(SearchRegion.Height, input.Height - oy);
                if (w <= 0 || h <= 0)
                {
                    ox = 0; oy = 0;
                    return new Mat(input, new Rect(0, 0, input.Width, input.Height));
                }
                return new Mat(input, new Rect(ox, oy, w, h));
            }

            ox = 0; oy = 0;
            return new Mat(input, new Rect(0, 0, input.Width, input.Height));
        }

        private Mat DrawOverlay(Mat inputImage,
            IReadOnlyList<(double x, double y, double angle, double scale, double score, FeatureMatchModel model)> instances)
        {
            var overlay = GetColorOverlayBase(inputImage);
            bool multi = instances.Count > 1;

            for (int idx = 0; idx < instances.Count; idx++)
            {
                var (cx, cy, angle, scale, score, model) = instances[idx];
                double cosA = Math.Cos(angle * Math.PI / 180.0);
                double sinA = Math.Sin(angle * Math.PI / 180.0);
                double hw = model.TemplateWidth / 2.0 * scale;
                double hh = model.TemplateHeight / 2.0 * scale;

                Point2d[] corners = { new(-hw, -hh), new(hw, -hh), new(hw, hh), new(-hw, hh) };
                var pts = new Point[4];
                for (int i = 0; i < 4; i++)
                {
                    double rx = corners[i].X * cosA - corners[i].Y * sinA + cx;
                    double ry = corners[i].X * sinA + corners[i].Y * cosA + cy;
                    pts[i] = new Point((int)Math.Round(rx), (int)Math.Round(ry));
                }

                for (int i = 0; i < 4; i++)
                    Cv2.Line(overlay, pts[i], pts[(i + 1) % 4], new Scalar(0, 255, 0), 2);

                // 중심 십자 — 매칭 각도에 맞춰 회전 (축 정렬 고정이라 검출 각도가 십자에
                // 반영되지 않던 현장 보고 2026-08-28). +X 화살표로 방향까지 명시
                // (십자만으로는 90° 배수가 구분되지 않는다).
                int cs = 15;
                var red = new Scalar(0, 0, 255);
                Point Rot(double lx, double ly) => new(
                    (int)Math.Round(lx * cosA - ly * sinA + cx),
                    (int)Math.Round(lx * sinA + ly * cosA + cy));
                Cv2.Line(overlay, Rot(-cs, 0), Rot(cs, 0), red, 2);
                Cv2.Line(overlay, Rot(0, -cs), Rot(0, cs), red, 2);
                Cv2.ArrowedLine(overlay, Rot(0, 0), Rot(cs * 2.2, 0), red, 2, tipLength: 0.3);

                foreach (var edge in model.ModelEdges)
                {
                    double rx = (edge.X * cosA - edge.Y * sinA) * scale + cx;
                    double ry = (edge.X * sinA + edge.Y * cosA) * scale + cy;
                    Cv2.Circle(overlay, new Point((int)Math.Round(rx), (int)Math.Round(ry)),
                        2, new Scalar(0, 255, 0), -1);
                }

                if (multi)
                    Cv2.PutText(overlay, $"#{idx + 1} S={score:F2}",
                        new Point((int)cx + 10, (int)cy - 10),
                        HersheyFonts.HersheySimplex, 0.5, new Scalar(0, 255, 0), 1);
            }

            if (UseSearchRegion && SearchRegion.Width > 0 && SearchRegion.Height > 0)
            {
                Cv2.Rectangle(overlay,
                    new Point(SearchRegion.X, SearchRegion.Y),
                    new Point(SearchRegion.X + SearchRegion.Width, SearchRegion.Y + SearchRegion.Height),
                    new Scalar(0, 255, 255), 2);
            }

            return overlay;
        }

        #endregion

        #region Clone

        public override List<string> GetAvailableResultKeys()
        {
            var keys = new List<string> { "Success", "Score", "CenterX", "CenterY", "Angle", "Scale", "Coverage", "MatchCount" };
            if (MaxInstances > 1)
            {
                int slots = Math.Max(MaxInstances, 4);
                for (int i = 0; i < slots; i++)
                {
                    keys.Add($"Match{i}_Score");
                    keys.Add($"Match{i}_CenterX");
                    keys.Add($"Match{i}_CenterY");
                    keys.Add($"Match{i}_Angle");
                    keys.Add($"Match{i}_Scale");
                    keys.Add($"Match{i}_Coverage");
                }
            }
            return keys;
        }

        public override VisionToolBase Clone()
        {
            var clone = new FeatureMatchTool
            {
                Name = this.Name, ToolType = this.ToolType,
                CannyLow = this.CannyLow, CannyHigh = this.CannyHigh,
                AngleStart = this.AngleStart, AngleExtent = this.AngleExtent, AngleStep = this.AngleStep,
                MinScale = this.MinScale, MaxScale = this.MaxScale, ScaleStep = this.ScaleStep,
                ScoreThreshold = this.ScoreThreshold, NumLevels = this.NumLevels,
                MaxInstances = this.MaxInstances, NmsDistanceFactor = this.NmsDistanceFactor,
                MinCoverage = this.MinCoverage,
                UseAngleJudgment = this.UseAngleJudgment,
                AngleLowerLimit = this.AngleLowerLimit, AngleUpperLimit = this.AngleUpperLimit,
                UseScaleJudgment = this.UseScaleJudgment,
                ScaleLowerLimit = this.ScaleLowerLimit, ScaleUpperLimit = this.ScaleUpperLimit,
                Greediness = this.Greediness, MaxModelPoints = this.MaxModelPoints,
                SearchRegion = this.SearchRegion, UseSearchRegion = this.UseSearchRegion,
                UseContrastInvariant = this.UseContrastInvariant,
                CurvatureWeight = this.CurvatureWeight,
                IsAutoTuneEnabled = this.IsAutoTuneEnabled,
                ReferenceImagePath = this.ReferenceImagePath,
                RetrainOriginMode = this.RetrainOriginMode
            };

            // Deep-copy each model (clone Mat images, rebuild arrays; native buffers allocated on retrain)
            foreach (var model in Models)
            {
                var clonedModel = new FeatureMatchModel
                {
                    Name = model.Name,
                    IsEnabled = model.IsEnabled,
                    TemplateImage = model.TemplateImage?.Clone(),
                    TrainedFeatureImage = model.TrainedFeatureImage?.Clone(),
                    TrainMask = model.TrainMask?.Clone(),
                    TemplateWidth = model.TemplateWidth,
                    TemplateHeight = model.TemplateHeight,
                    TrainedCenterX = model.TrainedCenterX,
                    TrainedCenterY = model.TrainedCenterY,
                    TrainedAngle = model.TrainedAngle,
                    ModelEdges = new List<EdgePoint>(model.ModelEdges),
                    ModelXArray = model.ModelXArray != null ? (float[])model.ModelXArray.Clone() : null,
                    ModelYArray = model.ModelYArray != null ? (float[])model.ModelYArray.Clone() : null,
                    BinOffsets = model.BinOffsets != null ? (int[])model.BinOffsets.Clone() : null,
                    BinIndices = model.BinIndices != null ? (int[])model.BinIndices.Clone() : null
                };

                // Clone GradBinTable
                if (model.GradBinTable != null)
                {
                    clonedModel.GradBinTable = new List<int>[NUM_GRAD_BINS];
                    for (int b = 0; b < NUM_GRAD_BINS; b++)
                        clonedModel.GradBinTable[b] = new List<int>(model.GradBinTable[b]);
                }

                clone.Models.Add(clonedModel);
            }

            if (clone.Models.Count > 0)
                clone.SelectedModel = clone.Models[0];

            CopyPlcMappingsTo(clone);
            return clone;
        }

        #endregion
    }
}
