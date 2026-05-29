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
            Assert.Equal(5, report.Items.Count);  // 5개 검사 항목
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
    }
}
