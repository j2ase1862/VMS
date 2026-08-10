using System;
using System.IO;
using VMS.Models;
using VMS.PLC.Models;
using VMS.Services;
using Xunit;

namespace VMS.Tests.Services
{
    /// <summary>
    /// 임시 디렉토리로 격리된 ConfigurationService 로 system_config / layout_config /
    /// plc_signals 3종 JSON 의 Save/Load 라운드트립 및 누락 / 손상 시 default 동작 검증.
    ///
    /// 각 테스트는 IDisposable 의 Dispose 에서 임시 디렉토리를 정리.
    /// </summary>
    public class ConfigurationServiceIntegrationTests : IDisposable
    {
        private readonly string _tempDir;
        private readonly ConfigurationService _service;

        public ConfigurationServiceIntegrationTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), $"config_int_{Guid.NewGuid():N}");
            _service = new ConfigurationService(_tempDir);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_tempDir))
                    Directory.Delete(_tempDir, recursive: true);
            }
            catch { }
        }

        // ─── Constructor — 디렉토리 자동 생성 + 경로 노출 ───────────

        [Fact]
        public void Ctor_CreatesDirectoryIfMissing()
        {
            var newDir = Path.Combine(_tempDir, "nested", "config");
            Assert.False(Directory.Exists(newDir));

            var svc = new ConfigurationService(newDir);
            Assert.True(Directory.Exists(newDir));
        }

        [Fact]
        public void ConfigDirectory_ExposesCustomDirectory()
        {
            Assert.Equal(_tempDir, _service.ConfigDirectory);
        }

        // ─── SystemConfiguration round-trip ───────────────────────

        [Fact]
        public void LoadSystemConfiguration_NoFile_ReturnsDefault()
        {
            var cfg = _service.LoadSystemConfiguration();
            Assert.NotNull(cfg);
            // 기본 인스턴스 — Cameras / IoBoards 등 빈 리스트.
            Assert.Empty(cfg.Cameras);
            Assert.Empty(cfg.IoBoards);
        }

        [Fact]
        public void SaveSystemConfiguration_WritesFile()
        {
            var cfg = new SystemConfiguration
            {
                PlcVendor = PlcVendor.Modbus,
                PlcIpAddress = "192.168.10.5",
                PlcPort = 502
            };
            var ok = _service.SaveSystemConfiguration(cfg);
            Assert.True(ok);
            Assert.True(File.Exists(Path.Combine(_tempDir, "system_config.json")));
        }

        [Fact]
        public void SystemConfiguration_RoundTrip_PreservesFields()
        {
            var cfg = new SystemConfiguration
            {
                ApplicationName = "BODA Test",
                PlcVendor = PlcVendor.Siemens,
                PlcIpAddress = "10.0.0.50",
                PlcPort = 1234,
                AutoReconnect = true,
                PollingIntervalMs = 25
            };
            _service.SaveSystemConfiguration(cfg);

            var loaded = _service.LoadSystemConfiguration();
            Assert.Equal("BODA Test", loaded.ApplicationName);
            Assert.Equal(PlcVendor.Siemens, loaded.PlcVendor);
            Assert.Equal("10.0.0.50", loaded.PlcIpAddress);
            Assert.Equal(1234, loaded.PlcPort);
            Assert.True(loaded.AutoReconnect);
            Assert.Equal(25, loaded.PollingIntervalMs);
        }

        [Fact]
        public void LoadSystemConfiguration_CorruptedJson_ReturnsDefault_NoThrow()
        {
            File.WriteAllText(Path.Combine(_tempDir, "system_config.json"), "{ not json");
            var loaded = _service.LoadSystemConfiguration();
            // best-effort — 손상된 JSON 도 기본 인스턴스 반환 (앱 종료 방지).
            Assert.NotNull(loaded);
            Assert.Empty(loaded.Cameras);
        }

        // ─── LayoutConfiguration round-trip ───────────────────────

        [Fact]
        public void LoadLayoutConfiguration_NoFile_ReturnsDefault()
        {
            var layout = _service.LoadLayoutConfiguration();
            Assert.NotNull(layout);
            Assert.Empty(layout.CameraLayouts);
        }

        [Fact]
        public void SaveLayoutConfiguration_WritesFile_AndUpdatesSavedAt()
        {
            var before = DateTime.UtcNow.AddSeconds(-1);
            var layout = new LayoutConfiguration();
            var ok = _service.SaveLayoutConfiguration(layout);

            Assert.True(ok);
            Assert.True(File.Exists(Path.Combine(_tempDir, "layout_config.json")));
            // SaveLayoutConfiguration 이 SavedAt 갱신.
            Assert.True(layout.SavedAt >= before);
        }

        [Fact]
        public void LayoutConfiguration_RoundTrip_PreservesSavedAt()
        {
            var layout = new LayoutConfiguration();
            _service.SaveLayoutConfiguration(layout);
            var savedTime = layout.SavedAt;

            var loaded = _service.LoadLayoutConfiguration();
            // ms 단위 정밀도 차이 가능 — 초 단위로 비교.
            Assert.Equal(savedTime.ToString("yyyy-MM-dd HH:mm:ss"),
                         loaded.SavedAt.ToString("yyyy-MM-dd HH:mm:ss"));
        }

        // ─── PlcSignalConfiguration round-trip ────────────────────

        [Fact]
        public void LoadPlcSignalConfiguration_NoFile_ReturnsDefault()
        {
            var sig = _service.LoadPlcSignalConfiguration();
            Assert.NotNull(sig);
            Assert.Empty(sig.SignalMaps);
            // 디폴트 폴링 간격.
            Assert.Equal(1000, sig.HeartbeatIntervalMs);
        }

        [Fact]
        public void SavePlcSignalConfiguration_WritesFile()
        {
            var sig = new PlcSignalConfiguration
            {
                HeartbeatIntervalMs = 500
            };
            var ok = _service.SavePlcSignalConfiguration(sig);
            Assert.True(ok);
            Assert.True(File.Exists(Path.Combine(_tempDir, "plc_signals.json")));
        }

        [Fact]
        public void PlcSignalConfiguration_RoundTrip_PreservesFields()
        {
            var sig = new PlcSignalConfiguration
            {
                HeartbeatIntervalMs = 2500,
                TriggerPollingIntervalMs = 30
            };
            _service.SavePlcSignalConfiguration(sig);

            var loaded = _service.LoadPlcSignalConfiguration();
            Assert.Equal(2500, loaded.HeartbeatIntervalMs);
            Assert.Equal(30, loaded.TriggerPollingIntervalMs);
        }

        [Fact]
        public void LoadPlcSignalConfiguration_CorruptedJson_ReturnsDefault_NoThrow()
        {
            File.WriteAllText(Path.Combine(_tempDir, "plc_signals.json"), "INVALID");
            var loaded = _service.LoadPlcSignalConfiguration();
            Assert.NotNull(loaded);
            Assert.Empty(loaded.SignalMaps);
        }

        // ─── 3종 동시 사용 — 분리된 파일 ────────────────────────

        [Fact]
        public void ThreeFiles_StoredSeparately_NoCrossContamination()
        {
            var sys = new SystemConfiguration { PlcVendor = PlcVendor.LS };
            var lay = new LayoutConfiguration();
            var sig = new PlcSignalConfiguration { HeartbeatIntervalMs = 777 };

            _service.SaveSystemConfiguration(sys);
            _service.SaveLayoutConfiguration(lay);
            _service.SavePlcSignalConfiguration(sig);

            Assert.True(File.Exists(Path.Combine(_tempDir, "system_config.json")));
            Assert.True(File.Exists(Path.Combine(_tempDir, "layout_config.json")));
            Assert.True(File.Exists(Path.Combine(_tempDir, "plc_signals.json")));

            // 각 파일이 자신의 형식만 가짐 — Load 시 충돌 없음.
            Assert.Equal(PlcVendor.LS, _service.LoadSystemConfiguration().PlcVendor);
            Assert.Equal(777, _service.LoadPlcSignalConfiguration().HeartbeatIntervalMs);
        }

        [Fact]
        public void SaveAndReload_Twice_OverwritesOnSecondSave()
        {
            var first = new SystemConfiguration { ApplicationName = "V1" };
            _service.SaveSystemConfiguration(first);
            Assert.Equal("V1", _service.LoadSystemConfiguration().ApplicationName);

            var second = new SystemConfiguration { ApplicationName = "V2" };
            _service.SaveSystemConfiguration(second);
            Assert.Equal("V2", _service.LoadSystemConfiguration().ApplicationName);
        }

        // ─── 병합 저장 — 타 앱 소유 키 보존 (현장 검증 2026-08-10) ────────
        // VMS 모델 전체 재직렬화가 AppSetup 의 securityMode/webSso/robot* 를 지워
        // 다음 부팅이 보안 정책 오류로 차단되던 사고의 회귀 테스트.

        private string SystemConfigPath => Path.Combine(_tempDir, "system_config.json");

        [Fact]
        public void SaveSystemConfiguration_PreservesKeysUnknownToVmsModel()
        {
            // AppSetup / 설정 화면들이 기록한, VMS 모델에 없는 키들
            File.WriteAllText(SystemConfigPath, @"{
              ""applicationName"": ""OP102"",
              ""securityMode"": ""Production"",
              ""webSso"": { ""enabled"": true, ""webServerUrl"": ""http://web:5292"" },
              ""isRobotEnabled"": true,
              ""robotIpAddress"": ""192.168.1.90"",
              ""auditRetentionDays"": 90
            }");

            var ok = _service.SaveSystemConfiguration(
                new SystemConfiguration { ApplicationName = "OP102-v2" });

            Assert.True(ok);
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(SystemConfigPath));
            var root = doc.RootElement;
            // 내 모델이 아는 키는 내 값으로
            Assert.Equal("OP102-v2", root.GetProperty("applicationName").GetString());
            // 모르는 키는 기존 값 보존
            Assert.Equal("Production", root.GetProperty("securityMode").GetString());
            Assert.True(root.GetProperty("webSso").GetProperty("enabled").GetBoolean());
            Assert.True(root.GetProperty("isRobotEnabled").GetBoolean());
            Assert.Equal("192.168.1.90", root.GetProperty("robotIpAddress").GetString());
            Assert.Equal(90, root.GetProperty("auditRetentionDays").GetInt32());
        }

        [Fact]
        public void SaveSystemConfiguration_WritesSteps_AndPreservesVisionSetupCameraFields()
        {
            // VisionSetup 이 기록한 카메라 부가 필드(model/serialNumber)는 VMS 모델에 없다
            File.WriteAllText(SystemConfigPath, @"{
              ""applicationName"": ""OP102"",
              ""cameras"": [
                { ""id"": ""cam-1"", ""name"": ""1"", ""model"": ""LOG-M"", ""serialNumber"": ""SN001"" }
              ]
            }");

            var cfg = new SystemConfiguration();
            cfg.Cameras.Add(new CameraConfiguration
            {
                Id = "cam-1",
                Name = "1",
                Steps =
                {
                    new StepConfiguration
                    {
                        StepNumber = 1, Use2DCameraDefault = false, Exposure = 12345, Gain = 2.5
                    }
                }
            });
            _service.SaveSystemConfiguration(cfg);

            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(SystemConfigPath));
            var cam = doc.RootElement.GetProperty("cameras")[0];
            var step = cam.GetProperty("steps")[0];
            Assert.Equal(12345, step.GetProperty("exposure").GetDouble());
            Assert.Equal(2.5, step.GetProperty("gain").GetDouble());
            Assert.False(step.GetProperty("use2DCameraDefault").GetBoolean());
            // entry 단위 병합 — 다른 앱의 부가 필드 보존
            Assert.Equal("LOG-M", cam.GetProperty("model").GetString());
            Assert.Equal("SN001", cam.GetProperty("serialNumber").GetString());
        }

        [Fact]
        public void SaveSystemConfiguration_CorruptExistingFile_StillSavesWithoutMerge()
        {
            File.WriteAllText(SystemConfigPath, "{ not json");

            var ok = _service.SaveSystemConfiguration(new SystemConfiguration { ApplicationName = "Fresh" });

            Assert.True(ok);
            Assert.Equal("Fresh", _service.LoadSystemConfiguration().ApplicationName);
        }

        [Fact]
        public void SaveSystemConfiguration_RemovedCamera_StaysRemoved()
        {
            // cameras 키는 양쪽 모델이 모두 알므로 병합 대상이 아니다 — 삭제 의도 존중
            File.WriteAllText(SystemConfigPath, @"{
              ""applicationName"": ""OP102"",
              ""cameras"": [ { ""id"": ""cam-old"", ""name"": ""old"" } ]
            }");

            _service.SaveSystemConfiguration(new SystemConfiguration());

            Assert.Empty(_service.LoadSystemConfiguration().Cameras);
        }
    }
}
