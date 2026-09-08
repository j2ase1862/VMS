using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;
using VMS.Core.Security;
using VMS.DeepLearning.Interfaces;
using VMS.DeepLearning.Models;

namespace VMS.DeepLearning.Services
{
    /// <summary>
    /// ONNX 모델 로드 실패 시 throw 되는 명시적 예외 — 손상/조작된 모델 파일,
    /// 호환 안 되는 OpRange, 메모리 부족 등 native 실패를 .NET 예외로 wrap.
    /// 호출자는 본 예외만 catch 하면 모델 결함을 일관 처리 가능.
    /// GS 인증 결함 허용성 요구사항 대응 (PR P1-#4).
    /// </summary>
    public sealed class OnnxLoadException : Exception
    {
        public string ModelPath { get; }
        public OnnxLoadException(string modelPath, string message, Exception inner)
            : base(message, inner)
        {
            ModelPath = modelPath;
        }
    }

    /// <summary>
    /// 검출 ONNX 추론 — VMS.VisionSetup의 YoloOnnxEngine / DFineOnnxEngine 과 동일 로직 미니멀 버전.
    /// YOLO: Letterbox 전처리 + [1,84,8400]/[1,8400,84] 자동 감지 + NMS.
    /// D-FINE(train_dfine.py, Apache-2.0): stretch 리사이즈 + images/orig_target_sizes → labels/boxes/scores
    /// (또는 HF 원본 logits/pred_boxes). 세션 입출력 이름으로 자동 판별.
    /// 학습 직후 best.onnx를 데이터셋 이미지에 바로 적용해 시각 검증할 때 사용.
    /// </summary>
    public class OnnxDetectionInference : IInferenceService
    {
        private InferenceSession? _session;
        private string _modelPath = string.Empty;
        private string[]? _classNames;
        private int _inputSize = 640;

        private int _numClasses;

        // D-FINE 규약 (세션 입출력 이름으로 판별)
        private bool _isDFine;
        private bool _dfineDeployLayout;
        private string _imagesInput = "images";
        private string? _sizesInput;

        /// <summary>현재 모델이 D-FINE(DETR) 규약이면 true (YOLO 면 false)</summary>
        public bool IsDFineModel => _isDFine;

        public bool IsLoaded => _session != null;
        public string CurrentModelPath => _modelPath;
        public int NumClasses => _numClasses;
        public int InputSize => _inputSize;

        public void LoadModel(string onnxPath)
        {
            UnloadModel();

            if (!File.Exists(onnxPath))
                throw new FileNotFoundException($"ONNX 모델을 찾을 수 없습니다: {onnxPath}");

            var options = new SessionOptions
            {
                GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL
            };

            // GS 결함 허용성: 손상/조작된 ONNX 가 native exception 으로 호출자 (LabelingMainViewModel
            // 등) 흐름을 깨지 않도록 OnnxLoadException 으로 wrap + AuditLog 기록.
            try
            {
                _session = new InferenceSession(onnxPath, options);
                _modelPath = onnxPath;

                // 메타데이터에서 클래스 이름과 input 크기 추출
                _classNames = ReadClassNamesFromMetadata(_session);
                _inputSize = ReadInputSizeFromMetadata(_session);

                DetectLayout(_session);

                // 출력 차원에서 클래스 수 추출
                _numClasses = _isDFine
                    ? (_classNames?.Length ?? ReadNumClassesFromDFineOutput(_session))
                    : ReadNumClassesFromOutput(_session);
            }
            catch (Exception ex) when (ex is not FileNotFoundException && ex is not OnnxLoadException)
            {
                UnloadModel();
                AuditLogger.Instance.Log(
                    AuditCategory.Inspection,
                    "Model Configuration Load Error",
                    AuditOutcome.Failure,
                    source: nameof(OnnxDetectionInference),
                    details: $"path={onnxPath} error={ex.GetType().Name}: {ex.Message}");
                throw new OnnxLoadException(
                    onnxPath,
                    $"ONNX 모델 로드 실패 ({Path.GetFileName(onnxPath)}): {ex.Message}",
                    ex);
            }
        }

