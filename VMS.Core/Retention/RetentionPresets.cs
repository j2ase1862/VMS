using System.Collections.Generic;
using VMS.Core.Backup;
using VMS.Core.Security;

namespace VMS.Core.Retention
{
    /// <summary>보존 정책 프리셋 1 항목 — 전역 3 + 카테고리별 9.</summary>
    public sealed class RetentionPreset
    {
        public string Name { get; init; } = string.Empty;
        public int Audit { get; init; }
        public int AutoBackup { get; init; }
        public int UploadQueue { get; init; }
        public IReadOnlyDictionary<AuditCategory, int> Categories { get; init; }
            = new Dictionary<AuditCategory, int>();
    }

    /// <summary>
    /// 사이트 운영 환경에 맞춘 빠른 보존 정책 프리셋 — UI(RetentionSettingsViewModel) 와
    /// 매뉴얼(docs/gs/gs_compliance_overview_v1.0.md §5.10) 의 단일 진실 공급원.
    ///
    /// 프리셋 의미:
    /// - Conservative — 규제 / 컴플라이언스 사이트 (장기 보존)
    /// - Standard     — GS 권장 기본값 (Defaults 와 일치)
    /// - Minimal      — 소형 / 디스크 제한 사이트
    ///
    /// 회귀 테스트 (VMS.Core.Tests/Retention/ManualPresetConsistencyTests) 가
    /// 매뉴얼 표와 본 상수의 일치를 자동 검증 — 한쪽만 수정시 빌드 실패.
    /// </summary>
    public static class RetentionPresets
    {
        public const string Conservative = "Conservative";
        public const string Standard = "Standard";
        public const string Minimal = "Minimal";

        /// <summary>이름 → 프리셋 매핑.</summary>
        public static readonly IReadOnlyDictionary<string, RetentionPreset> All
            = new Dictionary<string, RetentionPreset>
            {
                [Conservative] = new RetentionPreset
                {
                    Name = Conservative,
                    Audit = 730,
                    AutoBackup = 90,
                    UploadQueue = 60,
                    Categories = new Dictionary<AuditCategory, int>
                    {
                        [AuditCategory.Security]        = 1825,
                        [AuditCategory.UserManagement]  = 1825,
                        [AuditCategory.Configuration]   = 1825,
                        [AuditCategory.Authentication]  = 1095,
                        [AuditCategory.Authorization]   = 1095,
                        [AuditCategory.RecipeChange]    = 1095,
                        [AuditCategory.SequenceControl] = 730,
                        [AuditCategory.Inspection]      = 730,
                        [AuditCategory.System]          = 180,
                    }
                },
                [Standard] = new RetentionPreset
                {
                    Name = Standard,
                    Audit = AuditLogRetention.DefaultRetentionDays,
                    AutoBackup = AutoBackupOptions.DefaultRetentionDays,
                    UploadQueue = UploadQueueRetention.DefaultRetentionDays,
                    Categories = AuditCategoryRetention.Defaults,
                },
                [Minimal] = new RetentionPreset
                {
                    Name = Minimal,
                    Audit = 90,
                    AutoBackup = 7,
                    UploadQueue = 14,
                    Categories = new Dictionary<AuditCategory, int>
                    {
                        [AuditCategory.Security]        = 365,
                        [AuditCategory.UserManagement]  = 365,
                        [AuditCategory.Configuration]   = 365,
                        [AuditCategory.Authentication]  = 180,
                        [AuditCategory.Authorization]   = 180,
                        [AuditCategory.RecipeChange]    = 180,
                        [AuditCategory.SequenceControl] = 90,
                        [AuditCategory.Inspection]      = 90,
                        [AuditCategory.System]          = 30,
                    }
                },
            };
    }
}
