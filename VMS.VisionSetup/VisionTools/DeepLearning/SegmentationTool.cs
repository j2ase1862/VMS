using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using VMS.VisionSetup.Models;

namespace VMS.VisionSetup.VisionTools.DeepLearning
{
    /// <summary>
    /// Semantic Segmentation 도구 (Cognex ViDi Blue Locate / Red Supervised 대응 Phase 1).
    /// ONNX 모델 입력 [1,3,H,W], 출력 [1,C,H,W] logits 가정 — U-Net, DeepLabV3, SegFormer 등.
    /// 후처리: argmax → 라벨맵 → 클래스별 픽셀 카운트 → 컬러 오버레이.
    /// (인스턴스 분할은 향후 Phase 2에서 YOLOv8-seg로 별도 도구화 예정)
    /// </summary>
    public class SegmentationTool : VisionToolBase
    {
        private string _modelPath = string.Empty;
        public string ModelPath
        {
            get => _modelPath;
            set
            {
                if (SetProperty(ref _modelPath, value))
                    EnsureMetadataLoaded();
            }
        }

        private int _inputSize = 512;
        public int InputSize
        {
            get => _inputSize;
            set => SetProperty(ref _inputSize, Math.Max(32, value));
        }

        private bool _useImageNetNormalization = true;
        public bool UseImageNetNormalization
        {
            get => _useImageNetNormalization;
            set => SetProperty(ref _useImageNetNormalization, value);
        }

        private bool _showOverlay = true;
        public bool ShowOverlay
        {
            get => _showOverlay;
            set => SetProperty(ref _showOverlay, value);
        }

        private double _overlayOpacity = 0.4;
        public double OverlayOpacity
        {
            get => _overlayOpacity;
            set => SetProperty(ref _overlayOpacity, Math.Clamp(value, 0.0, 1.0));
        }

        private int _backgroundClass;
        public int BackgroundClass
        {
            get => _backgroundClass;
            set => SetProperty(ref _backgroundClass, Math.Max(0, value));
        }

        private string[] _classNames = Array.Empty<string>();
        public string[] ClassNames
        {
            get => _classNames;
            private set => SetProperty(ref _classNames, value);
        }

        private SegmentationOnnxEngine? _engine;

        public SegmentationTool()
        {
            Name = "Segmentation";
            ToolType = "SegmentationTool";
        }

        private void EnsureMetadataLoaded()
        {
            if (string.IsNullOrEmpty(ModelPath) || !System.IO.File.Exists(ModelPath)) return;
            try
            {
                var names = OnnxModelBase.ReadClassNamesFromFile(ModelPath);
                if (names != null && names.Length > 0)
                    ClassNames = names;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SegmentationTool] Metadata load failed: {ex.Message}");
            }
        }

        private void EnsureEngine()
        {
            _engine ??= OnnxEngineCache.GetSegmentation(ModelPath, InputSize, UseImageNetNormalization);
        }

