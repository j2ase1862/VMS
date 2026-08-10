using System;
using System.IO;
using System.Text.Json;
using VMS.AppSetup.Models;
using VMS.AppSetup.Services;
using Xunit;

namespace VMS.AppSetup.Tests
{
    /// <summary>
    /// ConfigurationService 저장 경로의 병합 검증 (현장 검증 2026-08-10) —
    /// 마법사 재저장이 SetupConfiguration 모델에 없는 키(VMS 가 기록한 카메라별
    /// steps/stepCount, 각 설정 화면의 보존정책·imageSave 등)를 지우면
    /// "AppSetup 을 다시 돌리면 카메라 노출/게인이 초기화"되는 사고가 된다.
    /// </summary>
    public class ConfigurationServiceMergeSaveTests : IDisposable
    {
        private readonly string _dir = Path.Combine(
            Path.GetTempPath(), $"vms_configsvc_merge_{Guid.NewGuid():N}");

        public void Dispose()
        {
            if (Directory.Exists(_dir))
                Directory.Delete(_dir, recursive: true);
        }

        private string ConfigPath => Path.Combine(_dir, "system_config.json");

        [Fact]
        public void SaveConfiguration_PreservesCameraSteps_WrittenByVms()
        {
            Directory.CreateDirectory(_dir);
            File.WriteAllText(ConfigPath, @"{
              ""applicationName"": ""OP102"",
              ""cameras"": [
                {
                  ""id"": ""cam-1"", ""name"": ""1"",
                  ""stepCount"": 1,
                  ""steps"": [ { ""stepNumber"": 1, ""use2DCameraDefault"": false, ""exposure"": 12345, ""gain"": 2.5 } ],
                  ""model"": ""LOG-M"", ""serialNumber"": ""SN001""
                }
              ],
              ""auditRetentionDays"": 60,
              ""imageSave"": { ""enabled"": true }
            }");

            var svc = new ConfigurationService(_dir);
            var config = new SetupConfiguration
            {
                ApplicationName = "OP102-renamed",
                Cameras = { new CameraConfiguration { Id = "cam-1", Name = "1-renamed" } },
            };
            Assert.True(svc.SaveConfiguration(config));

            using var doc = JsonDocument.Parse(File.ReadAllText(ConfigPath));
            var root = doc.RootElement;
            // 마법사가 아는 키는 마법사 값
            Assert.Equal("OP102-renamed", root.GetProperty("applicationName").GetString());
            var cam = root.GetProperty("cameras")[0];
            Assert.Equal("1-renamed", cam.GetProperty("name").GetString());
            // 마법사 모델에 없는 키는 보존 — VMS 의 노출/게인 저장(steps)과 부가 필드
            var step = cam.GetProperty("steps")[0];
            Assert.Equal(12345, step.GetProperty("exposure").GetDouble());
            Assert.Equal(2.5, step.GetProperty("gain").GetDouble());
            Assert.False(step.GetProperty("use2DCameraDefault").GetBoolean());
            Assert.Equal(1, cam.GetProperty("stepCount").GetInt32());
            Assert.Equal("LOG-M", cam.GetProperty("model").GetString());
            // 루트의 타 화면 소유 키 보존
            Assert.Equal(60, root.GetProperty("auditRetentionDays").GetInt32());
            Assert.True(root.GetProperty("imageSave").GetProperty("enabled").GetBoolean());
        }

        [Fact]
        public void SaveConfiguration_DeletedCamera_StaysDeleted()
        {
            Directory.CreateDirectory(_dir);
            File.WriteAllText(ConfigPath, @"{
              ""applicationName"": ""OP102"",
              ""cameras"": [ { ""id"": ""cam-old"", ""name"": ""old"", ""steps"": [] } ]
            }");

            var svc = new ConfigurationService(_dir);
            Assert.True(svc.SaveConfiguration(new SetupConfiguration()));

            using var doc = JsonDocument.Parse(File.ReadAllText(ConfigPath));
            Assert.Equal(0, doc.RootElement.GetProperty("cameras").GetArrayLength());
        }

        [Fact]
        public void SaveConfiguration_NoExistingFile_PlainWrite()
        {
            var svc = new ConfigurationService(_dir);
            Assert.True(svc.SaveConfiguration(new SetupConfiguration { ApplicationName = "Fresh" }));

            using var doc = JsonDocument.Parse(File.ReadAllText(ConfigPath));
            Assert.Equal("Fresh", doc.RootElement.GetProperty("applicationName").GetString());
        }

        [Fact]
        public void SaveConfiguration_CorruptExistingFile_StillSaves()
        {
            Directory.CreateDirectory(_dir);
            File.WriteAllText(ConfigPath, "{ not json");

            var svc = new ConfigurationService(_dir);
            Assert.True(svc.SaveConfiguration(new SetupConfiguration { ApplicationName = "Fresh" }));

            using var doc = JsonDocument.Parse(File.ReadAllText(ConfigPath));
            Assert.Equal("Fresh", doc.RootElement.GetProperty("applicationName").GetString());
        }
    }
}
