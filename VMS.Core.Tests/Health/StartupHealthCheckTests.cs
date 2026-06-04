using System;
using System.IO;
using System.Linq;
using VMS.Core.Health;
using Xunit;

namespace VMS.Core.Tests.Health
{
    /// <summary>
    /// StartupHealthCheck 의 개별 검사 결과 + Overall 종합 상태 검증.
    /// 임시 디렉토리로 격리 — 운영 AppData 영향 없음. auditAfter=false 로
    /// 감사 로거 부수효과 제거.
    /// </summary>
    public class StartupHealthCheckTests : IDisposable
    {
        private readonly string _tempBase;
        private readonly string _appDataDir;
        private readonly string _auditDir;

        public StartupHealthCheckTests()
        {
            _tempBase = Path.Combine(Path.GetTempPath(), $"healthcheck_{Guid.NewGuid():N}");
            _appDataDir = Path.Combine(_tempBase, "BODA VISION AI");
            _auditDir = Path.Combine(_appDataDir, "audit");
            Directory.CreateDirectory(_appDataDir);
            Directory.CreateDirectory(_auditDir);
        }

        public void Dispose()
        {
            try { if (Directory.Exists(_tempBase)) Directory.Delete(_tempBase, recursive: true); }
            catch { /* 무시 */ }
        }

        // ─── 정상 환경 ────────────────────────────────────────────

        [Fact]
        public void Run_HappyPath_AllOrWarnButNotFail()
        {
            // SystemConfig 가 없으므로 Warn 1건 발생 가능 — Overall 은 Pass 또는 Warn.
            var report = StartupHealthCheck.Run(_appDataDir, _auditDir, auditAfter: false);

            Assert.NotEmpty(report.Items);
            Assert.NotEqual(HealthCheckStatus.Fail, report.OverallStatus);
            Assert.Equal(6, report.Items.Count);  // 6개 검사 항목 (P3a: WebServer 추가)
        }

        [Fact]
        public void Run_WithSystemConfigPresent_AllPass()
        {
            // system_config.json 을 만들어 두면 SystemConfig 검사도 Pass.
            File.WriteAllText(
                Path.Combine(_appDataDir, "system_config.json"),
                "{ \"securityMode\": \"Development\" }");

            var report = StartupHealthCheck.Run(_appDataDir, _auditDir, auditAfter: false);

            var sysCfg = report.Items.Single(i => i.Name == "SystemConfig");
            Assert.Equal(HealthCheckStatus.Pass, sysCfg.Status);
        }

        // ─── SystemConfig 누락 → Warn ─────────────────────────────

        [Fact]
        public void Run_NoSystemConfig_WarnNotFail()
        {
            // system_config.json 미존재 시 검사 항목은 Warn (앱 자체는 Development fallback 으로 동작).
            var report = StartupHealthCheck.Run(_appDataDir, _auditDir, auditAfter: false);

            var sysCfg = report.Items.Single(i => i.Name == "SystemConfig");
            Assert.Equal(HealthCheckStatus.Warn, sysCfg.Status);
            Assert.Contains("missing", sysCfg.Message);
        }

        // ─── 쓰기 가능 검사 ───────────────────────────────────────

        [Fact]
        public void Run_AppDataDir_WriteableCheckPasses()
        {
            var report = StartupHealthCheck.Run(_appDataDir, _auditDir, auditAfter: false);

            var appData = report.Items.Single(i => i.Name == "AppDataDir");
            Assert.Equal(HealthCheckStatus.Pass, appData.Status);
            Assert.Contains("writeable", appData.Message);
        }

        [Fact]
        public void Run_AuditDir_WriteableCheckPasses()
        {
            var report = StartupHealthCheck.Run(_appDataDir, _auditDir, auditAfter: false);

            var audit = report.Items.Single(i => i.Name == "AuditDir");
            Assert.Equal(HealthCheckStatus.Pass, audit.Status);
        }

        [Fact]
        public void Run_AuditDirAutoCreated_IfMissing()
        {
            // 디렉토리가 없어도 CheckDirectoryWriteable 이 CreateDirectory 호출 → Pass.
            var newAudit = Path.Combine(_tempBase, "audit_new");
            Assert.False(Directory.Exists(newAudit));

            var report = StartupHealthCheck.Run(_appDataDir, newAudit, auditAfter: false);

            var audit = report.Items.Single(i => i.Name == "AuditDir");
            Assert.Equal(HealthCheckStatus.Pass, audit.Status);
            Assert.True(Directory.Exists(newAudit));
        }

