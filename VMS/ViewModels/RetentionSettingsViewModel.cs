using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VMS.Core.Backup;
using VMS.Core.Retention;
using VMS.Core.Security;

namespace VMS.ViewModels
{
    /// <summary>
    /// 보존 정책 통합 편집 ViewModel.
    /// - 전역 3개: Audit / AutoBackup / UploadQueue
    /// - 카테고리별 9개: AuditCategoryRetention (Security / UserMgmt / Configuration 등)
    /// system_config.json 의 해당 키만 JsonNode 격리 편집 — 다른 키 / autoBackup 객체
    /// 의 다른 필드 보존. 변경은 VMS 재시작 후 적용.
    /// </summary>
    public partial class RetentionSettingsViewModel : ObservableObject
    {
        private readonly string _configPath;

        public RetentionSettingsViewModel()
        {
            var appData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "BODA VISION AI");
            _configPath = Path.Combine(appData, "system_config.json");

            CategoryRetentions = new ObservableCollection<CategoryRetentionItem>();
            // 카테고리 순서 — GS overview §5.4 그룹 순서와 일치.
            foreach (var cat in CategoryOrder)
            {
                CategoryRetentions.Add(new CategoryRetentionItem
                {
                    Category = cat,
                    Description = CategoryDescriptions[cat],
                    Days = AuditCategoryRetention.Defaults[cat]
                });
            }

            Load();
        }

        // ─── 카테고리 순서 / 설명 ─────────────────────────────────

        private static readonly AuditCategory[] CategoryOrder =
        {
            AuditCategory.Security,
            AuditCategory.UserManagement,
            AuditCategory.Configuration,
            AuditCategory.Authentication,
            AuditCategory.Authorization,
            AuditCategory.RecipeChange,
            AuditCategory.SequenceControl,
            AuditCategory.Inspection,
            AuditCategory.System,
        };

        private static readonly Dictionary<AuditCategory, string> CategoryDescriptions = new()
        {
            [AuditCategory.Security]        = "보안 정책 위반 (HTTP / cert / DTO 거부)",
            [AuditCategory.UserManagement]  = "사용자 생성 / 삭제 / 변경 / 비밀번호",
            [AuditCategory.Configuration]   = "시스템 설정 변경 (PLC / 카메라 / 보안)",
            [AuditCategory.Authentication]  = "로그인 / 로그아웃 / 로그인 실패",
            [AuditCategory.Authorization]   = "권한 부족 거부 / 역할 변경",
            [AuditCategory.RecipeChange]    = "레시피 로드 / 저장 / 변경 / 삭제",
            [AuditCategory.SequenceControl] = "시퀀스 시작 / 중지 / Reset",
            [AuditCategory.Inspection]      = "검사 실행 / NG 발생",
            [AuditCategory.System]          = "보존 정리 / 헬스 체크 등 내부 운영",
        };

        // ─── 바인딩 — 전역 3개 ───────────────────────────────────

        [ObservableProperty] private int _auditRetentionDays = AuditLogRetention.DefaultRetentionDays;
        [ObservableProperty] private int _autoBackupRetentionDays = AutoBackupOptions.DefaultRetentionDays;
        [ObservableProperty] private int _uploadQueueRetentionDays = UploadQueueRetention.DefaultRetentionDays;

        // ─── 바인딩 — 카테고리별 ─────────────────────────────────

        public ObservableCollection<CategoryRetentionItem> CategoryRetentions { get; }

        // ─── Status ──────────────────────────────────────────────

        [ObservableProperty] private string _statusMessage = string.Empty;
        [ObservableProperty] private string _statusColor = "#22303C";  // BrushNeutral

        // 표시용 clamp 범위
        public string AuditRange => $"[{AuditLogRetention.MinRetentionDays}, {AuditLogRetention.MaxRetentionDays}]";
        public string AutoBackupRange => $"[{AutoBackupOptions.MinRetentionDays}, {AutoBackupOptions.MaxRetentionDays}]";
        public string UploadQueueRange => $"[{UploadQueueRetention.MinRetentionDays}, {UploadQueueRetention.MaxRetentionDays}]";

        // ─── 동작 ─────────────────────────────────────────────────

