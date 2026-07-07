#if DEBUG
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace VMS.Capture
{
    /// <summary>
    /// DEBUG 전용 — VMS 클라이언트/관리자 다이얼로그를 잘림 없이 전체 캡처하는 매뉴얼/문서용 유틸.
    ///
    /// 수동 영역 캡처가 다이얼로그보다 작게 잡혀 우/하단이 잘리던 매뉴얼 스샷 문제를, 창을 코드로
    /// 화면 밖에서 Show 하여 레이아웃을 확정한 뒤 루트를 통째로 RenderTargetBitmap 으로 렌더해 해결.
    /// VMS.VisionSetup 의 동명 유틸과 동일 원리(다이얼로그 부분).
    ///
    /// 전체 #if DEBUG 가드 → Release(배포) 빌드에서는 컴파일되지 않으며, App 이 "--capture-dialogs"
    /// 인자로 실행될 때만 호출된다. → 일반 실행/배포 동작에 일절 영향 없음.
    /// </summary>
    internal static class ControlCapturer
    {
        private const double Scale = 2.0;   // 2x (192 DPI)

        /// <summary>
        /// 다이얼로그 창들을 각각 전체(잘림 없이) 캡처한다. 화면 밖으로 Show 하여 레이아웃을 확정한
        /// 뒤 창 루트를 통째로 렌더(Left/Top 정렬)하고 닫는다.
        /// </summary>
        public static async Task RunWindowsFullAsync(
            IReadOnlyList<(string name, Window win, bool fitScroll)> windows, string outputDir, Window owner)
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

                    // 공칭 크기 — SizeToContent 로 win.Height 가 바뀌기 전에 먼저 읽는다.
                    double w = double.IsNaN(win.Width) || win.Width < 1 ? 1280 : win.Width;
                    double h = double.IsNaN(win.Height) || win.Height < 1 ? 800 : win.Height;

                    // 표준 Window(WindowStyle != None): OS 제목표시줄이 비클라이언트라 내부 ScrollViewer 가
                    // 공칭 높이에서 잘린다. → SizeToContent.Height 로 콘텐츠에 맞춰 키운다.
                    bool standard = win.WindowStyle != WindowStyle.None;
                    if (standard) win.SizeToContent = SizeToContent.Height;
                    win.Show();

                    await LayoutWindow(win, standard, w, h);

                    // Web 비동기 조회(WorkOrders / SyncParameters 등)가 렌더 전에 채워지도록 settle 대기.
                    // 로컬 서비스만 쓰는 창은 이미 채워져 있어 무해(대기만 추가).
                    await Task.Delay(900);
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
                    // 표준 창: 콘텐츠가 공칭보다 작으면 공칭 프레임(여백 포함)으로, 크면 콘텐츠 전체로.
                    double rw = standard ? System.Math.Max(root.ActualWidth, w) : (root.ActualWidth > 1 ? root.ActualWidth : w);
                    double rh = standard ? System.Math.Max(root.ActualHeight, h) : (root.ActualHeight > 1 ? root.ActualHeight : h);
                    RenderRootToFile(win, root, rw, rh, Path.Combine(outputDir, name + ".png"));
                }
                catch (Exception ex)
                {
                    File.AppendAllText(Path.Combine(outputDir, "_dialog.log"), name + ": " + ex + "\n");
                }
                finally { try { win.Close(); } catch { } }
            }
        }

        /// <summary>이미 보여진 윈도우(MainWindow 등)를 현재 상태 그대로 전체 렌더 (Show/Close 안 함).</summary>
        public static async Task CaptureLiveWindowAsync(Window win, string outputDir, string name)
        {
            Directory.CreateDirectory(outputDir);
            try
            {
                win.UpdateLayout();
                await win.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);
                var root = win.Content as FrameworkElement ?? (FrameworkElement)win;
                double rw = root.ActualWidth > 1 ? root.ActualWidth : (double.IsNaN(win.Width) ? 1920 : win.Width);
                double rh = root.ActualHeight > 1 ? root.ActualHeight : (double.IsNaN(win.Height) ? 1080 : win.Height);
                RenderRootToFile(win, root, rw, rh, Path.Combine(outputDir, name + ".png"));
            }
            catch (Exception ex)
            {
                File.AppendAllText(Path.Combine(outputDir, "_dialog.log"), name + ": " + ex + "\n");
            }
        }

        /// <summary>
        /// 메인 윈도우의 헤더 칩/버튼과 사이드 패널 섹션·컨트롤을 개별 PNG 로 캡처한다 (--capture-controls).
        /// AppSetup/VisionSetup 의 동명 유틸과 동일 원리(비주얼 트리 순회 + VisualBrush 렌더).
        /// 장면(Scene)별로 ViewModel 상태를 바꿔 조건부 UI(로그인 후 칩/Role 뱃지/Admin 섹션)를 노출하며,
        /// 칩·섹션 카드 같은 합성 단위는 명시 타깃으로 컨테이너째 캡처한다.
        /// 문서용 상태 주입은 DEBUG 캡처 전용이라 partial 훅(레시피 자동 로드 등) 부작용을 피해
        /// backing field + OnPropertyChanged 리플렉션으로 반영한다.
        /// </summary>
        public static async Task RunControlsAsync(
            Window window, ViewModels.MainViewModel vm, Interfaces.IUserService userService, string outputDir)
        {
            Directory.CreateDirectory(outputDir);
            string logp = Path.Combine(outputDir, "_controls.log");
            var seen = new HashSet<DependencyObject>();
            var manifest = new List<string>();
            int total = 0;

            Brush backdrop = window.Background ?? new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x2E));
            if (backdrop.CanFreeze) backdrop.Freeze();

            foreach (var (tag, apply, targets) in BuildMainScenes(window, vm, userService))
            {
                try { apply(); }
                catch (Exception ex) { File.AppendAllText(logp, tag + " apply: " + ex + "\n"); }

                for (int i = 0; i < 3; i++)
                {
                    window.UpdateLayout();
                    await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);
                }
                await Task.Delay(150);

                foreach (var (name, find) in targets)
                {
                    try
                    {
                        var fe = find();
                        if (fe == null || fe.ActualWidth < 1 || fe.ActualHeight < 1)
                        { manifest.Add($"| {tag} | Composite | — | {name} | (not found) |"); continue; }
                        string file = $"{tag}_00_{name}.png";
                        if (TryRenderElement(fe, Path.Combine(outputDir, file), backdrop))
                        { total++; manifest.Add($"| {tag} | Composite | {fe.GetType().Name} | {name} | {file} |"); }
                    }
                    catch (Exception ex) { File.AppendAllText(logp, tag + "/" + name + ": " + ex + "\n"); }
                }

                total += CaptureVisibleControls(window, tag, outputDir, seen, manifest, backdrop);

                // 사이드 패널 펼침 장면은 풀 윈도우도 저장 — 매뉴얼 05_vms_sidepanel 갱신용.
                if (tag == "S3_sidepanel")
                    await CaptureLiveWindowAsync(window, outputDir, "S3_full_sidepanel");
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("# VMS 메인 화면 컨트롤 캡처 인덱스");
            sb.AppendLine();
            sb.AppendLine($"총 {total} 개 / 출력 폴더: {outputDir}");
            sb.AppendLine();
            sb.AppendLine("| Scene | Kind | Type | Label | File |");
            sb.AppendLine("|-------|------|------|-------|------|");
            foreach (var line in manifest) sb.AppendLine(line);
            File.WriteAllText(Path.Combine(outputDir, "INDEX.md"), sb.ToString(), System.Text.Encoding.UTF8);
        }

        /// <summary>장면 목록 — 로그인 전 헤더 / 로그인 후(WO·레시피 칩, Role 뱃지, Admin 섹션) / 사이드 패널 펼침.</summary>
        private static IEnumerable<(string tag, Action apply, (string name, Func<FrameworkElement?> find)[] targets)>
            BuildMainScenes(Window window, ViewModels.MainViewModel vm, Interfaces.IUserService userService)
        {
            FrameworkElement? OperatorChip() =>
                FindAncestor<System.Windows.Controls.Border>(FindByContentText<Button>(window, "Login...")
                    ?? FindByContentText<Button>(window, "Logout"));

            yield return ("S1_header", () =>
            {
                // Ctx 칩에 문서용 대표 값 — public 프로퍼티(부작용: ParameterSyncService 필드 설정뿐).
                vm.WorkOrderIdText = "7144";
                vm.LotIdText = "12";
                vm.SerialNumberText = "SN-2026-0001";
            }, new (string, Func<FrameworkElement?>)[]
            {
                ("hdr_operator_chip", OperatorChip),
                ("hdr_workorders_btn", () => FindByAutomationName<Button>(window, "Work Orders")),
                ("hdr_ctx_chip", () => FindByToolTipPrefix<System.Windows.Controls.Border>(window, "검사 결과 업로드")),
                ("hdr_autorun_btn", () => FindByToolTipPrefix<Button>(window, "검사 시작")),
                ("hdr_panel_toggle", () => window.FindName("PanelToggle") as FrameworkElement),
            });

            yield return ("S2_loggedin", () =>
            {
                // VMS 시스템 사용자(Admin) — Image Saving(Admin 전용) 섹션 노출용.
                SetPropertyViaReflection(userService, "CurrentUser", new Models.User
                { Username = "admin", DisplayName = "Administrator", Grade = Models.UserGrade.Admin, LastLoginAt = DateTime.Now });
                InvokePrivate(vm, "UpdateUserDisplay");

                // Web 작업자 로그인 상태 — Role 뱃지(Supervisor) + External Tools 섹션 노출.
                vm.CurrentOperatorName = "민병준";
                vm.CurrentOperatorEmployeeNumber = "1003";
                vm.CurrentOperatorRole = VMS.Core.Models.ParameterSync.OperatorRoles.Supervisor;
                vm.IsOperatorLoggedIn = true;

                // WO/Recipe 칩 — partial 훅(레시피 자동 로드 등)을 타지 않게 backing field 로 주입.
                SetFieldViaReflection(vm, "_selectedWorkOrder", new VMS.Core.Models.ParameterSync.WorkOrderDto
                {
                    OrderNo = "WO-20260521-001", ProductName = "A001", RecipeName = "A1",
                    PlannedQuantity = 500, ProducedQuantity = 320, PassQuantity = 312, NgQuantity = 8,
                    Status = "InProgress"
                });
                RaisePropertyChanged(vm, nameof(ViewModels.MainViewModel.SelectedWorkOrder));
                RaisePropertyChanged(vm, "SelectedWorkOrderText");
                RaisePropertyChanged(vm, "SelectedWorkOrderProgressPercent");
                RaisePropertyChanged(vm, "HasSelectedWorkOrderProgress");

                SetFieldViaReflection(vm, "_currentRecipe", new Models.Recipe { Name = "A1", Version = "1.0.0" });
                RaisePropertyChanged(vm, "CurrentRecipe");
                vm.CurrentRecipeName = "A1";
            }, new (string, Func<FrameworkElement?>)[]
            {
                ("hdr_operator_chip_in", OperatorChip),
                ("hdr_wo_chip", () => FindByToolTipPrefix<System.Windows.Controls.Border>(window, "현재 선택된 작업지시")),
                ("hdr_recipe_chip", () => FindByToolTipPrefix<System.Windows.Controls.Border>(window, "현재 로드된 레시피")),
            });

            // 섹션 헤더에 ⓘ 도움말 Run 이 붙음(예: "Camera Control ⓘ"). Run 인라인으로 구성한
            // TextBlock 은 Text 프로퍼티가 비어 있어(Inlines 에만 존재) Inlines 연결 문자열로 찾는다.
            // prefix 매칭은 헤더 칩("Recipe:") 오매칭을 부르므로 "헤더" / "헤더 ⓘ" 정확일치만 허용.
            FrameworkElement? SectionCard(string header) =>
                FindAncestor<System.Windows.Controls.Border>(FindSectionHeader(window, header));

            yield return ("S3_sidepanel", () =>
            {
                // 애니메이션(토글 스토리보드) 대신 폭을 직접 지정 — 헤드리스에서도 확정 레이아웃.
                if (window.FindName("SidePanel") is System.Windows.Controls.Border p) p.Width = 340;
            }, new (string, Func<FrameworkElement?>)[]
            {
                ("sec_camera_control", () => SectionCard("Camera Control")),
                ("sec_recipe", () => SectionCard("Recipe")),
                ("sec_external_tools", () => SectionCard("External Tools")),
                ("sec_updates", () => SectionCard("Updates")),
                ("sec_web_parameters", () => SectionCard("Web Parameters")),
                ("sec_image_saving", () => SectionCard("Image Saving")),
                ("sec_recent", () => SectionCard("Recent Inspections")),
                ("sec_statistics", () => SectionCard("Statistics")),
            });
        }

        private static int CaptureVisibleControls(
            Window window, string tag, string outputDir,
            HashSet<DependencyObject> seen, List<string> manifest, Brush backdrop)
        {
            int count = 0, seq = 0;
            foreach (var element in EnumerateVisibleTree(window))
            {
                if (element is not FrameworkElement fe) continue;
                if (fe.ActualWidth < 1 || fe.ActualHeight < 1) continue;

                string? type = fe switch
                {
                    System.Windows.Controls.Primitives.ToggleButton and not CheckBox and not RadioButton => "ToggleButton",
                    RadioButton => "RadioButton",
                    CheckBox => "CheckBox",
                    Button => "Button",
                    ComboBox => "ComboBox",
                    ListBox => "ListBox",
                    TextBox => "TextBox",
                    _ => null
                };
                if (type == null) continue;
                if (!seen.Add(fe)) continue;

                seq++;
                string label = GetControlLabel(fe);
                string file = $"{tag}_{seq:D2}_{type}_{SanitizeName(label)}.png";
                if (TryRenderElement(fe, Path.Combine(outputDir, file), backdrop))
                {
                    count++;
                    manifest.Add($"| {tag} | Control | {type} | {label.Replace("|", "\\|").Replace("\n", " ").Trim()} | {file} |");
                }
            }
            return count;
        }

        private static string GetControlLabel(FrameworkElement fe)
        {
            string auto = System.Windows.Automation.AutomationProperties.GetName(fe);
            if (!string.IsNullOrWhiteSpace(auto)) return auto;
            switch (fe)
            {
                case ContentControl cc when cc.Content is string s && !string.IsNullOrWhiteSpace(s): return s;
                case ContentControl cc when cc.Content is TextBlock tb: return tb.Text;
                case TextBox t: return !string.IsNullOrEmpty(t.Name) ? t.Name : (!string.IsNullOrEmpty(t.Text) ? t.Text : "TextBox");
                case ComboBox cb: return cb.SelectedItem?.ToString() ?? (!string.IsNullOrEmpty(cb.Name) ? cb.Name : "ComboBox");
                default: return string.IsNullOrEmpty(fe.Name) ? fe.GetType().Name : fe.Name;
            }
        }

        private static IEnumerable<DependencyObject> EnumerateVisibleTree(DependencyObject root)
        {
            int n = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < n; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is UIElement ui && ui.Visibility != Visibility.Visible) continue;
                yield return child;
                foreach (var d in EnumerateVisibleTree(child)) yield return d;
            }
        }

        private static T? FindByAutomationName<T>(DependencyObject root, string name) where T : FrameworkElement
        {
            foreach (var d in EnumerateVisibleTree(root))
                if (d is T fe && System.Windows.Automation.AutomationProperties.GetName(fe) == name) return fe;
            return null;
        }

        private static T? FindByToolTipPrefix<T>(DependencyObject root, string prefix) where T : FrameworkElement
        {
            foreach (var d in EnumerateVisibleTree(root))
                if (d is T fe && fe.ToolTip is string tip && tip.StartsWith(prefix, StringComparison.Ordinal)) return fe;
            return null;
        }

        private static T? FindByContentText<T>(DependencyObject root, string text) where T : ContentControl
        {
            foreach (var d in EnumerateVisibleTree(root))
                if (d is T cc && cc.Content is string s && s == text) return cc;
            return null;
        }

        private static TextBlock? FindByExactText(DependencyObject root, string text)
        {
            foreach (var d in EnumerateVisibleTree(root))
                if (d is TextBlock tb && tb.Text == text) return tb;
            return null;
        }

        private static string InlineText(TextBlock tb)
        {
            if (!string.IsNullOrEmpty(tb.Text)) return tb.Text;
            var sb = new System.Text.StringBuilder();
            foreach (var inline in tb.Inlines)
                if (inline is System.Windows.Documents.Run r) sb.Append(r.Text);
            return sb.ToString();
        }

        private static TextBlock? FindSectionHeader(DependencyObject root, string header)
        {
            string withIcon = header + " ⓘ";
            foreach (var d in EnumerateVisibleTree(root))
                if (d is TextBlock tb)
                {
                    string t = InlineText(tb);
                    if (t == header || t == withIcon) return tb;
                }
            return null;
        }

        private static T? FindAncestor<T>(DependencyObject? node) where T : DependencyObject
        {
            while (node != null)
            {
                node = VisualTreeHelper.GetParent(node);
                if (node is T hit) return hit;
            }
            return null;
        }

        private static bool TryRenderElement(FrameworkElement fe, string path, Brush backdrop)
        {
            try
            {
                var bounds = new Rect(new Point(0, 0), new Size(fe.ActualWidth, fe.ActualHeight));
                var dv = new DrawingVisual();
                using (var ctx = dv.RenderOpen())
                {
                    ctx.DrawRectangle(backdrop, null, bounds);
                    ctx.DrawRectangle(new VisualBrush(fe) { Stretch = Stretch.None }, null, bounds);
                }
                var rtb = new RenderTargetBitmap(
                    (int)(bounds.Width * Scale), (int)(bounds.Height * Scale), 96 * Scale, 96 * Scale, PixelFormats.Pbgra32);
                rtb.Render(dv);
                var enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(rtb));
                using var fs = File.Create(path);
                enc.Save(fs);
                return true;
            }
            catch { return false; }
        }

        private static string SanitizeName(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "unnamed";
            var sb = new System.Text.StringBuilder();
            foreach (char c in s.Trim())
            {
                if (char.IsLetterOrDigit(c) || c == '-' || c == '_') sb.Append(c);
                else if (char.IsWhiteSpace(c)) sb.Append('_');
            }
            string r = sb.ToString();
            if (r.Length > 40) r = r.Substring(0, 40);
            return r.Length == 0 ? "unnamed" : r;
        }

        private static void SetPropertyViaReflection(object target, string prop, object? value) =>
            target.GetType().GetProperty(prop)!.SetValue(target, value);

        private static void SetFieldViaReflection(object target, string field, object? value)
        {
            for (var t = target.GetType(); t != null; t = t.BaseType)
            {
                var f = t.GetField(field, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (f != null) { f.SetValue(target, value); return; }
            }
            throw new MissingFieldException(target.GetType().Name, field);
        }

        private static void InvokePrivate(object target, string method)
        {
            for (var t = target.GetType(); t != null; t = t.BaseType)
            {
                var m = t.GetMethod(method, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance,
                    null, Type.EmptyTypes, null);
                if (m != null) { m.Invoke(target, null); return; }
            }
            throw new MissingMethodException(target.GetType().Name, method);
        }

        private static void RaisePropertyChanged(object vm, string prop)
        {
            for (var t = vm.GetType(); t != null; t = t.BaseType)
            {
                var m = t.GetMethod("OnPropertyChanged",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance,
                    null, new[] { typeof(string) }, null);
                if (m != null) { m.Invoke(vm, new object[] { prop }); return; }
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

        /// <summary>Content 가 Panel 인 ScrollViewer 중 콘텐츠가 가장 큰 것(=설정 패널 스크롤). 데이터그리드 제외.</summary>
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
    }
}
#endif
