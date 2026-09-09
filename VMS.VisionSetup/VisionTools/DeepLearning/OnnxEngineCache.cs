using OpenCvSharp;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using VMS.Core.DeepLearning;
using VMS.Core.Services;

namespace VMS.VisionSetup.VisionTools.DeepLearning
{
    /// <summary>
    /// 모델 경로 → 워밍업 완료된 엔진의 프로세스 전역 캐시.
    ///
    /// 목적:
    ///   (1) Recipe Load 시점에 모든 DL 도구의 ModelPath를 프리페치해, Step Load/Run 이전에 워밍업 진행.
    ///   (2) 동일 모델을 쓰는 여러 도구 인스턴스가 InferenceSession을 공유 (메모리 절약).
    ///
    /// 스레드 안전성:
    ///   ONNX Runtime InferenceSession.Run 은 동시 호출 안전(각 Run이 독립적 IoBinding/메모리 사용).
    ///   따라서 한 세션을 여러 도구가 공유해도 정합성 문제가 없다.
    ///
    /// 생명주기:
    ///   명시적 Clear 호출 전까지 엔진은 메모리에 남는다. 현재는 자동 축출/LRU을 두지 않음 —
    ///   레시피/모델이 다양해 메모리 사용이 문제되면 축출 정책 추가 검토.
    /// </summary>
    public static class OnnxEngineCache
    {
        private static readonly ConcurrentDictionary<string, Task<YoloOnnxEngine>> _yolo = new();
        private static readonly ConcurrentDictionary<string, Task<ClassifierOnnxEngine>> _cls = new();
        private static readonly ConcurrentDictionary<string, Task<AnomalyOnnxEngine>> _anom = new();
        private static readonly ConcurrentDictionary<string, Task<SegmentationOnnxEngine>> _seg = new();
        private static readonly ConcurrentDictionary<string, Task<YoloSegOnnxEngine>> _yoloSeg = new();
        private static readonly ConcurrentDictionary<string, Task<RfdetrSegOnnxEngine>> _rfdetrSeg = new();
        private static readonly ConcurrentDictionary<string, Task<IDetectionEngine>> _det = new();

        // ── Detector (DetectionTool — YOLO / D-FINE 규약 자동 판별) ──

        public static void PrefetchDetector(string modelPath, int inputSize = 640)
        {
            modelPath = Resolve(modelPath);
            if (!IsValidPath(modelPath)) return;
            _det.GetOrAdd(modelPath, p => Task.Run(() => CreateDetector(p, inputSize)));
        }

        public static IDetectionEngine GetDetector(string modelPath, int inputSize = 640)
        {
            modelPath = Resolve(modelPath);
            if (!IsValidPath(modelPath))
                return new YoloOnnxEngine(modelPath); // 캐시 못 쓰면 직접 생성 (FileNotFound 예외는 기존과 동일)

            var task = _det.GetOrAdd(modelPath, p => Task.Run(() => CreateDetector(p, inputSize)));
            return task.GetAwaiter().GetResult();
        }

        private static IDetectionEngine CreateDetector(string modelPath, int inputSize)
        {
            var sw = Stopwatch.StartNew();
            var format = DetectionModelFormatProbe.Probe(modelPath);
            IDetectionEngine engine = format == DetectionModelFormat.DFine
                ? new DFineOnnxEngine(modelPath)
                : new YoloOnnxEngine(modelPath);
            try
            {
                using var dummy = new Mat(inputSize, inputSize, MatType.CV_8UC3, Scalar.All(0));
                engine.Detect(dummy, inputSize, 0.25f, 0.45f, null);
            }
            catch (Exception ex) { Debug.WriteLine($"[OnnxCache] {format} warmup failed: {ex.Message}"); }
            Debug.WriteLine($"[OnnxCache] {format} loaded {Path.GetFileName(modelPath)} in {sw.ElapsedMilliseconds}ms (EP: {engine.ActiveProvider})");
            return engine;
        }

        // ── YOLO (DetectionTool) ──