        public void UnloadModel()
        {
            _session?.Dispose();
            _session = null;
            _modelPath = string.Empty;
            _classNames = null;
            _numClasses = 0;
            _isDFine = false;
            _dfineDeployLayout = false;
            _imagesInput = "images";
            _sizesInput = null;
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

            if (_isDFine)
                return PredictDFine(image, confThreshold, iouThreshold, result);

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

        // ─────────────── D-FINE ───────────────

        private void DetectLayout(InferenceSession session)
        {
            var inputs = session.InputNames.ToList();
            var outputs = session.OutputNames.ToList();

            bool deploy = outputs.Contains("scores") && outputs.Contains("boxes");
            bool raw = outputs.Contains("logits") && outputs.Contains("pred_boxes");
            _isDFine = inputs.Contains("orig_target_sizes") || deploy || raw;
            if (!_isDFine) return;

            _dfineDeployLayout = deploy || !raw;
            _sizesInput = inputs.FirstOrDefault(n => n == "orig_target_sizes")
                ?? inputs.FirstOrDefault(n => session.InputMetadata[n].ElementType == typeof(long));
            _imagesInput = inputs.FirstOrDefault(n => n != _sizesInput) ?? "images";
        }

        private static int ReadNumClassesFromDFineOutput(InferenceSession session)
        {
            try
            {
                if (session.OutputMetadata.TryGetValue("logits", out var logits) && logits.Dimensions.Length == 3)
                    return Math.Max(0, logits.Dimensions[2]);
                if (session.ModelMetadata.CustomMetadataMap.TryGetValue("nc", out var nc) && int.TryParse(nc, out int n))
                    return n;
            }
            catch { /* ignore */ }
            return 0;
        }

        private InferenceResult PredictDFine(Mat image, float confThreshold, float iouThreshold, InferenceResult result)
        {
            int inputSize = _inputSize;

            // stretch 리사이즈 (letterbox 없음) + RGB 0~1
            using var resized = new Mat();
            Cv2.Resize(image, resized, new Size(inputSize, inputSize));
            using var bgr = resized.Channels() == 3
                ? resized.Clone()
                : resized.CvtColor(resized.Channels() == 1 ? ColorConversionCodes.GRAY2BGR : ColorConversionCodes.BGRA2BGR);
            var tensor = PreprocessToCHW(bgr, inputSize);

            var inputs = new List<NamedOnnxValue> { NamedOnnxValue.CreateFromTensor(_imagesInput, tensor) };
            if (_sizesInput != null)
            {
                var sizes = new DenseTensor<long>(new[] { 1, 2 });
                sizes[0, 0] = image.Height;
                sizes[0, 1] = image.Width;
                inputs.Add(NamedOnnxValue.CreateFromTensor(_sizesInput, sizes));
            }

            using var outputs = _session!.Run(inputs);
            var byName = outputs.ToDictionary(o => o.Name, o => o);

            var raw = new List<DetectionPrediction>();
            float maxRaw = 0f;
            int above = 0;
            int imgW = image.Width, imgH = image.Height;

            if (_dfineDeployLayout)
            {
                var outList = outputs.ToList();
                var boxesV = byName.TryGetValue("boxes", out var b) ? b : (outList.Count > 1 ? outList[1] : null);
                var scoresV = byName.TryGetValue("scores", out var s) ? s : (outList.Count > 2 ? outList[2] : null);
                var labelsV = byName.TryGetValue("labels", out var l) ? l : (outList.Count > 0 ? outList[0] : null);
                if (boxesV == null || scoresV == null) return result;

                var boxes = boxesV.AsTensor<float>().ToArray();
                var scores = scoresV.AsTensor<float>().ToArray();
                long[] labels = Array.Empty<long>();
                if (labelsV != null)
                {
                    try { labels = labelsV.AsTensor<long>().ToArray(); }
                    catch { try { labels = labelsV.AsTensor<int>().ToArray().Select(x => (long)x).ToArray(); } catch { } }
                }

                int q = scores.Length;
                bool normalized = _sizesInput == null && boxes.Take(q * 4).All(v => v <= 1.0001f);
                float sx = normalized ? imgW : 1f, sy = normalized ? imgH : 1f;
                for (int i = 0; i < q && i * 4 + 3 < boxes.Length; i++)
                {
                    float score = scores[i];
                    if (score > maxRaw) maxRaw = score;
                    if (score > 0f) above++;
                    if (score < confThreshold) continue;
                    int cls = i < labels.Length ? (int)labels[i] : 0;
                    AddPrediction(raw, cls, score,
                        boxes[i * 4] * sx, boxes[i * 4 + 1] * sy, boxes[i * 4 + 2] * sx, boxes[i * 4 + 3] * sy, imgW, imgH);
                }
            }
            else
            {
                if (!byName.TryGetValue("logits", out var logitsV) || !byName.TryGetValue("pred_boxes", out var boxesV))
                    return result;
                var logits = logitsV.AsTensor<float>();
                var boxes = boxesV.AsTensor<float>().ToArray();
                int q = logits.Dimensions[1], nc = logits.Dimensions[2];
                var lbuf = logits.ToArray();
                for (int i = 0; i < q; i++)
                {
                    int best = 0; float bestLogit = float.NegativeInfinity;
                    for (int c = 0; c < nc; c++)
                    {
                        float v = lbuf[i * nc + c];
                        if (v > bestLogit) { bestLogit = v; best = c; }
                    }
                    float score = 1f / (1f + (float)Math.Exp(-bestLogit));
                    if (score > maxRaw) maxRaw = score;
                    if (score > 0f) above++;
                    if (score < confThreshold) continue;
                    float cx = boxes[i * 4] * imgW, cy = boxes[i * 4 + 1] * imgH;
                    float w = boxes[i * 4 + 2] * imgW, h = boxes[i * 4 + 3] * imgH;
                    AddPrediction(raw, best, score, cx - w / 2, cy - h / 2, cx + w / 2, cy + h / 2, imgW, imgH);
                }
            }

            result.MaxRawConfidence = maxRaw;
            result.CandidatesAboveZero = above;
            result.Predictions = NonMaxSuppression(raw, iouThreshold);
            return result;
        }

        private void AddPrediction(List<DetectionPrediction> list, int cls, float score,
            float x1f, float y1f, float x2f, float y2f, int imgW, int imgH)
        {
            int x1 = Math.Clamp((int)Math.Round(x1f), 0, imgW);
            int y1 = Math.Clamp((int)Math.Round(y1f), 0, imgH);
            int x2 = Math.Clamp((int)Math.Round(x2f), 0, imgW);
            int y2 = Math.Clamp((int)Math.Round(y2f), 0, imgH);
            if (x2 <= x1 || y2 <= y1) return;
            list.Add(new DetectionPrediction
            {
                X = x1, Y = y1, Width = x2 - x1, Height = y2 - y1,
                ClassId = cls,
                ClassName = _classNames != null && cls >= 0 && cls < _classNames.Length ? _classNames[cls] : cls.ToString(),
                Confidence = score
            });
        }

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
