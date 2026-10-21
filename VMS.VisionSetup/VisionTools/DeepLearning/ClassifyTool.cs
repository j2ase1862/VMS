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

        /// <summary>
        /// 양품 클래스 이름 (콤마 구분, 여러 개 가능). 지정하면 "양품 클래스이고 신뢰도 ≥ 임계값" 일 때만 OK.
        /// 비어 있으면 종전처럼 신뢰도만으로 판정한다 — 이때는 불량 클래스로 자신 있게 분류돼도 OK 가 된다.
        /// </summary>
        [ObservableProperty]
        private string _okClassesText = string.Empty;

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

        public string[] ClassNames => SplitNames(ClassNamesText);

        public string[] OkClasses => SplitNames(OkClassesText);

        private static string[] SplitNames(string text) => string.IsNullOrWhiteSpace(text)
            ? Array.Empty<string>()
            : text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

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

                // 양품 클래스 이름이 모델 클래스 목록에 없으면(오타 · 모델 교체) 모든 판정이 NG 가 된다 — 추론 전에 막는다
                var names = ClassNames;
                var okClasses = OkClasses;
                if (okClasses.Length > 0 && names.Length > 0)
                {
                    var unknown = okClasses.Where(c => !names.Contains(c, StringComparer.OrdinalIgnoreCase)).ToArray();
                    if (unknown.Length > 0)
                    {
                        result.Success = false;
                        result.Message = $"양품 클래스 '{string.Join("', '", unknown)}' 가 Class Names 에 없습니다 — " +
                                         "설정의 OK Classes 를 다시 고르세요.";
                        return result;
                    }
                }

                var roiImage = UseROI ? GetROIImage(inputImage) : inputImage;

                var engine = EnsureEngine();
                var classResult = engine.Classify(roiImage, InputWidth, InputHeight, UseImageNetNormalization);

                string className = classResult.ClassId < names.Length
                    ? names[classResult.ClassId]
                    : $"class{classResult.ClassId}";

                // 판정 = (양품 클래스를 지정했으면) 양품 클래스 + 신뢰도 ≥ 임계값.
                // 확신 없는 양품도 NG 로 보낸다(보수적). 지정이 없으면 종전처럼 신뢰도만 본다.
                bool confident = classResult.Confidence >= ConfidenceThreshold;
                bool okClass = okClasses.Length == 0 || okClasses.Contains(className, StringComparer.OrdinalIgnoreCase);
                bool pass = confident && okClass;

                result.Success = pass;
                result.Message = $"{className} ({classResult.Confidence:P1})" +
                                 (!okClass ? " — 불량 클래스" : !confident ? " — 신뢰도 부족" : "");
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
                OkClassesText = this.OkClassesText,
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