        public override VisionResult Execute(Mat inputImage)
        {
            var result = new VisionResult();
            var sw = Stopwatch.StartNew();

            try
            {
                if (string.IsNullOrEmpty(ModelPath) || !System.IO.File.Exists(ModelPath))
                {
                    result.Success = false;
                    result.Message = "Model path not set or file missing.";
                    return result;
                }

                using var roi = GetROIImage(inputImage);
                EnsureEngine();

                var segResult = _engine!.Segment(roi, InputSize, InputSize, UseImageNetNormalization);
                if (segResult.LabelMap == null || segResult.LabelMap.Empty())
                {
                    result.Success = false;
                    result.Message = "Inference produced empty label map.";
                    return result;
                }

                // 클래스 이름이 모델 메타에 있으면 갱신
                if (segResult.ClassNamesFromModel?.Length > 0 && ClassNames.Length == 0)
                    ClassNames = segResult.ClassNamesFromModel;

                // 결과 데이터
                int numClasses = segResult.NumClasses;
                result.Data["NumClasses"] = numClasses;
                result.Data["LabelMap"] = segResult.LabelMap;

                long totalPx = segResult.LabelMap.Width * (long)segResult.LabelMap.Height;
                long foregroundPx = 0;
                for (int c = 0; c < numClasses; c++)
                {
                    long count = segResult.ClassPixelCounts != null && c < segResult.ClassPixelCounts.Length
                        ? segResult.ClassPixelCounts[c]
                        : 0;
                    string name = c < ClassNames.Length ? ClassNames[c] : $"Class{c}";
                    result.Data[$"Class{c}_Name"] = name;
                    result.Data[$"Class{c}_Pixels"] = count;
                    result.Data[$"Class{c}_AreaRatio"] = totalPx > 0 ? (double)count / totalPx : 0.0;
                    if (c != BackgroundClass) foregroundPx += count;
                }
                result.Data["ForegroundPixels"] = foregroundPx;
                result.Data["ForegroundRatio"] = totalPx > 0 ? (double)foregroundPx / totalPx : 0.0;
                result.Data["ExecutionProvider"] = _engine.ActiveProvider;

                // 오버레이
                Mat overlay = ShowOverlay
                    ? BuildOverlay(inputImage, segResult.LabelMap, numClasses)
                    : GetColorOverlayBase(inputImage);

                result.OutputImage = segResult.LabelMap.Clone();
                result.OverlayImage = overlay;
                result.Success = true;
                result.Message = $"Segmented {numClasses} classes, foreground {foregroundPx}/{totalPx} px";
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"Segmentation failed: {ex.Message}";
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
        /// 라벨맵 위에 클래스별 컬러를 알파 블렌딩.
        /// 배경 클래스는 투명 유지.
        /// </summary>
        private Mat BuildOverlay(Mat inputImage, Mat labelMap, int numClasses)
        {
            // 입력 크기에 맞춰 라벨맵 리사이즈 (이미 맞으면 그대로)
            using var resizedLabel = labelMap.Size() == inputImage.Size()
                ? labelMap.Clone()
                : labelMap.Resize(inputImage.Size(), 0, 0, InterpolationFlags.Nearest);

            var baseImg = GetColorOverlayBase(inputImage);
            using var colorMap = new Mat(resizedLabel.Size(), MatType.CV_8UC3, Scalar.All(0));

            var palette = BuildPalette(numClasses);
            unsafe
            {
                byte* lbl = (byte*)resizedLabel.Data;
                byte* col = (byte*)colorMap.Data;
                int total = resizedLabel.Width * resizedLabel.Height;
                for (int i = 0; i < total; i++)
                {
                    byte cls = lbl[i];
                    if (cls == BackgroundClass) continue;
                    var c = palette[cls % palette.Length];
                    col[i * 3 + 0] = c.B;
                    col[i * 3 + 1] = c.G;
                    col[i * 3 + 2] = c.R;
                }
            }

            // 알파 블렌딩: result = (1-α)·base + α·color, 단 컬러가 0인 픽셀(배경)은 유지
            using var mask = new Mat();
            Cv2.CvtColor(colorMap, mask, ColorConversionCodes.BGR2GRAY);
            Cv2.Threshold(mask, mask, 0, 255, ThresholdTypes.Binary);

            var blended = new Mat();
            Cv2.AddWeighted(baseImg, 1.0 - OverlayOpacity, colorMap, OverlayOpacity, 0, blended);
            // mask로 컬러 부분만 blended 사용, 나머지는 baseImg 유지
            using var inv = new Mat();
            Cv2.BitwiseNot(mask, inv);
            baseImg.CopyTo(blended, inv);
            baseImg.Dispose();
            return blended;
        }

        private static (byte R, byte G, byte B)[] BuildPalette(int n)
        {
            // 결정론적 HSV → BGR 팔레트 (클래스 인덱스로 안정적인 색)
            var palette = new (byte, byte, byte)[Math.Max(n, 1)];
            for (int i = 0; i < palette.Length; i++)
            {
                double hue = (i * 47.0) % 180.0; // OpenCV는 H가 0~179
                using var hsv = new Mat(1, 1, MatType.CV_8UC3, new Scalar(hue, 220, 240));
                using var bgr = new Mat();
                Cv2.CvtColor(hsv, bgr, ColorConversionCodes.HSV2BGR);
                var p = bgr.Get<Vec3b>(0, 0);
                palette[i] = (p.Item2, p.Item1, p.Item0); // BGR → RGB 저장
            }
            return palette;
        }

        public override List<string> GetAvailableResultKeys()
        {
            var keys = new List<string>
            {
                "Success", "NumClasses", "ForegroundPixels", "ForegroundRatio", "ExecutionProvider"
            };
            // 클래스별 키 (모델 로드 후 ClassNames에 의해 결정)
            int maxToList = ClassNames.Length > 0 ? ClassNames.Length : 16;
            for (int i = 0; i < maxToList; i++)
            {
                keys.Add($"Class{i}_Name");
                keys.Add($"Class{i}_Pixels");
                keys.Add($"Class{i}_AreaRatio");
            }
            return keys;
        }

        public override VisionToolBase Clone()
        {
            var clone = new SegmentationTool
            {
                Name = this.Name,
                ToolType = this.ToolType,
                IsEnabled = this.IsEnabled,
                ROI = this.ROI,
                UseROI = this.UseROI,
                ROIAngle = this.ROIAngle,
                ROICenterX = this.ROICenterX,
                ROICenterY = this.ROICenterY,
                ModelPath = this.ModelPath,
                InputSize = this.InputSize,
                UseImageNetNormalization = this.UseImageNetNormalization,
                ShowOverlay = this.ShowOverlay,
                OverlayOpacity = this.OverlayOpacity,
                BackgroundClass = this.BackgroundClass
            };
            CopyPlcMappingsTo(clone);
            return clone;
        }
    }

    /// <summary>
    /// Semantic Segmentation ONNX 추론 엔진.
    /// 입력: [1,3,H,W] 정규화 RGB. 출력: [1,C,H,W] logits.
    /// </summary>
    public class SegmentationOnnxEngine : OnnxModelBase
    {
        public SegmentationOnnxEngine(string modelPath)
        {
            LoadModel(modelPath);
        }

        public string[] GetClassNames() => ReadClassNamesFromMetadata();

        public SegmentationInferenceResult Segment(Mat image, int inputW, int inputH, bool useImageNet)
        {
            if (_session == null) return new SegmentationInferenceResult();

            var tensor = useImageNet
                ? PreprocessImageNet(image, inputW, inputH)
                : PreprocessImageSimple(image, inputW, inputH);

            var inputs = CreateInput(GetInputName(), tensor);
            using var outputs = _session.Run(inputs);
            var output = outputs.First().AsTensor<float>();

            var dims = output.Dimensions;
            if (dims.Length != 4)
                throw new InvalidOperationException(
                    $"Expected output rank 4 [1,C,H,W], got rank {dims.Length}. Use a semantic segmentation model with logits output.");

            int outC = dims[1];
            int outH = dims[2];
            int outW = dims[3];

            // Argmax over channels → 라벨맵 + 클래스 카운트
            var labelMap = new Mat(outH, outW, MatType.CV_8UC1);
            var labelData = new byte[outH * outW];
            var counts = new long[outC];

            // Tensor 인덱서 호출이 느려서 RawData 직접 접근 (NCHW float32)
            var raw = output.ToArray(); // [C*H*W] 평탄화 (배치=1 가정)
            int planeSize = outH * outW;
            for (int y = 0; y < outH; y++)
            {
                for (int x = 0; x < outW; x++)
                {
                    int hwIdx = y * outW + x;
                    float maxScore = raw[hwIdx]; // c=0
                    int maxClass = 0;
                    for (int c = 1; c < outC; c++)
                    {
                        float v = raw[c * planeSize + hwIdx];
                        if (v > maxScore) { maxScore = v; maxClass = c; }
                    }
                    labelData[hwIdx] = (byte)maxClass;
                    counts[maxClass]++;
                }
            }
            Marshal.Copy(labelData, 0, labelMap.Data, labelData.Length);

            // 입력 이미지 크기로 리사이즈 (nearest로 라벨 보존)
            var resized = new Mat();
            Cv2.Resize(labelMap, resized, image.Size(), 0, 0, InterpolationFlags.Nearest);
            labelMap.Dispose();

            return new SegmentationInferenceResult
            {
                LabelMap = resized,
                ClassPixelCounts = counts,
                NumClasses = outC,
                ClassNamesFromModel = ReadClassNamesFromMetadata()
            };
        }
    }

    public class SegmentationInferenceResult
    {
        public Mat? LabelMap { get; set; }
        public long[]? ClassPixelCounts { get; set; }
        public int NumClasses { get; set; }
        public string[]? ClassNamesFromModel { get; set; }
    }
}
