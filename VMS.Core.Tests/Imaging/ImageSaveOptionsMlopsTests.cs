using System;
using System.IO;
using VMS.Camera.Configuration;
using VMS.Core.Imaging;
using Xunit;

namespace VMS.Core.Tests.Imaging
{
    /// <summary>
    /// imageSave 의 MLOps 수집 키(mlopsSendNg · mlopsOkSampleRate) 로드 — 키가 없으면 기본(끔 · 1/200),
    /// 있으면 그대로, 범위 밖이면 클램프. 실제 system_config.json 경로를 읽으므로 별도 인스턴스로 격리한다.
    /// </summary>
    [Collection("AppDataPathsState")]
    public class ImageSaveOptionsMlopsTests : IDisposable
    {
        private readonly string? _origEnvValue;
        private readonly string _root;

        public ImageSaveOptionsMlopsTests()
        {
            _origEnvValue = Environment.GetEnvironmentVariable(AppDataPaths.EnvVarName);
            Environment.SetEnvironmentVariable(AppDataPaths.EnvVarName, "ut-imgsave-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            AppDataPaths.ResetForTests();
            _root = AppDataPaths.Root;
            Directory.CreateDirectory(_root);
        }

        public void Dispose()
        {
            try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { }
            Environment.SetEnvironmentVariable(AppDataPaths.EnvVarName, _origEnvValue);
            AppDataPaths.ResetForTests();
        }

        [Fact]
        public void Missing_keys_default_to_off_and_one_in_200()
        {
            File.WriteAllText(AppDataPaths.SystemConfigFile, "{\"imageSave\":{\"saveNgImages\":true}}");
            var o = ImageSaveOptions.LoadFromAppData();
            Assert.False(o.MlopsSendNg);
            Assert.Equal(ImageSaveOptions.DefaultMlopsOkSampleRate, o.MlopsOkSampleRate);
            Assert.Equal(200, ImageSaveOptions.DefaultMlopsOkSampleRate);
        }

        [Fact]
        public void Keys_are_read_when_present()
        {
            File.WriteAllText(AppDataPaths.SystemConfigFile,
                "{\"imageSave\":{\"saveNgImages\":true,\"mlopsSendNg\":true,\"mlopsOkSampleRate\":50}}");
            var o = ImageSaveOptions.LoadFromAppData();
            Assert.True(o.MlopsSendNg);
            Assert.Equal(50, o.MlopsOkSampleRate);
        }

        [Fact]
        public void Out_of_range_rate_is_clamped()
        {
            File.WriteAllText(AppDataPaths.SystemConfigFile, "{\"imageSave\":{\"mlopsSendNg\":true,\"mlopsOkSampleRate\":-5}}");
            Assert.Equal(0, ImageSaveOptions.LoadFromAppData().MlopsOkSampleRate);

            Assert.Equal(0, ImageSaveOptions.ClampMlopsOkSampleRate(-1));
            Assert.Equal(ImageSaveOptions.MaxMlopsOkSampleRate, ImageSaveOptions.ClampMlopsOkSampleRate(int.MaxValue));
            Assert.Equal(200, ImageSaveOptions.ClampMlopsOkSampleRate(200));
        }
    }
}
