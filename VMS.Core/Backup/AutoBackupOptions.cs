using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace VMS.Core.Backup
{
    /// <summary>
    /// 자동 백업 스케줄러 설정 — system_config.json 의 "autoBackup" 객체에서 로드.
    /// Clamp 정책: 사용자 실수 / 손상 입력에서 운영 안정성 보호.
    /// </summary>
    public sealed class AutoBackupOptions
    {
        public const int DefaultIntervalHours = 24;
        public const int DefaultRetentionDays = 30;
        public const int MinIntervalHours = 1;
        public const int MaxIntervalHours = 24 * 30;       // 30일
        public const int MinRetentionDays = 1;
        public const int MaxRetentionDays = 365;

        public bool Enabled { get; init; }
        public int IntervalHours { get; init; } = DefaultIntervalHours;
        public int RetentionDays { get; init; } = DefaultRetentionDays;
        public string? BackupDir { get; init; }            // null 이면 기본 경로
        public bool IncludeAudit { get; init; }
        public string ProductVersion { get; init; } = string.Empty;

        /// <summary>
        /// system_config.json 의 "autoBackup" 객체에서 로드. 키 누락 / 파일 없음 시
        /// 모든 기본값 사용 + Enabled=false.
        /// 범위 밖 값은 [Min, Max] 로 clamp.
        /// </summary>
        public static AutoBackupOptions LoadFromAppData()
        {
            try
            {
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var path = Path.Combine(appData, "BODA VISION AI", "system_config.json");
                if (!File.Exists(path)) return new AutoBackupOptions();

                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                if (!doc.RootElement.TryGetProperty("autoBackup", out var elem)
                    || elem.ValueKind != JsonValueKind.Object)
                {
                    return new AutoBackupOptions();
                }

                bool enabled = false;
                int interval = DefaultIntervalHours;
                int retention = DefaultRetentionDays;
                string? backupDir = null;
                bool includeAudit = false;
                string productVersion = string.Empty;

                if (elem.TryGetProperty("enabled", out var enProp) && enProp.ValueKind == JsonValueKind.True)
                    enabled = true;
                if (elem.TryGetProperty("intervalHours", out var ivProp) && ivProp.TryGetInt32(out var ivVal))
                    interval = ClampInterval(ivVal);
                if (elem.TryGetProperty("retentionDays", out var rtProp) && rtProp.TryGetInt32(out var rtVal))
                    retention = ClampRetention(rtVal);
                if (elem.TryGetProperty("directory", out var dirProp) && dirProp.ValueKind == JsonValueKind.String)
                    backupDir = dirProp.GetString();
                if (elem.TryGetProperty("includeAudit", out var iaProp) && iaProp.ValueKind == JsonValueKind.True)
                    includeAudit = true;
                if (elem.TryGetProperty("productVersion", out var pvProp) && pvProp.ValueKind == JsonValueKind.String)
                    productVersion = pvProp.GetString() ?? string.Empty;

                return new AutoBackupOptions
                {
                    Enabled = enabled,
                    IntervalHours = interval,
                    RetentionDays = retention,
                    BackupDir = backupDir,
                    IncludeAudit = includeAudit,
                    ProductVersion = productVersion
                };
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AutoBackupOptions] Load 실패 — 비활성 fallback: {ex.Message}");
                return new AutoBackupOptions();
            }
        }

        public static int ClampInterval(int hours)
        {
            if (hours < MinIntervalHours) return MinIntervalHours;
            if (hours > MaxIntervalHours) return MaxIntervalHours;
            return hours;
        }

        public static int ClampRetention(int days)
        {
            if (days < MinRetentionDays) return MinRetentionDays;
            if (days > MaxRetentionDays) return MaxRetentionDays;
            return days;
        }
    }
}
