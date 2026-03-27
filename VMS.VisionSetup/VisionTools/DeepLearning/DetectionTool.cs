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
    /// Cognex ViDi Blue Locate 대응 — YOLO ONNX 기반 객체 검출 도구.
    /// YOLOv8/v11 ONNX 모델을 로드하여 이미지에서 객체를 검출합니다.
    /// </summary>
    public partial class DetectionTool : VisionToolBase
    {
        private YoloOnnxEngine? _engine;

        // ── Parameters ──

        [ObservableProperty]
        private string _modelPath = string.Empty;

        [ObservableProperty]
        private int _inputSize = 640;

        [ObservableProperty]
        private double _confidenceThreshold = 0.5;

        [ObservableProperty]
        private double _iouThreshold = 0.45;

        [ObservableProperty]
        private string _classNamesText = string.Empty;

        [ObservableProperty]
        private bool _drawOverlay = true;

        public DetectionTool()
        {
            Name = "Detection";
            ToolType = "DetectionTool";
        }

        partial void OnModelPathChanged(string value)
        {
            _engine?.Dispose();
            _engine = null;
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

                _engine ??= new YoloOnnxEngine(ModelPath);

                var detections = _engine.Detect(roiImage, InputSize, (float)ConfidenceThreshold, (float)IouThreshold);

                result.Success = detections.Count > 0;
                result.Message = $"{detections.Count}개 객체 검출";
                result.Data["DetectionCount"] = detections.Count;
                result.Data["Detections"] = detections;

                // 오버레이
                if (DrawOverlay)
                {
                    var overlay = GetColorOverlayBase(inputImage);
                    var classNames = ClassNames;
                    int roiOffsetX = UseROI ? ROI.X : 0;
                    int roiOffsetY = UseROI ? ROI.Y : 0;

                    foreach (var det in detections)
                    {
                        var rect = new Rect(
                            det.X + roiOffsetX, det.Y + roiOffsetY,
                            det.Width, det.Height);

                        var color = GetDetectionColor(det.ClassId);
                        Cv2.Rectangle(overlay, rect, color, 2);

                        string label = det.ClassId < classNames.Length
                            ? $"{classNames[det.ClassId]} {det.Confidence:F2}"
                            : $"class{det.ClassId} {det.Confidence:F2}";

                        Cv2.PutText(overlay, label,
                            new Point(rect.X, rect.Y - 6),
                            HersheyFonts.HersheySimplex, 0.5, color, 1);

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
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"Detection 오류: {ex.Message}";
            }

            return result;
        }

        public override VisionToolBase Clone()
        {
            return new DetectionTool
            {
                Name = this.Name,
                ModelPath = this.ModelPath,
                InputSize = this.InputSize,
                ConfidenceThreshold = this.ConfidenceThreshold,
                IouThreshold = this.IouThreshold,
                ClassNamesText = this.ClassNamesText,
                DrawOverlay = this.DrawOverlay,
                UseROI = this.UseROI,
                ROI = this.ROI
            };
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
    public class YoloOnnxEngine : OnnxModelBase
    {
        public YoloOnnxEngine(string modelPath)
        {
            LoadModel(modelPath);
        }

        public List<DetectionResult> Detect(Mat image, int inputSize, float confThreshold, float iouThreshold)
        {
            if (_session == null) return new List<DetectionResult>();

            // 전처리: letterbox resize
            float scaleX = (float)image.Width / inputSize;
            float scaleY = (float)image.Height / inputSize;

            var tensor = PreprocessImageSimple(image, inputSize, inputSize);

            var inputs = CreateInput(GetInputName(), tensor);

            using var outputs = _session.Run(inputs);
            var output = outputs.First().AsTensor<float>();

            return ParseYoloOutput(output, scaleX, scaleY, confThreshold, iouThreshold,
                image.Width, image.Height);
        }

        private static List<DetectionResult> ParseYoloOutput(
            Tensor<float> output, float scaleX, float scaleY,
            float confThreshold, float iouThreshold,
            int imgWidth, int imgHeight)
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
                    // [1, 84, 8400] — need to treat dim1 as channels, dim2 as detections
                    numChannels = dims[1];
                    numDetections = dims[2];
                    transposed = true;
                }
                else
                {
                    // [1, 8400, 84]
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

            var candidates = new List<(DetectionResult det, float score)>();

            for (int i = 0; i < numDetections; i++)
            {
                float cx, cy, w, h;
                if (transposed)
                {
                    cx = output[0, 0, i];
                    cy = output[0, 1, i];
                    w = output[0, 2, i];
                    h = output[0, 3, i];
                }
                else
                {
                    cx = output[0, i, 0];
                    cy = output[0, i, 1];
                    w = output[0, i, 2];
                    h = output[0, i, 3];
                }

                // 최대 클래스 점수 찾기
                float maxScore = 0;
                int maxClassId = 0;
                for (int c = 0; c < numClasses; c++)
                {
                    float score = transposed ? output[0, 4 + c, i] : output[0, i, 4 + c];
                    if (score > maxScore)
                    {
                        maxScore = score;
                        maxClassId = c;
                    }
                }

                if (maxScore < confThreshold) continue;

                int x1 = Math.Clamp((int)((cx - w / 2) * scaleX), 0, imgWidth);
                int y1 = Math.Clamp((int)((cy - h / 2) * scaleY), 0, imgHeight);
                int x2 = Math.Clamp((int)((cx + w / 2) * scaleX), 0, imgWidth);
                int y2 = Math.Clamp((int)((cy + h / 2) * scaleY), 0, imgHeight);

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

            // NMS
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
