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

            private static readonly bool _isAvailable = ProbeNative();

            public static bool IsAvailable => _isAvailable;

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
        /// </summary>
        public bool TrainPattern(Mat patternImage, FeatureMatchModel? targetModel = null)
        {
            try
            {
                var model = targetModel;
                bool isNew = model == null;

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

                model!.TemplateImage = patternImage.Clone();
                model.TemplateWidth = patternImage.Width;
                model.TemplateHeight = patternImage.Height;

                if (UseROI && ROI.Width > 0 && ROI.Height > 0)
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

                for (int y = 1; y < gray.Height - 1; y++)
                    for (int x = 1; x < gray.Width - 1; x++)
                    {
                        if (edges.At<byte>(y, x) == 0) continue;
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

                using var voteEdges = voteImg.Canny(CannyLow, CannyHigh);
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
                            new Scalar(255, 255, 0), 2);
                        result.OverlayImage = overlay;
                    }
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"실행 오류: {ex.Message}";
            }

            return result;
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
            double bestVoteVal = 0, bestVoteCx = 0, bestVoteCy = 0, bestVoteAngle = 0;

            int[] binOffsets = model.BinOffsets!;
            int[] binIndices = model.BinIndices!;

            const int BIN_SHIFT = 1;
            double invScale = 1.0 / pyramidScale;

            if (NativeVision.IsAvailable && model.ModelXArray != null && model.ModelYArray != null)
            {
                fixed (float* pModelX = model.ModelXArray, pModelY = model.ModelYArray)
                fixed (int* pBinOffsets = binOffsets, pBinIndices = binIndices)
                fixed (int* pSeX = seX, pSeY = seY, pSeBin = seBin)
                {
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
                    bestVoteCx = outCx;
                    bestVoteCy = outCy;
                    bestVoteAngle = outAngle;
                    bestVoteVal = outVotes;
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

                // Fine pass
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
                            if (maxVote > bestVoteVal)
                            {
                                bestVoteVal = maxVote;
                                bestVoteCx = peakCx;
                                bestVoteCy = peakCy;
                                bestVoteAngle = angle;
                            }
                        }
                    });
                }
            }

            // Map coarse-level coordinates back to full resolution
            bestVoteCx *= pyramidScale;
            bestVoteCy *= pyramidScale;

            // ── Phase 2: SIMD gradient dot-product refinement ──
            double fineAngleStep = Math.Max(0.1, AngleStep / 2.0);
            double fineScaleStep = Math.Max(0.001, ScaleStep);
            double scaleCenter = (MinScale + MaxScale) / 2.0;
            double scaleRange = (MaxScale - MinScale) / 2.0;

            int poseCount;
            PrecomputeFinePosesNative(
                model,
                bestVoteAngle, coarseAngleStep, fineAngleStep,
                scaleCenter, scaleRange, fineScaleStep,
                out poseCount);

            double bestScore = 0, bestX = bestVoteCx, bestY = bestVoteCy;
            double bestAngle = bestVoteAngle, bestScale = 1.0;
            int refRadius = actualLevels > 1
                ? Math.Max(4, (int)pyramidScale + 2)
                : 4;
            float thresh = (float)ScoreThreshold;
            float greedy = (float)Greediness;
            bool ciFlag = UseContrastInvariant;

            if (NativeVision.IsAvailable && poseCount > 0)
            {
                int bestDx, bestDy, bestPoseIdx;
                double score = NativeVision.EvaluateAllPosesNative(
                    (int)bestVoteCx, (int)bestVoteCy, refRadius,
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
                    bestX = (int)bestVoteCx + bestDx;
                    bestY = (int)bestVoteCy + bestDy;
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
                        int py = (int)bestVoteCy + dy;
                        if (py < fm || py >= H - fm) continue;
                        for (int dx = -refRadius; dx <= refRadius; dx++)
                        {
                            int px = (int)bestVoteCx + dx;
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

            int cs = 15;
            Cv2.Line(overlay, new Point((int)cx - cs, (int)cy), new Point((int)cx + cs, (int)cy), new Scalar(0, 0, 255), 2);
            Cv2.Line(overlay, new Point((int)cx, (int)cy - cs), new Point((int)cx, (int)cy + cs), new Scalar(0, 0, 255), 2);

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
                    new Scalar(255, 255, 0), 2);
            }

            return overlay;
        }

        #endregion

        #region Clone

        public override VisionToolBase Clone()
        {
            var clone = new FeatureMatchTool
            {
                Name = this.Name, ToolType = this.ToolType,
                CannyLow = this.CannyLow, CannyHigh = this.CannyHigh,
                AngleStart = this.AngleStart, AngleExtent = this.AngleExtent, AngleStep = this.AngleStep,
                MinScale = this.MinScale, MaxScale = this.MaxScale, ScaleStep = this.ScaleStep,
                ScoreThreshold = this.ScoreThreshold, NumLevels = this.NumLevels,
                Greediness = this.Greediness, MaxModelPoints = this.MaxModelPoints,
                SearchRegion = this.SearchRegion, UseSearchRegion = this.UseSearchRegion,
                UseContrastInvariant = this.UseContrastInvariant,
                CurvatureWeight = this.CurvatureWeight,
                IsAutoTuneEnabled = this.IsAutoTuneEnabled
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

            return clone;
        }

        #endregion
    }
}
