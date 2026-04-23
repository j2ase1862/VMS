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
    /// Cognex ViDi Green Classify 대응 — ONNX 기반 이미지 분류 도구.
    /// ResNet, MobileNet 등 분류 모델의 ONNX 파일을 로드하여 이미지를 분류합니다.
    /// </summary>
    public partial class ClassifyTool : VisionToolBase
    {
        private ClassifierOnnxEngine? _engine;
        private readonly object _engineLock = new();

        // ── Parameters ──

        [ObservableProperty]
        private string _modelPath = string.Empty;

        [ObservableProperty]
        private int _inputWidth = 224;

        [ObservableProperty]
        private int _inputHeight = 224;

        [ObservableProperty]
        private double _confidenceThreshold = 0.5;

        [ObservableProperty]
        private string _classNamesText = string.Empty;

        [ObservableProperty]
        private bool _drawOverlay = true;

        [ObservableProperty]
        private bool _useImageNetNormalization = true;

        /// <summary>ONNX 메타데이터에서 로드된 클래스 목록</summary>
        [ObservableProperty]
        private ObservableCollection<string> _modelClassNames = new();

        public ClassifyTool()
        {
            Name = "Classify";
            ToolType = "ClassifyTool";
        }

        partial void OnModelPathChanged(string value)
        {
            // 캐시가 엔진 생명주기를 관리한다 — 이 도구는 Dispose하지 않는다.
            lock (_engineLock)
            {
                _engine = null;
            }
            LoadModelMetadata(value);
            OnnxEngineCache.PrefetchClassifier(value, InputWidth, InputHeight, UseImageNetNormalization);
        }

        private ClassifierOnnxEngine EnsureEngine()
        {
            lock (_engineLock)
            {
                if (_engine != null) return _engine;
            }

            var engine = OnnxEngineCache.GetClassifier(ModelPath, InputWidth, InputHeight, UseImageNetNormalization);

            lock (_engineLock)
            {
                _engine = engine;
                return engine;
            }
        }

        private void LoadModelMetadata(string modelPath)
        {
            ModelClassNames.Clear();
            if (string.IsNullOrEmpty(modelPath) || !System.IO.File.Exists(modelPath))
                return;

            try
            {
                // InferenceSession 생성 없이 ONNX 파일에서 메타데이터만 직접 파싱한다.
                var names = OnnxModelBase.ReadClassNamesFromFile(modelPath);
                if (names.Length > 0)
                {
                    foreach (var name in names)
                        ModelClassNames.Add(name);
                    ClassNamesText = string.Join(", ", names);
                }
            }
            catch { }
        }

        public string[] ClassNames => string.IsNullOrWhiteSpace(ClassNamesText)
            ? Array.Empty<string>()
            : ClassNamesText.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        public override VisionResult Execute(Mat inputImage)
        {
            var result = new VisionResult();

            try
            {
                if (string.IsNullOrEmpty(ModelPath))
                {
                    result.Success = false;
                    result.Message = "모델 경로를 지정하세요.";
                    return result;
                }

                var roiImage = UseROI ? GetROIImage(inputImage) : inputImage;

                var engine = EnsureEngine();
                var classResult = engine.Classify(roiImage, InputWidth, InputHeight, UseImageNetNormalization);

                string className = classResult.ClassId < ClassNames.Length
                    ? ClassNames[classResult.ClassId]
                    : $"class{classResult.ClassId}";

                bool pass = classResult.Confidence >= ConfidenceThreshold;

                result.Success = pass;
                result.Message = $"{className} ({classResult.Confidence:P1})";
                result.Data["ClassName"] = className;
                result.Data["ClassId"] = classResult.ClassId;
                result.Data["Confidence"] = classResult.Confidence;
                result.Data["TopN"] = classResult.TopN;

                if (DrawOverlay)
                {
                    var overlay = GetColorOverlayBase(inputImage);
                    var color = pass ? new Scalar(0, 255, 0) : new Scalar(0, 0, 255);
                    string text = $"{className}: {classResult.Confidence:P1}";

                    Cv2.PutText(overlay, text,
                        new Point(10, 30),
                        HersheyFonts.HersheySimplex, 1.0, color, 2);

                    // 전체 이미지 테두리로 판정 표시
                    Cv2.Rectangle(overlay,
                        new Rect(0, 0, overlay.Width, overlay.Height),
                        color, 4);

                    result.OverlayImage = overlay;

                    result.Graphics.Add(new GraphicOverlay
                    {
                        Type = GraphicType.Text,
                        Position = new Point2d(10, 30),
                        Text = text,
                        Color = color
                    });
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"Classification 오류: {ex.Message}";
            }

            return result;
        }

        public override VisionToolBase Clone()
        {
            return new ClassifyTool
            {
                Name = this.Name,
                ModelPath = this.ModelPath,
                InputWidth = this.InputWidth,
                InputHeight = this.InputHeight,
                ConfidenceThreshold = this.ConfidenceThreshold,
                ClassNamesText = this.ClassNamesText,
                DrawOverlay = this.DrawOverlay,
                UseImageNetNormalization = this.UseImageNetNormalization,
                UseROI = this.UseROI,
                ROI = this.ROI
            };
        }

        public override List<string> GetAvailableResultKeys()
        {
            return new List<string> { "Success", "ClassName", "ClassId", "Confidence" };
        }
    }

    /// <summary>
    /// 분류 결과
    /// </summary>
    public class ClassificationResult
    {
        public int ClassId { get; set; }
        public float Confidence { get; set; }
        public List<(int ClassId, float Score)> TopN { get; set; } = new();
    }

    /// <summary>
    /// 분류 ONNX 추론 엔진
    /// </summary>
    public class ClassifierOnnxEngine : OnnxModelBase
    {
        public ClassifierOnnxEngine(string modelPath)
        {
            LoadModel(modelPath);
        }

        public string[] GetClassNames() => ReadClassNamesFromMetadata();

        public ClassificationResult Classify(Mat image, int inputW, int inputH, bool useImageNet)
        {
            if (_session == null)
                return new ClassificationResult();

            var tensor = useImageNet
                ? PreprocessImageNet(image, inputW, inputH)
                : PreprocessImageSimple(image, inputW, inputH);

            var inputs = CreateInput(GetInputName(), tensor);

            using var outputs = _session.Run(inputs);
            var output = outputs.First().AsTensor<float>();

            return ParseOutput(output);
        }

        private static ClassificationResult ParseOutput(Tensor<float> output)
        {
            var dims = output.Dimensions;
            int numClasses = dims.Length == 2 ? dims[1] : dims[0];

            // softmax
            var scores = new float[numClasses];
            float maxVal = float.MinValue;
            for (int i = 0; i < numClasses; i++)
            {
                float val = dims.Length == 2 ? output[0, i] : output[i];
                if (val > maxVal) maxVal = val;
                scores[i] = val;
            }

            float sumExp = 0;
            for (int i = 0; i < numClasses; i++)
            {
                scores[i] = MathF.Exp(scores[i] - maxVal);
                sumExp += scores[i];
            }
            for (int i = 0; i < numClasses; i++)
                scores[i] /= sumExp;

            // Top-N
            var topN = scores.Select((s, i) => (ClassId: i, Score: s))
                             .OrderByDescending(x => x.Score)
                             .Take(5)
                             .ToList();

            return new ClassificationResult
            {
                ClassId = topN[0].ClassId,
                Confidence = topN[0].Score,
                TopN = topN
            };
        }
    }
}
