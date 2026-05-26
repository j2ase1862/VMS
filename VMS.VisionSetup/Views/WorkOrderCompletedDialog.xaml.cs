using System;
using System.Media;
using System.Windows;
using VMS.Core.Models.ParameterSync;

namespace VMS.VisionSetup.Views
{
    public partial class WorkOrderCompletedDialog : Window
    {
        /// <summary>true = 다음 작업지시 선택, false = 닫기 (혹은 ESC).</summary>
        public bool PickNext { get; private set; }

        public WorkOrderCompletedDialog(WorkOrderProgressDto progress)
        {
            EnsureWindowStylesMerged();
            InitializeComponent();

            OrderNoText.Text = progress.OrderNo;
            PlannedText.Text = progress.PlannedQuantity.ToString();
            ProducedText.Text = progress.ProducedQuantity.ToString();
            PassText.Text = progress.PassQuantity.ToString();
            NgText.Text = progress.NgQuantity.ToString();
            PassRateText.Text = $"Pass rate {progress.PassRate:F1}%   ·   진척률 {progress.Progress:F1}%";

            Loaded += (_, _) =>
            {
                // 작업자 주의 환기 — Windows 표준 알림음 (시스템 볼륨 따름)
                try { SystemSounds.Asterisk.Play(); } catch { }
            };
        }

        private void NextButton_Click(object sender, RoutedEventArgs e)
        {
            PickNext = true;
            DialogResult = true;
            Close();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            PickNext = false;
            DialogResult = false;
            Close();
        }

        private static void EnsureWindowStylesMerged()
        {
            var app = Application.Current;
            if (app == null) return;
            if (app.Resources.Contains("BrushBgWindow")) return;
            try
            {
                var dict = new ResourceDictionary
                {
                    Source = new Uri(
                        "pack://application:,,,/VMS.VisionSetup;component/Styles/WindowStyles.xaml",
                        UriKind.Absolute)
                };
                app.Resources.MergedDictionaries.Add(dict);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[WorkOrderCompletedDialog] MergeStyles failed: {ex.Message}");
            }
        }
    }
}
