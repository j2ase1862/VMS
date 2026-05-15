using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;
using VMS.DeepLearning.Interfaces;
using VMS.DeepLearning.Models;

namespace VMS.DeepLearning.Services
{
    /// <summary>
    /// YOLOv8/v11 ONNX 추론 — VMS.VisionSetup의 YoloOnnxEngine과 동일 로직 미니멀 버전.
    /// Letterbox 전처리 + [1,84,8400]/[1,8400,84] 자동 감지 + NMS.
    /// 학습 직후 best.onnx를 데이터셋 이미지에 바로 적용해 시각 검증할 때 사용.
    /// </summary>
    public class OnnxDetectionInference : IInferenceService
    {
        private InferenceSession? _session;
        private string _modelPath = string.Empty;
        private string[]? _classNames;
        private int _inputSize = 640;

        private int _numClasses;

        public bool IsLoaded => _session != null;
        public string CurrentModelPath => _modelPath;
        public int NumClasses => _numClasses;
        public int InputSize => _inputSize;

        public void LoadModel(string onnxPath)
        {
            UnloadModel();

            var options = new SessionOptions
            {
                GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL
            };

            _session = new InferenceSession(onnxPath, options);
            _modelPath = onnxPath;

            // 메타데이터에서 클래스 이름과 input 크기 추출
            _classNames = ReadClassNamesFromMetadata(_session);
            _inputSize = ReadInputSizeFromMetadata(_session);

            // 출력 차원에서 클래스 수 추출
            _numClasses = ReadNumClassesFromOutput(_session);
        }

        public void UnloadModel()
        {
            _session?.Dispose();
            _session = null;
            _modelPath = string.Empty;
            _classNames = null;
            _numClasses = 0;
        }

        public InferenceResult Predict(Mat image, float confThreshold = 0.10f, float iouThreshold = 0.45f)
        {
            var result = new InferenceResult
            {
                InputSize = _inputSize,
                NumClasses = _numClasses,
                ClassNames = _classNames,
            };

            if (_session == null || image.Empty())
                return result;

            int inputSize = _inputSize;

            // Letterbox 전처리 — 비율 유지 + 회색 패딩
            float scale = Math.Min((float)inputSize / image.Width, (float)inputSize / image.Height);
            int newW = (int)(image.Width * scale);
            int newH = (int)(image.Height * scale);
            int padX = (inputSize - newW) / 2;
            int padY = (inputSize - newH) / 2;

            using var resized = new Mat();
            Cv2.Resize(image, resized, new Size(newW, newH));

            using var letterbox = new Mat(inputSize, inputSize, image.Type(), new Scalar(114, 114, 114));
            resized.CopyTo(letterbox[new Rect(padX, padY, newW, newH)]);

            var tensor = PreprocessToCHW(letterbox, inputSize);

            string inputName = _session.InputNames.FirstOrDefault() ?? "images";
            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor(inputName, tensor)
            };

            using var outputs = _session.Run(inputs);
            var output = (DenseTensor<float>)outputs.First().AsTensor<float>();

            var raw = ParseYoloOutput(output, scale, padX, padY, confThreshold,
                image.Width, image.Height, out float maxRawConf, out int candidatesAboveZero);

            result.MaxRawConfidence = maxRawConf;
            result.CandidatesAboveZero = candidatesAboveZero;
            result.Predictions = NonMaxSuppression(raw, iouThreshold);
            return result;
        }

        public void Dispose() => UnloadModel();

        // ─────────────── 내부 ───────────────