        [RelayCommand]
        private void Load()
        {
            try
            {
                AuditRetentionDays = AuditLogRetention.LoadRetentionDaysFromAppData();
                var autoBackup = AutoBackupOptions.LoadFromAppData();
                AutoBackupRetentionDays = autoBackup.RetentionDays;
                UploadQueueRetentionDays = UploadQueueRetention.LoadRetentionDaysFromAppData();

                var perCat = AuditCategoryRetention.LoadDaysFromAppData();
                foreach (var item in CategoryRetentions)
                {
                    item.Days = perCat.TryGetValue(item.Category, out var d)
                        ? d
                        : AuditCategoryRetention.Defaults[item.Category];
                }

                StatusMessage = File.Exists(_configPath)
                    ? "현재 system_config.json 값을 로드함"
                    : "system_config.json 없음 — 저장 시 새로 생성";
                StatusColor = "#22303C";  // BrushNeutral
            }
            catch (Exception ex)
            {
                StatusMessage = $"로드 실패: {ex.Message}";
                StatusColor = "#EF4444";  // BrushDanger
            }
        }

        [RelayCommand]
        private void Save()
        {
            try
            {
                // Clamp — 저장 직전 보호.
                var audit = ClampInRange(AuditRetentionDays,
                    AuditLogRetention.MinRetentionDays, AuditLogRetention.MaxRetentionDays);
                var ab = AutoBackupOptions.ClampRetention(AutoBackupRetentionDays);
                var queue = ClampInRange(UploadQueueRetentionDays,
                    UploadQueueRetention.MinRetentionDays, UploadQueueRetention.MaxRetentionDays);
                if (audit != AuditRetentionDays) AuditRetentionDays = audit;
                if (ab != AutoBackupRetentionDays) AutoBackupRetentionDays = ab;
                if (queue != UploadQueueRetentionDays) UploadQueueRetentionDays = queue;

                Directory.CreateDirectory(Path.GetDirectoryName(_configPath)!);

                JsonObject root;
                if (File.Exists(_configPath))
                {
                    var text = File.ReadAllText(_configPath);
                    var parsed = JsonNode.Parse(text);
                    root = parsed as JsonObject ?? new JsonObject();
                }
                else
                {
                    root = new JsonObject();
                }

                // Top-level: auditRetentionDays / uploadQueueRetentionDays
                root["auditRetentionDays"] = audit;
                root["uploadQueueRetentionDays"] = queue;

                // Nested: autoBackup.retentionDays — 객체 보존
                var abObj = root["autoBackup"] as JsonObject ?? new JsonObject();
                abObj["retentionDays"] = ab;
                root["autoBackup"] = abObj;

                // 카테고리별 — auditCategoryRetentionDays 객체 보존 + 9 키 갱신.
                var catObj = root["auditCategoryRetentionDays"] as JsonObject ?? new JsonObject();
                foreach (var item in CategoryRetentions)
                {
                    var clamped = ClampInRange(item.Days, 1, 3650);
                    if (clamped != item.Days) item.Days = clamped;
                    catObj[item.Category.ToString()] = clamped;
                }
                root["auditCategoryRetentionDays"] = catObj;

                File.WriteAllText(_configPath,
                    root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

                AuditLogger.Instance.Log(
                    AuditCategory.Configuration, "RetentionConfigSaved", AuditOutcome.Success,
                    source: nameof(RetentionSettingsViewModel),
                    details: $"Audit={audit}d, AutoBackup={ab}d, UploadQueue={queue}d, " +
                             $"CategoryKeys={CategoryRetentions.Count}");

                StatusMessage = "저장됨 — VMS 재시작 후 적용됩니다.";
                StatusColor = "#10B981";  // BrushSuccess
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[RetentionSettings] Save 실패: {ex.Message}");
                AuditLogger.Instance.Log(
                    AuditCategory.Configuration, "RetentionConfigSaved", AuditOutcome.Failure,
                    source: nameof(RetentionSettingsViewModel),
                    details: $"{ex.GetType().Name}: {ex.Message}");
                StatusMessage = $"저장 실패: {ex.Message}";
                StatusColor = "#EF4444";  // BrushDanger
            }
        }

        private static int ClampInRange(int v, int min, int max)
        {
            if (v < min) return min;
            if (v > max) return max;
            return v;
        }
    }
}
