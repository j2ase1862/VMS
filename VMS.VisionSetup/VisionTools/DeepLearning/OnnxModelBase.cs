using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using VMS.Core.Security;

namespace VMS.VisionSetup.VisionTools.DeepLearning
{
    /// <summary>
    /// ONNX 모델 로드 실패 시 throw 되는 명시적 예외 — 손상/조작된 모델 파일,
    /// 호환 안 되는 OpRange, 메모리 부족 등 native ONNX Runtime 실패를 .NET 예외로 wrap.
    /// 호출자는 본 예외만 잡으면 모델 관련 결함을 모두 처리할 수 있어 sequence engine
    /// 무한 폴백 부담을 줄인다. GS 인증 결함 허용성 요구사항 대응 (PR P1-#4).
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
    /// 추론 실행 제공자(Execution Provider) 우선순위
    /// </summary>
    public enum OnnxExecutionProvider
    {
        /// <summary>사용 가능한 최상위 EP 자동 선택 (CUDA → DirectML → CPU)</summary>
        Auto,
        Cpu,
        Cuda,
        DirectML,
        TensorRT
    }

    /// <summary>
    /// ONNX 추론 공용 베이스.
    /// YOLOv8, Classification, Anomaly 모델에서 공통으로 사용하는 전처리/세션 관리를 제공합니다.
    /// </summary>
    public abstract class OnnxModelBase : IDisposable
    {
        protected InferenceSession? _session;
        private bool _disposed;

        /// <summary>사용자가 원하는 기본 실행 제공자 (Auto 권장)</summary>
        public static OnnxExecutionProvider PreferredProvider { get; set; } = OnnxExecutionProvider.Auto;