        public static void PrefetchYolo(string modelPath, int inputSize = 640)
        {
            modelPath = Resolve(modelPath);
            if (!IsValidPath(modelPath)) return;
            _yolo.GetOrAdd(modelPath, p => Task.Run(() => CreateYolo(p, inputSize)));
        }

        public static YoloOnnxEngine GetYolo(string modelPath, int inputSize = 640)
        {
            modelPath = Resolve(modelPath);
            if (!IsValidPath(modelPath))
                return new YoloOnnxEngine(modelPath); // 캐시 못 쓰면 직접 생성 (File.Exists 실패 등)

            var task = _yolo.GetOrAdd(modelPath, p => Task.Run(() => CreateYolo(p, inputSize)));
            return task.GetAwaiter().GetResult();
        }

        private static YoloOnnxEngine CreateYolo(string modelPath, int inputSize)
        {
            var sw = Stopwatch.StartNew();
            var engine = new YoloOnnxEngine(modelPath);
            try
            {
                using var dummy = new Mat(inputSize, inputSize, MatType.CV_8UC3, Scalar.All(0));
                engine.Detect(dummy, inputSize, 0.25f, 0.45f, null);
            }
            catch (Exception ex) { Debug.WriteLine($"[OnnxCache] YOLO warmup failed: {ex.Message}"); }
            Debug.WriteLine($"[OnnxCache] YOLO loaded {Path.GetFileName(modelPath)} in {sw.ElapsedMilliseconds}ms (EP: {engine.ActiveProvider})");
            return engine;
        }

        // ── Classifier (ClassifyTool) ──

        public static void PrefetchClassifier(string modelPath, int inputWidth = 224, int inputHeight = 224,
            bool useImageNet = true)
        {
            modelPath = Resolve(modelPath);
            if (!IsValidPath(modelPath)) return;
            _cls.GetOrAdd(modelPath, p => Task.Run(() => CreateClassifier(p, inputWidth, inputHeight, useImageNet)));
        }

        public static ClassifierOnnxEngine GetClassifier(string modelPath, int inputWidth = 224, int inputHeight = 224,
            bool useImageNet = true)
        {
            modelPath = Resolve(modelPath);
            if (!IsValidPath(modelPath))
                return new ClassifierOnnxEngine(modelPath);

            var task = _cls.GetOrAdd(modelPath, p => Task.Run(() => CreateClassifier(p, inputWidth, inputHeight, useImageNet)));
            return task.GetAwaiter().GetResult();
        }

        private static ClassifierOnnxEngine CreateClassifier(string modelPath, int w, int h, bool useImageNet)
        {
            var sw = Stopwatch.StartNew();
            var engine = new ClassifierOnnxEngine(modelPath);
            try
            {
                using var dummy = new Mat(h, w, MatType.CV_8UC3, Scalar.All(0));
                engine.Classify(dummy, w, h, useImageNet);
            }
            catch (Exception ex) { Debug.WriteLine($"[OnnxCache] Classifier warmup failed: {ex.Message}"); }
            Debug.WriteLine($"[OnnxCache] Classifier loaded {Path.GetFileName(modelPath)} in {sw.ElapsedMilliseconds}ms (EP: {engine.ActiveProvider})");
            return engine;
        }

        // ── Anomaly (AnomalyTool) ──

        public static void PrefetchAnomaly(string modelPath, int inputSize = 224)
        {
            modelPath = Resolve(modelPath);
            if (!IsValidPath(modelPath)) return;
            _anom.GetOrAdd(modelPath, p => Task.Run(() => CreateAnomaly(p, inputSize)));
        }

        public static AnomalyOnnxEngine GetAnomaly(string modelPath, int inputSize = 224)
        {
            modelPath = Resolve(modelPath);
            if (!IsValidPath(modelPath))
                return new AnomalyOnnxEngine(modelPath);

            var task = _anom.GetOrAdd(modelPath, p => Task.Run(() => CreateAnomaly(p, inputSize)));
            return task.GetAwaiter().GetResult();
        }

