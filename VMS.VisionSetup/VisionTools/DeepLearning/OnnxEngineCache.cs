using OpenCvSharp;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

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

        // ── YOLO (DetectionTool) ──

        public static void PrefetchYolo(string modelPath, int inputSize = 640)
        {
            if (!IsValidPath(modelPath)) return;
            _yolo.GetOrAdd(modelPath, p => Task.Run(() => CreateYolo(p, inputSize)));
        }

        public static YoloOnnxEngine GetYolo(string modelPath, int inputSize = 640)
        {
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
            if (!IsValidPath(modelPath)) return;
            _cls.GetOrAdd(modelPath, p => Task.Run(() => CreateClassifier(p, inputWidth, inputHeight, useImageNet)));
        }

        public static ClassifierOnnxEngine GetClassifier(string modelPath, int inputWidth = 224, int inputHeight = 224,
            bool useImageNet = true)
        {
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
            if (!IsValidPath(modelPath)) return;
            _anom.GetOrAdd(modelPath, p => Task.Run(() => CreateAnomaly(p, inputSize)));
        }

        public static AnomalyOnnxEngine GetAnomaly(string modelPath, int inputSize = 224)
        {
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

        // ── 공통 ──

        private static bool IsValidPath(string? modelPath)
            => !string.IsNullOrEmpty(modelPath) && File.Exists(modelPath);

        /// <summary>모든 엔진을 Dispose하고 캐시를 비운다. 앱 종료나 전체 리셋 시 호출.</summary>
        public static void Clear()
        {
            DisposeAll(_yolo);
            DisposeAll(_cls);
            DisposeAll(_anom);
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
