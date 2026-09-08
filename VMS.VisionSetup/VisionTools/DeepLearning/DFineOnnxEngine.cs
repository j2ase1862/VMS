using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using VMS.Core.DeepLearning;

namespace VMS.VisionSetup.VisionTools.DeepLearning
{
    /// <summary>
    /// 객체 검출 엔진 공통 인터페이스.
    /// DetectionTool 은 백본(YOLO / D-FINE)에 무관하게 이 인터페이스만 호출한다.
    /// 반환 좌표는 항상 입력 Mat 기준 픽셀 좌표(좌상단 X/Y + 폭/높이).
    /// </summary>
    public interface IDetectionEngine : IDisposable
    {
        /// <summary>현재 세션에서 실제로 활성화된 실행 제공자 (CPU / CUDA / TensorRT …)</summary>
        string ActiveProvider { get; }

        /// <summary>ONNX 메타데이터 'names' 에서 읽은 클래스 이름 배열 (없으면 빈 배열)</summary>
        string[] GetClassNames();

        List<DetectionResult> Detect(
            Mat image, int inputSize,
            float confThreshold, float iouThreshold,
            float[]? perClassConfThresholds = null);
    }

    /// <summary>
    /// D-FINE (Apache-2.0, DETR 계열) ONNX 추론 엔진.
    /// 전처리: 비율 무시 stretch 리사이즈 + RGB 0~1 (letterbox 없음 — D-FINE 학습 규약과 동일).
    /// 지원 출력 규약:
    ///   • deploy — 입력 images[N,3,H,W] + orig_target_sizes[N,2](h,w) → labels[N,Q] · boxes[N,Q,4](xyxy, 원본 픽셀) · scores[N,Q]
    ///     (train_dfine.py export 및 공식 D-FINE/RT-DETR export_onnx.py 와 동일)
    ///   • raw — 입력 pixel_values → logits[N,Q,nc] · pred_boxes[N,Q,4](cxcywh 정규화)  (HF transformers 원본 export)
    /// 후처리: 임계값(클래스별 우선) → 클래스별 NMS(DETR 는 원칙상 불필요하나 낮은 임계값에서의 중복 억제용).
    /// </summary>
    public class DFineOnnxEngine : OnnxModelBase, IDetectionEngine
    {
        private readonly string _imagesInput = "images";
        private readonly string? _sizesInput;
        private readonly bool _deployLayout;
        private readonly string _labelsOut = "labels";
        private readonly string _boxesOut = "boxes";
        private readonly string _scoresOut = "scores";
        private readonly string _logitsOut = "logits";
        private readonly string _predBoxesOut = "pred_boxes";

        public bool IsDeployLayout => _deployLayout;
        public bool HasSizesInput => _sizesInput != null;

        public DFineOnnxEngine(string modelPath)
        {
            LoadModel(modelPath);
            if (_session == null) return;

            var inputs = _session.InputNames.ToList();
            var outputs = _session.OutputNames.ToList();

            // 크기 입력: 이름 우선, 없으면 int64 타입 입력
            _sizesInput = inputs.FirstOrDefault(n => n == "orig_target_sizes")
                ?? inputs.FirstOrDefault(n => _session.InputMetadata[n].ElementType == typeof(long));
            _imagesInput = inputs.FirstOrDefault(n => n != _sizesInput) ?? "images";

            if (outputs.Contains("logits") && outputs.Contains("pred_boxes"))
            {
                _deployLayout = false;
            }
            else if (outputs.Contains("scores") && outputs.Contains("boxes"))
            {
                _deployLayout = true;
                _labelsOut = outputs.FirstOrDefault(n => n == "labels") ?? outputs[0];
            }
            else if (outputs.Count >= 3)
            {
                // 이름이 다른 deploy export — 위치 기반 (labels, boxes, scores)
                _deployLayout = true;
                _labelsOut = outputs[0];
                _boxesOut = outputs[1];
                _scoresOut = outputs[2];
            }
            else if (outputs.Count == 2)
            {
                // 이름이 다른 raw export — 위치 기반 (logits, pred_boxes)
                _deployLayout = false;
                _logitsOut = outputs[0];
                _predBoxesOut = outputs[1];
            }
            else
            {
                throw new OnnxLoadException(modelPath,
                    $"D-FINE 출력 규약을 인식할 수 없습니다 (outputs: {string.Join(", ", outputs)})",
                    new InvalidOperationException("unsupported output layout"));
            }
        }

        public string[] GetClassNames() => ReadClassNamesFromMetadata();

        public List<DetectionResult> Detect(
            Mat image, int inputSize,
            float confThreshold, float iouThreshold,
            float[]? perClassConfThresholds = null)
        {
            if (_session == null || image.Empty()) return new List<DetectionResult>();

            // stretch 리사이즈 + RGB 0~1 (letterbox 없음)
            var tensor = PreprocessImageSimple(image, inputSize, inputSize);
            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor(_imagesInput, tensor)
            };

            if (_sizesInput != null)
            {
                var sizes = new DenseTensor<long>(new[] { 1, 2 });
                sizes[0, 0] = image.Height;
                sizes[0, 1] = image.Width;
                inputs.Add(NamedOnnxValue.CreateFromTensor(_sizesInput, sizes));
            }

            using var outputs = _session.Run(inputs);
            var byName = outputs.ToDictionary(o => o.Name, o => o);

            var candidates = _deployLayout
                ? ParseDeploy(byName, image.Width, image.Height, confThreshold, perClassConfThresholds)
                : ParseRaw(byName, image.Width, image.Height, confThreshold, perClassConfThresholds);

