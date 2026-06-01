using System;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using VMS.Core.SupportPackage;

namespace VMS.ViewModels
{
    /// <summary>
    /// 지원 패키지 export 윈도우 ViewModel.
    /// SupportPackageService.Export 호출 — 백업과 분리된 지원/엔지니어링 분석용 ZIP 생성.
    /// 민감 파일 (BodaVision.db, recipes/) 은 서비스 레벨에서 절대 미포함.
    /// </summary>
    public partial class SupportPackageViewModel : ObservableObject
    {
        private readonly string _appDataDir;

        public SupportPackageViewModel()
        {
            _appDataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "BODA VISION AI");
            DefaultFileName = $"BODA-VMS-support-{DateTime.Now:yyyyMMdd-HHmmss}.zip";
        }

        // ─── 바인딩 ───────────────────────────────────────────────

        [ObservableProperty] private string _defaultFileName = string.Empty;
        [ObservableProperty] private int _auditDays = 7;
        [ObservableProperty] private bool _includeEnvironment = true;
        [ObservableProperty] private bool _includeBackupIndex = true;
        [ObservableProperty] private bool _includeMachineName;
        [ObservableProperty] private string _productVersion = "1.2.0";

        [ObservableProperty] private string _status = "Ready";
        [ObservableProperty] private string _statusColor = "#22303C";  // BrushNeutral
        [ObservableProperty] private string _summaryLine = string.Empty;

        // ─── 동작 ─────────────────────────────────────────────────

        [RelayCommand]
        private void RunExport()
        {
            var dlg = new SaveFileDialog
            {
                Filter = "지원 패키지 (*.zip)|*.zip|모든 파일 (*.*)|*.*",
                FileName = DefaultFileName,
                DefaultExt = ".zip"
            };
            if (dlg.ShowDialog() != true) return;

            var result = SupportPackageService.Export(_appDataDir, dlg.FileName,
                new SupportPackageOptions
                {
                    AuditDays = AuditDays,
                    IncludeEnvironment = IncludeEnvironment,
                    IncludeBackupIndex = IncludeBackupIndex,
                    IncludeMachineName = IncludeMachineName,
                    ProductVersion = ProductVersion
                });

            if (result.Success)
            {
                Status = "Success";
                StatusColor = "#10B981";  // BrushSuccess
                SummaryLine =
                    $"{result.FileCount} 항목 · {result.BytesWritten / 1024.0:F1} KB → {result.ZipPath}";
            }
            else
            {
                Status = "Failed";
                StatusColor = "#EF4444";  // BrushDanger
                SummaryLine = result.ErrorMessage ?? "(원인 불명)";
            }
        }
    }
}
