using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using VMS.VisionSetup.Models;

namespace VMS.VisionSetup.VisionTools.DeepLearning
{
    /// <summary>
    /// Cognex ViDi Red Analyze 대응 — ONNX 기반 이상 탐지 도구.
    /// PatchCore, FastFlow, EfficientAD 등 anomaly detection 모델의 ONNX를 로드하여
    /// 정상/이상 판정 및 히트맵을 생성합니다.
    ///
    /// 모델 출력 형태:
    ///   - anomaly_score: [1] or [1,1] — 전체 이미지 이상 점수
    ///   - anomaly_map: [1,1,H,W] — 픽셀별 이상 히트맵 (선택)
    /// </summary>
    public partial class AnomalyTool : VisionToolBase
    {
        private AnomalyOnnxEngine? _engine;

        // ── Parameters ──

        [ObservableProperty]
        private string _modelPath = string.Empty;

        [ObservableProperty]
        private int _inputSize = 224;

        [ObservableProperty]
        private double _anomalyThreshold = 0.5;

        [ObservableProperty]
        private bool _drawOverlay = true;

        [ObservableProperty]
        private bool _showHeatmap = true;

        [ObservableProperty]
        private double _heatmapOpacity = 0.4;

        public AnomalyTool()
        {
            Name = "Anomaly";
            ToolType = "AnomalyTool";
        }

        partial void OnModelPathChanged(string value)
        {
            _engine?.Dispose();
            _engine = null;
        }

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

                _engine ??= new AnomalyOnnxEngine(ModelPath);

                var anomalyResult = _engine.Detect(roiImage, InputSize);

                bool isNormal = anomalyResult.AnomalyScore < AnomalyThreshold;

                result.Success = isNormal;
                result.Message = isNormal
                    ? $"정상 (score: {anomalyResult.AnomalyScore:F3})"
                    : $"이상 감지 (score: {anomalyResult.AnomalyScore:F3})";
                result.Data["AnomalyScore"] = anomalyResult.AnomalyScore;
                result.Data["IsNormal"] = isNormal;
                result.Data["Threshold"] = AnomalyThreshold;

                if (DrawOverlay)
                {
                    var overlay = GetColorOverlayBase(inputImage);
                    var color = isNormal ? new Scalar(0, 255, 0) : new Scalar(0, 0, 255);

                    // 판정 텍스트
                    string label = isNormal ? "NORMAL" : "ANOMALY";
                    Cv2.PutText(overlay, $"{label}: {anomalyResult.AnomalyScore:F3}",
                        new Point(10, 30),
                        HersheyFonts.HersheySimplex, 1.0, color, 2);

                    // 히트맵 오버레이
                    if (ShowHeatmap && anomalyResult.AnomalyMap != null)
                    {
                        using var heatmap = new Mat();
                        using var resizedMap = new Mat();

                        // anomaly map을 원본 크기로 리사이즈
                        Cv2.Resize(anomalyResult.AnomalyMap, resizedMap,
                            new Size(overlay.Width, overlay.Height));

                        // 0~1 → 0~255 정규화
                        using var normalized = new Mat();
                        resizedMap.ConvertTo(normalized, MatType.CV_8UC1, 255.0);

                        // 컬러 히트맵 적용
                        Cv2.ApplyColorMap(normalized, heatmap, ColormapTypes.Jet);

                        // 알파 블렌딩
                        Cv2.AddWeighted(overlay, 1.0 - HeatmapOpacity, heatmap, HeatmapOpacity, 0, overlay);
                    }

                    // 테두리
                    Cv2.Rectangle(overlay,
                        new Rect(0, 0, overlay.Width, overlay.Height),
                        color, 4);

                    result.OverlayImage = overlay;

                    result.Graphics.Add(new GraphicOverlay
                    {
                        Type = GraphicType.Text,
                        Position = new Point2d(10, 30),
                        Text = $"{label}: {anomalyResult.AnomalyScore:F3}",
                        Color = color
                    });
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"Anomaly 오류: {ex.Message}";
            }

            return result;
        }

        public override VisionToolBase Clone()
        {
            return new AnomalyTool
            {
                Name = this.Name,
                ModelPath = this.ModelPath,
                InputSize = this.InputSize,
                AnomalyThreshold = this.AnomalyThreshold,
                DrawOverlay = this.DrawOverlay,
                ShowHeatmap = this.ShowHeatmap,
                HeatmapOpacity = this.HeatmapOpacity,
                UseROI = this.UseROI,
                ROI = this.ROI
            };
        }

        public override List<string> GetAvailableResultKeys()
        {
            return new List<string> { "Success", "AnomalyScore", "IsNormal" };
        }
    }

    /// <summary>
    /// 이상 탐지 결과
    /// </summary>
    public class AnomalyResult
    {
        public float AnomalyScore { get; set; }
        public Mat? AnomalyMap { get; set; }
    }

    /// <summary>
    /// Anomaly Detection ONNX 추론 엔진.
    /// anomalib 호환 모델 (PatchCore, FastFlow, EfficientAD 등)을 지원합니다.
    /// </summary>
    public class AnomalyOnnxEngine : OnnxModelBase
    {
        public AnomalyOnnxEngine(string modelPath)
        {
            LoadModel(modelPath);
        }

        public AnomalyResult Detect(Mat image, int inputSize)
        {
            if (_session == null)
                return new AnomalyResult { AnomalyScore = 0 };

            var tensor = PreprocessImageNet(image, inputSize, inputSize);
            var inputs = CreateInput(GetInputName(), tensor);

            using var outputs = _session.Run(inputs);

            var result = new AnomalyResult();

            // 출력 이름별 파싱 (anomalib 모델 호환)
            foreach (var output in outputs)
            {
                var outTensor = output.AsTensor<float>();
                var dims = outTensor.Dimensions;

                string name = output.Name.ToLower();

                if (name.Contains("score") || name.Contains("pred_score") ||
                    (dims.Length == 1 && dims[0] == 1) ||
                    (dims.Length == 2 && dims[0] == 1 && dims[1] == 1))
                {
                    // anomaly score
                    result.AnomalyScore = dims.Length == 2
                        ? outTensor[0, 0]
                        : outTensor[0];
                }
                else if (name.Contains("map") || name.Contains("anomaly_map") ||
                         (dims.Length == 4 && dims[1] == 1))
                {
                    // anomaly map: [1, 1, H, W]
                    int mapH = dims[2];
                    int mapW = dims[3];
                    var mapMat = new Mat(mapH, mapW, MatType.CV_32FC1);

                    unsafe
                    {
                        float* ptr = (float*)mapMat.Data;
                        for (int y = 0; y < mapH; y++)
                            for (int x = 0; x < mapW; x++)
                                ptr[y * mapW + x] = Math.Clamp(outTensor[0, 0, y, x], 0f, 1f);
                    }

                    result.AnomalyMap = mapMat;
                }
            }

            // 단일 출력만 있는 경우 (score만 출력하는 모델)
            if (result.AnomalyScore == 0 && outputs.Count() == 1)
            {
                var singleOutput = outputs.First().AsTensor<float>();
                if (singleOutput.Dimensions.Length <= 2)
                {
                    result.AnomalyScore = singleOutput.Dimensions.Length == 2
                        ? singleOutput[0, 0]
                        : singleOutput[0];
                }
            }

            return result;
        }
    }
}