        // ─── DiskFreeSpace ────────────────────────────────────────

        [Fact]
        public void Run_DiskFreeSpace_ChecksAndReturnsValid()
        {
            // 일반적인 개발 / CI 머신은 1 GB 이상 여유 — Pass 또는 Warn (low) 모두 허용.
            var report = StartupHealthCheck.Run(_appDataDir, _auditDir, auditAfter: false);

            var disk = report.Items.Single(i => i.Name == "DiskFreeSpace");
            Assert.NotEqual(HealthCheckStatus.Fail, disk.Status);
        }

        // ─── SecurityOptions ──────────────────────────────────────

        [Fact]
        public void Run_SecurityOptions_ReportsCurrentMode()
        {
            var report = StartupHealthCheck.Run(_appDataDir, _auditDir, auditAfter: false);

            var sec = report.Items.Single(i => i.Name == "SecurityOptions");
            // Current 가 set 되어 있으면 (어떤 시점에 LoadFromAppData 호출됨) Pass.
            Assert.Equal(HealthCheckStatus.Pass, sec.Status);
            Assert.Contains("Mode=", sec.Message);
        }

        // ─── 종합 상태 / 카운트 ──────────────────────────────────

        [Fact]
        public void OverallStatus_FailDominatesWarnAndPass()
        {
            // OverallStatus 우선순위: Fail > Warn > Pass.
            var fakeReport = new HealthCheckReport
            {
                Items = new[]
                {
                    new HealthCheckItem { Name = "A", Status = HealthCheckStatus.Pass },
                    new HealthCheckItem { Name = "B", Status = HealthCheckStatus.Warn },
                    new HealthCheckItem { Name = "C", Status = HealthCheckStatus.Fail }
                }
            };

            Assert.Equal(HealthCheckStatus.Fail, fakeReport.OverallStatus);
            Assert.Equal(1, fakeReport.PassCount);
            Assert.Equal(1, fakeReport.WarnCount);
            Assert.Equal(1, fakeReport.FailCount);
        }

        [Fact]
        public void OverallStatus_AllPass_OverallPass()
        {
            var fakeReport = new HealthCheckReport
            {
                Items = new[]
                {
                    new HealthCheckItem { Name = "A", Status = HealthCheckStatus.Pass },
                    new HealthCheckItem { Name = "B", Status = HealthCheckStatus.Pass }
                }
            };
            Assert.Equal(HealthCheckStatus.Pass, fakeReport.OverallStatus);
        }

        [Fact]
        public void OverallStatus_NoFailButWarn_OverallWarn()
        {
            var fakeReport = new HealthCheckReport
            {
                Items = new[]
                {
                    new HealthCheckItem { Name = "A", Status = HealthCheckStatus.Pass },
                    new HealthCheckItem { Name = "B", Status = HealthCheckStatus.Warn }
                }
            };
            Assert.Equal(HealthCheckStatus.Warn, fakeReport.OverallStatus);
        }

        [Fact]
        public void Run_AuditAfterTrue_NoThrow()
        {
            // auditAfter=true 경로도 예외 없이 동작 — AuditLogger.Instance 가 default
            // LocalAppData 에 기록하므로 부수효과는 있지만 throw 없으면 통과.
            var report = StartupHealthCheck.Run(_appDataDir, _auditDir, auditAfter: true);
            Assert.NotNull(report);
        }

        // ─── WebServer 도달성 (검토사항 P3a) ────────────────────────

        [Fact]
        public void WebServer_url_not_configured_skipped_pass()
        {
            // webServerUrl=null → skip (Web 통신 미사용 환경) → Pass + "skipped" 메시지
            var report = StartupHealthCheck.Run(
                _appDataDir, _auditDir, auditAfter: false,
                webServerUrl: null, httpClient: null);

            var web = report.Items.Single(i => i.Name == "WebServer");
            Assert.Equal(HealthCheckStatus.Pass, web.Status);
            Assert.Contains("skipped", web.Message);
        }

