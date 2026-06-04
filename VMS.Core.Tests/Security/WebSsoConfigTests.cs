using System;
using System.IO;
using VMS.Core.Security;
using Xunit;

namespace VMS.Core.Tests.Security
{
    /// <summary>
    /// WebSsoConfig.LoadFromAppData — system_config.json:webSso 객체 + 루트 webServerUrl 폴백 검증 (SSO PR2).
    /// </summary>
    public class WebSsoConfigTests : IDisposable
    {
        private readonly string _tempAppData;

        public WebSsoConfigTests()
        {
            _tempAppData = Path.Combine(Path.GetTempPath(), "boda-websso-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempAppData);
        }

        public void Dispose()
        {
            try { Directory.Delete(_tempAppData, recursive: true); } catch { /* best effort */ }
        }

        private void WriteConfig(string json) =>
            File.WriteAllText(Path.Combine(_tempAppData, "system_config.json"), json);

        [Fact]
        public void LoadFromAppData_no_config_returns_Disabled()
        {
            var cfg = WebSsoConfig.LoadFromAppData(_tempAppData);
            Assert.False(cfg.Enabled);
            Assert.Equal(string.Empty, cfg.WebServerUrl);
        }

        [Fact]
        public void LoadFromAppData_webSso_object_enabled_true_with_url()
        {
            WriteConfig("{\"webSso\":{\"enabled\":true,\"webServerUrl\":\"http://localhost:5292\"}}");
            var cfg = WebSsoConfig.LoadFromAppData(_tempAppData);

            Assert.True(cfg.Enabled);
            Assert.Equal("http://localhost:5292", cfg.WebServerUrl);
        }

        [Fact]
        public void LoadFromAppData_webSso_enabled_false_returns_disabled_with_url_kept()
        {
            // 명시적으로 enabled=false 면 비활성, url 은 보존 (재활성 대비)
            WriteConfig("{\"webSso\":{\"enabled\":false,\"webServerUrl\":\"http://localhost:5292\"}}");
            var cfg = WebSsoConfig.LoadFromAppData(_tempAppData);

            Assert.False(cfg.Enabled);
            Assert.Equal("http://localhost:5292", cfg.WebServerUrl);
        }

        [Fact]
        public void LoadFromAppData_falls_back_to_root_webServerUrl()
        {
            // webSso 객체에 url 없으면 루트 webServerUrl 사용 (기존 운영 설정 호환)
            WriteConfig("{\"webSso\":{\"enabled\":true},\"webServerUrl\":\"http://10.0.0.5:5292\"}");
            var cfg = WebSsoConfig.LoadFromAppData(_tempAppData);

            Assert.True(cfg.Enabled);
            Assert.Equal("http://10.0.0.5:5292", cfg.WebServerUrl);
        }

        [Fact]
        public void LoadFromAppData_no_webSso_object_returns_disabled()
        {
            WriteConfig("{\"webServerUrl\":\"http://localhost:5292\",\"securityMode\":\"Production\"}");
            var cfg = WebSsoConfig.LoadFromAppData(_tempAppData);

            // webSso 객체 자체가 없으면 비활성 (안전 디폴트)
            Assert.False(cfg.Enabled);
            // 단, URL 만은 루트에서 가져옴 (재활성 시점에 즉시 사용 가능)
            Assert.Equal("http://localhost:5292", cfg.WebServerUrl);
        }

        [Fact]
        public void LoadFromAppData_corrupt_json_returns_Disabled()
        {
            WriteConfig("{ broken");
            var cfg = WebSsoConfig.LoadFromAppData(_tempAppData);

            // 파싱 실패는 비활성 폴백 — 진단 자체가 config 오류로 throw 하면 안 됨
            Assert.False(cfg.Enabled);
            Assert.Equal(string.Empty, cfg.WebServerUrl);
        }

        [Fact]
        public void LoadFromAppData_enabled_string_not_true_returns_disabled()
        {
            // JSON boolean 만 인정 — "true" 문자열은 비활성으로 안전 처리
            WriteConfig("{\"webSso\":{\"enabled\":\"true\",\"webServerUrl\":\"http://x\"}}");
            var cfg = WebSsoConfig.LoadFromAppData(_tempAppData);

            Assert.False(cfg.Enabled);
        }
    }
}
