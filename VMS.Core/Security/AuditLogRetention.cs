using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using VMS.Camera.Configuration;

namespace VMS.Core.Security
{
    /// <summary>
    /// 감사 로그 보존 정책 — 일별 회전 파일 (YYYY-MM-DD.jsonl) 중 보존 기간 초과분 자동 삭제.
    ///
    /// 정책:
    /// - 기본 보존 365일 (GS 인증 권장 — 1년치 활동 이력 보존)
    /// - %LocalAppData%\BODA VISION AI\system_config.json 의 "auditRetentionDays" 키로 덮어쓰기 가능
    /// - 최소 7일, 최대 3650일 (10년) 로 clamp — 실수 / 손상 방지
    /// - 오늘 날짜 파일은 보존 기간 무관 항상 유지
    /// - 정리 작업 자체를 AuditCategory.System 이벤트로 기록 → 보존 / 삭제 이력 추적
    ///
    /// 호출 시점: VMS App.xaml.cs OnStartup — 세션당 1회.
    /// </summary>
    public static class AuditLogRetention
    {
        public const int DefaultRetentionDays = 365;
        public const int MinRetentionDays = 7;
        public const int MaxRetentionDays = 3650;

        /// <summary>
        /// system_config.json 의 "auditRetentionDays" 키를 읽어 보존 기간을 반환.
        /// 파일 / 키 누락 또는 범위 밖이면 <see cref="DefaultRetentionDays"/> 사용.
        /// </summary>
        public static int LoadRetentionDaysFromAppData()
        {
            try
            {
                var path = AppDataPaths.SystemConfigFile;
                if (!File.Exists(path)) return DefaultRetentionDays;

                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                if (doc.RootElement.TryGetProperty("auditRetentionDays", out var prop)
                    && prop.TryGetInt32(out var days))
                {
                    return Clamp(days);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AuditRetention] LoadRetentionDays 실패: {ex.Message}");
            }
            return DefaultRetentionDays;
        }

        /// <summary>
        /// <paramref name="auditDir"/> 의 YYYY-MM-DD.jsonl 파일 중 <paramref name="retentionDays"/>
        /// 보다 오래된 것 삭제. 삭제 결과를 AuditCategory.System 으로 기록.
        /// </summary>
        /// <returns>실제 삭제된 파일 수.</returns>
        public static int CleanupOldFiles(string auditDir, int retentionDays)
        {
            if (string.IsNullOrWhiteSpace(auditDir) || !Directory.Exists(auditDir)) return 0;
            retentionDays = Clamp(retentionDays);

            var today = DateTime.UtcNow.Date;
            var threshold = today.AddDays(-retentionDays);

            int deleted = 0;
            int skipped = 0;
            DateTime? oldestRemaining = null;
            DateTime? oldestDeleted = null;

            try
            {
                foreach (var file in Directory.EnumerateFiles(auditDir, "*.jsonl"))
                {
                    var name = Path.GetFileNameWithoutExtension(file);
                    if (!DateTime.TryParseExact(name, "yyyy-MM-dd",
                            System.Globalization.CultureInfo.InvariantCulture,
                            System.Globalization.DateTimeStyles.None, out var fileDate))
                    {
                        // 예상 패턴(YYYY-MM-DD.jsonl) 외 파일은 절대 삭제하지 않음.
                        skipped++;
                        continue;
                    }

                    // 오늘 파일은 보존 기간 무관 유지 — 진행 중 세션 보호.
                    if (fileDate >= today) continue;

                    if (fileDate < threshold)
                    {
                        try
                        {
                            File.Delete(file);
                            deleted++;
                            if (!oldestDeleted.HasValue || fileDate < oldestDeleted.Value)
                                oldestDeleted = fileDate;
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"[AuditRetention] 삭제 실패 {file}: {ex.Message}");
                        }
                    }
                    else
                    {
                        if (!oldestRemaining.HasValue || fileDate < oldestRemaining.Value)
                            oldestRemaining = fileDate;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AuditRetention] 디렉토리 열거 실패: {ex.Message}");
            }

            // 정리 행위 자체를 감사 로그에 — 보존 정책 적용 이력 추적.
            // deleted=0 이어도 정책이 동작했다는 사실을 기록 (운영 모니터링).
            var details = $"RetentionDays={retentionDays}, Deleted={deleted}, Skipped={skipped}";
            if (oldestDeleted.HasValue)
                details += $", OldestDeleted={oldestDeleted.Value:yyyy-MM-dd}";
            if (oldestRemaining.HasValue)
                details += $", OldestRemaining={oldestRemaining.Value:yyyy-MM-dd}";

            AuditLogger.Instance.Log(
                AuditCategory.System, "AuditLogRetention", AuditOutcome.Success,
                source: nameof(AuditLogRetention), details: details);

            return deleted;
        }

        private static int Clamp(int days)
        {
            if (days < MinRetentionDays) return MinRetentionDays;
            if (days > MaxRetentionDays) return MaxRetentionDays;
            return days;
        }
    }
}