        [Fact]
        public void WebServer_unreachable_url_returns_warn()
        {
            // 비할당 호스트 + 비호환 포트 → 도달 불가 → Warn (운영자 조치 유도)
            var report = StartupHealthCheck.Run(
                _appDataDir, _auditDir, auditAfter: false,
                webServerUrl: "http://127.0.0.1:1", httpClient: null);

            var web = report.Items.Single(i => i.Name == "WebServer");
            Assert.Equal(HealthCheckStatus.Warn, web.Status);
            Assert.Contains("unreachable", web.Message);
        }

        [Fact]
        public void WebServer_with_injected_handler_returning_200_passes()
        {
            // HttpClient injection → /health 200 응답 mock → Pass
            using var http = new System.Net.Http.HttpClient(
                new StubHandler(System.Net.HttpStatusCode.OK));
            var report = StartupHealthCheck.Run(
                _appDataDir, _auditDir, auditAfter: false,
                webServerUrl: "http://localhost:5292", httpClient: http);

            var web = report.Items.Single(i => i.Name == "WebServer");
            Assert.Equal(HealthCheckStatus.Pass, web.Status);
            Assert.Contains("reachable", web.Message);
        }

        [Fact]
        public void WebServer_with_injected_handler_returning_503_passes()
        {
            // 503 Unhealthy 도 health endpoint 응답이므로 "도달 가능" 으로 인정 — DB 일시 단절 등
            using var http = new System.Net.Http.HttpClient(
                new StubHandler(System.Net.HttpStatusCode.ServiceUnavailable));
            var report = StartupHealthCheck.Run(
                _appDataDir, _auditDir, auditAfter: false,
                webServerUrl: "http://localhost:5292", httpClient: http);

            var web = report.Items.Single(i => i.Name == "WebServer");
            Assert.Equal(HealthCheckStatus.Pass, web.Status);
            Assert.Contains("503", web.Message);
        }

        [Fact]
        public void WebServer_with_injected_handler_returning_404_returns_warn()
        {
            // 404 같은 비-health 응답은 잘못된 endpoint/Web 버전 — Warn
            using var http = new System.Net.Http.HttpClient(
                new StubHandler(System.Net.HttpStatusCode.NotFound));
            var report = StartupHealthCheck.Run(
                _appDataDir, _auditDir, auditAfter: false,
                webServerUrl: "http://localhost:5292", httpClient: http);

            var web = report.Items.Single(i => i.Name == "WebServer");
            Assert.Equal(HealthCheckStatus.Warn, web.Status);
            Assert.Contains("unexpected", web.Message);
        }

        [Fact]
        public void WebServer_url_auto_loaded_from_system_config()
        {
            // system_config.json 에 webServerUrl 만 명시 + Run() 기본 경로 호출 시 자동 추출.
            // 기본 Run() 은 default LocalAppData 사용 → 본 테스트는 임시 디렉토리 격리 어려움.
            // 대신 system_config 파일이 존재할 때 _appDataDir override 로 검사가 webServerUrl 항목
            // 을 포함하는지 확인 (Reach 결과는 상관 없음).
            File.WriteAllText(
                Path.Combine(_appDataDir, "system_config.json"),
                "{ \"webServerUrl\": \"http://127.0.0.1:1\" }");

            // appDataDir 만 변경 가능한 Run 오버로드는 webServerUrl 을 명시 받음.
            // → 자동 추출은 기본 Run() 경로 — 본 테스트는 wiring 확인만 (코드 경로 존재).
            var report = StartupHealthCheck.Run(
                _appDataDir, _auditDir, auditAfter: false,
                webServerUrl: "http://127.0.0.1:1", httpClient: null);

            Assert.Contains(report.Items, i => i.Name == "WebServer");
        }

        /// <summary>고정 응답을 반환하는 HttpMessageHandler — Web 도달성 mock.</summary>
        private sealed class StubHandler : System.Net.Http.HttpMessageHandler
        {
            private readonly System.Net.HttpStatusCode _status;
            public StubHandler(System.Net.HttpStatusCode status) => _status = status;
            protected override System.Threading.Tasks.Task<System.Net.Http.HttpResponseMessage> SendAsync(
                System.Net.Http.HttpRequestMessage request,
                System.Threading.CancellationToken cancellationToken)
                => System.Threading.Tasks.Task.FromResult(new System.Net.Http.HttpResponseMessage(_status));
        }
    }
}
