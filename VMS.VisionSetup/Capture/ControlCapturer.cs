#if DEBUG
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace VMS.VisionSetup.Capture
{
    /// <summary>
    /// DEBUG 전용 — WPF 컨트롤을 개별 PNG 로 코드 캡처하는 매뉴얼/문서용 유틸리티 (VisionSetup 판).
    ///
    /// PicPick 등의 "윈도우 컨트롤 캡처"는 자식 Win32 HWND 를 열거하는데, WPF 컨트롤은 최상위
    /// Window 1개만 HWND 를 가지고 내부는 전부 drawn 요소라 개별 컨트롤로 잡히지 않는다. 그래서
    /// 비주얼 트리를 직접 순회하여 RenderTargetBitmap 으로 컨트롤별 PNG 를 떨어뜨린다.
    ///
    /// VMS.AppSetup 의 동명 유틸과 동일 원리. AppSetup 은 6단계 위저드라 페이지/장면(Scene)을
    /// 순회하지만, VisionSetup 은 단일 MainView 라 현재 화면을 한 번 순회한다.
    ///
    /// 전체 파일이 #if DEBUG 로 감싸여 Release(배포) 빌드에서는 컴파일되지 않으며, App 이
    /// "--capture-controls" 인자로 실행될 때만 호출된다. → 일반 실행/배포 동작에 일절 영향 없음.
    /// </summary>
    internal static class ControlCapturer
    {
        private const double Scale = 2.0;       // 2x (192 DPI) — 매뉴얼용 선명도

        /// <summary>
        /// 윈도우(MainView)의 현재 비주얼 트리에서 보이는 모든 대상 컨트롤을 PNG 로 저장한다.
        /// </summary>
        public static async Task RunAsync(Window window, string outputDir)
        {
            Directory.CreateDirectory(outputDir);

            var seen = new HashSet<DependencyObject>();
            var manifest = new List<string>();

            // MainView 는 작업영역 크기(동적)라 ActualSize 우선, 없으면 1920x1080 fallback.
            double w = window.Width;
            double h = window.Height;
            if (double.IsNaN(w) || w < 1) w = 1920;
            if (double.IsNaN(h) || h < 1) h = 1080;
            var size = new Size(w, h);

            Brush backdrop = window.Background ?? new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x2E));
            if (backdrop.CanFreeze) backdrop.Freeze();

            // headless(무 데스크톱) 환경에서도 동작하도록 레이아웃을 코드로 강제.
            for (int pass = 0; pass < 2; pass++)
            {
                window.Measure(size);
                window.Arrange(new Rect(new Point(0, 0), size));
                window.UpdateLayout();
                await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);
            }

            int total = CaptureVisible(window, outputDir, seen, manifest, backdrop);

            var sb = new StringBuilder();
            sb.AppendLine("# VMS.VisionSetup 컨트롤 캡처 인덱스");
            sb.AppendLine();
            sb.AppendLine($"총 {total} 개 컨트롤 / 출력 폴더: {outputDir}");
            sb.AppendLine();
            sb.AppendLine("| Type | Label | File |");
            sb.AppendLine("|------|-------|------|");
            foreach (var line in manifest)
                sb.AppendLine(line);
            File.WriteAllText(Path.Combine(outputDir, "INDEX.md"), sb.ToString(), Encoding.UTF8);
        }

        private static int CaptureVisible(
            Window window, string outputDir,
            HashSet<DependencyObject> seen, List<string> manifest, Brush backdrop)
        {
            int count = 0;
            int seq = 0;

            foreach (var element in EnumerateVisualTree(window))
            {
                if (element is not FrameworkElement fe) continue;
                if (fe.ActualWidth < 1 || fe.ActualHeight < 1) continue;

                string? type = Classify(fe);
                if (type == null) continue;

                // 라벨(TextBlock)은 Button/RadioButton 등의 내부 콘텐츠면 중복이라 제외.
                if (fe is TextBlock && IsInsideCapturedControl(fe)) continue;

                if (!seen.Add(fe)) continue;

                seq++;
                string label = GetLabel(fe);
                string fileName = $"{seq:D3}_{type}_{Sanitize(label)}.png";
                string fullPath = Path.Combine(outputDir, fileName);

                if (TryRender(fe, fullPath, backdrop))
                {
                    count++;
                    manifest.Add($"| {type} | {Escape(label)} | {fileName} |");
                }
            }

            return count;
        }

        /// <summary>대상 컨트롤 분류 — 해당 없으면 null.</summary>
        private static string? Classify(FrameworkElement fe) => fe switch
        {
            RadioButton => "RadioButton",
            CheckBox => "CheckBox",
            ToggleButton => "ToggleButton",
            Button => "Button",
            ComboBox => "ComboBox",
            ListBox => "ListBox",
            Slider => "Slider",
            PasswordBox => "PasswordBox",
            TextBox => "TextBox",
            TextBlock => "Label",
            _ => null
        };

        private static string GetLabel(FrameworkElement fe)
        {
            switch (fe)
            {
                case TextBlock tb:
                    return tb.Text;
                case ContentControl cc:
                    if (cc.Content is string s) return s;
                    if (cc.Content is TextBlock inner) return inner.Text;
                    return cc.Name;
                case TextBox txt:
                    return !string.IsNullOrEmpty(txt.Name) ? txt.Name
                         : !string.IsNullOrEmpty(txt.Text) ? txt.Text : "TextBox";
                case PasswordBox pb:
                    return !string.IsNullOrEmpty(pb.Name) ? pb.Name : "PasswordBox";
                case ComboBox cb:
                    return cb.SelectedItem?.ToString() ?? (!string.IsNullOrEmpty(cb.Name) ? cb.Name : "ComboBox");
                default:
                    return string.IsNullOrEmpty(fe.Name) ? fe.GetType().Name : fe.Name;
            }
        }

        /// <summary>TextBlock 이 Button/RadioButton/CheckBox/Combo 등의 내부 콘텐츠인지.</summary>
        private static bool IsInsideCapturedControl(DependencyObject node)
        {
            var parent = VisualTreeHelper.GetParent(node);
            while (parent != null)
            {
                if (parent is ButtonBase || parent is Selector || parent is PasswordBox)
                    return true;
                parent = VisualTreeHelper.GetParent(parent);
            }
            return false;
        }

        /// <summary>
        /// 컨트롤을 화면상의 위치/오프셋과 무관하게 원점에 렌더링하여 PNG 저장.
        /// 다크 배경을 먼저 깔아 문서에서 가독성 확보.
        /// </summary>
        private static bool TryRender(FrameworkElement fe, string path, Brush backdrop)
        {
            try
            {
                double w = fe.ActualWidth;
                double h = fe.ActualHeight;
                var bounds = new Rect(new Point(0, 0), new Size(w, h));

                var dv = new DrawingVisual();
                using (var ctx = dv.RenderOpen())
                {
                    ctx.DrawRectangle(backdrop, null, bounds);
                    ctx.DrawRectangle(new VisualBrush(fe) { Stretch = Stretch.None }, null, bounds);
                }

                var rtb = new RenderTargetBitmap(
                    (int)(w * Scale), (int)(h * Scale),
                    96 * Scale, 96 * Scale,
                    PixelFormats.Pbgra32);
                rtb.Render(dv);

                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(rtb));
                using var fs = File.Create(path);
                encoder.Save(fs);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static IEnumerable<DependencyObject> EnumerateVisualTree(DependencyObject root)
        {
            int childCount = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < childCount; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);

                // Collapsed/Hidden 서브트리(비활성 탭·패널)는 통째로 건너뛴다.
                if (child is UIElement ui && ui.Visibility != Visibility.Visible)
                    continue;

                yield return child;
                foreach (var descendant in EnumerateVisualTree(child))
                    yield return descendant;
            }
        }

        private static string Sanitize(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "unnamed";
            var sb = new StringBuilder();
            foreach (char c in s.Trim())
            {
                if (char.IsLetterOrDigit(c) || c == '-' || c == '_')
                    sb.Append(c);
                else if (char.IsWhiteSpace(c))
                    sb.Append('_');
            }
            string result = sb.ToString();
            if (result.Length > 40) result = result.Substring(0, 40);
            return result.Length == 0 ? "unnamed" : result;
        }

        private static string Escape(string s) => s.Replace("|", "\\|").Replace("\n", " ").Trim();
    }
}
#endif