        /// <summary>
        /// BGR → RGB, [0..1] 정규화, HWC → CHW.
        /// 참조 구현(VMS.VisionSetup OnnxModelBase.PreprocessImage)과 동일하게
        /// byte 포인터 + 실제 row stride 사용 — alignment 안전.
        /// </summary>
        private static DenseTensor<float> PreprocessToCHW(Mat bgr, int size)
        {
            using var rgb = new Mat();
            Cv2.CvtColor(bgr, rgb, ColorConversionCodes.BGR2RGB);

            var tensor = new DenseTensor<float>(new[] { 1, 3, size, size });
            var dst = tensor.Buffer.Span;

            int channelStride = size * size;
            int rBase = 0;
            int gBase = channelStride;
            int bBase = 2 * channelStride;

            const float scale = 1f / 255f;

            unsafe
            {
                byte* ptr = (byte*)rgb.Data;
                int rowStride = (int)rgb.Step();  // 실제 byte row stride

                for (int y = 0; y < size; y++)
                {
                    byte* row = ptr + y * rowStride;
                    int rowOffset = y * size;
                    for (int x = 0; x < size; x++)
                    {
                        int src = x * 3;
                        int dstOffset = rowOffset + x;
                        dst[rBase + dstOffset] = row[src + 0] * scale; // R
                        dst[gBase + dstOffset] = row[src + 1] * scale; // G
                        dst[bBase + dstOffset] = row[src + 2] * scale; // B
                    }
                }
            }

            return tensor;
        }

        /// <summary>
        /// 일부 ONNX export는 classification head에 sigmoid가 빠져 raw logit을 출력합니다.
        /// 출력의 최대 클래스 점수가 1을 크게 넘으면 sigmoid를 자동 적용.
        /// </summary>
        private static float MaybeSigmoid(float v, bool needsSigmoid)
            => needsSigmoid ? 1f / (1f + (float)Math.Exp(-v)) : v;

        private List<DetectionPrediction> ParseYoloOutput(
            DenseTensor<float> output, float scale, int padX, int padY,
            float confThreshold, int imgW, int imgH,
            out float maxRawConf, out int candidatesAboveZero)
        {
            maxRawConf = 0f;
            candidatesAboveZero = 0;
            var results = new List<DetectionPrediction>();
            var dims = output.Dimensions;
            if (dims.Length != 3) return results;

            // [1,C,N] (transposed) vs [1,N,C]
            int numDetections, numChannels;
            bool transposed;
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

            int numClasses = numChannels - 4;
            if (numClasses <= 0) return results;

            ReadOnlySpan<float> buf = output.Buffer.Span;

            // 1차 패스: raw 최댓값 추출. > 1.5면 sigmoid 미적용 모델로 판단(안전망).
            float globalMax = 0f;
            int classOffsetStart = transposed ? 4 * numDetections : 4;
            int classStrideGlobal = transposed ? 1 : 1;  // 단순 max만 보면 됨
            for (int i = 4 * (transposed ? numDetections : 1); i < buf.Length; i++)
            {
                float v = buf[i];
                if (v > globalMax) globalMax = v;
            }
            bool needsSigmoid = globalMax > 1.5f;

            for (int i = 0; i < numDetections; i++)
            {
                float cx, cy, w, h;
                int classBase, classStride;
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

                float maxScore = 0;
                int maxClassId = 0;
                int idx = classBase;
                for (int c = 0; c < numClasses; c++)
                {
                    float s = MaybeSigmoid(buf[idx], needsSigmoid);
                    if (s > maxScore) { maxScore = s; maxClassId = c; }
                    idx += classStride;
                }

                // 진단: 임계값 무관 최고 confidence + 0보다 큰 후보 수
                if (maxScore > maxRawConf) maxRawConf = maxScore;
                if (maxScore > 0) candidatesAboveZero++;

                if (maxScore < confThreshold) continue;

                int x1 = Math.Clamp((int)((cx - w / 2 - padX) / scale), 0, imgW);
                int y1 = Math.Clamp((int)((cy - h / 2 - padY) / scale), 0, imgH);
                int x2 = Math.Clamp((int)((cx + w / 2 - padX) / scale), 0, imgW);
                int y2 = Math.Clamp((int)((cy + h / 2 - padY) / scale), 0, imgH);

                string className = (_classNames != null && maxClassId < _classNames.Length)
                    ? _classNames[maxClassId]
                    : maxClassId.ToString();

                results.Add(new DetectionPrediction
                {
                    ClassId = maxClassId,
                    ClassName = className,
                    Confidence = maxScore,
                    X = x1,
                    Y = y1,
                    Width = x2 - x1,
                    Height = y2 - y1
                });
            }

            return results;
        }

