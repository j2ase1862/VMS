using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
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
    /// 6. Web 서버 도달성 (검토사항 P3a) — system_config.json:webServerUrl 의 /health 응답.
    ///    Web 통신 미설정이면 skip, URL 잘못/서버 미가동이면 Warn (운영자 조치 필요).
    ///
    /// 결과는 단일 AuditCategory.System "StartupHealthCheck" 이벤트로 기록 —
    /// Overall=Pass/Warn/Fail + 각 항목 상태를 details 에 인라인.
    /// </summary>
    public static class StartupHealthCheck
    {
        public const long MinFreeBytes = 1L * 1024 * 1024 * 1024;  // 1 GB
        public static readonly TimeSpan WebReachabilityTimeout = TimeSpan.FromSeconds(5);

        /// <summary>
        /// 기본 AppData 위치 (`%LocalAppData%\BODA VISION AI`) 로 진단 실행 + 감사 기록.
        /// system_config.json 에서 webServerUrl 추출해 도달성 검사 포함.
        /// </summary>
        public static HealthCheckReport Run()
        {
            var appData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "BODA VISION AI");
            var auditDir = Path.Combine(appData, "audit");
            return Run(appData, auditDir, auditAfter: true,
                webServerUrl: TryReadWebServerUrl(appData),
                httpClient: null);
        }

        /// <summary>
        /// 임의 경로 + 옵션으로 실행 — 테스트 격리용.
        /// </summary>
        /// <param name="auditAfter">true 면 결과를 AuditCategory.System 으로 기록.</param>
        /// <param name="webServerUrl">Web 도달성 검사 대상 URL. null/빈 값이면 검사 skip.</param>
        /// <param name="httpClient">Web 도달성 검사용 HttpClient. null 이면 기본 인스턴스 생성.</param>
        public static HealthCheckReport Run(
            string appDataDir,
            string auditDir,
            bool auditAfter,
            string? webServerUrl = null,
            HttpClient? httpClient = null)
        {
            var items = new List<HealthCheckItem>
            {
                CheckDirectoryWriteable("AppDataDir", appDataDir),
                CheckDirectoryWriteable("AuditDir", auditDir),
                CheckSystemConfigPresent(appDataDir),
                CheckSecurityOptionsLoaded(),
                CheckDiskFreeSpace(appDataDir, MinFreeBytes),
                CheckWebServerReachable(webServerUrl, httpClient)
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

        /// <summary>
        /// system_config.json 에서 webServerUrl 키 추출. 누락/오류 시 null 반환.
        /// SecurityOptions 와 유사 패턴 — 진단 자체가 config 오류로 실패해서는 안 됨.
        /// </summary>
        private static string? TryReadWebServerUrl(string appDataDir)
        {
            try
            {
                var path = Path.Combine(appDataDir, "system_config.json");
                if (!File.Exists(path)) return null;

                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                if (doc.RootElement.TryGetProperty("webServerUrl", out var prop))
                {
                    var value = prop.GetString();
                    return string.IsNullOrWhiteSpace(value) ? null : value;
                }
                return null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Web 서버 /health endpoint 도달성 검사 — VMS 가 운영 중 보낼 heartbeat/result POST 가
        /// 실제로 도달 가능한지 시작 시점에 확인. 미설정이면 skip (Web 통신 미사용 환경).
        /// 5초 timeout — 시작 지연 최소화. Web 측 /health 는 익명 endpoint 라 X-API-Key 불필요.
        /// </summary>
        private static HealthCheckItem CheckWebServerReachable(string? webServerUrl, HttpClient? injected)
        {
            if (string.IsNullOrWhiteSpace(webServerUrl))
            {
                return new HealthCheckItem
                {
                    Name = "WebServer",
                    Status = HealthCheckStatus.Pass,
                    Message = "skipped — webServerUrl 미설정 (Web 통신 사용 안 함)"
                };
            }

            HttpClient? owned = null;
            var http = injected;
            if (http == null)
            {
                owned = new HttpClient { Timeout = WebReachabilityTimeout };
                http = owned;
            }

            try
            {
                var url = webServerUrl.TrimEnd('/') + "/health";
                using var cts = new CancellationTokenSource(WebReachabilityTimeout);
                var resp = http.GetAsync(url, cts.Token).GetAwaiter().GetResult();
                // 200 Healthy / 503 Unhealthy — 둘 다 health endpoint 응답이므로 "도달" 으로 간주.
                // 다른 상태 (404 / 401 등) 는 잘못된 endpoint 또는 인증 필요 — Warn 으로 운영자 조치 유도.
                if ((int)resp.StatusCode == 200 || (int)resp.StatusCode == 503)
                {
                    return new HealthCheckItem
                    {
                        Name = "WebServer",
                        Status = HealthCheckStatus.Pass,
                        Message = $"reachable {url} → HTTP {(int)resp.StatusCode}"
                    };
                }
                return new HealthCheckItem
                {
                    Name = "WebServer",
                    Status = HealthCheckStatus.Warn,
                    Message = $"unexpected response {url} → HTTP {(int)resp.StatusCode} " +
                              "(/health endpoint 가 200/503 외 응답 — Web 버전/경로 확인 필요)"
                };
            }
            catch (Exception ex)
            {
                return new HealthCheckItem
                {
                    Name = "WebServer",
                    Status = HealthCheckStatus.Warn,
                    Message = $"unreachable {webServerUrl}: {ex.GetType().Name} — " +
                              "Web 서버 미가동, URL 오타, 방화벽 차단, 또는 시작 직후 짧은 지연일 수 있음"
                };
            }
            finally
            {
                owned?.Dispose();
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
