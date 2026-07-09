using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using VMS.Core.Security;
using VMS.Camera.Configuration;

namespace VMS.Core.Retention
{
    /// <summary>
    /// upload_queue 보존 정책 — 실패한 검사 결과 업로드 JSON 이 무한 누적되는 것을 방지.
    ///
    /// 대상: %LocalAppData%\BODA VISION AI\upload_queue\*.json
    /// 파일명 패턴: yyyyMMddHHmmssfff_{guid:N}.json (ParameterSyncService.EnqueueFailed)
    ///
    /// 정책:
    /// - 기본 보존 30일 (Web 백엔드 다운 / 사이트 네트워크 장애 복구 충분 기간)
    /// - %LocalAppData%\BODA VISION AI\system_config.json 의 "uploadQueueRetentionDays" 로 덮어쓰기
    /// - 최소 1일, 최대 365일 clamp — 실수 / 손상 방지
    /// - 파일명 prefix 가 timestamp 패턴과 일치하지 않으면 절대 삭제 X (사용자 임의 파일 보호)
    /// - 정리 작업 자체를 AuditCategory.System "UploadQueueRetention" 으로 기록
    ///
    /// AuditLogRetention 과 동일 호출 시점: VMS App.xaml.cs OnStartup 1회.
    /// </summary>
    public static class UploadQueueRetention
    {
        public const int DefaultRetentionDays = 30;
        public const int MinRetentionDays = 1;
        public const int MaxRetentionDays = 365;

        private const string TimestampFormat = "yyyyMMddHHmmssfff";

        /// <summary>
        /// system_config.json 의 "uploadQueueRetentionDays" 키 로드. 키 누락 / 파일 없음 시
        /// <see cref="DefaultRetentionDays"/> 사용.
        /// </summary>
        public static int LoadRetentionDaysFromAppData()
        {
            try
            {
                var path = AppDataPaths.SystemConfigFile;
                if (!File.Exists(path)) return DefaultRetentionDays;

                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                if (doc.RootElement.TryGetProperty("uploadQueueRetentionDays", out var prop)
                    && prop.TryGetInt32(out var days))
                {
                    return Clamp(days);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[UploadQueueRetention] LoadRetentionDays 실패: {ex.Message}");
            }
            return DefaultRetentionDays;
        }

        /// <summary>
        /// <paramref name="queueDir"/> 내 timestamp-prefix .json 파일 중 <paramref name="retentionDays"/>
        /// 보다 오래된 것 삭제. 결과를 AuditCategory.System 으로 기록.
        /// </summary>
        public static int CleanupOldFiles(string queueDir, int retentionDays)
        {
            if (string.IsNullOrWhiteSpace(queueDir) || !Directory.Exists(queueDir)) return 0;
            retentionDays = Clamp(retentionDays);

            var thresholdUtc = DateTime.UtcNow.AddDays(-retentionDays);
            int deleted = 0;
            int skipped = 0;
            DateTime? oldestRemaining = null;
            DateTime? oldestDeleted = null;

            try
            {
                foreach (var file in Directory.EnumerateFiles(queueDir, "*.json"))
                {
                    var name = Path.GetFileNameWithoutExtension(file);
                    if (string.IsNullOrEmpty(name))
                    {
                        skipped++;
                        continue;
                    }

                    var underscoreIdx = name.IndexOf('_');
                    if (underscoreIdx != TimestampFormat.Length)
                    {
                        // 패턴 외 파일 (사용자 수동 파일 등) — 절대 삭제하지 않음.
                        skipped++;
                        continue;
                    }

                    var tsPart = name.Substring(0, underscoreIdx);
                    if (!DateTime.TryParseExact(tsPart, TimestampFormat,
                            CultureInfo.InvariantCulture,
                            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                            out var fileUtc))
                    {
                        skipped++;
                        continue;
                    }

                    if (fileUtc < thresholdUtc)
                    {
                        try
                        {
                            File.Delete(file);
                            deleted++;
                            if (!oldestDeleted.HasValue || fileUtc < oldestDeleted.Value)
                                oldestDeleted = fileUtc;
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"[UploadQueueRetention] 삭제 실패 {file}: {ex.Message}");
                        }
                    }
                    else
                    {
                        if (!oldestRemaining.HasValue || fileUtc < oldestRemaining.Value)
                            oldestRemaining = fileUtc;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[UploadQueueRetention] 열거 실패: {ex.Message}");
            }

            var details = $"RetentionDays={retentionDays}, Deleted={deleted}, Skipped={skipped}";
            if (oldestDeleted.HasValue)
                details += $", OldestDeleted={oldestDeleted.Value:yyyy-MM-dd}";
            if (oldestRemaining.HasValue)
                details += $", OldestRemaining={oldestRemaining.Value:yyyy-MM-dd}";

            AuditLogger.Instance.Log(
                AuditCategory.System, "UploadQueueRetention", AuditOutcome.Success,
                source: nameof(UploadQueueRetention), details: details);

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
