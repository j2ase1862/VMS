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
    /// "inspectionHistory": { "enabled": true, "retentionDays": 90, "maxRows": 2000000 }
    /// </code>
    ///
    /// - enabled: 검사 사이클마다 로컬 DB 에 1행 기록 (기본 true). 단독 모드의 유일한 영구 이력이며,
    ///   Web 연동 모드에서는 오프라인 백업 역할.
    /// - retentionDays: 보존 일수 (기본 90, clamp [1, 3650]).
    /// - maxRows: 행 수 상한 (기본 2,000,000, clamp [10,000, 50,000,000]). 보존 일수 안이라도 이 수를 넘으면
    ///   가장 오래된 행부터 지운다 — 고속 라인에서 디스크가 차는 것을 막는 안전장치. 행당 약 1 KB 라
    ///   기본값이면 DB 는 대략 2 GB 이내. 화면에는 없고 system_config.json 에서만 바꾼다.
    /// 정리는 VMS 시작 2 분 뒤 한 번 + 켜 둔 동안 1 시간마다(<c>InspectionHistoryRetentionScheduler</c>).
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
        public const int DefaultMaxRows = 2_000_000;
        public const int MinMaxRows = 10_000;
        public const int MaxMaxRows = 50_000_000;

        public bool Enabled { get; init; } = DefaultEnabled;
        public int RetentionDays { get; init; } = DefaultRetentionDays;
        public int MaxRows { get; init; } = DefaultMaxRows;

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
                // 숫자가 아닌 값은 그 키만 기본값 — TryGetInt32 는 숫자 아닌 요소에서 예외를 던져
                // 다른 키(enabled · retentionDays)까지 기본값으로 되돌리던 것을 막는다.
                if (section.TryGetProperty("retentionDays", out var rd) && rd.ValueKind == JsonValueKind.Number
                    && rd.TryGetInt32(out var d))
                    days = ClampRetention(d);

                var maxRows = DefaultMaxRows;
                if (section.TryGetProperty("maxRows", out var mr) && mr.ValueKind == JsonValueKind.Number)
                {
                    if (mr.TryGetInt32(out var m)) maxRows = ClampMaxRows(m);
                    else if (mr.TryGetInt64(out var big)) maxRows = big > 0 ? MaxMaxRows : MinMaxRows;   // int 범위 밖
                }

                return new InspectionHistoryOptions { Enabled = enabled, RetentionDays = days, MaxRows = maxRows };
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

        public static int ClampMaxRows(int rows)
        {
            if (rows < MinMaxRows) return MinMaxRows;
            if (rows > MaxMaxRows) return MaxMaxRows;
            return rows;
        }
    }
}