        /// <summary>
        /// TensorRT 엔진 캐시 폴더 (재실행 시 재빌드 방지). 빈 문자열이면 캐시 비활성.
        /// 기본값: %LocalAppData%\BODA VISION AI\trt_cache — TRT를 처음 쓰는 사용자도 자동 캐시가
        /// 동작하게 한다. TRT 엔진 빌드는 모델당 30~60초가 걸릴 수 있어 캐시가 없으면 매 실행마다 반복된다.
        /// </summary>
        public static string TensorRTCachePath { get; set; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BODA VISION AI", "trt_cache");

        /// <summary>TensorRT 빌드 시 FP16 최적화 활성화 (RTX 이상 GPU에서 2~3배 속도 향상)</summary>
        public static bool TensorRTFp16 { get; set; } = true;

        /// <summary>현재 세션에서 실제로 활성화된 EP (LoadModel 이후 유효)</summary>
        public string ActiveProvider { get; private set; } = "CPU";

        protected void LoadModel(string modelPath)
            => LoadModel(modelPath, PreferredProvider);

        protected void LoadModel(string modelPath, OnnxExecutionProvider preferred)
        {
            if (!File.Exists(modelPath))
                throw new FileNotFoundException($"ONNX 모델을 찾을 수 없습니다: {modelPath}");

            var options = new SessionOptions
            {
                InterOpNumThreads = 1,
                IntraOpNumThreads = Math.Max(1, Environment.ProcessorCount / 2),
                GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL
            };

            ActiveProvider = AttachExecutionProvider(options, preferred);

            _session?.Dispose();

            // GS 결함 허용성: 손상/조작된 ONNX 파일이 native exception (OnnxRuntimeException,
            // AccessViolationException 등) 으로 sequence engine 전체를 다운시키지 않도록 wrap.
            // 호출자는 OnnxLoadException 만 catch 하면 모델 결함을 일관 처리 가능.
            try
            {
                _session = new InferenceSession(modelPath, options);
            }
            catch (Exception ex)
            {
                _session = null;
                AuditLogger.Instance.Log(
                    AuditCategory.Inspection,
                    "Model Configuration Load Error",
                    AuditOutcome.Failure,
                    source: nameof(OnnxModelBase),
                    details: $"path={modelPath} provider={ActiveProvider} error={ex.GetType().Name}: {ex.Message}");
                throw new OnnxLoadException(
                    modelPath,
                    $"ONNX 모델 로드 실패 ({Path.GetFileName(modelPath)}): {ex.Message}",
                    ex);
            }
        }

        /// <summary>
        /// 시스템에 설치된 ORT가 주어진 EP를 지원하는지 빠르게 점검합니다.
        /// 실제 로드 전 UI 표시용 (실패 시에도 예외 없이 bool 반환).
        /// </summary>
        public static bool IsProviderAvailable(OnnxExecutionProvider ep)
        {
            try
            {
                using var testOptions = new SessionOptions();
                return ep switch
                {
                    OnnxExecutionProvider.Cpu      => true,
                    OnnxExecutionProvider.Cuda     => TryAppendProvider(testOptions, "CUDA"),
                    OnnxExecutionProvider.DirectML => TryAppendProvider(testOptions, "DML"),
                    OnnxExecutionProvider.TensorRT => TryAppendProvider(testOptions, "TensorRT"),
                    _ => true
                };
            }
            catch { return false; }
        }

        /// <summary>
        /// preferred 순서대로 EP를 시도하고, 성공한 EP 이름을 반환합니다.
        /// 실패 시 CPU로 폴백합니다.
        /// </summary>
        private static string AttachExecutionProvider(SessionOptions options, OnnxExecutionProvider preferred)
        {
            // 시도 순서 결정
            var order = preferred switch
            {
                OnnxExecutionProvider.Cpu      => new[] { "CPU" },
                OnnxExecutionProvider.Cuda     => new[] { "CUDA", "CPU" },
                OnnxExecutionProvider.DirectML => new[] { "DML", "CPU" },
                OnnxExecutionProvider.TensorRT => new[] { "TensorRT", "CUDA", "CPU" },
                _ /* Auto */                   => new[] { "CUDA", "DML", "CPU" }
            };

            foreach (var ep in order)
            {
                if (TryAppendProvider(options, ep))
                    return ep;
            }
            return "CPU";
        }

        /// <summary>
        /// 리플렉션 기반으로 EP 메서드를 호출합니다.
        /// GPU 런타임 DLL이 없는 경우 예외를 삼키고 false를 반환합니다.
        /// (Microsoft.ML.OnnxRuntime / .Gpu / .DirectML 패키지 차이에 따른 API 존재 여부를 모두 커버)
        /// </summary>
        private static bool TryAppendProvider(SessionOptions options, string providerName)
        {
            try
            {
                switch (providerName)
                {
                    case "CPU":
                        // CPU EP는 항상 기본으로 등록되어 있음
                        return true;

                    case "CUDA":
                    {
                        var m = typeof(SessionOptions).GetMethod(
                            "AppendExecutionProvider_CUDA",
                            BindingFlags.Public | BindingFlags.Instance,
                            binder: null, types: new[] { typeof(int) }, modifiers: null);
                        if (m == null) return false;
                        m.Invoke(options, new object[] { 0 });
                        return true;
                    }

                    case "DML":
                    {
                        var m = typeof(SessionOptions).GetMethod(
                            "AppendExecutionProvider_DML",
                            BindingFlags.Public | BindingFlags.Instance,
                            binder: null, types: new[] { typeof(int) }, modifiers: null);
                        if (m == null) return false;
                        m.Invoke(options, new object[] { 0 });
                        return true;
                    }

                    case "TensorRT":
                    {
                        // ORT 1.21은 공개 API AppendExecutionProvider(string, Dictionary<,>) 를 지원.
                        // 리플렉션 제거해 조용한 실패(→캐시 미적용)를 방지.
                        if (TryAppendTensorRTWithOptions(options))
                        {
                            System.Diagnostics.Debug.WriteLine("[ORT] TensorRT EP attached with cache options");
                            return true;
                        }

                        // 폴백: 옵션 없이 TRT EP만 붙임 (캐시 비활성, 매 실행 재빌드)
                        System.Diagnostics.Debug.WriteLine(
                            "[ORT] TensorRT EP attach with options FAILED — falling back to plain EP (캐시 비활성, 매 Run 재빌드)");
                        var m = typeof(SessionOptions).GetMethod(
                            "AppendExecutionProvider_Tensorrt",
                            BindingFlags.Public | BindingFlags.Instance,
                            binder: null, types: new[] { typeof(int) }, modifiers: null);
                        if (m == null) return false;
                        m.Invoke(options, new object[] { 0 });
                        return true;
                    }

                    default:
                        return false;
                }
            }
            catch
            {
                // 런타임 DLL 미존재/CUDA 초기화 실패 등 → 폴백
                return false;
            }
        }

        /// <summary>
        /// TensorRT Provider Options(FP16, 엔진·타이밍 캐시)를 지정한 등록 경로.
        /// ORT 1.14+의 전용 API AppendExecutionProvider_Tensorrt(OrtTensorRTProviderOptions) 사용.
        /// (일반형 AppendExecutionProvider(string, Dict)은 TRT를 받지 않고 InvalidArgument를 던진다.)
        ///
        /// 캐시 옵션:
        ///   - trt_engine_cache_*  : 최적화된 TRT 엔진 바이너리를 디스크에 저장 (재빌드 30~60s → 2~5s)
        ///   - trt_timing_cache_*  : 커널 튜닝 결과 저장 (첫 빌드 자체도 빨라짐)
        /// </summary>
        private static bool TryAppendTensorRTWithOptions(SessionOptions options)
        {
            OrtTensorRTProviderOptions? trtOptions = null;
            try
            {
                trtOptions = new OrtTensorRTProviderOptions();

                var optDict = new Dictionary<string, string>
                {
                    ["device_id"] = "0",
                    ["trt_fp16_enable"] = TensorRTFp16 ? "1" : "0"
                };

                if (!string.IsNullOrWhiteSpace(TensorRTCachePath))
                {
                    Directory.CreateDirectory(TensorRTCachePath);
                    optDict["trt_engine_cache_enable"] = "1";
                    optDict["trt_engine_cache_path"] = TensorRTCachePath;
                    optDict["trt_timing_cache_enable"] = "1";
                    optDict["trt_timing_cache_path"] = TensorRTCachePath;

                    int existing = Directory.GetFiles(TensorRTCachePath).Length;
                    System.Diagnostics.Debug.WriteLine(
                        $"[ORT] TRT cache dir: {TensorRTCachePath} (기존 파일 {existing}개)");
                }

                trtOptions.UpdateOptions(optDict);
                options.AppendExecutionProvider_Tensorrt(trtOptions);
                // OrtTensorRTProviderOptions는 SessionOptions가 레퍼런스로 쥐고 있으므로 여기서 Dispose하지 않는다.
                // 세션 빌드 후 SessionOptions가 해제될 때 함께 정리 (또는 GC 파이널라이저).
                return true;
            }
            catch (Exception ex)
            {
                trtOptions?.Dispose();
                System.Diagnostics.Debug.WriteLine($"[ORT] TRT options attach exception: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// ONNX 메타데이터에서 클래스 이름을 읽습니다.
        /// Ultralytics 모델은 metadata에 "names" 키로 {0: 'class0', 1: 'class1', ...} 형태로 저장합니다.
        /// </summary>
        protected string[] ReadClassNamesFromMetadata()
        {
            if (_session == null) return Array.Empty<string>();

            var metadata = _session.ModelMetadata.CustomMetadataMap;
            if (!metadata.TryGetValue("names", out var namesStr))
                return Array.Empty<string>();

            return ParseUltralyticsNames(namesStr);
        }

        /// <summary>
        /// InferenceSession을 만들지 않고 ONNX 파일에서 직접 클래스 이름을 읽는다.
        /// Recipe Load 중 메타데이터만 필요할 때 사용 — 대용량 모델 로딩 오버헤드를 피한다.
        /// </summary>
        public static string[] ReadClassNamesFromFile(string modelPath)
        {
            if (string.IsNullOrEmpty(modelPath) || !File.Exists(modelPath))
                return Array.Empty<string>();

            var metadata = OnnxMetadataReader.Read(modelPath);
            return metadata.TryGetValue("names", out var namesStr)
                ? ParseUltralyticsNames(namesStr)
                : Array.Empty<string>();
        }

        private static string[] ParseUltralyticsNames(string namesStr)
        {
            try
            {
                // "{0: 'object', 1: 'logo'}" 형태 파싱
                var entries = new SortedDictionary<int, string>();
                var cleaned = namesStr.Trim('{', '}');
                foreach (var pair in cleaned.Split(','))
                {
                    var parts = pair.Split(':', 2);
                    if (parts.Length == 2 &&
                        int.TryParse(parts[0].Trim(), out int idx))
                    {
                        var name = parts[1].Trim().Trim('\'', '"', ' ');
                        entries[idx] = name;
                    }
                }
                return entries.Values.ToArray();
            }
            catch
            {
                return Array.Empty<string>();
            }
        }

        /// <summary>
        /// 이미지를 NCHW float 텐서로 변환 (mean/std 정규화).
        /// 성능 주의: DenseTensor의 4D 인덱서는 매 호출마다 bounds check + stride 곱셈을 수행하므로
        /// 640×640 입력 기준 약 1.2M 번 호출되면 수백 ms가 소요된다.
        /// 버퍼 Span에 직접 쓰는 방식으로 교체해 인덱서 오버헤드를 제거한다.
        /// </summary>
        protected static DenseTensor<float> PreprocessImage(Mat image, int targetW, int targetH,
            float[] mean, float[] std)
        {
            using var resized = new Mat();
            Cv2.Resize(image, resized, new Size(targetW, targetH));

            using var rgb = new Mat();
            if (resized.Channels() == 1)
                Cv2.CvtColor(resized, rgb, ColorConversionCodes.GRAY2RGB);
            else if (resized.Channels() == 4)
                Cv2.CvtColor(resized, rgb, ColorConversionCodes.BGRA2RGB);
            else
                Cv2.CvtColor(resized, rgb, ColorConversionCodes.BGR2RGB);

            var tensor = new DenseTensor<float>(new[] { 1, 3, targetH, targetW });
            var dst = tensor.Buffer.Span;

            int channelStride = targetH * targetW;
            int rChannelBase = 0 * channelStride;
            int gChannelBase = 1 * channelStride;
            int bChannelBase = 2 * channelStride;

            // (pixel/255 - mean) / std = pixel * (1/(255*std)) - mean/std
            // → 픽셀당 곱셈 1회 + 뺄셈 1회로 축약
            float scaleR = 1f / (255f * std[0]);
            float scaleG = 1f / (255f * std[1]);
            float scaleB = 1f / (255f * std[2]);
            float offR = mean[0] / std[0];
            float offG = mean[1] / std[1];
            float offB = mean[2] / std[2];

            unsafe
            {
                byte* ptr = (byte*)rgb.Data;
                int stride = (int)rgb.Step();

                for (int y = 0; y < targetH; y++)
                {
                    byte* row = ptr + y * stride;
                    int rowOffset = y * targetW;
                    for (int x = 0; x < targetW; x++)
                    {
                        int src = x * 3;
                        int dstOffset = rowOffset + x;
                        dst[rChannelBase + dstOffset] = row[src] * scaleR - offR;
                        dst[gChannelBase + dstOffset] = row[src + 1] * scaleG - offG;
                        dst[bChannelBase + dstOffset] = row[src + 2] * scaleB - offB;
                    }
                }
            }

            return tensor;
        }

        /// <summary>
        /// 단순 0~1 정규화 (mean=0, std=1)
        /// </summary>
        protected static DenseTensor<float> PreprocessImageSimple(Mat image, int targetW, int targetH)
        {
            return PreprocessImage(image, targetW, targetH,
                new[] { 0f, 0f, 0f }, new[] { 1f, 1f, 1f });
        }

        /// <summary>
        /// ImageNet 정규화
        /// </summary>
        protected static DenseTensor<float> PreprocessImageNet(Mat image, int targetW, int targetH)
        {
            return PreprocessImage(image, targetW, targetH,
                new[] { 0.485f, 0.456f, 0.406f }, new[] { 0.229f, 0.224f, 0.225f });
        }

        /// <summary>
        /// CLAHE(Contrast Limited Adaptive Histogram Equalization)로 대비를 향상시킵니다.
        /// 컬러 이미지는 LAB 색공간의 L 채널에만 적용해 색상 왜곡을 방지합니다.
        /// 공장 조명 변동 환경에서 결함 검출률을 개선하는 전처리 옵션입니다.
        /// </summary>
        public static Mat ApplyClahe(Mat src, double clipLimit = 2.0, int tileGridSize = 8)
        {
            if (src == null || src.Empty()) return src!;
            int tile = Math.Max(2, tileGridSize);
            var size = new Size(tile, tile);

            if (src.Channels() == 1)
            {
                var gray = new Mat();
                using var clahe = Cv2.CreateCLAHE(clipLimit, size);
                clahe.Apply(src, gray);
                return gray;
            }

            // BGR/BGRA → LAB → L 채널 CLAHE → BGR 복원
            using var bgr = src.Channels() == 4 ? src.CvtColor(ColorConversionCodes.BGRA2BGR) : src.Clone();
            using var lab = new Mat();
            Cv2.CvtColor(bgr, lab, ColorConversionCodes.BGR2Lab);
            var channels = Cv2.Split(lab);
            try
            {
                using var clahe = Cv2.CreateCLAHE(clipLimit, size);
                clahe.Apply(channels[0], channels[0]);
                Cv2.Merge(channels, lab);
                var outImg = new Mat();
                Cv2.CvtColor(lab, outImg, ColorConversionCodes.Lab2BGR);
                return outImg;
            }
            finally
            {
                foreach (var c in channels) c.Dispose();
            }
        }

        protected List<NamedOnnxValue> CreateInput(string name, DenseTensor<float> tensor)
        {
            return new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor(name, tensor)
            };
        }

        protected string GetInputName()
        {
            return _session?.InputNames.FirstOrDefault() ?? "images";
        }

        public bool IsLoaded => _session != null;

        public void Dispose()
        {
            if (!_disposed)
            {
                _session?.Dispose();
                _session = null;
                _disposed = true;
            }
            GC.SuppressFinalize(this);
        }
    }
}
