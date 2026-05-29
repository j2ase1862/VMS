using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using VMS.Core.Security;

namespace VMS.Core.Health
{
    /// <summary>
    /// 헬스 체크 1 항목 결과.
    /// </summary>
    public enum HealthCheckStatus
    {
        /// <summary>통과 — 정상 동작 보장.</summary>
        Pass,
        /// <summary>경고 — 즉시 영향은 없으나 사이트 운영자가 확인해야 할 사항.</summary>
        Warn,
        /// <summary>실패 — 운영 중 장애가 발생할 가능성 높음, 조치 필요.</summary>
        Fail
    }

    /// <summary>
    /// 개별 검사 항목 결과.
    /// </summary>
    public sealed class HealthCheckItem
    {
        public string Name { get; init; } = string.Empty;
        public HealthCheckStatus Status { get; init; }
        public string Message { get; init; } = string.Empty;
    }

    /// <summary>
    /// 전체 헬스 체크 리포트 — 항목 리스트 + 종합 상태.
    /// </summary>
    public sealed class HealthCheckReport
    {
        public IReadOnlyList<HealthCheckItem> Items { get; init; } = Array.Empty<HealthCheckItem>();

        /// <summary>가장 심각한 항목의 상태를 반환 — Fail > Warn > Pass.</summary>
        public HealthCheckStatus OverallStatus
        {
            get
            {
                if (Items.Any(i => i.Status == HealthCheckStatus.Fail)) return HealthCheckStatus.Fail;
                if (Items.Any(i => i.Status == HealthCheckStatus.Warn)) return HealthCheckStatus.Warn;
                return HealthCheckStatus.Pass;
            }
        }

        public int PassCount => Items.Count(i => i.Status == HealthCheckStatus.Pass);
        public int WarnCount => Items.Count(i => i.Status == HealthCheckStatus.Warn);
        public int FailCount => Items.Count(i => i.Status == HealthCheckStatus.Fail);
    }

    /// <summary>
    /// VMS 시작 시 환경을 빠르게 검증하는 자기 진단 — 설치 직후 / 사이트 첫 가동 시
    /// 운영 중 발생할 수 있는 환경 문제를 조기에 표면화.
    ///
    /// 검사 항목:
    /// 1. AppData 베이스 디렉토리 쓰기 가능
    /// 2. 감사 로그 디렉토리 쓰기 가능
    /// 3. system_config.json 존재 (없으면 Warn — Development 모드 fallback)
    /// 4. SecurityOptions 로드됨 (LoadFromAppData 호출됨)
    /// 5. 디스크 여유 용량 ≥ 1 GB (운영 시 검사 결과 / 감사 로그 누적 여유)
    ///
    /// 결과는 단일 AuditCategory.System "StartupHealthCheck" 이벤트로 기록 —
    /// Overall=Pass/Warn/Fail + 각 항목 상태를 details 에 인라인.
    /// </summary>
    public static class StartupHealthCheck
    {
        public const long MinFreeBytes = 1L * 1024 * 1024 * 1024;  // 1 GB

        /// <summary>
        /// 기본 AppData 위치 (`%LocalAppData%\BODA VISION AI`) 로 진단 실행 + 감사 기록.
        /// </summary>
        public static HealthCheckReport Run()
        {
            var appData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "BODA VISION AI");
            var auditDir = Path.Combine(appData, "audit");
            return Run(appData, auditDir, auditAfter: true);
        }

        /// <summary>
        /// 임의 경로로 실행 — 테스트 격리용.
        /// </summary>
        /// <param name="auditAfter">true 면 결과를 AuditCategory.System 으로 기록.</param>
        public static HealthCheckReport Run(string appDataDir, string auditDir, bool auditAfter)
        {
            var items = new List<HealthCheckItem>
            {
                CheckDirectoryWriteable("AppDataDir", appDataDir),
                CheckDirectoryWriteable("AuditDir", auditDir),
                CheckSystemConfigPresent(appDataDir),
                CheckSecurityOptionsLoaded(),
                CheckDiskFreeSpace(appDataDir, MinFreeBytes)
            };

            var report = new HealthCheckReport { Items = items };

            if (auditAfter)
            {
                try
                {
                    var summary = $"Overall={report.OverallStatus}, " +
                                  $"Pass={report.PassCount}, Warn={report.WarnCount}, Fail={report.FailCount}, " +
                                  string.Join(", ", items.Select(i => $"{i.Name}={i.Status}"));
                    var outcome = report.OverallStatus switch
                    {
                        HealthCheckStatus.Pass => AuditOutcome.Success,
                        HealthCheckStatus.Warn => AuditOutcome.Success,  // 정보성 — 운영 차단 X
                        _ => AuditOutcome.Failure
                    };
                    AuditLogger.Instance.Log(
                        AuditCategory.System, "StartupHealthCheck", outcome,
                        source: nameof(StartupHealthCheck), details: summary);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[StartupHealthCheck] 감사 로그 실패: {ex.Message}");
                }
            }

            return report;
        }

