using System;
using System.IO;
using VMS.Core.Security;
using Xunit;

namespace VMS.Core.Tests.Security
{
    /// <summary>
    /// SecurityOptions.LoadFromAppData 의 폴백 정책 / 환경변수 / config 우선순위 검증 (GS P1-#3).
    /// 운영 환경에서 system_config.json 손상 시 보안 모드가 조용히 Development 로
    /// 다운그레이드되는 위험 차단을 위한 명시적 정책 분기 동작 보장.
    /// </summary>
    [Collection("SecurityOptionsState")]
    public class SecurityOptionsLoadTests : IDisposable
    {
        private const string EnvVarName = "BODA_VMS_SECURITY_MODE";
        private readonly string _tempLocalAppData;
        private readonly string? _origEnvValue;
        private readonly SecurityOptions _origCurrent;

        public SecurityOptionsLoadTests()
        {
            // 테스트 전용 임시 LocalAppData — 실제 사용자 AppData 손대지 않음.
            // Environment.GetFolderPath 는 Win32 SHGetKnownFolderPath 라 LOCALAPPDATA 환경변수로
            // 덮어쓰기 불가 → SecurityOptions 의 internal appDataOverride 파라미터 사용.
            _tempLocalAppData = Path.Combine(Path.GetTempPath(),
                "boda-security-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempLocalAppData);

            _origEnvValue = Environment.GetEnvironmentVariable(EnvVarName);
            Environment.SetEnvironmentVariable(EnvVarName, null);

            _origCurrent = SecurityOptions.Current;
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable(EnvVarName, _origEnvValue);
            SecurityOptions.Current = _origCurrent;
            try { Directory.Delete(_tempLocalAppData, recursive: true); } catch { /* best effort */ }
        }

        private void WriteConfig(string mode)
        {
            var dir = Path.Combine(_tempLocalAppData, "BODA VISION AI");
            Directory.CreateDirectory(dir);
            File.WriteAllText(
                Path.Combine(dir, "system_config.json"),
                $"{{\"securityMode\":\"{mode}\"}}");
        }

        // ─── Config file path ───────────────────────────────────

        [Fact]
        public void LoadFromAppData_config_Production_sets_Production_and_source_ConfigFile()
        {
            WriteConfig("Production");
            SecurityOptions.LoadFromAppData(appDataOverride: _tempLocalAppData);

            Assert.Equal(SecurityMode.Production, SecurityOptions.Current.Mode);
            Assert.Equal(SecurityModeSource.ConfigFile, SecurityOptions.CurrentSource);
        }

        [Fact]
        public void LoadFromAppData_config_Development_sets_Development_and_source_ConfigFile()
        {
            WriteConfig("Development");
            SecurityOptions.LoadFromAppData(appDataOverride: _tempLocalAppData);

            Assert.Equal(SecurityMode.Development, SecurityOptions.Current.Mode);
            Assert.Equal(SecurityModeSource.ConfigFile, SecurityOptions.CurrentSource);
        }

        [Fact]
        public void LoadFromAppData_config_corrupt_falls_back_with_source_FallbackOnError()
        {
            var dir = Path.Combine(_tempLocalAppData, "BODA VISION AI");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "system_config.json"), "{ broken json");

            SecurityOptions.LoadFromAppData(appDataOverride: _tempLocalAppData);

            Assert.Equal(SecurityMode.Development, SecurityOptions.Current.Mode);
            Assert.Equal(SecurityModeSource.FallbackOnError, SecurityOptions.CurrentSource);
        }

        // ─── 환경변수 우선순위 ─────────────────────────────────

        [Fact]
        public void LoadFromAppData_env_Production_overrides_config_Development()
        {
            // 운영 강제 시나리오: config 는 Dev 인데 운영자가 환경변수로 Production 강제
            WriteConfig("Development");
            Environment.SetEnvironmentVariable(EnvVarName, "Production");

            SecurityOptions.LoadFromAppData(appDataOverride: _tempLocalAppData);

            Assert.Equal(SecurityMode.Production, SecurityOptions.Current.Mode);
            Assert.Equal(SecurityModeSource.Environment, SecurityOptions.CurrentSource);
        }

        [Fact]
        public void LoadFromAppData_env_invalid_value_ignored_and_falls_to_config()
        {
            Environment.SetEnvironmentVariable(EnvVarName, "Garbage");
            WriteConfig("Production");

            SecurityOptions.LoadFromAppData(appDataOverride: _tempLocalAppData);

            Assert.Equal(SecurityMode.Production, SecurityOptions.Current.Mode);
            Assert.Equal(SecurityModeSource.ConfigFile, SecurityOptions.CurrentSource);
        }

        // ─── 폴백 정책 ─────────────────────────────────────────

        [Fact]
        public void LoadFromAppData_no_config_no_env_WarnOnFallback_returns_Development_with_FallbackOnError()
        {
            // config 도 env 도 없음 — 현행 호환 정책은 Development 폴백
            SecurityOptions.LoadFromAppData(SecurityLoadPolicy.WarnOnFallback, _tempLocalAppData);

            Assert.Equal(SecurityMode.Development, SecurityOptions.Current.Mode);
            Assert.Equal(SecurityModeSource.FallbackOnError, SecurityOptions.CurrentSource);
        }

        [Fact]
        public void LoadFromAppData_no_config_no_env_RequireExplicit_throws()
        {
            // 운영 보호 정책 — 명시적 신호 없으면 부팅 중단 (조용한 다운그레이드 차단)
            Assert.Throws<InvalidOperationException>(
                () => SecurityOptions.LoadFromAppData(SecurityLoadPolicy.RequireExplicit, _tempLocalAppData));
        }

        [Fact]
        public void LoadFromAppData_corrupt_config_RequireExplicit_throws()
        {
            // 손상된 config 도 명시적 신호로 인정 안 함 → throw
            var dir = Path.Combine(_tempLocalAppData, "BODA VISION AI");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "system_config.json"), "{ broken");

            Assert.Throws<InvalidOperationException>(
                () => SecurityOptions.LoadFromAppData(SecurityLoadPolicy.RequireExplicit, _tempLocalAppData));
        }

        [Fact]
        public void LoadFromAppData_env_only_RequireExplicit_succeeds()
        {
            // env 명시면 config 없어도 RequireExplicit 통과
            Environment.SetEnvironmentVariable(EnvVarName, "Production");

            SecurityOptions.LoadFromAppData(SecurityLoadPolicy.RequireExplicit, _tempLocalAppData);

            Assert.Equal(SecurityMode.Production, SecurityOptions.Current.Mode);
            Assert.Equal(SecurityModeSource.Environment, SecurityOptions.CurrentSource);
        }

        [Fact]
        public void LoadFromAppData_config_only_RequireExplicit_succeeds()
        {
            // config 명시면 env 없어도 RequireExplicit 통과
            WriteConfig("Production");

            SecurityOptions.LoadFromAppData(SecurityLoadPolicy.RequireExplicit, _tempLocalAppData);

            Assert.Equal(SecurityMode.Production, SecurityOptions.Current.Mode);
            Assert.Equal(SecurityModeSource.ConfigFile, SecurityOptions.CurrentSource);
        }
    }
}
