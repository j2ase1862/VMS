using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using VMS.VisionSetup.VisionTools.DeepLearning;

namespace VMS.VisionSetup.Services
{
    /// <summary>
    /// ONNX Runtime 전역 설정(%LocalAppData%/BODA VISION AI/system_config.json 내 3개 키)을
    /// 읽고/쓰는 서비스. App.xaml.cs의 LoadAIConfig와 동일한 파일을 공유한다.
    ///
    /// 관리 키:
    ///   - onnxExecutionProvider : string (Auto/Cpu/Cuda/DirectML/TensorRT)
    ///   - tensorRTCachePath     : string
    ///   - tensorRTFp16          : bool
    /// </summary>
    public static class OnnxSettingsService
    {
        private const string EpKey = "onnxExecutionProvider";
        private const string CacheKey = "tensorRTCachePath";
        private const string Fp16Key = "tensorRTFp16";

        public static string ConfigPath => VMS.Camera.Configuration.AppDataPaths.SystemConfigFile;

        public readonly record struct OnnxSettings(
            OnnxExecutionProvider Provider,
            string TensorRTCachePath,
            bool TensorRTFp16);

        /// <summary>파일이 없거나 키가 없으면 OnnxModelBase의 현재(기본) 값을 반환한다.</summary>
        public static OnnxSettings Load()
        {
            var ep = OnnxModelBase.PreferredProvider;
            var cache = OnnxModelBase.TensorRTCachePath;
            var fp16 = OnnxModelBase.TensorRTFp16;

            try
            {
                if (!File.Exists(ConfigPath))
                    return new OnnxSettings(ep, cache, fp16);

                var json = File.ReadAllText(ConfigPath);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (root.TryGetProperty(EpKey, out var epProp) &&
                    Enum.TryParse<OnnxExecutionProvider>(epProp.GetString(), true, out var parsedEp))
                    ep = parsedEp;

                if (root.TryGetProperty(CacheKey, out var cacheProp))
                {
                    var cachePath = cacheProp.GetString();
                    // 빈 문자열이면 OnnxModelBase의 기본 캐시 경로를 유지한다 (App.xaml.cs LoadAIConfig와 동일 정책).
                    if (!string.IsNullOrWhiteSpace(cachePath))
                        cache = cachePath;
                }

                if (root.TryGetProperty(Fp16Key, out var fp16Prop))
                    fp16 = fp16Prop.GetBoolean();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[OnnxSettings] Load failed: {ex.Message}");
            }

            return new OnnxSettings(ep, cache, fp16);
        }

        /// <summary>
        /// system_config.json의 다른 키는 보존하면서 ONNX 관련 3개 키만 갱신한 뒤,
        /// OnnxModelBase 정적 속성에도 즉시 반영한다.
        /// </summary>
        public static void Save(OnnxSettings settings)
        {
            try
            {
                var dir = Path.GetDirectoryName(ConfigPath);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                JsonObject root;
                if (File.Exists(ConfigPath))
                {
                    var existing = File.ReadAllText(ConfigPath);
                    root = JsonNode.Parse(existing) as JsonObject ?? new JsonObject();
                }
                else
                {
                    root = new JsonObject();
                }

                root[EpKey] = settings.Provider.ToString();
                root[CacheKey] = settings.TensorRTCachePath ?? string.Empty;
                root[Fp16Key] = settings.TensorRTFp16;

                var options = new JsonSerializerOptions { WriteIndented = true };
                File.WriteAllText(ConfigPath, root.ToJsonString(options));

                // 정적 속성 즉시 반영 — 이후 생성되는 InferenceSession부터 새 설정을 사용.
                OnnxModelBase.PreferredProvider = settings.Provider;
                OnnxModelBase.TensorRTCachePath = settings.TensorRTCachePath ?? string.Empty;
                OnnxModelBase.TensorRTFp16 = settings.TensorRTFp16;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[OnnxSettings] Save failed: {ex.Message}");
                throw;
            }
        }
    }
}
