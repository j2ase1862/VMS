using System;
using System.IO;
using VMS.VisionSetup.Services;
using VMS.VisionSetup.VisionTools.DeepLearning;
using Xunit;

namespace VMS.VisionSetup.Tests
{
    /// <summary>
    /// [ONNX 설정] 값(system_config.json 의 3개 키)을 읽어 OnnxModelBase 에 반영하는 규칙 — VisionSetup 과
    /// VMS 본체가 같은 OnnxSettingsService.ApplySaved 를 쓴다. 본체가 이걸 안 부르면 AUTO RUN 이 설정과 무관하게
    /// Auto(CUDA → DML → CPU)로 돌던 결함의 회귀 방지.
    /// </summary>
    public class OnnxSettingsApplyTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), $"onnx_settings_{Guid.NewGuid():N}");
        private readonly OnnxExecutionProvider _savedProvider = OnnxModelBase.PreferredProvider;
        private readonly string _savedCache = OnnxModelBase.TensorRTCachePath;
        private readonly bool _savedFp16 = OnnxModelBase.TensorRTFp16;

        public OnnxSettingsApplyTests() => Directory.CreateDirectory(_dir);

        public void Dispose()
        {
            OnnxModelBase.PreferredProvider = _savedProvider;
            OnnxModelBase.TensorRTCachePath = _savedCache;
            OnnxModelBase.TensorRTFp16 = _savedFp16;
            try { Directory.Delete(_dir, true); } catch { }
        }

        private string WriteConfig(string json)
        {
            var path = Path.Combine(_dir, "system_config.json");
            File.WriteAllText(path, json);
            return path;
        }

        [Fact]
        public void Load_reads_provider_cache_and_fp16_ignoring_other_keys()
        {
            var path = WriteConfig("""
                { "webServerUrl": "http://x", "onnxExecutionProvider": "tensorrt",
                  "tensorRTCachePath": "D:\\trt", "tensorRTFp16": false }
                """);

            var s = OnnxSettingsService.Load(path);

            Assert.Equal(OnnxExecutionProvider.TensorRT, s.Provider);
            Assert.Equal(@"D:\trt", s.TensorRTCachePath);
            Assert.False(s.TensorRTFp16);
        }

        [Fact]
        public void Load_keeps_current_values_when_keys_missing_or_cache_blank()
        {
            // 빈 캐시 경로는 기본 캐시 유지 — 비우면 TRT 엔진을 매번 다시 빌드(30~60초)한다.
            var path = WriteConfig("""{ "tensorRTCachePath": "  ", "onnxExecutionProvider": "없는값" }""");

            var s = OnnxSettingsService.Load(path);

            Assert.Equal(OnnxModelBase.PreferredProvider, s.Provider);
            Assert.Equal(OnnxModelBase.TensorRTCachePath, s.TensorRTCachePath);
            Assert.Equal(OnnxModelBase.TensorRTFp16, s.TensorRTFp16);
        }

        [Fact]
        public void Load_missing_or_broken_file_returns_current_values()
        {
            var missing = OnnxSettingsService.Load(Path.Combine(_dir, "none.json"));
            Assert.Equal(OnnxModelBase.PreferredProvider, missing.Provider);

            var broken = OnnxSettingsService.Load(WriteConfig("{ not json"));
            Assert.Equal(OnnxModelBase.PreferredProvider, broken.Provider);
        }

        [Fact]
        public void Apply_sets_the_statics_used_by_new_sessions()
        {
            OnnxSettingsService.Apply(new OnnxSettingsService.OnnxSettings(
                OnnxExecutionProvider.Cpu, @"D:\trt_apply", false));

            Assert.Equal(OnnxExecutionProvider.Cpu, OnnxModelBase.PreferredProvider);
            Assert.Equal(@"D:\trt_apply", OnnxModelBase.TensorRTCachePath);
            Assert.False(OnnxModelBase.TensorRTFp16);
        }
    }
}
