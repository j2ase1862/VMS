#if DEBUG
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Boda.LicGen.App.Services;
using Boda.LicGen.App.ViewModels;
using Boda.LicGen.App.Views;
using VMS.Core.Security.Licensing;

namespace Boda.LicGen.App.Capture
{
    /// <summary>
    /// 매뉴얼 그림용 캡처(DEBUG 전용) — 임시 키 폴더에 버리는 서명 키와 예시 대장 3줄을 만들어
    /// [라이선스 발급] · [발급 대장] 탭을 각각 PNG(2배 해상도)로 저장하고 임시 폴더를 지운 뒤 끝낸다.
    /// 화면 입력은 쓰지 않는다(탭은 SelectedIndex 로 바꾼다).
    /// </summary>
    internal static class WindowCapture
    {
        public static void Run(Application app, string outDir)
        {
            Directory.CreateDirectory(outDir);
            var keyDir = Path.Combine(Path.GetTempPath(), "licgen_capture_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(keyDir);

            // 버리는 키 — 캡처가 끝나면 폴더째 지운다. 배너에는 키 ID 만 보인다.
            var (privatePem, _) = LicenseCrypto.CreateKeyPair();
            File.WriteAllText(Path.Combine(keyDir, "prod-2026.private.pem"), privatePem);
            SeedLedger(keyDir);

            var vm = new MainViewModel(new DialogService(), new LedgerNotesStore(keyDir), keyDir);
            vm.Issue.Customer = "예시 고객(주) 1공장";
            vm.Issue.Fingerprint = "ABCDE-12345-FGHIJ";
            vm.Issue.SeatsText = "2";
            vm.Issue.OptionRobot = false;
            vm.Issue.OptionLineScan = false;
            // 대장 위치 줄 — 임시 폴더 경로 대신 실제 기본 위치로 보인다.
            vm.Ledger.SummaryText = $@"총 {vm.Ledger.Rows.Count}건 — %USERPROFILE%\.boda-licgen\ledger.jsonl";

            var win = new MainWindow { DataContext = vm };
            app.MainWindow = win;
            win.Show();

            _ = win.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(async () =>
            {
                var log = Path.Combine(outDir, "_capture.log");
                try
                {
                    var tabs = FindChild<TabControl>(win);
                    if (tabs != null) tabs.SelectedIndex = 0;
                    await Task.Delay(600);
                    Save(win, Path.Combine(outDir, "LicGen_Issue.png"));

                    if (tabs != null) tabs.SelectedIndex = 1;
                    await Task.Delay(600);
                    Save(win, Path.Combine(outDir, "LicGen_Ledger.png"));
                }
                catch (Exception ex)
                {
                    File.AppendAllText(log, ex + Environment.NewLine);
                }
                finally
                {
                    try { Directory.Delete(keyDir, recursive: true); } catch { }
                    app.Shutdown();
                }
            }));
        }

        private static void SeedLedger(string keyDir)
        {
            var today = DateOnly.FromDateTime(DateTime.Today);
            (string Customer, string Kind, string Fp, int Seats, string? Exp, string[] Opts)[] samples =
            {
                ("예시 고객(주) 2공장", "Production", "KLMNO-67890-PQRST", 1, null, new[] { "plc", "3d-camera" }),
                ("예시 시험 라인", "Trial", "UVWXY-13579-ZABCD", 1, today.AddDays(30).ToString("yyyy-MM-dd"), new[] { "mlops" }),
                ("사내 개발 PC", "Internal", "EFGHI-24680-JKLMN", 3, today.AddDays(60).ToString("yyyy-MM-dd"), new[] { "3d-camera", "robot", "mlops", "plc", "line-scan" }),
            };
            int i = 0;
            foreach (var s in samples)
            {
                LedgerStore.Append(keyDir, new LedgerEntry(
                    LedgerStore.NextLicenseId(keyDir, today), s.Customer, s.Kind, s.Fp, s.Seats,
                    today.AddYears(1).ToString("yyyy-MM-dd"), s.Exp,
                    DateTime.UtcNow.AddHours(-(3 - i++) * 5).ToString("o"), "admin", s.Opts));
            }
        }

        private static void Save(Window win, string path)
        {
            win.UpdateLayout();
            const double scale = 2.0;
            int w = (int)Math.Ceiling(win.ActualWidth * scale), h = (int)Math.Ceiling(win.ActualHeight * scale);
            var rtb = new RenderTargetBitmap(w, h, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
            rtb.Render(win);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(rtb));
            using var fs = File.Create(path);
            enc.Save(fs);
        }

        private static T? FindChild<T>(DependencyObject parent) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var c = VisualTreeHelper.GetChild(parent, i);
                if (c is T t) return t;
                if (FindChild<T>(c) is T found) return found;
            }
            return null;
        }
    }
}
#endif
