using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using VMS.Core.Backup;
using VMS.Core.Security;

namespace VMS.ViewModels
{
    /// <summary>
    /// 자동 백업 설정 윈도우 ViewModel.
    /// system_config.json 의 "autoBackup" 키만 읽고 쓰는 격리 편집 — 다른 키
    /// (securityMode, plc, 카메라 등) 는 JsonNode 로 보존.
    /// </summary>
    public partial class AutoBackupSettingsViewModel : ObservableObject
    {
        private readonly string _configPath;

        public AutoBackupSettingsViewModel()
        {
            var appData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "BODA VISION AI");
            _configPath = Path.Combine(appData, "system_config.json");
            Load();
        }

        // ─── 바인딩 ───────────────────────────────────────────────

        [ObservableProperty] private bool _enabled;
        [ObservableProperty] private int _intervalHours = AutoBackupOptions.DefaultIntervalHours;
        [ObservableProperty] private int _retentionDays = AutoBackupOptions.DefaultRetentionDays;
        [ObservableProperty] private string _backupDir = string.Empty;
        [ObservableProperty] private bool _includeAudit;
        [ObservableProperty] private string _productVersion = string.Empty;

        [ObservableProperty] private string _statusMessage = string.Empty;
        [ObservableProperty] private string _statusColor = "#9E9E9E";

        // ─── 동작 ─────────────────────────────────────────────────

        [RelayCommand]
        private void Load()
        {
            try
            {
                var loaded = AutoBackupOptions.LoadFromAppData();
                Enabled = loaded.Enabled;
                IntervalHours = loaded.IntervalHours;
                RetentionDays = loaded.RetentionDays;
                BackupDir = loaded.BackupDir ?? string.Empty;
                IncludeAudit = loaded.IncludeAudit;
                ProductVersion = loaded.ProductVersion;
                StatusMessage = File.Exists(_configPath)
                    ? "현재 system_config.json 값을 로드함"
                    : "system_config.json 없음 — 저장 시 새로 생성";
                StatusColor = "#9E9E9E";
            }
            catch (Exception ex)
            {
                StatusMessage = $"로드 실패: {ex.Message}";
                StatusColor = "#F44336";
            }
        }

        [RelayCommand]
        private void PickBackupDir()
        {
            // WPF 기본 OpenFolderDialog (.NET 8) — 폴더 선택만 허용.
            var dlg = new OpenFolderDialog
            {
                Title = "백업 저장 디렉토리 선택"
            };
            if (dlg.ShowDialog() == true) BackupDir = dlg.FolderName;
        }

        [RelayCommand]
        private void Save()
        {
            try
            {
                // Clamp — 잘못된 입력이 디스크에 그대로 가지 않게 보호.
                var iv = AutoBackupOptions.ClampInterval(IntervalHours);
                var rt = AutoBackupOptions.ClampRetention(RetentionDays);
                if (iv != IntervalHours) IntervalHours = iv;
                if (rt != RetentionDays) RetentionDays = rt;

                // 다른 키 보존을 위해 JsonNode 로 read-modify-write.
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

                var autoBackup = root["autoBackup"] as JsonObject ?? new JsonObject();
                autoBackup["enabled"] = Enabled;
                autoBackup["intervalHours"] = IntervalHours;
                autoBackup["retentionDays"] = RetentionDays;
                autoBackup["includeAudit"] = IncludeAudit;
                if (!string.IsNullOrWhiteSpace(BackupDir))
                    autoBackup["directory"] = BackupDir;
                else
                    autoBackup.Remove("directory");
                if (!string.IsNullOrWhiteSpace(ProductVersion))
                    autoBackup["productVersion"] = ProductVersion;
                else
                    autoBackup.Remove("productVersion");
                root["autoBackup"] = autoBackup;

                var options = new JsonSerializerOptions { WriteIndented = true };
                File.WriteAllText(_configPath, root.ToJsonString(options));

                AuditLogger.Instance.Log(
                    AuditCategory.Configuration, "AutoBackupConfigSaved", AuditOutcome.Success,
                    source: nameof(AutoBackupSettingsViewModel),
                    details: $"Enabled={Enabled}, Interval={IntervalHours}h, " +
                             $"Retention={RetentionDays}d, IncludeAudit={IncludeAudit}, " +
                             $"Dir={(string.IsNullOrEmpty(BackupDir) ? "(default)" : BackupDir)}");

                StatusMessage = "저장됨 — VMS 재시작 후 적용됩니다.";
                StatusColor = "#4CAF50";
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AutoBackupSettings] Save 실패: {ex.Message}");
                AuditLogger.Instance.Log(
                    AuditCategory.Configuration, "AutoBackupConfigSaved", AuditOutcome.Failure,
                    source: nameof(AutoBackupSettingsViewModel),
                    details: $"{ex.GetType().Name}: {ex.Message}");
                StatusMessage = $"저장 실패: {ex.Message}";
                StatusColor = "#F44336";
            }
        }
    }
}