        // ─── 개별 검사 ────────────────────────────────────────────

        private static HealthCheckItem CheckDirectoryWriteable(string name, string dir)
        {
            try
            {
                Directory.CreateDirectory(dir);
                var probe = Path.Combine(dir, $".healthprobe_{Guid.NewGuid():N}.tmp");
                File.WriteAllText(probe, "x");
                File.Delete(probe);
                return new HealthCheckItem
                {
                    Name = name,
                    Status = HealthCheckStatus.Pass,
                    Message = $"writeable: {dir}"
                };
            }
            catch (Exception ex)
            {
                return new HealthCheckItem
                {
                    Name = name,
                    Status = HealthCheckStatus.Fail,
                    Message = $"NOT writeable ({dir}): {ex.GetType().Name} {ex.Message}"
                };
            }
        }

        private static HealthCheckItem CheckSystemConfigPresent(string appDataDir)
        {
            var path = Path.Combine(appDataDir, "system_config.json");
            if (File.Exists(path))
            {
                return new HealthCheckItem
                {
                    Name = "SystemConfig",
                    Status = HealthCheckStatus.Pass,
                    Message = $"found: {path}"
                };
            }
            return new HealthCheckItem
            {
                Name = "SystemConfig",
                Status = HealthCheckStatus.Warn,
                Message = $"missing ({path}) — AppSetup 마법사 미실행, Development 모드 fallback"
            };
        }

        private static HealthCheckItem CheckSecurityOptionsLoaded()
        {
            // SecurityOptions.Current 가 어떤 값이든 set 되어 있으면 LoadFromAppData 호출됨.
            // null 이라면 App.xaml.cs OnStartup 순서가 잘못된 것.
            try
            {
                var mode = SecurityOptions.Current.Mode;
                return new HealthCheckItem
                {
                    Name = "SecurityOptions",
                    Status = HealthCheckStatus.Pass,
                    Message = $"loaded, Mode={mode}"
                };
            }
            catch (Exception ex)
            {
                return new HealthCheckItem
                {
                    Name = "SecurityOptions",
                    Status = HealthCheckStatus.Fail,
                    Message = $"not loaded: {ex.Message}"
                };
            }
        }

        private static HealthCheckItem CheckDiskFreeSpace(string anyPath, long minBytes)
        {
            try
            {
                var root = Path.GetPathRoot(Path.GetFullPath(anyPath));
                if (string.IsNullOrEmpty(root))
                {
                    return new HealthCheckItem
                    {
                        Name = "DiskFreeSpace",
                        Status = HealthCheckStatus.Warn,
                        Message = $"root 추출 실패: {anyPath}"
                    };
                }
                var drive = new DriveInfo(root);
                if (!drive.IsReady)
                {
                    return new HealthCheckItem
                    {
                        Name = "DiskFreeSpace",
                        Status = HealthCheckStatus.Warn,
                        Message = $"드라이브 미준비: {root}"
                    };
                }
                var free = drive.AvailableFreeSpace;
                var freeGB = free / 1024.0 / 1024.0 / 1024.0;
                if (free < minBytes)
                {
                    return new HealthCheckItem
                    {
                        Name = "DiskFreeSpace",
                        Status = HealthCheckStatus.Warn,
                        Message = $"low: {freeGB:F2} GB free on {root} (권장 ≥ 1 GB)"
                    };
                }
                return new HealthCheckItem
                {
                    Name = "DiskFreeSpace",
                    Status = HealthCheckStatus.Pass,
                    Message = $"{freeGB:F2} GB free on {root}"
                };
            }
            catch (Exception ex)
            {
                return new HealthCheckItem
                {
                    Name = "DiskFreeSpace",
                    Status = HealthCheckStatus.Warn,
                    Message = $"확인 실패: {ex.GetType().Name} {ex.Message}"
                };
            }
        }
    }
}
