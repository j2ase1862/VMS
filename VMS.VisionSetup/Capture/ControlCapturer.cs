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

            await LayoutPass(window, size);

            // 1) 기본 화면 캡처
            int total = CaptureVisible(window, outputDir, seen, manifest, backdrop);

            // 2) 숨은 컨트롤 노출 — 접힌 Expander 펼치기 + TabControl 모든 탭 순회.
            //    중첩(탭 안의 Expander 등)을 위해 최대 3 라운드 반복. seen 집합으로 중복 1회만.
            for (int round = 0; round < 3; round++)
            {
                bool expanded = ExpandAllExpanders(window);
                if (expanded) await LayoutPass(window, size);

                int before = total;
                total += CaptureVisible(window, outputDir, seen, manifest, backdrop);

                foreach (var tc in FindAll<TabControl>(window))
                {
                    if (tc.Items.Count <= 1) continue;
                    int orig = tc.SelectedIndex;
                    for (int i = 0; i < tc.Items.Count; i++)
                    {
                        tc.SelectedIndex = i;
                        await LayoutPass(window, size);
                        total += CaptureVisible(window, outputDir, seen, manifest, backdrop);
                    }
                    tc.SelectedIndex = orig;
                }

                await LayoutPass(window, size);
                total += CaptureVisible(window, outputDir, seen, manifest, backdrop);
                if (total == before && !expanded) break;   // 더 이상 새 컨트롤 없음 → 수렴
            }

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

        /// <summary>
        /// MainView 전체 화면(헤더+패널+팔레트 등)을 한 장의 PNG 로 캡처한다.
        /// 접힌 Expander 를 모두 펼쳐 Tool Palette 전체가 보이도록 한 뒤 윈도우 루트를 통째로 렌더.
        /// </summary>
        public static async Task RunFullPageAsync(Window window, string outputDir)
        {
            Directory.CreateDirectory(outputDir);

            double w = window.Width;
            double h = window.Height;
            if (double.IsNaN(w) || w < 1) w = 1920;
            if (double.IsNaN(h) || h < 1) h = 1080;
            var size = new Size(w, h);

            Brush backdrop = window.Background ?? new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x2E));
            if (backdrop.CanFreeze) backdrop.Freeze();

            await LayoutPass(window, size);
            for (int round = 0; round < 3; round++)
            {
                if (!ExpandAllExpanders(window)) break;
                await LayoutPass(window, size);
            }

            var root = window.Content as FrameworkElement ?? window;
            double rw = root.ActualWidth > 1 ? root.ActualWidth : w;
            double rh = root.ActualHeight > 1 ? root.ActualHeight : h;
            var bounds = new Rect(new Point(0, 0), new Size(rw, rh));

            var dv = new DrawingVisual();
            using (var ctx = dv.RenderOpen())
            {
                ctx.DrawRectangle(backdrop, null, bounds);
                ctx.DrawRectangle(new VisualBrush(root) { Stretch = Stretch.None }, null, bounds);
            }
            var rtb = new RenderTargetBitmap(
                (int)(rw * Scale), (int)(rh * Scale), 96 * Scale, 96 * Scale, PixelFormats.Pbgra32);
            rtb.Render(dv);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(rtb));
            using var fs = File.Create(Path.Combine(outputDir, "FullPage_MainView.png"));
            encoder.Save(fs);
        }

        /// <summary>
        /// 모든 비전 툴의 우측 Tool Settings 파라미터 패널을 한 장씩 캡처한다.
        /// 각 ToolType 으로 VisionTool 을 만들어 MainViewModel.SelectedTool 에 할당하면
        /// 우측 ToolSettingsControl(ScrollViewer>StackPanel) 이 해당 툴 패널로 렌더된다.
        /// 스크롤에 가려지는 부분까지 StackPanel 을 무한 높이로 측정해 전체를 담는다.
        /// </summary>
        public static async Task RunToolPanelsAsync(Window window, string outputDir)
        {
            Directory.CreateDirectory(outputDir);
            if (window.DataContext is not ViewModels.MainViewModel vm) return;

            double w = window.Width;
            double h = window.Height;
            if (double.IsNaN(w) || w < 1) w = 1920;
            if (double.IsNaN(h) || h < 1) h = 1080;
            var size = new Size(w, h);

            Brush backdrop = window.Background ?? new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x2E));
            if (backdrop.CanFreeze) backdrop.Freeze();

            await LayoutPass(window, size);

            var settingsHost = window.FindName("ToolSettingsControl") as FrameworkElement;
            if (settingsHost == null) return;

            var manifest = new List<string>();
            int seq = 0;
            foreach (var cat in vm.ToolTree)
            {
                foreach (var ti in cat.Tools)
                {
                    var vt = Services.VisionService.CreateTool(ti.ToolType);
                    if (vt == null)
                    {
                        manifest.Add($"| {ti.Name} | {ti.ToolType} | (CreateTool null) |");
                        continue;
                    }
                    var sel = new Models.ToolItem { Name = ti.Name, ToolType = ti.ToolType, VisionTool = vt };
                    try { vm.SelectedTool = sel; }
                    catch { manifest.Add($"| {ti.Name} | {ti.ToolType} | (select 예외) |"); continue; }

                    await LayoutPass(window, size);
                    await LayoutPass(window, size);   // 템플릿 셀렉터 렌더 안정화

                    var sp = FindSettingsStackPanel(settingsHost);
                    seq++;
                    string file = $"{seq:D2}_{Sanitize(ti.Name)}.png";
                    bool ok = sp != null && RenderFullElement(sp, backdrop, Path.Combine(outputDir, file));
                    manifest.Add($"| {ti.Name} | {ti.ToolType} | {(ok ? file : "(패널 없음)")} |");
                }
            }
            try { vm.SelectedTool = null; } catch { }

            var sb = new StringBuilder();
            sb.AppendLine("# VMS.VisionSetup 툴 파라미터 패널 캡처 인덱스");
            sb.AppendLine();
            sb.AppendLine($"출력 폴더: {outputDir}");
            sb.AppendLine();
            sb.AppendLine("| Tool | ToolType | File |");
            sb.AppendLine("|------|----------|------|");
            foreach (var line in manifest) sb.AppendLine(line);
            File.WriteAllText(Path.Combine(outputDir, "INDEX.md"), sb.ToString(), Encoding.UTF8);
        }

        /// <summary>ToolSettingsControl 내부의 설정 콘텐츠 StackPanel(ScrollViewer 의 Content)을 찾는다.</summary>
        private static StackPanel? FindSettingsStackPanel(DependencyObject host)
        {
            int n = VisualTreeHelper.GetChildrenCount(host);
            for (int i = 0; i < n; i++)
            {
                var child = VisualTreeHelper.GetChild(host, i);
                if (child is ScrollViewer sv && sv.Content is StackPanel sp)
                    return sp;
                var found = FindSettingsStackPanel(child);
                if (found != null) return found;
            }
            return null;
        }

        /// <summary>요소를 무한 높이로 강제 측정해 스크롤에 가려지는 부분까지 full 로 렌더(패딩 포함).</summary>
        private static bool RenderFullElement(FrameworkElement element, Brush backdrop, string path)
        {
            try
            {
                double w = element.ActualWidth > 1 ? element.ActualWidth : 300;
                element.Measure(new Size(w, double.PositiveInfinity));
                double h = element.DesiredSize.Height;
                if (h < 1) h = element.ActualHeight;
                element.Arrange(new Rect(new Point(0, 0), new Size(w, h)));
                element.UpdateLayout();

                const double pad = 12;
                double tw = w + 2 * pad, th = h + 2 * pad;
                var dv = new DrawingVisual();
                using (var ctx = dv.RenderOpen())
                {
                    ctx.DrawRectangle(backdrop, null, new Rect(0, 0, tw, th));
                    var vb = new VisualBrush(element) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top };
                    ctx.DrawRectangle(vb, null, new Rect(pad, pad, w, h));
                }
                var rtb = new RenderTargetBitmap(
                    (int)(tw * Scale), (int)(th * Scale), 96 * Scale, 96 * Scale, PixelFormats.Pbgra32);
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

        /// <summary>
        /// 다이얼로그 창들을 각각 전체(잘림 없이) 캡처한다. 화면 밖으로 Show 하여 레이아웃을
        /// 확정한 뒤 창 루트를 통째로 렌더하고 닫는다. (수동 영역 캡처가 다이얼로그보다 작게
        /// 잡혀 우/하단이 잘리던 문제를, 창 공칭 크기 전체 렌더로 해결.)
        /// </summary>
        public static async Task RunWindowsFullAsync(
            System.Collections.Generic.IReadOnlyList<(string name, Window win, bool fitScroll)> windows,
            string outputDir, Window owner)
        {
            Directory.CreateDirectory(outputDir);
            foreach (var (name, win, fitScroll) in windows)
            {
                try
                {
                    win.Owner = owner;
                    win.ShowInTaskbar = false;
                    win.WindowStartupLocation = WindowStartupLocation.Manual;
                    win.Left = -32000; win.Top = -32000;   // 사용자 화면에 깜빡이지 않게 오프스크린

                    double w = double.IsNaN(win.Width) || win.Width < 1 ? 1280 : win.Width;
                    double h = double.IsNaN(win.Height) || win.Height < 1 ? 800 : win.Height;
                    bool standard = win.WindowStyle != WindowStyle.None;
                    if (standard) win.SizeToContent = SizeToContent.Height;
                    win.Show();

                    await LayoutWindow(win, standard, w, h);

                    // fitScroll: chromeless 창에서 메인 콘텐츠 ScrollViewer 가 넘치면 창을 키워 전체 담기.
                    if (fitScroll && !standard)
                    {
                        var sv = FindContentScrollViewer(win);
                        if (sv?.Content is FrameworkElement sc)
                        {
                            double cw = sc.ActualWidth > 1 ? sc.ActualWidth : w;
                            sc.Measure(new Size(cw, double.PositiveInfinity));
                            double extra = sc.DesiredSize.Height - sv.ActualHeight;
                            if (extra > 2) { h += extra + 10; await LayoutWindow(win, false, w, h); }
                        }
                    }

                    var root = win.Content as FrameworkElement ?? (FrameworkElement)win;
                    double rw = standard ? Math.Max(root.ActualWidth, w) : (root.ActualWidth > 1 ? root.ActualWidth : w);
                    double rh = standard ? Math.Max(root.ActualHeight, h) : (root.ActualHeight > 1 ? root.ActualHeight : h);
                    RenderRootToFile(win, root, rw, rh, Path.Combine(outputDir, name + ".png"));
                }
                catch (System.Exception ex)
                {
                    File.AppendAllText(Path.Combine(outputDir, "_dialog.log"), name + ": " + ex + "\n");
                }
                finally { try { win.Close(); } catch { } }
            }
        }

        private static async Task LayoutWindow(Window win, bool standard, double w, double h)
        {
            for (int i = 0; i < 3; i++)
            {
                if (standard)
                {
                    win.Measure(new Size(w, double.PositiveInfinity));
                    win.Arrange(new Rect(new Point(0, 0), new Size(w, win.DesiredSize.Height)));
                }
                else
                {
                    win.Measure(new Size(w, h));
                    win.Arrange(new Rect(new Point(0, 0), new Size(w, h)));
                }
                win.UpdateLayout();
                await win.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);
            }
        }

        private static ScrollViewer? FindContentScrollViewer(DependencyObject root)
        {
            ScrollViewer? best = null;
            double bestH = 0;
            void Walk(DependencyObject n)
            {
                int c = VisualTreeHelper.GetChildrenCount(n);
                for (int i = 0; i < c; i++)
                {
                    var ch = VisualTreeHelper.GetChild(n, i);
                    if (ch is ScrollViewer sv && sv.Content is Panel p && p.ActualHeight > bestH)
                    {
                        bestH = p.ActualHeight; best = sv;
                    }
                    Walk(ch);
                }
            }
            Walk(root);
            return best;
        }

        private static void RenderRootToFile(Window win, FrameworkElement root, double rw, double rh, string path)
        {
            Brush backdrop = win.Background ?? new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x2E));
            var bounds = new Rect(new Point(0, 0), new Size(rw, rh));
            var dv = new DrawingVisual();
            using (var ctx = dv.RenderOpen())
            {
                ctx.DrawRectangle(backdrop, null, bounds);
                ctx.DrawRectangle(new VisualBrush(root)
                { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top },
                    null, bounds);
            }
            var rtb = new RenderTargetBitmap(
                (int)(rw * Scale), (int)(rh * Scale), 96 * Scale, 96 * Scale, PixelFormats.Pbgra32);
            rtb.Render(dv);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(rtb));
            using var fs = File.Create(path);
            enc.Save(fs);
        }

        private static async Task LayoutPass(Window window, Size size)
        {
            for (int pass = 0; pass < 2; pass++)
            {
                window.Measure(size);
                window.Arrange(new Rect(new Point(0, 0), size));
                window.UpdateLayout();
                await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);
            }
        }

        /// <summary>
        /// 접힌 Expander 와 TreeViewItem(Tool Palette 카테고리)을 모두 펼친다.
        /// 하나라도 새로 펼쳤으면 true. (Tool Palette 는 TreeView 라 카테고리=TreeViewItem)
        /// </summary>
        private static bool ExpandAllExpanders(DependencyObject root)
        {
            bool changed = false;
            foreach (var ex in FindAll<Expander>(root))
            {
                if (!ex.IsExpanded) { ex.IsExpanded = true; changed = true; }
            }
            foreach (var ti in FindAll<TreeViewItem>(root))
            {
                // 자식이 있는(=펼칠 수 있는) 카테고리만. 잎 노드(툴)는 무시.
                if (!ti.IsExpanded && ti.Items.Count > 0) { ti.IsExpanded = true; changed = true; }
            }
            return changed;
        }

        /// <summary>비주얼 트리에서 타입 T 인스턴스를 모두 수집(비활성/Collapsed 포함 — 펼치기 대상이므로).</summary>
        private static List<T> FindAll<T>(DependencyObject root) where T : DependencyObject
        {
            var result = new List<T>();
            int n = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < n; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is T t) result.Add(t);
                result.AddRange(FindAll<T>(child));
            }
            return result;
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
