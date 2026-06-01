using System;
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
    /// 3개 보존 정책 (Audit / AutoBackup / UploadQueue) 통합 편집 ViewModel.
    /// system_config.json 의 해당 키만 JsonNode 격리 편집 — 다른 키는 보존.
    /// 변경은 VMS 재시작 후 적용 (모든 보존 정책이 OnStartup 에서 실행됨).
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
            Load();
        }

        // ─── 바인딩 ───────────────────────────────────────────────

        [ObservableProperty] private int _auditRetentionDays = AuditLogRetention.DefaultRetentionDays;
        [ObservableProperty] private int _autoBackupRetentionDays = AutoBackupOptions.DefaultRetentionDays;
        [ObservableProperty] private int _uploadQueueRetentionDays = UploadQueueRetention.DefaultRetentionDays;

        [ObservableProperty] private string _statusMessage = string.Empty;
        // WindowStyles 토큰 — BrushNeutral / BrushSuccess / BrushDanger.
        [ObservableProperty] private string _statusColor = "#22303C";

        // 표시용 clamp 범위 (UI 라벨)
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

                File.WriteAllText(_configPath,
                    root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

                AuditLogger.Instance.Log(
                    AuditCategory.Configuration, "RetentionConfigSaved", AuditOutcome.Success,
                    source: nameof(RetentionSettingsViewModel),
                    details: $"Audit={audit}d, AutoBackup={ab}d, UploadQueue={queue}d");

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
