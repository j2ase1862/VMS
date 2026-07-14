using System;
using System.IO;
using System.Text.Json;
using VMS.AppSetup.Models;
using VMS.AppSetup.Services;
using Xunit;

namespace VMS.AppSetup.Tests
{
    /// <summary>
    /// Page 7 (보안 모드) 저장 검증 — wizard 가 쓰는 system_config.json 의 "securityMode" 키가
    /// VMS.Core.Security.SecurityOptions.LoadFromAppData 의 읽기 방식
    /// (JsonDocument.TryGetProperty + Enum.TryParse, ignoreCase) 과 호환되는지 확인.
    /// 이 키 누락 시 RELEASE VMS 는 RequireExplicit 정책으로 부팅 중단하므로
    /// 직렬화 스키마가 깨지면 현장 설치 직후 부팅 실패로 이어진다.
    /// </summary>
    public class SetupConfigurationSecurityModeTests : IDisposable
    {
        private readonly string _exportPath = Path.Combine(
            Path.GetTempPath(), $"vms_appsetup_test_{Guid.NewGuid():N}.json");

        public void Dispose()
        {
            if (File.Exists(_exportPath))
                File.Delete(_exportPath);
        }

        private JsonDocument ExportAndParse(SetupConfiguration config)
        {
            // ExportConfiguration 은 SaveConfiguration 과 동일한 JsonOptions 를 사용 —
            // 실제 wizard 저장 경로(AppData)를 건드리지 않고 직렬화 결과를 검증.
            Assert.True(ConfigurationService.Instance.ExportConfiguration(config, _exportPath));
            return JsonDocument.Parse(File.ReadAllText(_exportPath));
        }

        [Fact]
        public void DefaultConfig_WritesSecurityModeProduction()
        {
            using var doc = ExportAndParse(new SetupConfiguration());

            // VMS.Core 읽기 로직과 동일한 접근: camelCase 키 + 문자열 enum 값
            Assert.True(doc.RootElement.TryGetProperty("securityMode", out var prop));
            Assert.Equal("Production", prop.GetString());
        }

        [Theory]
        [InlineData(SecurityMode.Production, "Production")]
        [InlineData(SecurityMode.Development, "Development")]
        public void SecurityMode_SerializesAsEnumName_ParsableByVmsCore(
            SecurityMode mode, string expectedJsonValue)
        {
            using var doc = ExportAndParse(new SetupConfiguration { SecurityMode = mode });

            Assert.True(doc.RootElement.TryGetProperty("securityMode", out var prop));
            Assert.Equal(expectedJsonValue, prop.GetString());

            // SecurityOptions.LoadFromAppData 와 동일한 파싱 (ignoreCase Enum.TryParse) 성공 확인
            Assert.True(Enum.TryParse<SecurityMode>(prop.GetString(), ignoreCase: true, out var parsed));
            Assert.Equal(mode, parsed);
        }

        [Fact]
        public void SecurityMode_RoundTrips_ThroughSetupConfiguration()
        {
            // 기존 config 재로드(LoadExistingConfiguration) 시 Development 선택이 보존되는지
            using var doc = ExportAndParse(new SetupConfiguration { SecurityMode = SecurityMode.Development });

            var reloaded = JsonSerializer.Deserialize<SetupConfiguration>(
                doc.RootElement.GetRawText(),
                new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
                });

            Assert.NotNull(reloaded);
            Assert.Equal(SecurityMode.Development, reloaded!.SecurityMode);
        }

        [Fact]
        public void LegacyConfig_WithoutSecurityModeKey_DefaultsToProduction()
        {
            // 구버전 wizard 가 만든 config (securityMode 키 없음) 재로드 시 Production 기본값 —
            // 재저장하면 키가 채워져 RELEASE VMS 부팅 차단이 해소된다.
            var reloaded = JsonSerializer.Deserialize<SetupConfiguration>(
                "{\"applicationName\":\"BODA Vision System\"}",
                new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
                });

            Assert.NotNull(reloaded);
            Assert.Equal(SecurityMode.Production, reloaded!.SecurityMode);
        }
    }
}