        private static AnomalyOnnxEngine CreateAnomaly(string modelPath, int inputSize)
        {
            var sw = Stopwatch.StartNew();
            var engine = new AnomalyOnnxEngine(modelPath);
            try
            {
                using var dummy = new Mat(inputSize, inputSize, MatType.CV_8UC3, Scalar.All(0));
                engine.Detect(dummy, inputSize);
            }
            catch (Exception ex) { Debug.WriteLine($"[OnnxCache] Anomaly warmup failed: {ex.Message}"); }
            Debug.WriteLine($"[OnnxCache] Anomaly loaded {Path.GetFileName(modelPath)} in {sw.ElapsedMilliseconds}ms (EP: {engine.ActiveProvider})");
            return engine;
        }

        // ── Segmentation (SegmentationTool) ──

        public static void PrefetchSegmentation(string modelPath, int inputSize = 512, bool useImageNet = true)
        {
            modelPath = Resolve(modelPath);
            if (!IsValidPath(modelPath)) return;
            _seg.GetOrAdd(modelPath, p => Task.Run(() => CreateSegmentation(p, inputSize, useImageNet)));
        }

        public static SegmentationOnnxEngine GetSegmentation(string modelPath, int inputSize = 512, bool useImageNet = true)
        {
            modelPath = Resolve(modelPath);
            if (!IsValidPath(modelPath))
                return new SegmentationOnnxEngine(modelPath);

            var task = _seg.GetOrAdd(modelPath, p => Task.Run(() => CreateSegmentation(p, inputSize, useImageNet)));
            return task.GetAwaiter().GetResult();
        }

        private static SegmentationOnnxEngine CreateSegmentation(string modelPath, int inputSize, bool useImageNet)
        {
            var sw = Stopwatch.StartNew();
            var engine = new SegmentationOnnxEngine(modelPath);
            try
            {
                using var dummy = new Mat(inputSize, inputSize, MatType.CV_8UC3, Scalar.All(0));
                engine.Segment(dummy, inputSize, inputSize, useImageNet);
            }
            catch (Exception ex) { Debug.WriteLine($"[OnnxCache] Segmentation warmup failed: {ex.Message}"); }
            Debug.WriteLine($"[OnnxCache] Segmentation loaded {Path.GetFileName(modelPath)} in {sw.ElapsedMilliseconds}ms (EP: {engine.ActiveProvider})");
            return engine;
        }

        // ── YoloSeg (YoloSegTool) ──

        public static void PrefetchYoloSeg(string modelPath, int inputSize = 640)
        {
            modelPath = Resolve(modelPath);
            if (!IsValidPath(modelPath)) return;
            _yoloSeg.GetOrAdd(modelPath, p => Task.Run(() => CreateYoloSeg(p, inputSize)));
        }

        public static YoloSegOnnxEngine GetYoloSeg(string modelPath, int inputSize = 640)
        {
            modelPath = Resolve(modelPath);
            if (!IsValidPath(modelPath))
                return new YoloSegOnnxEngine(modelPath);

            var task = _yoloSeg.GetOrAdd(modelPath, p => Task.Run(() => CreateYoloSeg(p, inputSize)));
            return task.GetAwaiter().GetResult();
        }

        private static YoloSegOnnxEngine CreateYoloSeg(string modelPath, int inputSize)
        {
            var sw = Stopwatch.StartNew();
            var engine = new YoloSegOnnxEngine(modelPath);
            try
            {
                using var dummy = new Mat(inputSize, inputSize, MatType.CV_8UC3, Scalar.All(0));
                engine.Segment(dummy, inputSize, 0.25f, 0.45f, 0.5f);
            }
            catch (Exception ex) { Debug.WriteLine($"[OnnxCache] YoloSeg warmup failed: {ex.Message}"); }
            Debug.WriteLine($"[OnnxCache] YoloSeg loaded {Path.GetFileName(modelPath)} in {sw.ElapsedMilliseconds}ms (EP: {engine.ActiveProvider})");
            return engine;
        }

