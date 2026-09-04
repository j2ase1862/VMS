using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using VMS.Camera.Configuration;

namespace VMS.Core.Retention
{
    /// <summary>
    /// 로컬 검사 이력(SQLite) 옵션 — system_config.json 의 <c>inspectionHistory</c> 객체.
    ///
    /// <code>
    /// "inspectionHistory": { "enabled": true, "retentionDays": 90 }
    /// </code>
    ///
    /// - enabled: 검사 사이클마다 로컬 DB 에 1행 기록 (기본 true). 단독 모드의 유일한 영구 이력이며,
    ///   Web 연동 모드에서는 오프라인 백업 역할.
    /// - retentionDays: 보존 일수 (기본 90, clamp [1, 3650]). 부팅 시 + 주기적으로 오래된 행 삭제.
    ///
    /// 읽기는 <see cref="ImageSaveOptions"/> 와 같은 수동 JsonDocument 파싱 — 키 누락/손상 시
    /// 기본값으로 안전 폴백. 쓰기는 RetentionSettingsViewModel 이 JsonNode 격리 편집으로 수행.
    /// </summary>
    public sealed class InspectionHistoryOptions
    {
        public const string SectionKey = "inspectionHistory";
        public const bool DefaultEnabled = true;
        public const int DefaultRetentionDays = 90;
        public const int MinRetentionDays = 1;
        public const int MaxRetentionDays = 3650;

        public bool Enabled { get; init; } = DefaultEnabled;
        public int RetentionDays { get; init; } = DefaultRetentionDays;

        public static InspectionHistoryOptions LoadFromAppData()
            => LoadFromFile(AppDataPaths.SystemConfigFile);

        public static InspectionHistoryOptions LoadFromFile(string configPath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(configPath) || !File.Exists(configPath))
                    return new InspectionHistoryOptions();

                using var doc = JsonDocument.Parse(File.ReadAllText(configPath));
                if (!doc.RootElement.TryGetProperty(SectionKey, out var section)
                    || section.ValueKind != JsonValueKind.Object)
                    return new InspectionHistoryOptions();

                var enabled = DefaultEnabled;
                if (section.TryGetProperty("enabled", out var en)
                    && (en.ValueKind == JsonValueKind.True || en.ValueKind == JsonValueKind.False))
                    enabled = en.ValueKind == JsonValueKind.True;

                var days = DefaultRetentionDays;
                if (section.TryGetProperty("retentionDays", out var rd) && rd.TryGetInt32(out var d))
                    days = ClampRetention(d);

                return new InspectionHistoryOptions { Enabled = enabled, RetentionDays = days };
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[InspectionHistoryOptions] Load 실패: {ex.Message}");
                return new InspectionHistoryOptions();
            }
        }

        public static int ClampRetention(int days)
        {
            if (days < MinRetentionDays) return MinRetentionDays;
            if (days > MaxRetentionDays) return MaxRetentionDays;
            return days;
        }
    }
}
