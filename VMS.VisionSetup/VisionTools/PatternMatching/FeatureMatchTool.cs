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

            private static readonly bool _isAvailable = ProbeNative();
            private static readonly bool _hasTopKExport = ProbeExport("HoughVotingTopKNative");

            public static bool IsAvailable => _isAvailable;

            /// <summary>구버전 DLL(단일 피크 export만)과의 호환 — 없으면 단일 경로 폴백.</summary>
            public static bool HasTopKExport => _hasTopKExport;

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
                }
                else if (UseROI && ROI.Width > 0 && ROI.Height > 0)
                {
                    model.TrainedCenterX = ROI.X + patternImage.Width / 2.0;
                    model.TrainedCenterY = ROI.Y + patternImage.Height / 2.0;
                }
                else
                {
                    model.TrainedCenterX = patternImage.Width / 2.0;
                    model.TrainedCenterY = patternImage.Height / 2.0;
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
                    double coarseA = Math.Max(AngleStep, 4.0);
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
            double density = (double)edgePixels / (gray.Width * gray.Height);

            int maxModelPoints = density < 0.02 ? 100 :
                                 density < 0.05 ? 150 :
                                 density < 0.10 ? 200 : 300;

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

            var (s, mx, my, ma, ms, _) = MatchSingleModel(model, gray, dxPtr, dyPtr, magPtr,
                W, H, 0, 0, pyramidScale, actualLevels, vW, vH, seX, seY, seBin, sei, pool);

            pool.Return(seX);
            pool.Return(seY);
            pool.Return(seBin);
            if (coarseImg != null && coarseImg != gray) coarseImg.Dispose();

            score = s; x = mx; y = my; angle = NormalizeAngle(ma); scale = ms;
            return s > 0;
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

                // ── 4. Iterate all enabled models ──
                double globalBestScore = 0;
                double globalBestX = 0, globalBestY = 0, globalBestAngle = 0, globalBestScale = 1.0;
                FeatureMatchModel? bestModel = null;
                double globalBestVoteVal = 0;

                foreach (var model in enabledModels)
                {
                    var (modelScore, modelX, modelY, modelAngle, modelScale, modelVoteVal) =
                        MatchSingleModel(model, searchGray, dxPtr, dyPtr, magPtr,
                            W, H, offsetX, offsetY, pyramidScale, actualLevels,
                            vW, vH, seX, seY, seBin, searchEdgeCount, pool);

                    if (modelScore > globalBestScore)
                    {
                        globalBestScore = modelScore;
                        globalBestX = modelX;
                        globalBestY = modelY;
                        globalBestAngle = modelAngle;
                        globalBestScale = modelScale;
                        bestModel = model;
                        globalBestVoteVal = modelVoteVal;
                    }
                }

                pool.Return(seX);
                pool.Return(seY);
                pool.Return(seBin);
                if (coarseImg != null && coarseImg != searchGray)
                    coarseImg.Dispose();

                // ── 5. Build result ──
                sw.Stop();
                ExecutionTime = sw.Elapsed.TotalMilliseconds;
                LastMatchedModel = bestModel;

                if (globalBestScore >= ScoreThreshold && bestModel != null)
                {
                    double finalX = globalBestX + offsetX;
                    double finalY = globalBestY + offsetY;

                    // 정련이 코스 최적각 ±코스스텝을 탐색하므로 360° 검색(Start -180, Extent 360)
                    // 경계에서 ±180 밖 각도(예: 183°)가 나올 수 있다 — 보고/판정 전 정규화.
                    // 미정규화 시 각도 판정(허용 -180~180)이 경계 근처 정상품을 오탐 NG 처리.
                    globalBestAngle = NormalizeAngle(globalBestAngle);

                    result.Success = true;
                    string modelInfo = Models.Count > 1 ? $", Model={bestModel.Name}" : "";
                    result.Message = $"Score={globalBestScore:F3}, Pos=({finalX:F1},{finalY:F1}), Angle={globalBestAngle:F2}, Scale={globalBestScale:F3}{modelInfo}";
                    result.Data["Score"] = globalBestScore;
                    result.Data["CenterX"] = finalX;
                    result.Data["CenterY"] = finalY;
                    result.Data["Angle"] = globalBestAngle;
                    result.Data["Scale"] = globalBestScale;
                    result.Data["MatchedModel"] = bestModel.Name;
                    result.Data["TrainedCenterX"] = bestModel.TrainedCenterX;
                    result.Data["TrainedCenterY"] = bestModel.TrainedCenterY;
                    result.OverlayImage = DrawOverlay(inputImage, finalX, finalY, globalBestAngle, globalBestScale,
                        bestModel.TemplateWidth, bestModel.TemplateHeight, bestModel.ModelEdges);

                    // 각도/스케일 판정 — 매칭은 찾았으나 자세가 허용 범위를 벗어나면 NG.
                    // Data/오버레이는 그대로 남겨 측정값 확인·Web 업로드가 가능하게 한다.
                    var poseNg = EvaluatePoseJudgment(globalBestAngle, globalBestScale);
                    if (poseNg != null)
                    {
                        result.Success = false;
                        result.Message += " — " + poseNg;
                    }
                }
                else
                {
                    result.Success = false;
                    result.Message = $"패턴을 찾지 못했습니다. (최대 Score={globalBestScore:F3}, Votes={globalBestVoteVal})";
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
        /// Run matching for a single model and return its best result.
        /// </summary>
        private (double score, double x, double y, double angle, double scale, double voteVal)
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
            // 정련(점수) 단계에서 판가름 난다. 분리 반경은 템플릿 절반 크기(투표 px).
            const int TOP_CANDIDATES = 3;
            var voteCands = new List<(double cx, double cy, double angle, int votes)>(TOP_CANDIDATES);
            double minSeparation = Math.Max(4.0,
                Math.Min(model.TemplateWidth, model.TemplateHeight) * 0.5 * invScale);

            if (NativeVision.IsAvailable && model.ModelXArray != null && model.ModelYArray != null)
            {
                fixed (float* pModelX = model.ModelXArray, pModelY = model.ModelYArray)
                fixed (int* pBinOffsets = binOffsets, pBinIndices = binIndices)
                fixed (int* pSeX = seX, pSeY = seY, pSeBin = seBin)
                {
                    if (NativeVision.HasTopKExport)
                    {
                        double* oCx = stackalloc double[TOP_CANDIDATES];
                        double* oCy = stackalloc double[TOP_CANDIDATES];
                        double* oAng = stackalloc double[TOP_CANDIDATES];
                        int* oVotes = stackalloc int[TOP_CANDIDATES];
                        int filled = NativeVision.HoughVotingTopKNative(
                            pModelX, pModelY, N,
                            pBinOffsets, pBinIndices, NUM_GRAD_BINS,
                            pSeX, pSeY, pSeBin, searchEdgeCount,
                            vW, vH,
                            AngleStart, AngleExtent,
                            coarseAngleStep, fineVoteAngleStep, 5,
                            invScale, BIN_SHIFT,
                            minSeparation, TOP_CANDIDATES,
                            oCx, oCy, oAng, oVotes);
                        for (int i = 0; i < filled; i++)
                            voteCands.Add((oCx[i], oCy[i], oAng[i], oVotes[i]));
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
                            invScale, BIN_SHIFT,
                            &outCx, &outCy, &outAngle, &outVotes);
                        voteCands.Add((outCx, outCy, outAngle, outVotes));
                    }
                }
            }
            else
            {
                // C# fallback with multi-resolution angle search
                int bW = (vW >> BIN_SHIFT) + 1;
                int bH = (vH >> BIN_SHIFT) + 1;
                int accLen = bW * bH;
                int numCoarseAngles = Math.Max(1, (int)(AngleExtent / coarseAngleStep) + 1);
                const int TopK = 5;

                var coarseCandidates = new (double angle, double cx, double cy, int votes)[TopK];
                object lockObj = new();
                var modelEdges = model.ModelEdges;

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
                        rotX[i] = (int)Math.Round((mp.X * cosA - mp.Y * sinA) * invScale);
                        rotY[i] = (int)Math.Round((mp.X * sinA + mp.Y * cosA) * invScale);
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

                    int maxVote = 0, maxIdx = 0;
                    for (int i = 0; i < accLen; i++)
                    {
                        if (acc[i] > maxVote) { maxVote = acc[i]; maxIdx = i; }
                    }

                    pool.Return(rotX);
                    pool.Return(rotY);
                    pool.Return(acc);

                    double peakCx = (maxIdx % bW) * (1 << BIN_SHIFT) + (1 << BIN_SHIFT) / 2;
                    double peakCy = (maxIdx / bW) * (1 << BIN_SHIFT) + (1 << BIN_SHIFT) / 2;

                    lock (lockObj)
                    {
                        if (maxVote > coarseCandidates[TopK - 1].votes)
                        {
                            coarseCandidates[TopK - 1] = (angle, peakCx, peakCy, maxVote);
                            for (int k = TopK - 1; k > 0 && coarseCandidates[k].votes > coarseCandidates[k - 1].votes; k--)
                                (coarseCandidates[k], coarseCandidates[k - 1]) = (coarseCandidates[k - 1], coarseCandidates[k]);
                        }
                    }
                });

                // Fine pass — 모든 파인 결과를 모아 두었다가 NMS 로 상위 K개 선별
                var fineAll = new List<(double cx, double cy, double angle, int votes)>();
                foreach (var cand in coarseCandidates)
                {
                    if (cand.votes == 0) continue;
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
                            rotX[i] = (int)Math.Round((mp.X * cosA - mp.Y * sinA) * invScale);
                            rotY[i] = (int)Math.Round((mp.X * sinA + mp.Y * cosA) * invScale);
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

                        int maxVote = 0, maxIdx = 0;
                        for (int i = 0; i < accLen; i++)
                        {
                            if (acc[i] > maxVote) { maxVote = acc[i]; maxIdx = i; }
                        }

                        pool.Return(rotX);
                        pool.Return(rotY);
                        pool.Return(acc);

                        double peakCx = (maxIdx % bW) * (1 << BIN_SHIFT) + (1 << BIN_SHIFT) / 2;
                        double peakCy = (maxIdx / bW) * (1 << BIN_SHIFT) + (1 << BIN_SHIFT) / 2;

                        lock (lockObj)
                        {
                            fineAll.Add((peakCx, peakCy, angle, maxVote));
                        }
                    });
                }

                voteCands.AddRange(SelectTopKCandidates(fineAll, TOP_CANDIDATES, minSeparation));
                if (voteCands.Count == 0)
                    voteCands.Add((coarseCandidates[0].cx, coarseCandidates[0].cy,
                        coarseCandidates[0].angle, coarseCandidates[0].votes));
            }

            double bestVoteVal = 0;
            foreach (var vc in voteCands)
                if (vc.votes > bestVoteVal) bestVoteVal = vc.votes;

            // ── Phase 2: 후보별 SIMD gradient dot-product refinement — 최고 점수 채택 ──
            double fineAngleStep = Math.Max(0.1, AngleStep / 2.0);
            double fineScaleStep = Math.Max(0.001, ScaleStep);
            double scaleCenter = (MinScale + MaxScale) / 2.0;
            double scaleRange = (MaxScale - MinScale) / 2.0;

            double bestScore = 0, bestX = 0, bestY = 0, bestAngle = 0, bestScale = 1.0;
            if (voteCands.Count > 0)
            {
                bestX = voteCands[0].cx * pyramidScale;
                bestY = voteCands[0].cy * pyramidScale;
                bestAngle = voteCands[0].angle;
            }

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

            foreach (var vc in voteCands)
            {
                // 최고 득표 대비 미미한 후보는 정련 생략 (비용 절약)
                if (vc.votes < bestVoteVal * 0.25) continue;

                double candCx = vc.cx * pyramidScale;
                double candCy = vc.cy * pyramidScale;

                int poseCount;
                PrecomputeFinePosesNative(
                    model,
                    vc.angle, coarseAngleStep, fineAngleStep,
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
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestX = (int)candCx + bestDx;
                        bestY = (int)candCy + bestDy;
                        bestAngle = model.NativeAngleBuf[bestPoseIdx];
                        bestScale = model.NativeScaleBuf[bestPoseIdx];
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
                                if (score > bestScore)
                                {
                                    bestScore = score;
                                    bestX = px; bestY = py;
                                    bestAngle = model.NativeAngleBuf[pi];
                                    bestScale = model.NativeScaleBuf[pi];
                                }
                            }
                        }
                    }
                }
            }

            // Sub-pixel parabolic refinement
            if (bestScore >= ScoreThreshold)
            {
                int bxi = (int)bestX, byi = (int)bestY;

                double sxm = EvaluateSinglePose(model.ModelEdges, bestAngle, bestScale, bxi - 1, byi, dxPtr, dyPtr, magPtr, W, N, thresh, greedy, ciFlag);
                double sxp = EvaluateSinglePose(model.ModelEdges, bestAngle, bestScale, bxi + 1, byi, dxPtr, dyPtr, magPtr, W, N, thresh, greedy, ciFlag);
                bestX = bxi + ParabolicPeak(sxm, bestScore, sxp);

                double sym = EvaluateSinglePose(model.ModelEdges, bestAngle, bestScale, bxi, byi - 1, dxPtr, dyPtr, magPtr, W, N, thresh, greedy, ciFlag);
                double syp = EvaluateSinglePose(model.ModelEdges, bestAngle, bestScale, bxi, byi + 1, dxPtr, dyPtr, magPtr, W, N, thresh, greedy, ciFlag);
                bestY = byi + ParabolicPeak(sym, bestScore, syp);

                double sam = EvaluateSinglePose(model.ModelEdges, bestAngle - fineAngleStep, bestScale, bxi, byi, dxPtr, dyPtr, magPtr, W, N, thresh, greedy, ciFlag);
                double sap = EvaluateSinglePose(model.ModelEdges, bestAngle + fineAngleStep, bestScale, bxi, byi, dxPtr, dyPtr, magPtr, W, N, thresh, greedy, ciFlag);
                bestAngle += ParabolicPeak(sam, bestScore, sap) * fineAngleStep;

                double ssm = EvaluateSinglePose(model.ModelEdges, bestAngle, bestScale - fineScaleStep, bxi, byi, dxPtr, dyPtr, magPtr, W, N, thresh, greedy, ciFlag);
                double ssp = EvaluateSinglePose(model.ModelEdges, bestAngle, bestScale + fineScaleStep, bxi, byi, dxPtr, dyPtr, magPtr, W, N, thresh, greedy, ciFlag);
                bestScale += ParabolicPeak(ssm, bestScore, ssp) * fineScaleStep;
            }

            return (bestScore, bestX, bestY, bestAngle, bestScale, bestVoteVal);
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

        private Mat DrawOverlay(Mat inputImage, double cx, double cy, double angle, double scale,
            int templateWidth, int templateHeight, List<EdgePoint> modelEdges)
        {
            var overlay = GetColorOverlayBase(inputImage);
            double cosA = Math.Cos(angle * Math.PI / 180.0);
            double sinA = Math.Sin(angle * Math.PI / 180.0);
            double hw = templateWidth / 2.0 * scale;
            double hh = templateHeight / 2.0 * scale;

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

            foreach (var edge in modelEdges)
            {
                double rx = (edge.X * cosA - edge.Y * sinA) * scale + cx;
                double ry = (edge.X * sinA + edge.Y * cosA) * scale + cy;
                Cv2.Circle(overlay, new Point((int)Math.Round(rx), (int)Math.Round(ry)),
                    2, new Scalar(0, 255, 0), -1);
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
            return new List<string> { "Success", "Score", "CenterX", "CenterY", "Angle", "Scale" };
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
                UseAngleJudgment = this.UseAngleJudgment,
                AngleLowerLimit = this.AngleLowerLimit, AngleUpperLimit = this.AngleUpperLimit,
                UseScaleJudgment = this.UseScaleJudgment,
                ScaleLowerLimit = this.ScaleLowerLimit, ScaleUpperLimit = this.ScaleUpperLimit,
                Greediness = this.Greediness, MaxModelPoints = this.MaxModelPoints,
                SearchRegion = this.SearchRegion, UseSearchRegion = this.UseSearchRegion,
                UseContrastInvariant = this.UseContrastInvariant,
                CurvatureWeight = this.CurvatureWeight,
                IsAutoTuneEnabled = this.IsAutoTuneEnabled,
                ReferenceImagePath = this.ReferenceImagePath
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