        private static List<DetectionPrediction> NonMaxSuppression(
            List<DetectionPrediction> dets, float iouThreshold)
        {
            // 클래스별 NMS
            var result = new List<DetectionPrediction>();
            foreach (var group in dets.GroupBy(d => d.ClassId))
            {
                var sorted = group.OrderByDescending(d => d.Confidence).ToList();
                while (sorted.Count > 0)
                {
                    var best = sorted[0];
                    result.Add(best);
                    sorted.RemoveAt(0);
                    sorted.RemoveAll(d => Iou(best, d) > iouThreshold);
                }
            }
            return result;
        }

        private static float Iou(DetectionPrediction a, DetectionPrediction b)
        {
            int xa = Math.Max(a.X, b.X);
            int ya = Math.Max(a.Y, b.Y);
            int xb = Math.Min(a.X + a.Width, b.X + b.Width);
            int yb = Math.Min(a.Y + a.Height, b.Y + b.Height);
            int interW = Math.Max(0, xb - xa);
            int interH = Math.Max(0, yb - ya);
            float inter = interW * interH;
            float union = (float)(a.Width * a.Height) + b.Width * b.Height - inter;
            return union <= 0 ? 0 : inter / union;
        }

        /// <summary>ONNX 메타데이터의 'names' 키에서 클래스 이름 추출(Ultralytics 표준).</summary>
        private static string[]? ReadClassNamesFromMetadata(InferenceSession session)
        {
            try
            {
                var meta = session.ModelMetadata.CustomMetadataMap;
                if (meta.TryGetValue("names", out var raw) && !string.IsNullOrEmpty(raw))
                {
                    // 예: "{0: 'defect', 1: 'scratch'}"
                    var parts = raw.Trim('{', '}').Split(',');
                    var dict = new SortedDictionary<int, string>();
                    foreach (var p in parts)
                    {
                        var kv = p.Split(':', 2);
                        if (kv.Length != 2) continue;
                        if (int.TryParse(kv[0].Trim(), out int id))
                            dict[id] = kv[1].Trim().Trim('\'', '"');
                    }
                    if (dict.Count > 0) return dict.Values.ToArray();
                }
            }
            catch
            {
                // metadata 없으면 무시 — ClassId 문자열로 폴백
            }
            return null;
        }

        /// <summary>
        /// 출력 텐서 차원에서 클래스 수 추출. YOLOv8: numChannels - 4(cx,cy,w,h).
        /// </summary>
        private static int ReadNumClassesFromOutput(InferenceSession session)
        {
            try
            {
                var outputMeta = session.OutputMetadata.Values.FirstOrDefault();
                if (outputMeta == null) return 0;

                var dims = outputMeta.Dimensions;
                if (dims.Length != 3) return 0;

                int c = dims[1] < dims[2] ? dims[1] : dims[2];
                return Math.Max(0, c - 4);
            }
            catch { return 0; }
        }

        /// <summary>ONNX 메타데이터의 'imgsz' 키 또는 input shape에서 입력 크기 추출.</summary>
        private static int ReadInputSizeFromMetadata(InferenceSession session)
        {
            try
            {
                var meta = session.ModelMetadata.CustomMetadataMap;
                if (meta.TryGetValue("imgsz", out var raw))
                {
                    // 예: "[640, 640]" or "640"
                    var clean = raw.Trim('[', ']', ' ').Split(',')[0].Trim();
                    if (int.TryParse(clean, out int size)) return size;
                }

                // Fallback: input metadata shape
                var inputMeta = session.InputMetadata.Values.FirstOrDefault();
                if (inputMeta != null && inputMeta.Dimensions.Length == 4)
                {
                    int h = inputMeta.Dimensions[2];
                    if (h > 0) return h;
                }
            }
            catch { }
            return 640;
        }
    }
}
