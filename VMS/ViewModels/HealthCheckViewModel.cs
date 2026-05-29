using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VMS.Core.Health;

namespace VMS.ViewModels
{
    /// <summary>
    /// 시작 헬스 체크 윈도우용 ViewModel.
    /// StartupHealthCheck.Run() 을 호출해 5개 검사 결과를 표시,
    /// Refresh 로 재실행 + 결과 텍스트를 클립보드로 복사 (운영자가 IT 에 전달).
    /// </summary>
    public partial class HealthCheckViewModel : ObservableObject
    {
        public ObservableCollection<HealthCheckItem> Items { get; } = new();

        [ObservableProperty] private string _overallStatus = string.Empty;
        // WindowStyles.xaml 색 토큰 — BrushNeutral.
        [ObservableProperty] private string _overallStatusColor = "#22303C";
        [ObservableProperty] private string _summaryLine = string.Empty;
        [ObservableProperty] private DateTime _lastRunAt;

        public HealthCheckViewModel()
        {
            Refresh();
        }

        [RelayCommand]
        private void Refresh()
        {
            var appData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "BODA VISION AI");
            var auditDir = Path.Combine(appData, "audit");

            // auditAfter=true — UI 의 Refresh 도 감사 흔적 남김 (Admin 의 진단 실행 추적).
            var report = StartupHealthCheck.Run(appData, auditDir, auditAfter: true);

            Items.Clear();
            foreach (var item in report.Items) Items.Add(item);

            OverallStatus = report.OverallStatus.ToString();
            OverallStatusColor = StatusColor(report.OverallStatus);
            SummaryLine =
                $"Pass {report.PassCount} · Warn {report.WarnCount} · Fail {report.FailCount}";
            LastRunAt = DateTime.Now;
        }

        [RelayCommand]
        private void CopyToClipboard()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"BODA Vision AI Health Check — {LastRunAt:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"Overall: {OverallStatus} ({SummaryLine})");
            sb.AppendLine();
            foreach (var item in Items)
            {
                sb.AppendLine($"[{item.Status,-4}] {item.Name}: {item.Message}");
            }
            try
            {
                System.Windows.Clipboard.SetText(sb.ToString());
            }
            catch
            {
                // 클립보드 접근 실패는 무시 — UI 동작은 계속.
            }
        }

        /// <summary>주어진 상태 enum 에 대응하는 색 헥스 — WindowStyles.xaml 토큰과 일치.</summary>
        public static string StatusColor(HealthCheckStatus status) => status switch
        {
            HealthCheckStatus.Pass => "#10B981",  // BrushSuccess
            HealthCheckStatus.Warn => "#F59E0B",  // BrushWarning
            HealthCheckStatus.Fail => "#EF4444",  // BrushDanger
            _ => "#22303C"                         // BrushNeutral
        };
    }
}
