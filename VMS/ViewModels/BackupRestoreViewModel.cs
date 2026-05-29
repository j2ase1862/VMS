using System;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using VMS.Core.Backup;

namespace VMS.ViewModels
{
    /// <summary>
    /// 백업 / 복원 윈도우용 ViewModel.
    /// 두 섹션 (Backup / Restore) — Microsoft.Win32 파일 다이얼로그로 경로 선택,
    /// BackupRestoreService 호출 후 결과 / manifest 메타데이터 표시.
    /// </summary>
    public partial class BackupRestoreViewModel : ObservableObject
    {
        private readonly string _appDataDir;

        public BackupRestoreViewModel()
        {
            _appDataDir = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "BODA VISION AI");
            BackupFileName = $"BODA-VMS-backup-{DateTime.Now:yyyyMMdd-HHmmss}.zip";
        }

        // ─── Backup 섹션 ─────────────────────────────────────────

        [ObservableProperty] private string _backupFileName = string.Empty;
        [ObservableProperty] private bool _includeAudit;
        [ObservableProperty] private string _productVersion = "1.2.0";
        [ObservableProperty] private string _backupStatus = "Ready";
        [ObservableProperty] private string _backupStatusColor = "#9E9E9E";
        [ObservableProperty] private string _lastBackupSummary = string.Empty;

        [RelayCommand]
        private void RunBackup()
        {
            var dlg = new SaveFileDialog
            {
                Filter = "VMS 백업 (*.zip)|*.zip|모든 파일 (*.*)|*.*",
                FileName = BackupFileName,
                DefaultExt = ".zip"
            };
            if (dlg.ShowDialog() != true) return;

            var result = BackupRestoreService.CreateBackup(_appDataDir, dlg.FileName,
                new BackupOptions
                {
                    IncludeAudit = IncludeAudit,
                    ProductVersion = ProductVersion
                });

            if (result.Success)
            {
                BackupStatus = "Success";
                BackupStatusColor = "#4CAF50";
                LastBackupSummary =
                    $"{result.FileCount} 파일 · {result.BytesWritten / 1024.0:F1} KB → {result.ZipPath}";
            }
            else
            {
                BackupStatus = "Failed";
                BackupStatusColor = "#F44336";
                LastBackupSummary = result.ErrorMessage ?? "(원인 불명)";
            }
        }

        // ─── Restore 섹션 ────────────────────────────────────────

        [ObservableProperty] private string _restoreFilePath = string.Empty;
        [ObservableProperty] private bool _overwrite = true;
        [ObservableProperty] private bool _restoreAudit = true;
        [ObservableProperty] private string _restoreStatus = "Ready";
        [ObservableProperty] private string _restoreStatusColor = "#9E9E9E";
        [ObservableProperty] private string _lastRestoreSummary = string.Empty;
        [ObservableProperty] private string _manifestSummary = string.Empty;

        [RelayCommand]
        private void PickRestoreFile()
        {
            var dlg = new OpenFileDialog
            {
                Filter = "VMS 백업 (*.zip)|*.zip|모든 파일 (*.*)|*.*"
            };
            if (dlg.ShowDialog() == true) RestoreFilePath = dlg.FileName;
        }

        [RelayCommand]
        private void RunRestore()
        {
            if (string.IsNullOrWhiteSpace(RestoreFilePath) || !File.Exists(RestoreFilePath))
            {
                RestoreStatus = "Failed";
                RestoreStatusColor = "#F44336";
                LastRestoreSummary = "백업 파일을 먼저 선택하세요.";
                return;
            }

            var result = BackupRestoreService.RestoreBackup(RestoreFilePath, _appDataDir,
                new RestoreOptions
                {
                    Overwrite = Overwrite,
                    RestoreAudit = RestoreAudit
                });

            if (result.Success)
            {
                RestoreStatus = "Success";
                RestoreStatusColor = "#4CAF50";
                LastRestoreSummary = $"복원 {result.FilesRestored} 파일 · skip {result.FilesSkipped} 파일";
                if (result.Manifest != null)
                {
                    ManifestSummary =
                        $"백업 시각: {result.Manifest.CreatedAtUtc:yyyy-MM-dd HH:mm:ss} UTC · " +
                        $"버전: {result.Manifest.ProductVersion} · " +
                        $"DB 포함: {result.Manifest.IncludesUsersDb} · " +
                        $"Audit 포함: {result.Manifest.IncludesAudit}";
                }
            }
            else
            {
                RestoreStatus = "Failed";
                RestoreStatusColor = "#F44336";
                LastRestoreSummary = result.ErrorMessage ?? "(원인 불명)";
                ManifestSummary = string.Empty;
            }
        }
    }
}