            var final = YoloOnnxEngine.GlobalNMS(candidates, iouThreshold);
            System.Diagnostics.Debug.WriteLine(
                $"[DFINE-Diag] layout={(_deployLayout ? "deploy" : "raw")} candidates={candidates.Count} afterNMS={final.Count}");
            return final;
        }

        // ── deploy: labels / boxes(xyxy) / scores ──
        private List<DetectionResult> ParseDeploy(
            Dictionary<string, DisposableNamedOnnxValue> o, int imgW, int imgH,
            float confThreshold, float[]? perClassConf)
        {
            var results = new List<DetectionResult>();
            if (!o.TryGetValue(_boxesOut, out var boxesV) || !o.TryGetValue(_scoresOut, out var scoresV))
                return results;

            var boxes = boxesV.AsTensor<float>();
            var scores = scoresV.AsTensor<float>();
            long[] labels = o.TryGetValue(_labelsOut, out var labelsV) ? ReadLabels(labelsV) : Array.Empty<long>();

            var bdims = boxes.Dimensions;
            if (bdims.Length != 3 || bdims[2] != 4) return results;
            int q = bdims[1];

            var bbuf = boxes.ToArray();
            var sbuf = scores.ToArray();
            if (sbuf.Length < q) return results;

            // 좌표가 전부 0~1 이면 정규화 출력으로 간주해 원본 크기로 환산 (sizes 입력이 없는 export 대비)
            bool normalized = _sizesInput == null && IsNormalized(bbuf, q);
            float sx = normalized ? imgW : 1f;
            float sy = normalized ? imgH : 1f;

            for (int i = 0; i < q; i++)
            {
                float score = sbuf[i];
                int cls = i < labels.Length ? (int)labels[i] : 0;

                float thr = confThreshold;
                if (perClassConf != null && cls >= 0 && cls < perClassConf.Length)
                    thr = perClassConf[cls];
                if (score < thr) continue;

                int b = i * 4;
                AddBox(results, cls, score,
                    bbuf[b] * sx, bbuf[b + 1] * sy, bbuf[b + 2] * sx, bbuf[b + 3] * sy, imgW, imgH);
            }
            return results;
        }

        // ── raw: logits[1,Q,nc] (sigmoid 전) / pred_boxes[1,Q,4] (cxcywh 정규화) ──
        private List<DetectionResult> ParseRaw(
            Dictionary<string, DisposableNamedOnnxValue> o, int imgW, int imgH,
            float confThreshold, float[]? perClassConf)
        {
            var results = new List<DetectionResult>();
            if (!o.TryGetValue(_logitsOut, out var logitsV) || !o.TryGetValue(_predBoxesOut, out var boxesV))
                return results;

            var logits = logitsV.AsTensor<float>();
            var boxes = boxesV.AsTensor<float>();
            var ldims = logits.Dimensions;
            var bdims = boxes.Dimensions;
            if (ldims.Length != 3 || bdims.Length != 3 || bdims[2] != 4) return results;

            int q = Math.Min(ldims[1], bdims[1]);
            int nc = ldims[2];
            var lbuf = logits.ToArray();
            var bbuf = boxes.ToArray();

            for (int i = 0; i < q; i++)
            {
                int best = 0;
                float bestLogit = float.NegativeInfinity;
                int baseIdx = i * nc;
                for (int c = 0; c < nc; c++)
                {
                    float v = lbuf[baseIdx + c];
                    if (v > bestLogit) { bestLogit = v; best = c; }
                }
                float score = 1f / (1f + MathF.Exp(-bestLogit));

                float thr = confThreshold;
                if (perClassConf != null && best < perClassConf.Length)
                    thr = perClassConf[best];
                if (score < thr) continue;

                int b = i * 4;
                float cx = bbuf[b] * imgW, cy = bbuf[b + 1] * imgH;
                float w = bbuf[b + 2] * imgW, h = bbuf[b + 3] * imgH;
                AddBox(results, best, score, cx - w / 2, cy - h / 2, cx + w / 2, cy + h / 2, imgW, imgH);
            }
            return results;
        }

        private static void AddBox(List<DetectionResult> list, int cls, float score,
            float x1f, float y1f, float x2f, float y2f, int imgW, int imgH)
        {
            int x1 = Math.Clamp((int)MathF.Round(x1f), 0, imgW);
            int y1 = Math.Clamp((int)MathF.Round(y1f), 0, imgH);
            int x2 = Math.Clamp((int)MathF.Round(x2f), 0, imgW);
            int y2 = Math.Clamp((int)MathF.Round(y2f), 0, imgH);
            if (x2 <= x1 || y2 <= y1) return;

            list.Add(new DetectionResult
            {
                ClassId = cls,
                Confidence = score,
                X = x1,
                Y = y1,
                Width = x2 - x1,
                Height = y2 - y1
            });
        }

        private static bool IsNormalized(float[] boxes, int q)
        {
            float max = 0f;
            int n = Math.Min(boxes.Length, q * 4);
            for (int i = 0; i < n; i++) max = Math.Max(max, boxes[i]);
            return max <= 1.0001f;
        }

        private static long[] ReadLabels(DisposableNamedOnnxValue v)
        {
            try
            {
                return v.ElementType switch
                {
                    TensorElementType.Int64 => v.AsTensor<long>().ToArray(),
                    TensorElementType.Int32 => v.AsTensor<int>().ToArray().Select(x => (long)x).ToArray(),
                    TensorElementType.Float => v.AsTensor<float>().ToArray().Select(x => (long)MathF.Round(x)).ToArray(),
                    _ => Array.Empty<long>()
                };
            }
            catch
            {
                return Array.Empty<long>();
            }
        }
    }
}