        // ── RfdetrSeg (RfdetrSegTool) ──

        public static void PrefetchRfdetrSeg(string modelPath, int inputSize = 560)
        {
            modelPath = Resolve(modelPath);
            if (!IsValidPath(modelPath)) return;
            _rfdetrSeg.GetOrAdd(modelPath, p => Task.Run(() => CreateRfdetrSeg(p, inputSize)));
        }

        public static RfdetrSegOnnxEngine GetRfdetrSeg(string modelPath, int inputSize = 560)
        {
            modelPath = Resolve(modelPath);
            if (!IsValidPath(modelPath))
                return new RfdetrSegOnnxEngine(modelPath);

            var task = _rfdetrSeg.GetOrAdd(modelPath, p => Task.Run(() => CreateRfdetrSeg(p, inputSize)));
            return task.GetAwaiter().GetResult();
        }

        private static RfdetrSegOnnxEngine CreateRfdetrSeg(string modelPath, int inputSize)
        {
            var sw = Stopwatch.StartNew();
            var engine = new RfdetrSegOnnxEngine(modelPath);
            // 예열은 모델이 실제로 받는 크기로 한다. 도구의 InputSize 는 모델을 열기 전 값이라
            // 배수가 안 맞으면 예열이 헛돌고 첫 실제 추론이 콜드 스타트를 그대로 치른다.
            int warm = engine.ModelInputSize > 0 ? engine.ModelInputSize : inputSize;
            try
            {
                using var dummy = new Mat(warm, warm, MatType.CV_8UC3, Scalar.All(0));
                engine.Segment(dummy, warm, 0.5f, 100);
            }
            catch (Exception ex) { Debug.WriteLine($"[OnnxCache] RfdetrSeg warmup failed: {ex.Message}"); }
            Debug.WriteLine($"[OnnxCache] RfdetrSeg loaded {Path.GetFileName(modelPath)} in {sw.ElapsedMilliseconds}ms (EP: {engine.ActiveProvider})");
            return engine;
        }

        // ── 공통 ──

        private static bool IsValidPath(string? modelPath)
            => !string.IsNullOrEmpty(modelPath) && File.Exists(modelPath);

        /// <summary>
        /// 레시피가 담은 값이 model:// 참조면 이미 받아 둔 로컬 파일로 바꾼다.
        /// 참조가 아니면(= 예전처럼 절대 경로면) 그대로 돌려준다.
        ///
        /// <para>
        /// 여기서 네트워크를 쓰지 않는 것이 중요하다 — 검사 한 장 도는 사이에 HTTP 를 기다릴 수 없다.
        /// 아직 안 받은 참조는 원문 그대로 돌려보내 IsValidPath 에서 걸리게 한다.
        /// 그래야 "model://… 를 찾을 수 없습니다" 라고 무엇이 없는지가 오류에 남는다.
        /// 실제 내려받기는 레시피를 열 때 RecipeService 가 먼저 해 둔다.
        /// </para>
        /// </summary>
        private static string Resolve(string modelPath)
        {
            if (!ModelReference.IsReference(modelPath)) return modelPath;
            return ModelReferenceResolver.Current?.ToLocalPath(modelPath) ?? modelPath;
        }

        /// <summary>모든 엔진을 Dispose하고 캐시를 비운다. 앱 종료나 전체 리셋 시 호출.</summary>
        public static void Clear()
        {
            DisposeAll(_yolo);
            DisposeAll(_det);
            DisposeAll(_cls);
            DisposeAll(_anom);
            DisposeAll(_seg);
            DisposeAll(_yoloSeg);
            DisposeAll(_rfdetrSeg);
        }

        private static void DisposeAll<T>(ConcurrentDictionary<string, Task<T>> dict) where T : IDisposable
        {
            foreach (var kv in dict)
            {
                try
                {
                    if (kv.Value.IsCompletedSuccessfully)
                        kv.Value.Result.Dispose();
                }
                catch { }
            }
            dict.Clear();
        }
    }
}
