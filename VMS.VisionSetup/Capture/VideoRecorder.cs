#if DEBUG
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ShapePath = System.Windows.Shapes.Path;
using Ellipse = System.Windows.Shapes.Ellipse;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Messaging;
using VMS.VisionSetup.Models;
using VMS.VisionSetup.ViewModels;

namespace VMS.VisionSetup.Capture
{
    /// <summary>
    /// 사용 안내 동영상 녹화 (DEBUG 전용, "--record-video [폴더]").
    ///
    /// <para>창을 화면 밖에 띄우고(입력 장치·화면 캡처를 쓰지 않는다) 시나리오 JSON 의 동작을 VM 수준으로 실행하면서,
    /// 메인 창 루트와 그 위에 열린 창(레시피 관리자 · 메시지 창 등)을 RenderTargetBitmap 으로 합성해 JPEG 프레임과 시각(timeline.json)을 남긴다.
    /// 커서 · 클릭 물결 · 강조 상자 · 툴팁 · 드롭다운 메뉴는 프레임 위에 직접 그린다 — 실제 마우스는 움직이지 않는다.</para>
    ///
    /// 환경 변수: VMS_RECORD_SCENARIO = 시나리오 JSON, VMS_RECORD_DURATIONS = 구간별 내레이션 길이(초) JSON.
    /// 출력: &lt;폴더&gt;/frames/f000000.jpg …, &lt;폴더&gt;/timeline.json {frames:[{name,t}], marks:[{id,t}]}, _record.log
    /// </summary>
    public sealed class VideoRecorder
    {
        private const int W = 1920, H = 1080;
        private const double Gap = 0.7;
        private const double OffLeft = -6000;

        private readonly Window _win;
        private readonly MainViewModel _vm;
        private readonly string _out;
        private readonly Dictionary<string, double> _durs;
        private readonly List<(string Name, double T)> _frames = new();
        private readonly List<(string Id, double T)> _marks = new();
        private readonly ConcurrentBag<Task> _encodes = new();
        private readonly StreamWriter _log;
        private DispatcherTimer? _timer;
        private int _frameNo;
        private bool _busy;
        private string? _segId;
        private double _segT0;

        // 덧그림 상태
        private Point _cur = new(1100, 620);
        private double _clickT = -10;
        private Point _clickP;
        private List<Rect> _hl = new();
        private double _hlT0;
        private string? _ghost;               // 끌기 중 커서 옆 도구 이름
        private Point? _lineFrom;            // 연결 중 임시 선 시작점
        private Brush _lineBrush = Brushes.DeepSkyBlue;
        private string? _tipText;
        private Rect _tipAnchor;
        // 드롭다운 · 우클릭 메뉴 — 화면 밖 창의 팝업은 그려지지 않으므로 항목을 직접 그린다
        private MenuView? _menu;
        private int _menuHover = -1;
        // 파일 열기 · 폴더 · 저장 대화상자 대신 쓸 경로 (OS 대화상자는 화면 밖 녹화에 담기지 않는다)
        private RecordingDialogService? _dlg;
        // 도움말 팝업 등 Popup 내용 — 열지 않고(실제 화면에 뜨므로) 내용만 배치해 그린다
        private FrameworkElement? _popupEl;
        private Rect _popupRect;

        private static readonly Brush Accent = Freeze(new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x1A)));

        private VideoRecorder(Window win, MainViewModel vm, string outDir, Dictionary<string, double> durs)
        {
            _win = win; _vm = vm; _out = outDir; _durs = durs;
            Directory.CreateDirectory(System.IO.Path.Combine(outDir, "frames"));
            foreach (var f in Directory.GetFiles(System.IO.Path.Combine(outDir, "frames"))) File.Delete(f);
            _log = new StreamWriter(System.IO.Path.Combine(outDir, "_record.log"), false, new UTF8Encoding(false)) { AutoFlush = true };
        }

        /// <summary>Show 전에 호출 — 1920x1080, 화면 밖, 활성화·작업 표시줄 없음.</summary>
        public static void PrepareWindow(Window w)
        {
            w.WindowStartupLocation = WindowStartupLocation.Manual;
            w.WindowState = WindowState.Normal;
            w.Width = W; w.Height = H;
            w.Left = OffLeft; w.Top = 0;
            w.ShowActivated = false;
            w.ShowInTaskbar = false;
            w.Topmost = false;
        }

        public static async Task RunAsync(Window win, string outDir)
        {
            if (win.DataContext is not MainViewModel vm) return;
            var durs = new Dictionary<string, double>();
            var dp = Environment.GetEnvironmentVariable("VMS_RECORD_DURATIONS");
            if (!string.IsNullOrEmpty(dp) && File.Exists(dp))
                foreach (var p in JsonDocument.Parse(File.ReadAllText(dp)).RootElement.EnumerateObject())
                    durs[p.Name] = p.Value.GetDouble();
            var rec = new VideoRecorder(win, vm, outDir, durs);
            // 새 창이 실제 화면에 보이기 전에 메인 창(화면 밖) 위로 옮긴다 — 창의 첫 레이아웃 시점
            EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.SizeChangedEvent,
                new SizeChangedEventHandler((s, _) => { if (s is Window w && !ReferenceEquals(w, win)) rec.PlaceOver(w, "early"); }));
            try { await rec.RunScenarioAsync(); }
            catch (Exception ex) { rec.Log("FATAL " + ex); }
            finally { await rec.FinishAsync(); }
        }

        // ───────────────────────── 프레임 ─────────────────────────

        private static double Now() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
        private static T Freeze<T>(T f) where T : Freezable { f.Freeze(); return f; }
        private FrameworkElement Root => (FrameworkElement)_win.Content;

        /// <summary>메인 창 위에 떠 있는 다른 창 (열린 순서 = 아래부터).</summary>
        private List<Window> OverWindows() =>
            Application.Current.Windows.OfType<Window>()
                .Where(w => !ReferenceEquals(w, _win) && w.IsVisible && w.Content is FrameworkElement fe && fe.ActualWidth > 0)
                .ToList();

        /// <summary>창 콘텐츠 왼쪽 위의 메인 창 기준 좌표 (메인은 0,0).</summary>
        private Point OffsetOf(Window w)
        {
            if (ReferenceEquals(w, _win)) return new Point(0, 0);
            try { return Root.PointFromScreen(((Visual)w.Content).PointToScreen(new Point(0, 0))); }
            catch { return new Point(w.Left - _win.Left, w.Top - _win.Top); }
        }

        /// <summary>새로 뜬 창이 실제 화면 안에 나타났으면(CenterScreen 등) 즉시 메인 창 가운데로 옮긴다.</summary>
        private void KeepOffScreen()
        {
            foreach (var w in Application.Current.Windows.OfType<Window>())
                if (!ReferenceEquals(w, _win) && w.IsVisible) PlaceOver(w, "frame");
        }

        private void PlaceOver(Window w, string when)
        {
            if (!(w.Left > OffLeft / 2) && !double.IsNaN(w.Left)) return;
            double ww = w.ActualWidth > 0 ? w.ActualWidth : (double.IsNaN(w.Width) ? 600 : w.Width);
            double wh = w.ActualHeight > 0 ? w.ActualHeight : (double.IsNaN(w.Height) ? 400 : w.Height);
            Log($"  창 위치 보정({when}): {w.GetType().Name} ({w.Left:F0},{w.Top:F0} {ww:F0}x{wh:F0} {w.WindowStartupLocation})");
            w.WindowStartupLocation = WindowStartupLocation.Manual;
            w.ShowActivated = false;
            w.Left = _win.Left + (W - ww) / 2;
            w.Top = _win.Top + Math.Max(0, (H - wh) / 2);
        }

        private void StartFrames()
        {
            _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(50) };
            _timer.Tick += (_, _) => CaptureFrame();
            _timer.Start();
        }

        private void CaptureFrame()
        {
            if (_busy) return;
            _busy = true;
            try
            {
                KeepOffScreen();
                double t = Now();
                var rtb = new RenderTargetBitmap(W, H, 96, 96, PixelFormats.Pbgra32);
                var bg = new DrawingVisual();
                using (var dc = bg.RenderOpen())
                    dc.DrawRectangle(_win.Background ?? Brushes.Black, null, new Rect(0, 0, W, H));
                rtb.Render(bg);
                rtb.Render(Root);
                foreach (var w in OverWindows())
                {
                    // 창 클라이언트 영역 = 창 템플릿 루트(여백 없음)
                    var content = VisualTreeHelper.GetChildrenCount(w) > 0 && VisualTreeHelper.GetChild(w, 0) is FrameworkElement tr
                        ? tr : (FrameworkElement)w.Content;
                    Point o;
                    try { o = Root.PointFromScreen(content.PointToScreen(new Point(0, 0))); }
                    catch { o = OffsetOf(w); }
                    var r = new Rect(o.X, o.Y, content.ActualWidth, content.ActualHeight);
                    var dv = new DrawingVisual();
                    using (var dc = dv.RenderOpen())
                    {
                        // OS 제목 표시줄이 있는 창은 비클라이언트 영역이 안 담기므로 Windows 밝은 테마 기본 모양으로 그린다
                        bool chrome = w.WindowStyle != WindowStyle.None;
                        var full = chrome ? new Rect(r.X, r.Y - 31, r.Width, r.Height + 31) : r;
                        for (int i = 1; i <= (w.AllowsTransparency ? 0 : 4); i++)
                        {
                            var sr = full; sr.Inflate(i * 3, i * 3); sr.Offset(0, i * 2);
                            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb((byte)(34 - i * 6), 0, 0, 0)), null, sr, 6 + i * 3, 6 + i * 3);
                        }
                        if (chrome)
                        {
                            var bar = new Rect(r.X, r.Y - 31, r.Width, 31);
                            dc.DrawRectangle(Brushes.White, null, bar);
                            var tt = new FormattedText(w.Title ?? "", CultureInfo.GetCultureInfo("ko-KR"), FlowDirection.LeftToRight,
                                new Typeface("Segoe UI"), 12, Brushes.Black, 1.0);
                            dc.DrawText(tt, new Point(bar.X + 12, bar.Y + (31 - tt.Height) / 2));
                            var mdl = new Typeface(new FontFamily("Segoe MDL2 Assets"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
                            string[] caps = w.ResizeMode == ResizeMode.NoResize ? new[] { "" } : new[] { "", "", "" };
                            for (int ci = 0; ci < caps.Length; ci++)
                            {
                                var gt = new FormattedText(caps[ci], CultureInfo.InvariantCulture, FlowDirection.LeftToRight, mdl, 10, Brushes.Black, 1.0);
                                double cx = bar.Right - 46 * (caps.Length - ci) + (46 - gt.Width) / 2;
                                dc.DrawText(gt, new Point(cx, bar.Y + (31 - gt.Height) / 2));
                            }
                        }
                        if (!w.AllowsTransparency) dc.DrawRectangle(w.Background ?? Brushes.Black, null, r);
                        dc.DrawRectangle(new VisualBrush(content)
                        {
                            Stretch = Stretch.Fill, ViewboxUnits = BrushMappingMode.Absolute,
                            Viewbox = new Rect(0, 0, content.ActualWidth, content.ActualHeight),
                        }, null, r);
                    }
                    rtb.Render(dv);
                }
                var ov = new DrawingVisual();
                using (var dc = ov.RenderOpen()) DrawOverlay(dc, t);
                rtb.Render(ov);
                rtb.Freeze();
                string name = $"f{_frameNo++:D6}.jpg";
                _frames.Add((name, t));
                string path = System.IO.Path.Combine(_out, "frames", name);
                _encodes.Add(Task.Run(() =>
                {
                    var enc = new JpegBitmapEncoder { QualityLevel = 92 };
                    enc.Frames.Add(BitmapFrame.Create(rtb));
                    using var fs = File.Create(path);
                    enc.Save(fs);
                }));
            }
            catch (Exception ex) { Log("frame: " + ex.Message); }
            finally { _busy = false; }
        }

        private void DrawOverlay(DrawingContext dc, double t)
        {
            // 강조 상자 — 바깥을 살짝 어둡게, 주황 둥근 테두리
            if (_hl.Count > 0)
            {
                double a = Math.Clamp((t - _hlT0) / 0.35, 0, 1);
                var r = _hl.Aggregate(Rect.Union);
                r.Inflate(8, 8);
                var full = new RectangleGeometry(new Rect(0, 0, W, H));
                var hole = new RectangleGeometry(r, 14, 14);
                var dim = new CombinedGeometry(GeometryCombineMode.Exclude, full, hole);
                dc.DrawGeometry(new SolidColorBrush(Color.FromArgb((byte)(46 * a), 15, 23, 42)), null, dim);
                dc.DrawRoundedRectangle(null, new Pen(new SolidColorBrush(Color.FromArgb((byte)(90 * a), 0xFF, 0x6B, 0x1A)), 12), r, 14, 14);
                dc.DrawRoundedRectangle(null, new Pen(new SolidColorBrush(Color.FromArgb((byte)(255 * a), 0xFF, 0x6B, 0x1A)), 4), r, 14, 14);
            }
            if (_menu != null) DrawMenu(dc);
            if (_popupEl != null)
            {
                var sh = _popupRect; sh.Offset(3, 5);
                dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(90, 0, 0, 0)), null, sh, 6, 6);
                dc.DrawRectangle(new VisualBrush(_popupEl) { Stretch = Stretch.Fill, ViewboxUnits = BrushMappingMode.Absolute,
                    Viewbox = new Rect(0, 0, _popupEl.ActualWidth, _popupEl.ActualHeight) }, null, _popupRect);
            }
            // 연결 중 임시 선
            if (_lineFrom is Point lf)
            {
                var pen = new Pen(_lineBrush, 3) { DashStyle = DashStyles.Dash };
                dc.DrawLine(pen, lf, _cur);
            }
            // 툴팁 — 화면 밖 창의 툴팁 팝업은 그려지지 않으므로 요소의 ToolTip 글자를 직접 그린다
            if (_tipText != null)
            {
                var ft = new FormattedText(_tipText, CultureInfo.GetCultureInfo("ko-KR"), FlowDirection.LeftToRight,
                    new Typeface("Malgun Gothic"), 15, Brushes.White, 1.0) { MaxTextWidth = 460 };
                double bw = ft.Width + 24, bh = ft.Height + 16;
                double x = Math.Clamp(_tipAnchor.X + _tipAnchor.Width / 2 - bw / 2, 8, W - bw - 8);
                double y = _tipAnchor.Bottom + 10;
                if (y + bh > H - 8) y = _tipAnchor.Top - bh - 10;
                var box = new Rect(x, y, bw, bh);
                var sh = box; sh.Offset(0, 3);
                dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(70, 0, 0, 0)), null, sh, 6, 6);
                dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(0x24, 0x27, 0x30)),
                    new Pen(new SolidColorBrush(Color.FromRgb(0x5A, 0x60, 0x6C)), 1), box, 6, 6);
                dc.DrawText(ft, new Point(x + 12, y + 8));
            }
            // 클릭 물결
            double k = (t - _clickT) / 0.55;
            if (k >= 0 && k <= 1)
            {
                double rad = 22 * (1 + 0.6 * k);
                dc.DrawEllipse(new SolidColorBrush(Color.FromArgb((byte)(46 * (1 - k)), 0xFF, 0x6B, 0x1A)),
                    new Pen(new SolidColorBrush(Color.FromArgb((byte)(230 * (1 - k)), 0xFF, 0x6B, 0x1A)), 3), _clickP, rad, rad);
            }
            // 끌기 중인 도구 이름표
            if (_ghost != null)
            {
                var ft = new FormattedText(_ghost, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    new Typeface("Segoe UI Semibold"), 15, Brushes.White, 1.0);
                var box = new Rect(_cur.X + 22, _cur.Y + 18, ft.Width + 20, ft.Height + 10);
                dc.DrawRoundedRectangle(Accent, null, box, 6, 6);
                dc.DrawText(ft, new Point(box.X + 10, box.Y + 5));
            }
            // 커서 (화살표 24 → 30px)
            var g = Geometry.Parse("M4,2.5 L10.6,20.7 L13.2,13.3 L20.5,10.6 Z").Clone();
            g.Transform = new TransformGroup { Children = { new ScaleTransform(1.25, 1.25), new TranslateTransform(_cur.X - 5, _cur.Y - 3.5) } };
            dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(70, 0, 0, 0)), null, Shift(g, 1.5, 2));
            dc.DrawGeometry(new SolidColorBrush(Color.FromRgb(0x11, 0x11, 0x11)), new Pen(Brushes.White, 2) { LineJoin = PenLineJoin.Round }, g);
        }

        private static Geometry Shift(Geometry g, double dx, double dy)
        {
            var c = g.Clone();
            c.Transform = new TransformGroup { Children = { g.Transform, new TranslateTransform(dx, dy) } };
            return c;
        }

        // ───────────────────────── 드롭다운 · 우클릭 메뉴 ─────────────────────────

        /// <summary>그려 보일 메뉴 — 메뉴 바 항목의 하위 메뉴(밝은 기본 모양) 또는 요소의 ContextMenu(그 메뉴의 색).</summary>
        private sealed class MenuView
        {
            public List<object> Items = new();
            public Point TopLeft;
            public bool Light;
            public Rect? HeaderRect;          // 메뉴 바 머리글 (열린 동안 눌린 모양)
            public FrameworkElement? Owner;   // ContextMenu 를 가진 요소
            public ContextMenu? Context;
            public double Width = 240;
        }

        private double MenuItemH => _menu?.Light == true ? 30 : 32;
        private const double MenuSepH = 9;

        private List<(object Item, Rect R)> MenuLayout()
        {
            var list = new List<(object, Rect)>();
            if (_menu == null) return list;
            double x = Math.Clamp(_menu.TopLeft.X, 8, W - _menu.Width - 8), y = _menu.TopLeft.Y + 4;
            foreach (var it in _menu.Items)
            {
                double h = it is Separator ? MenuSepH : MenuItemH;
                list.Add((it, new Rect(x + 4, y, _menu.Width - 8, h)));
                y += h;
            }
            return list;
        }

        private static string HeaderText(MenuItem mi) => (mi.Header?.ToString() ?? "").Replace("_", "");
        private static string ItemText(object it) => it is MenuItem mi ? HeaderText(mi) : it?.ToString() ?? "";

        private bool MenuItemEnabled(MenuItem mi)
        {
            var cmd = ResolveMenuCommand(mi);
            if (cmd != null) return cmd.CanExecute(mi.CommandParameter);
            return mi.IsEnabled;
        }

        private ICommand? ResolveMenuCommand(MenuItem mi)
        {
            if (mi.Command != null) return mi.Command;
            if (BindingOperations.GetBinding(mi, MenuItem.CommandProperty) is { } bd)   // 열린 적 없는 메뉴는 DataContext 가 없을 수 있다
                return _vm.GetType().GetProperty(bd.Path.Path)?.GetValue(_vm) as ICommand;
            return null;
        }

        private void DrawMenu(DrawingContext dc)
        {
            var lay = MenuLayout();
            if (lay.Count == 0 || _menu == null) return;
            bool light = _menu.Light;
            Brush bg = light ? new SolidColorBrush(Color.FromRgb(0xF2, 0xF2, 0xF2)) : (_menu.Context?.Background ?? new SolidColorBrush(Color.FromRgb(0x2D, 0x2D, 0x30)));
            Brush border = light ? new SolidColorBrush(Color.FromRgb(0xA0, 0xA0, 0xA0)) : (_menu.Context?.BorderBrush ?? Brushes.Gray);
            Brush fg = light ? new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E)) : (_menu.Context?.Foreground ?? Brushes.White);
            Brush dis = light ? new SolidColorBrush(Color.FromRgb(0x9A, 0x9A, 0x9A)) : new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x80));
            Brush hover = light ? new SolidColorBrush(Color.FromRgb(0xD4, 0xE6, 0xF7)) : new SolidColorBrush(Color.FromArgb(56, 0xFF, 0xFF, 0xFF));
            if (_menu.HeaderRect is Rect hr)
                dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(60, 0xFF, 0xFF, 0xFF)), null, hr, 3, 3);
            var box = new Rect(lay[0].R.X - 4, lay[0].R.Y - 4, _menu.Width, lay[^1].R.Bottom - lay[0].R.Y + 8);
            var sh = box; sh.Offset(2, 4);
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(80, 0, 0, 0)), null, sh, 4, 4);
            dc.DrawRectangle(bg, new Pen(border, 1), box);
            for (int i = 0; i < lay.Count; i++)
            {
                var (it, r) = lay[i];
                if (it is Separator)
                {
                    dc.DrawRectangle(light ? new SolidColorBrush(Color.FromRgb(0xD0, 0xD0, 0xD0)) : border, null, new Rect(r.X + 30, r.Y + r.Height / 2, r.Width - 36, 1));
                    continue;
                }
                if (it is string str)   // 콤보 펼침 목록 — 글자만
                {
                    if (i == _menuHover)
                        dc.DrawRoundedRectangle(light ? hover : new SolidColorBrush(Color.FromRgb(0x09, 0x47, 0x71)), null, r, 3, 3);
                    var st = new FormattedText(str, CultureInfo.GetCultureInfo("ko-KR"), FlowDirection.LeftToRight, new Typeface("Segoe UI"), 13, fg, 1.0);
                    dc.DrawText(st, new Point(r.X + 10, r.Y + (r.Height - st.Height) / 2));
                    continue;
                }
                if (it is not MenuItem mi) continue;
                bool en = MenuItemEnabled(mi);
                if (i == _menuHover && en)
                    dc.DrawRoundedRectangle(hover, light ? new Pen(new SolidColorBrush(Color.FromRgb(0x9A, 0xC6, 0xEE)), 1) : null, r, 3, 3);
                var itemFg = !en ? dis : (light ? fg : (mi.ReadLocalValue(Control.ForegroundProperty) != DependencyProperty.UnsetValue ? mi.Foreground : fg));
                DrawMenuIcon(dc, mi.Icon, new Rect(r.X + 8, r.Y + (r.Height - 16) / 2, 16, 16), en ? 1.0 : 0.4);
                var t = new FormattedText(HeaderText(mi), CultureInfo.GetCultureInfo("ko-KR"), FlowDirection.LeftToRight,
                    new Typeface("Segoe UI"), 13, itemFg, 1.0);
                dc.DrawText(t, new Point(r.X + 36, r.Y + (r.Height - t.Height) / 2));
                if (!string.IsNullOrEmpty(mi.InputGestureText))
                {
                    var gt = new FormattedText(mi.InputGestureText, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                        new Typeface("Segoe UI"), 12, dis, 1.0);
                    dc.DrawText(gt, new Point(r.Right - gt.Width - 12, r.Y + (r.Height - gt.Height) / 2));
                }
            }
        }

        /// <summary>메뉴 아이콘(Path · Ellipse · Canvas · Grid · TextBlock)을 16px 칸에 그린다.</summary>
        private static void DrawMenuIcon(DrawingContext dc, object? icon, Rect box, double opacity)
        {
            if (icon == null) return;
            dc.PushOpacity(opacity);
            try
            {
                switch (icon)
                {
                    case TextBlock tb:
                        var g = new FormattedText(tb.Text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                            new Typeface(tb.FontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), tb.FontSize, tb.Foreground ?? Brushes.Gray, 1.0);
                        dc.DrawText(g, new Point(box.X, box.Y + (box.Height - g.Height) / 2));
                        break;
                    case Ellipse el:
                    {
                        double w = double.IsNaN(el.Width) ? 10 : el.Width, h = double.IsNaN(el.Height) ? 10 : el.Height;
                        dc.DrawEllipse(el.Fill, null, new Point(box.X + box.Width / 2, box.Y + box.Height / 2), w / 2, h / 2);
                        break;
                    }
                    case ShapePath p when p.Data != null:
                    {
                        double w = double.IsNaN(p.Width) ? 14 : Math.Min(p.Width, 16), h = double.IsNaN(p.Height) ? 14 : Math.Min(p.Height, 16);
                        var gb = p.Data.Bounds;
                        if (gb.IsEmpty || gb.Width <= 0 || gb.Height <= 0) break;
                        double s = Math.Min(w / gb.Width, h / gb.Height);
                        var geo = p.Data.Clone();
                        double ox = box.X + (box.Width - gb.Width * s) / 2 - gb.X * s, oy = box.Y + (box.Height - gb.Height * s) / 2 - gb.Y * s;
                        geo.Transform = new TransformGroup { Children = { new ScaleTransform(s, s), new TranslateTransform(ox, oy) } };
                        dc.DrawGeometry(p.Fill, p.Stroke != null ? new Pen(p.Stroke, Math.Max(1, p.StrokeThickness)) : null, geo);
                        break;
                    }
                    case Canvas cv:
                    {
                        double cw = double.IsNaN(cv.Width) ? 14 : cv.Width, ch = double.IsNaN(cv.Height) ? 14 : cv.Height;
                        dc.PushTransform(new TranslateTransform(box.X + (box.Width - cw) / 2, box.Y + (box.Height - ch) / 2));
                        foreach (var c in cv.Children.OfType<ShapePath>().Where(c => c.Data != null))
                            dc.DrawGeometry(c.Fill, c.Stroke != null ? new Pen(c.Stroke, Math.Max(1, c.StrokeThickness)) : null, c.Data);
                        dc.Pop();
                        break;
                    }
                    case Panel pn:
                        foreach (var c in pn.Children.OfType<UIElement>()) { DrawMenuIcon(dc, c, box, 1.0); break; }
                        break;
                }
            }
            finally { dc.Pop(); }
        }

        private async Task FinishAsync()
        {
            await Task.Delay(1000);
            _marks.Add(("__end", Now()));
            _timer?.Stop();
            await Task.WhenAll(_encodes.ToArray());
            var sb = new StringBuilder("{\"frames\":[");
            sb.Append(string.Join(",", _frames.Select(f => $"{{\"name\":\"{f.Name}\",\"t\":{f.T.ToString("F3", CultureInfo.InvariantCulture)}}}")));
            sb.Append("],\"marks\":[");
            sb.Append(string.Join(",", _marks.Select(m => $"{{\"id\":\"{m.Id}\",\"t\":{m.T.ToString("F3", CultureInfo.InvariantCulture)}}}")));
            sb.Append("]}");
            File.WriteAllText(System.IO.Path.Combine(_out, "timeline.json"), sb.ToString());
            Log($"frames {_frames.Count}, marks {_marks.Count}");
            // 녹화 중 연 창은 닫고 끝낸다 (모달이 남아 있으면 종료가 막힌다)
            foreach (var w in OverWindows().AsEnumerable().Reverse()) { try { w.Close(); } catch { } }
            _log.Dispose();
        }

        private void Log(string s) => _log.WriteLine($"{DateTime.Now:HH:mm:ss.fff} {s}");

        // ───────────────────────── 시나리오 ─────────────────────────

        private async Task RunScenarioAsync()
        {
            var path = Environment.GetEnvironmentVariable("VMS_RECORD_SCENARIO");
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) { Log("VMS_RECORD_SCENARIO 없음"); return; }
            using var doc = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            var root = doc.RootElement;
            if (root.TryGetProperty("setup", out var setup))
                foreach (var a in setup.EnumerateArray()) await DoAsync(a);
            await Settle();
            StartFrames();
            await Task.Delay(1200);
            foreach (var a in root.GetProperty("actions").EnumerateArray()) await DoAsync(a);
        }

        private async Task DoAsync(JsonElement a)
        {
            string op = a.GetProperty("op").GetString()!;
            try
            {
                switch (op)
                {
                    case "seg":
                    {
                        string id = a.GetProperty("id").GetString()!;
                        double t0 = Now();
                        _segId = id; _segT0 = t0;
                        _marks.Add((id, t0));
                        Log($"[{id}] start");
                        if (a.TryGetProperty("do", out var list))
                            foreach (var x in list.EnumerateArray()) await DoAsync(x);
                        double remain = (_durs.TryGetValue(id, out var d) ? d : 0) + Gap - (Now() - t0);
                        if (remain < 0) Log($"  ⚠ {id}: 동작이 내레이션보다 {-remain:F1}s 길다");
                        if (remain > 0) await Task.Delay(TimeSpan.FromSeconds(remain));
                        break;
                    }
                    case "wait": await Task.Delay(TimeSpan.FromSeconds(a.GetProperty("s").GetDouble())); break;
                    case "at":
                    {
                        // 이 구간 내레이션의 f 지점(0~1)까지 기다린다 — 자막 문장과 화면 동작을 맞추는 용도. 이미 지났으면 바로 진행
                        if (_segId != null && _durs.TryGetValue(_segId, out var sd))
                        {
                            double left = _segT0 + a.GetProperty("f").GetDouble() * sd - Now();
                            if (left > 0) await Task.Delay(TimeSpan.FromSeconds(left));
                        }
                        break;
                    }
                    case "move": await MoveTo(ResolvePoint(a), Ms(a, 700)); break;
                    case "click": await ClickAsync(a); break;
                    case "hl":
                    {
                        var rects = new List<Rect>();
                        if (a.TryGetProperty("targets", out var tl))
                            foreach (var tEl in tl.EnumerateArray())
                            {
                                var fe = Find(tEl.GetString()!);
                                if (fe != null) rects.Add(Bounds(fe));
                                else Log("  hl 대상 없음: " + tEl.GetString());
                            }
                        if (a.TryGetProperty("rect", out var rr))
                            rects.Add(new Rect(rr[0].GetDouble(), rr[1].GetDouble(), rr[2].GetDouble(), rr[3].GetDouble()));
                        if (a.TryGetProperty("img", out var ir))
                            rects.Add(ImageRect(ir[0].GetDouble(), ir[1].GetDouble(), ir[2].GetDouble(), ir[3].GetDouble()));
                        _hl = rects; _hlT0 = Now();
                        break;
                    }
                    case "hlClear": _hl = new(); break;
                    case "ripple": _clickT = Now(); _clickP = _cur; await Task.Delay(250); break;   // 빈 곳 클릭 연출 (동작 없음)
                    case "tip":
                    {
                        var fe = Find(a.GetProperty("target").GetString()!);
                        if (fe == null) { Log("  tip 대상 없음: " + a.GetProperty("target").GetString()); break; }
                        _tipAnchor = Bounds(fe);
                        _tipText = a.TryGetProperty("text", out var tt) ? tt.GetString() : ToolTipText(fe);
                        if (string.IsNullOrEmpty(_tipText)) { Log("  tip 글자 없음"); _tipText = null; }
                        else Log("  tip: " + _tipText.Replace('\n', ' '));
                        break;
                    }
                    case "tipClear": _tipText = null; break;
                    case "loadRecipe":
                    {
                        var recipe = Services.RecipeService.Instance.LoadRecipe(a.GetProperty("path").GetString()!);
                        if (recipe == null) { Log("레시피 로드 실패"); break; }
                        Services.RecipeService.Instance.SetCurrentRecipe(recipe);
                        await Settle();
                        int si = a.TryGetProperty("step", out var s) ? s.GetInt32() : 0;
                        if (si >= 0 && si < recipe.Steps.Count) _vm.SelectedStep = recipe.Steps[si];   // step:-1 = 스텝 고르지 않음
                        await Settle();
                        break;
                    }
                    case "openImage":
                    {
                        if (a.TryGetProperty("target", out var tg)) await ClickVisual(Find(tg.GetString()!));
                        var mat = OpenCvSharp.Cv2.ImRead(a.GetProperty("path").GetString()!);
                        if (!mat.Empty()) _vm.CurrentImage = mat;
                        _vm.SelectedDisplayMode = ImageDisplayMode.OriginalImage;
                        await Settle();
                        ZoomFit();
                        await Settle();
                        break;
                    }
                    case "expand":
                    {
                        string cat = a.GetProperty("text").GetString()!;
                        bool open = !a.TryGetProperty("open", out var eo) || eo.GetBoolean();
                        var tv = (TreeView?)Find("name:ToolPaletteTreeView");
                        foreach (var tvi in FindAll<TreeViewItem>(tv!))
                            if (tvi.DataContext is ToolCategory c && c.CategoryName == cat) { tvi.IsExpanded = open; if (open) tvi.BringIntoView(); }
                        await Settle();
                        break;
                    }
                    case "addTool": await AddToolAsync(a); break;
                    case "selectTool":
                    {
                        var item = Tool(a.GetProperty("tool").GetString()!);
                        await ClickVisual(NodeOf(item), Ms(a, 700));
                        bool force = a.TryGetProperty("force", out var fo) && fo.GetBoolean();   // 연결 뒤 설정 패널 갱신용
                        if (force && ReferenceEquals(_vm.SelectedTool, item)) { _vm.SelectedTool = null; await Settle(); }
                        if (!ReferenceEquals(_vm.SelectedTool, item))
                        {
                            foreach (var t in _vm.DroppedTools) t.IsSelected = false;
                            item.IsSelected = true;
                            _vm.SelectedTool = item;
                        }
                        await Settle();
                        break;
                    }
                    case "drawRoi": await DrawRoiAsync(a); break;
                    case "set":
                    {
                        // on: "win" = 맨 위에 뜬 창의 DataContext, "vm" = 메인 VM, 없으면 도구(tool 이름 또는 선택 도구)의 VisionTool
                        string onv = a.TryGetProperty("on", out var on) ? on.GetString() ?? "" : "";
                        object target = onv == "win" ? (WinVm() ?? _vm)
                                      : onv == "vm" ? _vm
                                      : (a.TryGetProperty("tool", out var tn) ? Tool(tn.GetString()!) : _vm.SelectedTool!).VisionTool!;
                        if (a.TryGetProperty("target", out var stg)) await ClickVisual(Find(stg.GetString()!), Ms(a, 700));
                        foreach (var p in a.GetProperty("values").EnumerateObject())
                            SetProp(target, p.Name, p.Value);
                        await Settle();
                        break;
                    }
                    case "cmd":
                    {
                        // VM 의 ICommand 실행 — 커서는 target 으로 먼저 이동·클릭 연출. modal:true 면 디스패처로 넘겨 모달 창이 떠도 흐름이 막히지 않게
                        if (a.TryGetProperty("target", out var tg)) await ClickVisual(Find(tg.GetString()!), Ms(a, 700));
                        object src = a.TryGetProperty("on", out var on) && on.GetString() == "win" ? (WinVm() ?? _vm) : _vm;
                        string name = a.GetProperty("name").GetString()!;
                        var c = src.GetType().GetProperty(name)?.GetValue(src) as ICommand;
                        if (c == null || !c.CanExecute(null)) { Log("  명령 실행 불가: " + name); break; }
                        if (a.TryGetProperty("modal", out var md) && md.GetBoolean())
                        {
                            _ = _win.Dispatcher.BeginInvoke(() => c.Execute(null));
                            await Settle(); await Task.Delay(150); await Settle();
                        }
                        else { c.Execute(null); await Settle(); }
                        break;
                    }
                    case "call":
                    {
                        // 메인 VM 메서드 직접 호출 (공개 · 비공개) — tool 이 있으면 그 ToolItem 을 인자로. 모달이 뜰 수 있어 디스패처로 넘긴다
                        string name = a.GetProperty("name").GetString()!;
                        var m = _vm.GetType().GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        if (m == null) { Log("  ⚠ 메서드 없음: " + name); break; }
                        object?[] args = a.TryGetProperty("tool", out var ct) ? new object?[] { Tool(ct.GetString()!) } : Array.Empty<object?>();
                        _ = _win.Dispatcher.BeginInvoke(() => m.Invoke(_vm, args));
                        await Settle(); await Task.Delay(150); await Settle();
                        break;
                    }
                    case "connect": await ConnectAsync(a); break;
                    case "connectTo": await ConnectToAsync(a); break;
                    case "settingsSet":
                    {
                        // 선택 도구의 설정 VM 속성 (IsExpertMode 등) — 커서는 target 으로 클릭 연출
                        if (a.TryGetProperty("target", out var tg)) await ClickVisual(Find(tg.GetString()!), Ms(a, 700));
                        if (_vm.SelectedToolSettings != null)
                            foreach (var p in a.GetProperty("values").EnumerateObject())
                                SetProp(_vm.SelectedToolSettings, p.Name, p.Value);
                        await Settle();
                        break;
                    }
                    case "runAll":
                    {
                        if (a.TryGetProperty("target", out var tg)) await ClickVisual(Find(tg.GetString()!));
                        await _vm.RunAllToolsAsync();
                        await Settle();
                        Log($"  RunAll: {_vm.StatusMessage} | " + string.Join(" / ", _vm.ToolRunResults.Select(r => $"{r.ToolName}={(r.Result ? "OK" : "NG")}")));
                        break;
                    }
                    case "display":
                        _vm.SelectedDisplayMode = a.GetProperty("mode").GetString() == "Original" ? ImageDisplayMode.OriginalImage : ImageDisplayMode.ResultImage;
                        await Settle();
                        break;
                    case "showRoi":
                        if (_vm.SelectedToolSettings?.ShowROICommand.CanExecute(null) == true) _vm.SelectedToolSettings.ShowROICommand.Execute(null);
                        await Settle();
                        break;
                    case "scrollTo":
                    {
                        // top:true 면 대상이 감싼 ScrollViewer 맨 위(여백 offset)로 오게 스크롤 — 아니면 BringIntoView
                        var fe = Find(a.GetProperty("target").GetString()!);
                        if (fe == null) break;
                        if (a.TryGetProperty("top", out var tp) && tp.GetBoolean() && FindAncestor<ScrollViewer>(fe) is ScrollViewer sv)
                        {
                            double off = a.TryGetProperty("offset", out var of) ? of.GetDouble() : 8;
                            double y = fe.TransformToAncestor(sv).Transform(new Point(0, 0)).Y;
                            sv.ScrollToVerticalOffset(Math.Max(0, sv.VerticalOffset + y - off));
                        }
                        else fe.BringIntoView();
                        await Settle();
                        break;
                    }
                    case "zoom": await ZoomAsync(a); break;
                    case "save":
                        if (a.TryGetProperty("target", out var st)) await ClickVisual(Find(st.GetString()!));
                        _vm.SaveCurrentRecipe();
                        await Settle();
                        break;
                    case "closeWindow":
                    {
                        var w = OverWindows().LastOrDefault();
                        if (a.TryGetProperty("target", out var tg)) await ClickVisual(Find(tg.GetString()!), Ms(a, 700));
                        if (w != null) { w.Close(); Log("  closeWindow: " + w.GetType().Name); }
                        else Log("  닫을 창 없음");
                        await Settle();
                        break;
                    }
                    case "tab":
                    {
                        var fe = Find(a.GetProperty("target").GetString()!);
                        await ClickVisual(fe, Ms(a, 700));
                        var ti = fe as TabItem ?? (fe != null ? FindAncestor<TabItem>(fe) : null);
                        if (ti != null) ti.IsSelected = true; else Log("  탭 없음");
                        await Settle();
                        break;
                    }
                    case "type":
                    {
                        // 글자 칸에 한 글자씩 입력 연출 — TextBox.Text 를 바꾸고 바인딩을 원본으로 밀어 넣는다
                        var fe = Find(a.GetProperty("target").GetString()!);
                        await ClickVisual(fe, Ms(a, 700));
                        string text = a.GetProperty("text").GetString()!;
                        int per = a.TryGetProperty("perChar", out var pc) ? pc.GetInt32() : 45;
                        if (fe != null && fe is not TextBox) fe = FindAll<TextBox>(fe).FirstOrDefault(x => x.ActualWidth > 0) ?? fe;   // TextBoxParameter 등
                        if (fe is not TextBox tb) { Log("  글자 칸 아님: " + a.GetProperty("target").GetString()); break; }
                        if (!(a.TryGetProperty("append", out var ap) && ap.GetBoolean())) tb.Text = "";
                        string start = tb.Text;
                        for (int i = 1; i <= text.Length; i++)
                        {
                            tb.Text = start + text[..i];
                            tb.CaretIndex = tb.Text.Length;
                            await Task.Delay(per);
                        }
                        tb.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
                        if (a.TryGetProperty("enter", out var en) && en.GetBoolean())
                            tb.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(tb) ?? PresentationSource.FromVisual(Root)!, 0, Key.Enter) { RoutedEvent = UIElement.KeyDownEvent });
                        await Settle();
                        break;
                    }
                    case "combo":
                    {
                        // 콤보 상자 항목 고르기 — 항목 글자(ToString / 표시 경로)로
                        var fe = Find(a.GetProperty("target").GetString()!);
                        await ClickVisual(fe, Ms(a, 700));
                        var cb = fe as ComboBox ?? (fe != null ? FindAncestor<ComboBox>(fe) ?? FindAll<ComboBox>(fe).FirstOrDefault(x => x.ActualWidth > 0) : null);
                        if (cb == null) { Log("  콤보 아님: " + a.GetProperty("target").GetString()); break; }
                        string want = a.GetProperty("item").GetString()!;
                        object? hit = null;
                        foreach (var it in cb.Items)
                            if (ComboText(cb, it) == want) { hit = it; break; }
                        if (hit == null) { Log($"  콤보 항목 없음: {want} — " + string.Join(", ", cb.Items.Cast<object>().Select(o => ComboText(cb, o)))); break; }
                        if (a.TryGetProperty("show", out var cshow))   // 펼친 목록을 그려 보이고 그 항목을 짚어 고른다 (팝업은 화면 밖에서 안 그려진다)
                        {
                            var cbr = Bounds(cb);
                            var texts = cb.Items.Cast<object>().Select(o => ComboText(cb, o)).ToList();
                            _menu = new MenuView { Items = texts.Cast<object>().ToList(), TopLeft = new Point(cbr.X - 4, cbr.Bottom - 2),
                                Width = Math.Max(cbr.Width + 8, texts.Max(t => new FormattedText(t, CultureInfo.GetCultureInfo("ko-KR"), FlowDirection.LeftToRight, new Typeface("Segoe UI"), 13, Brushes.Black, 1.0).Width) + 30),
                                Light = false };
                            _menuHover = texts.IndexOf(ComboText(cb, cb.SelectedItem));
                            await Task.Delay((int)(cshow.GetDouble() * 1000));
                            int hi = texts.IndexOf(want);
                            var lay = MenuLayout();
                            await MoveTo(new Point(lay[hi].R.X + 60, lay[hi].R.Y + lay[hi].R.Height / 2), 600);
                            _menuHover = hi;
                            await Task.Delay(450);
                            _clickT = Now(); _clickP = _cur;
                            await Task.Delay(200);
                            _menu = null; _menuHover = -1;
                        }
                        cb.SelectedItem = hit;
                        await Settle();
                        break;
                    }
                    case "check":
                    {
                        // 체크 상자 · 토글 켜기/끄기 (value 없으면 반전)
                        var fe = Find(a.GetProperty("target").GetString()!);
                        await ClickVisual(fe, Ms(a, 700));
                        var tb = fe as ToggleButton ?? (fe != null ? FindAncestor<ToggleButton>(fe) : null);
                        if (tb == null) { Log("  토글 아님: " + a.GetProperty("target").GetString()); break; }
                        tb.IsChecked = a.TryGetProperty("value", out var v) ? v.GetBoolean() : !(tb.IsChecked ?? false);
                        tb.GetBindingExpression(ToggleButton.IsCheckedProperty)?.UpdateSource();
                        await Settle();
                        break;
                    }
                    case "menu":
                    {
                        // 메뉴 바 항목(menu:File)의 하위 메뉴 또는 요소(tool:X 등)의 우클릭 메뉴를 그려 보이고 항목을 고른다.
                        // 항목은 명령이 있으면 명령, 없으면 Click 이벤트 — 디스패처로 넘겨 모달 창이 떠도 막히지 않게
                        string spec = a.GetProperty("target").GetString()!;
                        var el = Find(spec);
                        if (el == null) { Log("  메뉴 대상 없음: " + spec); break; }
                        await ClickVisual(el, Ms(a, 700));
                        var mv = new MenuView();
                        if (el is MenuItem top)
                        {
                            if (!top.HasItems) { Log("  하위 메뉴 없음: " + spec); break; }
                            var hb = Bounds(top);
                            mv.Light = true; mv.HeaderRect = hb; mv.TopLeft = new Point(hb.X, hb.Bottom + 1);
                            mv.Items = top.Items.Cast<object>().Where(o => o is not UIElement u || u.Visibility == Visibility.Visible).ToList();
                        }
                        else
                        {
                            if (el.ContextMenu == null) { Log("  우클릭 메뉴 없음: " + spec); break; }
                            mv.Context = el.ContextMenu; mv.Owner = el; mv.TopLeft = new Point(_cur.X + 2, _cur.Y + 2);
                            mv.Items = el.ContextMenu.Items.Cast<object>().Where(o => o is not UIElement u || u.Visibility == Visibility.Visible).ToList();
                            // 우클릭은 도구를 고른다 (Item_MouseRightButtonDown 과 같음)
                            if (el.DataContext is ToolItem ti)
                            {
                                foreach (var t in _vm.DroppedTools) t.IsSelected = false;
                                ti.IsSelected = true; _vm.SelectedTool = ti;
                            }
                        }
                        double maxw = 0;
                        foreach (var mi in mv.Items.OfType<MenuItem>())
                        {
                            var ft = new FormattedText(HeaderText(mi), CultureInfo.GetCultureInfo("ko-KR"), FlowDirection.LeftToRight, new Typeface("Segoe UI"), 13, Brushes.Black, 1.0);
                            double gw = string.IsNullOrEmpty(mi.InputGestureText) ? 0 : 60;
                            maxw = Math.Max(maxw, ft.Width + gw);
                        }
                        mv.Width = Math.Max(200, maxw + 70);
                        _menu = mv; _menuHover = -1;
                        await Task.Delay(a.TryGetProperty("show", out var shw) ? (int)(shw.GetDouble() * 1000) : 700);
                        if (a.TryGetProperty("hover", out var hv))   // 항목을 차례로 짚어 보기 (고르지 않음)
                            foreach (var h in hv.EnumerateArray())
                            {
                                var lay0 = MenuLayout();
                                int hi = lay0.FindIndex(l => l.Item is MenuItem m && HeaderText(m) == h.GetString());
                                if (hi < 0) { Log("  메뉴 항목 없음: " + h.GetString()); continue; }
                                await MoveTo(new Point(lay0[hi].R.X + 80, lay0[hi].R.Y + lay0[hi].R.Height / 2), 450);
                                _menuHover = hi;
                                await Task.Delay(a.TryGetProperty("hoverWait", out var hw) ? (int)(hw.GetDouble() * 1000) : 500);
                            }
                        if (!a.TryGetProperty("pick", out var pk)) break;   // 펼쳐 보이기만 (다음 menuClose 까지)
                        var lay = MenuLayout();
                        int idx = lay.FindIndex(l => l.Item is MenuItem m && HeaderText(m) == pk.GetString());
                        if (idx < 0) { Log("  메뉴 항목 없음: " + pk.GetString()); _menu = null; break; }
                        var r = lay[idx].R;
                        await MoveTo(new Point(r.X + 80, r.Y + r.Height / 2), 600);
                        _menuHover = idx;
                        await Task.Delay(450);
                        _clickT = Now(); _clickP = _cur;
                        await Task.Delay(220);
                        var pmi = (MenuItem)lay[idx].Item;
                        var owner = _menu.Owner; var cm = _menu.Context;
                        _menu = null; _menuHover = -1;
                        if (!MenuItemEnabled(pmi)) { Log("  메뉴 항목 비활성: " + pk.GetString()); break; }
                        if (cm != null && owner != null) cm.PlacementTarget = owner;
                        var cmd = ResolveMenuCommand(pmi);
                        var prm = pmi.CommandParameter;
                        if (cmd != null) _ = _win.Dispatcher.BeginInvoke(() => cmd.Execute(prm));
                        else _ = _win.Dispatcher.BeginInvoke(() => pmi.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, pmi)));
                        await Settle(); await Task.Delay(200); await Settle();
                        break;
                    }
                    case "menuClose": _menu = null; _menuHover = -1; break;
                    case "popup":
                    {
                        // 요소 안의 Popup(도움말 ? 등) 내용을 열지 않고 배치해 오른쪽(또는 왼쪽)에 그린다 — 열면 실제 화면에 뜬다
                        var host = Find(a.GetProperty("target").GetString()!);
                        if (host == null) { Log("  popup 대상 없음"); break; }
                        var pop = FindAll<FrameworkElement>(host).Select(e => e as Popup ?? e.FindName("HelpPopup") as Popup).FirstOrDefault(x => x != null)
                                  ?? (host.FindName("HelpPopup") as Popup)
                                  ?? LogicalTreeHelper.GetChildren(host).OfType<Popup>().FirstOrDefault();
                        if (pop == null && host is UserControl uc && uc.Content is DependencyObject ucc)
                            pop = LogicalTreeHelper.GetChildren(ucc).OfType<Popup>().FirstOrDefault();
                        if (pop?.Child is not FrameworkElement child) { Log("  popup 없음"); break; }
                        var hr = Bounds(host);
                        if (a.TryGetProperty("hover", out var ph) && ph.GetBoolean()) await MoveTo(new Point(hr.X + hr.Width / 2, hr.Y + hr.Height / 2), Ms(a, 600));
                        child.Measure(new Size(a.TryGetProperty("w", out var pw) ? pw.GetDouble() : 420, a.TryGetProperty("h", out var phh) ? phh.GetDouble() : 900));
                        child.Arrange(new Rect(child.DesiredSize));
                        child.UpdateLayout();
                        double cw = child.ActualWidth, ch = child.ActualHeight;
                        double x = hr.Right + 6, y = hr.Top - 6;
                        if (x + cw > W - 8) x = hr.Left - cw - 6;
                        y = Math.Clamp(y, 8, H - ch - 8);
                        _popupEl = child; _popupRect = new Rect(x, y, cw, ch);
                        Log($"  popup {cw:F0}x{ch:F0} at {x:F0},{y:F0}");
                        break;
                    }
                    case "popupClear": _popupEl = null; break;
                    case "spawn":
                    {
                        // 바깥 프로그램을 띄워 두고 기다리지 않는다 (VMS 메인을 화면 밖 녹화 모드로 함께 실행 등). env 로 환경 변수 지정
                        var psi = new ProcessStartInfo(a.GetProperty("exe").GetString()!) { UseShellExecute = false, CreateNoWindow = true };
                        if (a.TryGetProperty("cwd", out var cwd)) psi.WorkingDirectory = cwd.GetString()!;
                        foreach (var x in a.GetProperty("args").EnumerateArray()) psi.ArgumentList.Add(x.GetString()!);
                        if (a.TryGetProperty("env", out var envs))
                            foreach (var ev in envs.EnumerateObject()) psi.Environment[ev.Name] = ev.Value.GetString();
                        var sp = Process.Start(psi);
                        Log($"  spawn {System.IO.Path.GetFileName(psi.FileName)}: pid {sp?.Id}");
                        break;
                    }
                    case "fileDialog":
                    {
                        // 다음 파일 열기 · 폴더 · 저장 대화상자 대신 돌려줄 경로 — MainViewModel 의 IDialogService 를 녹화용 래퍼로 감싼다
                        if (_dlg == null)
                        {
                            var f = typeof(MainViewModel).GetField("_dialogService", BindingFlags.NonPublic | BindingFlags.Instance);
                            if (f?.GetValue(_vm) is Interfaces.IDialogService inner)
                            {
                                _dlg = new RecordingDialogService(inner, Log);
                                f.SetValue(_vm, _dlg);
                            }
                            else { Log("  ⚠ 대화상자 서비스 없음"); break; }
                        }
                        if (a.TryGetProperty("save", out var sv)) _dlg.Saves.Enqueue(sv.GetString()!);
                        if (a.TryGetProperty("open", out var opn)) _dlg.Opens.Enqueue(opn.GetString()!);
                        if (a.TryGetProperty("folder", out var fld)) _dlg.Folders.Enqueue(fld.GetString()!);
                        break;
                    }
                    case "waitFor":
                    {
                        // 대상이 보일 때까지(또는 gone 이면 사라질 때까지) 기다린다 — 비동기로 뜨는 창 · 값 대기
                        string spec = a.GetProperty("target").GetString()!;
                        bool gone = a.TryGetProperty("gone", out var gn) && gn.GetBoolean();
                        var sw = Stopwatch.StartNew();
                        while (sw.ElapsedMilliseconds < Ms(a, 10000))
                        {
                            if ((Find(spec, quiet: true) != null) != gone) break;
                            await Task.Delay(50);
                        }
                        bool done = (Find(spec, quiet: true) != null) != gone;
                        Log($"  waitFor {spec}{(gone ? " (사라짐)" : "")}: {(done ? "OK" : "⚠ 시간 초과")} {sw.ElapsedMilliseconds} ms");
                        await Settle();
                        break;
                    }
                    case "run":
                    {
                        // 녹화 중 바깥 프로그램 실행 — 끝날 때까지 기다린다. 인자는 로그에 쓰지 않는다.
                        var psi = new ProcessStartInfo(a.GetProperty("exe").GetString()!) { UseShellExecute = false, CreateNoWindow = true,
                            RedirectStandardOutput = true, RedirectStandardError = true };
                        foreach (var x in a.GetProperty("args").EnumerateArray()) psi.ArgumentList.Add(x.GetString()!);
                        using var p = Process.Start(psi)!;
                        var so = p.StandardOutput.ReadToEndAsync();
                        var se = p.StandardError.ReadToEndAsync();
                        var sw = Stopwatch.StartNew();
                        while (!p.HasExited && sw.ElapsedMilliseconds < Ms(a, 20000)) await Task.Delay(50);
                        if (!p.HasExited) { try { p.Kill(); } catch { } Log("  ⚠ run 시간 초과"); break; }
                        string label = a.TryGetProperty("label", out var lb) ? lb.GetString()! : System.IO.Path.GetFileName(psi.FileName);
                        Log($"  run {label}: exit {p.ExitCode} ({sw.ElapsedMilliseconds} ms) {(await so).Trim().Replace('\n', ' ')}");
                        if (p.ExitCode != 0) Log("  ⚠ run 실패: " + (await se).Trim().Replace('\n', ' '));
                        await Settle();
                        break;
                    }
                    case "pick": await PickAsync(a); break;
                    case "cellType": await CellTypeAsync(a); break;
                    case "seqAdd": await SeqAddAsync(a); break;
                    case "seqSelect":
                    {
                        var vm = SeqVm(); var node = SeqNode(a.GetProperty("node").GetString()!);
                        if (vm == null || node == null) { Log("  ⚠ seqSelect: 노드 없음"); break; }
                        await ClickVisual(SeqNodeEl(node), Ms(a, 700));
                        foreach (var n in vm.Nodes) n.IsSelected = false;
                        node.IsSelected = true; vm.SelectedNode = node;
                        await Settle();
                        break;
                    }
                    case "seqConnect": await SeqConnectAsync(a); break;
                    case "seqMove":
                    {
                        // 노드 끌어 옮기기 (Node_MouseMove 와 같음) — at = 캔버스 좌표(노드 왼쪽 위)
                        var node = SeqNode(a.GetProperty("node").GetString()!);
                        if (node == null) { Log("  ⚠ seqMove: 노드 없음"); break; }
                        await ClickVisual(SeqNodeEl(node), 500);
                        double x0 = node.X, y0 = node.Y, x1 = a.GetProperty("at")[0].GetDouble(), y1 = a.GetProperty("at")[1].GetDouble();
                        var c0 = _cur; int ms = Ms(a, 800); var sw = Stopwatch.StartNew();
                        while (true)
                        {
                            double k = Math.Min(1, sw.ElapsedMilliseconds / (double)ms), e = k < .5 ? 2 * k * k : 1 - Math.Pow(-2 * k + 2, 2) / 2;
                            node.X = x0 + (x1 - x0) * e; node.Y = y0 + (y1 - y0) * e;
                            _cur = new Point(c0.X + (x1 - x0) * e, c0.Y + (y1 - y0) * e);
                            SeqRefreshEdges();
                            if (k >= 1) break;
                            await Task.Delay(15);
                        }
                        await Settle();
                        break;
                    }
                    case "mockInput":
                    {
                        // 시험용 가상 IO 보드(Description "[MOCK]")의 입력 채널 값 — 센서 신호 대신
                        var vm = SeqVm();
                        var boards = vm?.GetType().GetField("_boards", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(vm) as System.Collections.IDictionary;
                        string dev = a.GetProperty("device").GetString()!;
                        var board = boards?[dev];
                        var bits = board?.GetType().BaseType?.GetField("_inputBits", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(board) as bool[];
                        if (bits == null) { Log("  ⚠ mockInput: 가상 보드 없음 " + dev); break; }
                        int ch = a.GetProperty("ch").GetInt32();
                        bits[ch] = a.GetProperty("value").GetBoolean();
                        Log($"  mockInput {dev} ch{ch} = {bits[ch]}");
                        break;
                    }
                    case "winSet":
                    {
                        // 맨 위 창 VM 의 속성 — 화면 조작으로 바꿀 수 없는 값만(예: 콤보가 아닌 내부 상태). target 으로 클릭 연출
                        if (a.TryGetProperty("target", out var tg)) await ClickVisual(Find(tg.GetString()!), Ms(a, 700));
                        object? dc = WinVm();
                        if (dc == null) { Log("  ⚠ winSet: 창 VM 없음"); break; }
                        foreach (var p in a.GetProperty("values").EnumerateObject()) SetProp(dc, p.Name, p.Value);
                        await Settle();
                        break;
                    }
                    case "winSize":
                    {
                        // 맨 위 창 크기 — 녹화 화면(1920x1080) 안에서 가운데로. 사용자가 창 가장자리를 끌어 키우는 것과 같음
                        var w = OverWindows().LastOrDefault();
                        if (w == null) { Log("  ⚠ winSize: 창 없음"); break; }
                        w.Width = a.GetProperty("w").GetDouble(); w.Height = a.GetProperty("h").GetDouble();
                        w.Left = _win.Left + (W - w.Width) / 2; w.Top = _win.Top + Math.Max(0, (H - w.Height) / 2);
                        await Settle();
                        Log($"  winSize {w.GetType().Name} {w.Width}x{w.Height}");
                        break;
                    }
                    case "winLog":
                    {
                        object? dc = WinVm();
                        foreach (var n in a.GetProperty("props").EnumerateArray())
                        {
                            object? v = dc;
                            foreach (var part in n.GetString()!.Split('.')) v = v?.GetType().GetProperty(part)?.GetValue(v);
                            Log($"  win {n.GetString()} = {v}");
                        }
                        break;
                    }
                    case "dump": Dump(a.TryGetProperty("filter", out var fl) ? fl.GetString() : null); break;
                    case "log": Log("  status: " + _vm.StatusMessage); break;
                    default: Log("알 수 없는 op " + op); break;
                }
            }
            catch (Exception ex) { Log($"op {op} 실패: {ex}"); }
            if (op != "seg" && op != "wait" && op != "at" && op != "move" && op != "hl" && op != "hlClear" && op != "tip" && op != "tipClear" && op != "ripple" && op != "menuClose")
                Log($"  {op}: {_vm.StatusMessage}");
        }

        private static int Ms(JsonElement a, int def) => a.TryGetProperty("ms", out var m) ? m.GetInt32() : def;

        private static string ComboText(ComboBox cb, object? it)
        {
            string s = it is ContentControl cci ? cci.Content?.ToString() ?? "" : it?.ToString() ?? "";
            if (!string.IsNullOrEmpty(cb.DisplayMemberPath))
                s = it?.GetType().GetProperty(cb.DisplayMemberPath)?.GetValue(it)?.ToString() ?? s;
            else if (it != null && s.StartsWith(it.GetType().Name + " {", StringComparison.Ordinal)
                     && it.GetType().GetProperty("Display")?.GetValue(it) is string disp)   // record 기본 ToString — 표시 글자(Display)로
                s = disp;
            return s;
        }

        private async Task Settle()
        {
            for (int i = 0; i < 2; i++)
            {
                _win.UpdateLayout();
                foreach (var w in OverWindows()) w.UpdateLayout();
                await _win.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            }
        }

        // ───────────────────────── 커서 ─────────────────────────

        private async Task MoveTo(Point p, int ms)
        {
            var s = _cur;
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < ms)
            {
                double k = sw.ElapsedMilliseconds / (double)ms;
                double e = k < .5 ? 2 * k * k : 1 - Math.Pow(-2 * k + 2, 2) / 2;
                _cur = new Point(s.X + (p.X - s.X) * e, s.Y + (p.Y - s.Y) * e);
                await Task.Delay(15);
            }
            _cur = p;
        }

        private async Task ClickVisual(FrameworkElement? fe, int ms = 700)
        {
            if (fe == null) { Log("  클릭 대상 없음"); return; }
            fe.BringIntoView();
            await Settle();
            var r = Bounds(fe);
            await MoveTo(new Point(r.X + r.Width / 2, r.Y + r.Height / 2), ms);
            await Task.Delay(150);
            _clickT = Now(); _clickP = _cur;
            await Task.Delay(120);
        }

        private async Task ClickAsync(JsonElement a)
        {
            var fe = Find(a.GetProperty("target").GetString()!);
            await ClickVisual(fe, Ms(a, 700));
            if (fe == null || (a.TryGetProperty("invoke", out var inv) && !inv.GetBoolean())) return;
            if (a.TryGetProperty("onclick", out var oc) && oc.GetBoolean() && fe is ButtonBase ob)
            {
                // 버튼 누름 그대로 — Click 처리기와 Command 를 함께 (ButtonBase.OnClick). 비활성이면 누르지 않는다.
                if (!ob.IsEnabled) Log("  버튼 비활성: " + a.GetProperty("target").GetString());
                else
                {
                    var m = typeof(ButtonBase).GetMethod("OnClick", BindingFlags.NonPublic | BindingFlags.Instance)!;
                    _ = _win.Dispatcher.BeginInvoke(() => m.Invoke(ob, null));
                }
                await Settle(); await Task.Delay(120); await Settle();
                return;
            }
            object hitObj = fe.Name == "PART_Header" && FindAncestor<TreeViewItem>(fe) is TreeViewItem hdrOwner ? hdrOwner : fe;   // tvi: 는 머리글 줄을 돌려준다
            switch (hitObj)
            {
                case TabItem ti: ti.IsSelected = true; break;
                case RadioButton rb: rb.IsChecked = true; rb.GetBindingExpression(ToggleButton.IsCheckedProperty)?.UpdateSource(); break;
                case ToggleButton tb when tb.Command != null:
                    if (tb.Command.CanExecute(tb.CommandParameter)) { var c = tb.Command; var p = tb.CommandParameter; _ = _win.Dispatcher.BeginInvoke(() => c.Execute(p)); }
                    else Log("  버튼 비활성: " + a.GetProperty("target").GetString());
                    break;
                case ToggleButton tb: tb.IsChecked = !(tb.IsChecked ?? false); break;
                case MenuItem mi:
                {
                    var cmd = ResolveMenuCommand(mi); var prm = mi.CommandParameter;
                    if (cmd != null && cmd.CanExecute(prm)) _ = _win.Dispatcher.BeginInvoke(() => cmd.Execute(prm));
                    else if (cmd == null) _ = _win.Dispatcher.BeginInvoke(() => mi.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, mi)));
                    else Log("  메뉴 비활성: " + a.GetProperty("target").GetString());
                    break;
                }
                case ButtonBase b:
                    if (b.Command != null)
                    {
                        if (b.Command.CanExecute(b.CommandParameter))
                        {
                            var cmd = b.Command; var prm = b.CommandParameter;
                            _ = _win.Dispatcher.BeginInvoke(() => cmd.Execute(prm));
                        }
                        else Log("  버튼 비활성: " + a.GetProperty("target").GetString());
                    }
                    else _ = _win.Dispatcher.BeginInvoke(() => b.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, b)));
                    break;
                case ListBoxItem li: li.IsSelected = true; break;
                case TreeViewItem tvi: tvi.IsSelected = true; break;
                case DataGridRow dgr:
                {
                    var grid = FindAncestor<DataGrid>(dgr);
                    bool desel = a.TryGetProperty("deselect", out var ds) && ds.GetBoolean();   // 펼친 상세 접기 (선택된 행을 다시 누름)
                    if (grid != null) grid.SelectedItem = desel ? null : dgr.Item; else dgr.IsSelected = !desel;
                    break;
                }
            }
            await Settle();
            await Task.Delay(120);
            await Settle();
        }

        private Point ResolvePoint(JsonElement a)
        {
            if (a.TryGetProperty("target", out var t))
            {
                var fe = Find(t.GetString()!);
                if (fe != null)
                {
                    // ax: 0 = 왼쪽 끝 · 0.5 = 가운데(기본) · 1 = 오른쪽 끝, dx · dy: 더 옮길 화소 — 값 글자를 커서가 가리지 않게
                    var r = Bounds(fe);
                    double ax = a.TryGetProperty("ax", out var axe) ? axe.GetDouble() : 0.5;
                    double dx = a.TryGetProperty("dx", out var dxe) ? dxe.GetDouble() : 0, dy = a.TryGetProperty("dy", out var dye) ? dye.GetDouble() : 0;
                    return new Point(r.X + r.Width * ax + dx, r.Y + r.Height / 2 + dy);
                }
            }
            if (a.TryGetProperty("img", out var im)) return ImagePoint(im[0].GetDouble(), im[1].GetDouble());
            if (a.TryGetProperty("rect", out var rr))
                return new Point(rr[0].GetDouble() + rr[2].GetDouble() / 2, rr[1].GetDouble() + rr[3].GetDouble() / 2);
            if (a.TryGetProperty("x", out var x)) return new Point(x.GetDouble(), a.GetProperty("y").GetDouble());
            Log("  이동 대상 없음");
            return _cur;
        }

        // ───────────────────────── 도구 · ROI · 연결 ─────────────────────────

        private ToolItem Tool(string name) =>
            _vm.DroppedTools.FirstOrDefault(t => t.Name == name) ?? throw new InvalidOperationException("도구 없음: " + name);

        private FrameworkElement? NodeOf(ToolItem item) =>
            FindAll<FrameworkElement>(Root).FirstOrDefault(e => e.Name == "ToolOuterBorder" && ReferenceEquals(e.DataContext, item) && e.ActualWidth > 0);

        private async Task AddToolAsync(JsonElement a)
        {
            string paletteName = a.GetProperty("palette").GetString()!;
            string name = a.TryGetProperty("name", out var n) ? n.GetString()! : paletteName;
            var src = _vm.ToolTree.SelectMany(c => c.Tools).First(t => t.Name == paletteName);
            var tv = (TreeView)Find("name:ToolPaletteTreeView")!;
            await Settle();
            FrameworkElement? paletteEl = FindAll<TextBlock>(tv).FirstOrDefault(t => t.Text == paletteName && t.ActualWidth > 0);
            if (paletteEl != null) paletteEl = (FrameworkElement?)FindAncestor<Border>(paletteEl) ?? paletteEl;
            if (paletteEl != null) { paletteEl.BringIntoView(); await Settle(); }
            double wx = a.GetProperty("at")[0].GetDouble(), wy = a.GetProperty("at")[1].GetDouble();

            if (paletteEl != null) await ClickVisual(paletteEl);
            var area = (FrameworkElement)Find("name:WorkspaceArea")!;
            var dst = area.TranslatePoint(new Point(wx + 70, wy + 30), Root);
            _ghost = paletteName;
            await MoveTo(dst, Ms(a, 1100));
            _ghost = null;
            _clickT = Now(); _clickP = _cur;

            var item = _vm.CreateDroppedTool(src, wx, wy);
            if (item == null) { Log("도구 생성 실패 " + paletteName); return; }
            if (name != item.Name) { item.Name = name; if (item.VisionTool != null) item.VisionTool.Name = name; }
            foreach (var t in _vm.DroppedTools) t.IsSelected = false;
            item.IsSelected = true;
            _vm.SelectedTool = item;
            await Settle();
        }

        private async Task DrawRoiAsync(JsonElement a)
        {
            var r = a.GetProperty("rect");
            double x = r[0].GetDouble(), y = r[1].GetDouble(), w = r[2].GetDouble(), h = r[3].GetDouble();
            bool affine = a.TryGetProperty("affine", out var af) && af.GetBoolean();
            if (a.TryGetProperty("target", out var tg)) await ClickVisual(Find(tg.GetString()!));
            _vm.SelectedDisplayMode = ImageDisplayMode.OriginalImage;
            await Settle();

            await MoveTo(ImagePoint(x, y), 700);
            _clickT = Now(); _clickP = _cur;
            ROIShape shape = affine
                ? new RectangleAffineROI(x + 0.5, y + 0.5, 1, 1, 0) { Name = $"{_vm.SelectedTool?.Name} ROI" }
                : new RectangleROI(x, y, 1, 1) { Name = $"{_vm.SelectedTool?.Name} ROI" };
            WeakReferenceMessenger.Default.Send(new RequestShowToolROIMessage(shape));

            int ms = Ms(a, 1500);
            var sw = Stopwatch.StartNew();
            while (true)
            {
                double k = Math.Min(1, sw.ElapsedMilliseconds / (double)ms);
                double e = k < .5 ? 2 * k * k : 1 - Math.Pow(-2 * k + 2, 2) / 2;
                double cw = Math.Max(1, w * e), ch = Math.Max(1, h * e);
                if (shape is RectangleAffineROI ra) { ra.CenterX = x + cw / 2; ra.CenterY = y + ch / 2; ra.Width = cw; ra.Height = ch; }
                else if (shape is RectangleROI rr) { rr.Width = cw; rr.Height = ch; }
                WeakReferenceMessenger.Default.Send(new RequestRefreshROIMessage(shape));
                _cur = ImagePoint(x + cw, y + ch);
                if (k >= 1) break;
                await Task.Delay(15);
            }
            _vm.OnROICreated(shape);
            WeakReferenceMessenger.Default.Send(new RequestShowToolROIMessage(shape));
            await Settle();
        }

        // ───────────────────────── 다른 창 (캘리브레이션 · 시퀀스) ─────────────────────────

        /// <summary>맨 위에 뜬 창의 VM — 창 DataContext 가 없으면 안쪽 뷰의 것.</summary>
        private object? WinVm()
        {
            var w = OverWindows().LastOrDefault();
            if (w == null) return null;
            return w.DataContext ?? FindAll<FrameworkElement>((DependencyObject)w.Content).Select(e => e.DataContext).FirstOrDefault(d => d != null);
        }

        private SequenceEditorViewModel? SeqVm() =>
            OverWindows().AsEnumerable().Reverse()
                .SelectMany(w => FindAll<FrameworkElement>((DependencyObject)w.Content).Select(e => e.DataContext).Prepend(w.DataContext))
                .OfType<SequenceEditorViewModel>().FirstOrDefault();

        private SequenceNodeItem? SeqNode(string name) => SeqVm()?.Nodes.FirstOrDefault(n => n.Name == name);

        private FrameworkElement? SeqNodeEl(SequenceNodeItem node) =>
            OverWindows().AsEnumerable().Reverse().SelectMany(w => FindAll<FrameworkElement>((DependencyObject)w.Content))
                .FirstOrDefault(e => e.Name == "NodeBorder" && ReferenceEquals(e.DataContext, node) && e.ActualWidth > 0);

        private void SeqRefreshEdges()
        {
            var view = OverWindows().SelectMany(w => FindAll<Views.Sequence.SequenceEditorView>((DependencyObject)w.Content)).FirstOrDefault();
            view?.GetType().GetMethod("UpdateEdgeLines", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public)?.Invoke(view, null);
        }

        /// <summary>
        /// 캘리브레이션 창 미리보기 이미지의 원본 화소 (x, y) 를 클릭 — 코드비하인드 PreviewImage_MouseLeftButtonDown 과 같은 변환의 역.
        /// {"op":"pick","x":..,"y":..,"ms":700}
        /// </summary>
        private async Task PickAsync(JsonElement a)
        {
            var img = Find("name:PreviewImageControl") as Image;
            if (img?.Source is not BitmapSource bmp) { Log("  ⚠ pick: 미리보기 이미지 없음"); return; }
            double px = a.GetProperty("x").GetDouble(), py = a.GetProperty("y").GetDouble();
            double scale = Math.Min(img.ActualWidth / bmp.PixelWidth, img.ActualHeight / bmp.PixelHeight);
            double ox = (img.ActualWidth - bmp.PixelWidth * scale) / 2.0, oy = (img.ActualHeight - bmp.PixelHeight * scale) / 2.0;
            var r = Bounds(img);
            await MoveTo(new Point(r.X + ox + px * scale, r.Y + oy + py * scale), Ms(a, 700));
            await Task.Delay(150);
            _clickT = Now(); _clickP = _cur;
            await Task.Delay(120);
            var dc = img.DataContext ?? WinVm();
            var m = dc?.GetType().GetMethod("AddPickedPoint");
            if (m == null) { Log("  ⚠ pick: AddPickedPoint 없음"); return; }
            m.Invoke(dc, new object[] { px, py });
            await Settle();
            Log($"  pick ({px:F1}, {py:F1})");
        }

        /// <summary>
        /// 표 칸에 숫자 입력 연출 — {"op":"cellType","grid":"grid:0","row":0,"col":"Xmm","prop":"WorldXmm","text":"56"}.
        /// 칸으로 커서를 옮겨 누르고, 행 항목의 속성을 한 글자씩(앞부분 숫자) 바꾼다.
        /// </summary>
        private async Task CellTypeAsync(JsonElement a)
        {
            var grid = Find(a.GetProperty("grid").GetString()!) as DataGrid;
            int ri = a.GetProperty("row").GetInt32();
            if (grid == null || ri >= grid.Items.Count) { Log("  ⚠ cellType: 표 · 행 없음"); return; }
            var item = grid.Items[ri];
            var col = grid.Columns.FirstOrDefault(c => c.Header?.ToString() == a.GetProperty("col").GetString());
            grid.ScrollIntoView(item);
            await Settle();
            var cell = col?.GetCellContent(item) as FrameworkElement;
            if (cell != null) await ClickVisual(cell, Ms(a, 600));
            var p = item.GetType().GetProperty(a.GetProperty("prop").GetString()!);
            if (p == null) { Log("  ⚠ cellType: 속성 없음"); return; }
            string text = a.GetProperty("text").GetString()!;
            int per = a.TryGetProperty("perChar", out var pc) ? pc.GetInt32() : 120;
            for (int i = 1; i <= text.Length; i++)
            {
                if (double.TryParse(text[..i], NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) p.SetValue(item, v);
                await Task.Delay(per);
            }
            await Settle();
        }

        /// <summary>시퀀스 노드 팔레트 항목을 캔버스 at(캔버스 좌표)로 끌어다 놓기 — Canvas_Drop 과 같게 AddNode 후 위치 지정.</summary>
        private async Task SeqAddAsync(JsonElement a)
        {
            var vm = SeqVm();
            string name = a.GetProperty("palette").GetString()!;
            var pi = vm?.PaletteItems.FirstOrDefault(p => p.DisplayName == name);
            var canvas = Find("name:SequenceCanvas");
            if (vm == null || pi == null || canvas == null) { Log("  ⚠ seqAdd: 팔레트 항목 · 캔버스 없음 " + name); return; }
            var li = FindAll<ListBoxItem>(Window.GetWindow(canvas)!).FirstOrDefault(l => ReferenceEquals(l.DataContext, pi));
            if (li != null) { li.BringIntoView(); await Settle(); await ClickVisual(li, 600); }
            double cx = a.GetProperty("at")[0].GetDouble(), cy = a.GetProperty("at")[1].GetDouble();
            var cr = Bounds(canvas);
            _ghost = name;
            await MoveTo(new Point(cr.X + cx, cr.Y + cy), Ms(a, 1000));
            _ghost = null;
            _clickT = Now(); _clickP = _cur;
            vm.AddNodeCommand.Execute(pi);
            var last = vm.Nodes.LastOrDefault();
            if (last != null && last.Name == pi.DisplayName) { last.X = cx - 60; last.Y = cy - 20; }
            if (a.TryGetProperty("name", out var nn) && last != null) last.Name = nn.GetString()!;
            await Settle();
            SeqRefreshEdges();
        }

        /// <summary>노드 우클릭 → "연결 시작" → 대상 노드 클릭 (StartConnection · CompleteConnection 명령).</summary>
        private async Task SeqConnectAsync(JsonElement a)
        {
            var vm = SeqVm();
            var from = SeqNode(a.GetProperty("from").GetString()!);
            var to = SeqNode(a.GetProperty("to").GetString()!);
            if (vm == null || from == null || to == null) { Log("  ⚠ seqConnect: 노드 없음"); return; }
            var fe = SeqNodeEl(from); var te = SeqNodeEl(to);
            if (fe == null || te == null) { Log("  ⚠ seqConnect: 노드 요소 없음"); return; }
            await ClickVisual(fe, Ms(a, 600));
            foreach (var n in vm.Nodes) n.IsSelected = false;
            from.IsSelected = true; vm.SelectedNode = from;
            if (fe.ContextMenu != null)
            {
                var mv = new MenuView { Context = fe.ContextMenu, Owner = fe, TopLeft = new Point(_cur.X + 2, _cur.Y + 2), Width = 200,
                    Items = fe.ContextMenu.Items.Cast<object>().ToList() };
                _menu = mv; _menuHover = -1;
                await Task.Delay(a.TryGetProperty("show", out var shw) ? (int)(shw.GetDouble() * 1000) : 500);
                var lay = MenuLayout();
                int idx = lay.FindIndex(l => l.Item is MenuItem m && HeaderText(m) == "연결 시작");
                if (idx >= 0)
                {
                    await MoveTo(new Point(lay[idx].R.X + 60, lay[idx].R.Y + lay[idx].R.Height / 2), 450);
                    _menuHover = idx; await Task.Delay(350);
                    _clickT = Now(); _clickP = _cur; await Task.Delay(180);
                }
                _menu = null; _menuHover = -1;
            }
            vm.StartConnectionCommand.Execute(from);
            await Settle();
            if (a.TryGetProperty("pause", out var pz)) await Task.Delay((int)(pz.GetDouble() * 1000));
            var r0 = Bounds(fe);
            _lineBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xD7, 0x00));
            _lineFrom = new Point(r0.X + r0.Width / 2, r0.Y + r0.Height / 2);
            var r1 = Bounds(te);
            await MoveTo(new Point(r1.X + r1.Width / 2, r1.Y + r1.Height / 2), Ms(a, 900));
            await Task.Delay(150);
            _clickT = Now(); _clickP = _cur;
            _lineFrom = null;
            vm.CompleteConnectionCommand.Execute(to);
            await Settle();
            SeqRefreshEdges();
            Log($"  seqConnect {from.Name} → {to.Name}: 연결 {vm.Edges.Count}개 · {vm.StatusMessage}");
        }

        private static Brush LineBrushOf(ConnectionType type) => type switch
        {
            ConnectionType.Coordinates => new SolidColorBrush(Color.FromRgb(0x3B, 0x82, 0xF6)),
            ConnectionType.Result => Accent,
            _ => new SolidColorBrush(Color.FromRgb(0x22, 0xC5, 0x5E)),
        };

        private async Task ConnectAsync(JsonElement a)
        {
            var from = Tool(a.GetProperty("from").GetString()!);
            var to = Tool(a.GetProperty("to").GetString()!);
            var type = Enum.Parse<ConnectionType>(a.GetProperty("type").GetString()!);
            _lineBrush = LineBrushOf(type);
            var fn = NodeOf(from); var tn = NodeOf(to);
            if (fn != null) await ClickVisual(fn, 600);
            _lineFrom = _cur;
            if (tn != null) { var r = Bounds(tn); await MoveTo(new Point(r.X + r.Width / 2, r.Y + r.Height / 2), Ms(a, 900)); }
            _clickT = Now(); _clickP = _cur;
            _lineFrom = null;
            _vm.AddConnection(from, to, type);
            await Settle();
        }

        /// <summary>
        /// 우클릭 메뉴 Connect … 로 시작한 연결 모드(MainView 의 안내 문구가 뜬 상태)에서 대상 도구를 클릭해 연결을 끝낸다 —
        /// 제품의 Item_MouseLeftButtonDown → TryCompleteConnection 과 같은 경로. 임시 선은 녹화기가 그린다.
        /// </summary>
        private async Task ConnectToAsync(JsonElement a)
        {
            var to = Tool(a.GetProperty("to").GetString()!);
            var mv = (FrameworkElement)_win;
            var srcF = mv.GetType().GetField("_connectionSourceTool", BindingFlags.NonPublic | BindingFlags.Instance);
            var typeF = mv.GetType().GetField("_pendingConnectionType", BindingFlags.NonPublic | BindingFlags.Instance);
            var done = mv.GetType().GetMethod("TryCompleteConnection", BindingFlags.NonPublic | BindingFlags.Instance);
            if (srcF?.GetValue(mv) is not ToolItem from || typeF == null || done == null) { Log("  ⚠ connectTo: 연결 모드가 아님"); return; }
            _lineBrush = LineBrushOf((ConnectionType)typeF.GetValue(mv)!);
            var fn = NodeOf(from); var tn = NodeOf(to);
            if (fn != null) { var r0 = Bounds(fn); _lineFrom = new Point(r0.X + r0.Width / 2, r0.Y + r0.Height / 2); }
            if (tn != null) { var r = Bounds(tn); await MoveTo(new Point(r.X + r.Width / 2, r.Y + r.Height / 2), Ms(a, 900)); }
            await Task.Delay(150);
            _clickT = Now(); _clickP = _cur;
            _lineFrom = null;
            done.Invoke(mv, new object[] { to });
            await Settle();
            Log($"  connectTo {from.Name} → {to.Name}: 연결 {_vm.Connections.Count}개");
        }

        private void SetProp(object target, string name, JsonElement v)
        {
            var p = target.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (p == null || !p.CanWrite) { Log($"  속성 없음: {target.GetType().Name}.{name}"); return; }
            var pt = Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType;
            object? val = pt switch
            {
                var t when v.ValueKind == JsonValueKind.Null => null,
                var t when t == typeof(bool) => v.GetBoolean(),
                var t when t == typeof(int) => v.GetInt32(),
                var t when t == typeof(double) => v.GetDouble(),
                var t when t == typeof(string) => v.GetString(),
                var t when t.IsEnum => Enum.Parse(t, v.GetString()!),
                _ => "\u0001",
            };
            if (val is string s0 && s0 == "\u0001") { Log($"  형식 미지원: {name} {p.PropertyType}"); return; }
            p.SetValue(target, val);
        }

        // ───────────────────────── 좌표 · 찾기 ─────────────────────────

        /// <summary>요소의 메인 창 기준 사각형 — 다른 창 안의 요소면 그 창의 위치를 더한다.</summary>
        private Rect Bounds(FrameworkElement fe)
        {
            var w = Window.GetWindow(fe);
            if (w == null || ReferenceEquals(w, _win))
                return fe.TransformToAncestor(Root).TransformBounds(new Rect(0, 0, fe.ActualWidth, fe.ActualHeight));
            var content = (Visual)w.Content;
            var r = fe.TransformToAncestor(content).TransformBounds(new Rect(0, 0, fe.ActualWidth, fe.ActualHeight));
            var o = OffsetOf(w);
            r.Offset(o.X, o.Y);
            return r;
        }

        private static string? ToolTipText(FrameworkElement fe) => fe.ToolTip switch
        {
            string s => s,
            ToolTip tt when tt.Content is string s2 => s2,
            TextBlock tb => tb.Text,
            null => null,
            var o => o.ToString(),
        };

        private Image? DisplayImage() =>
            (Find("name:ImageCanvasControl") as FrameworkElement)?.FindName("DisplayImage") as Image;

        private Point ImagePoint(double x, double y)
        {
            var img = DisplayImage();
            return img != null ? img.TranslatePoint(new Point(x, y), Root) : new Point(x, y);
        }

        private Rect ImageRect(double x, double y, double w, double h) => new(ImagePoint(x, y), ImagePoint(x + w, y + h));

        /// <summary>
        /// 이미지 확대 — {"op":"zoom","img":[x,y,w,h],"fill":0.8,"ms":700} 은 그 영역이 화면을 fill 만큼 채우도록
        /// 확대하고 가운데로 옮긴다. {"op":"zoom","fit":true} 는 전체 보기. 작은 코드 · 글자를 영상에서 읽히게 하려는 것.
        /// </summary>
        private async Task ZoomAsync(JsonElement a)
        {
            var ic = Find("name:ImageCanvasControl");
            var sv = ic?.FindName("ImageScrollViewer") as ScrollViewer;
            var setZoom = ic?.GetType().GetMethod("SetZoom", BindingFlags.NonPublic | BindingFlags.Instance);
            var zf = ic?.GetType().GetField("_zoomLevel", BindingFlags.NonPublic | BindingFlags.Instance);
            if (ic == null || sv == null || setZoom == null || zf == null) { Log("  zoom: 이미지 캔버스를 찾지 못함"); return; }
            double z0 = (double)zf.GetValue(ic)!;
            double vw = sv.ViewportWidth, vh = sv.ViewportHeight;
            double cx0 = (sv.HorizontalOffset + vw / 2) / z0, cy0 = (sv.VerticalOffset + vh / 2) / z0;
            double z1, cx1, cy1;
            if (a.TryGetProperty("fit", out var fit) && fit.GetBoolean())
            {
                var img = DisplayImage();
                double iw = img?.Source?.Width ?? vw, ih = img?.Source?.Height ?? vh;
                z1 = Math.Min(vw / iw, vh / ih) * 0.9; cx1 = iw / 2; cy1 = ih / 2;
            }
            else
            {
                var r = a.GetProperty("img");
                double x = r[0].GetDouble(), y = r[1].GetDouble(), w = r[2].GetDouble(), h = r[3].GetDouble();
                double fill = a.TryGetProperty("fill", out var f) ? f.GetDouble() : 0.8;
                z1 = Math.Clamp(Math.Min(vw / w, vh / h) * fill, 0.1, 5.0); cx1 = x + w / 2; cy1 = y + h / 2;
            }
            int steps = Math.Max(1, Ms(a, 700) / 33);
            for (int i = 1; i <= steps; i++)
            {
                double k = (double)i / steps, e = k < .5 ? 2 * k * k : 1 - Math.Pow(-2 * k + 2, 2) / 2;
                double z = z0 + (z1 - z0) * e, cx = cx0 + (cx1 - cx0) * e, cy = cy0 + (cy1 - cy0) * e;
                setZoom.Invoke(ic, new object[] { z });
                sv.UpdateLayout();
                sv.ScrollToHorizontalOffset(cx * z - vw / 2);
                sv.ScrollToVerticalOffset(cy * z - vh / 2);
                await Task.Delay(33);
            }
            await Settle();
        }

        private void ZoomFit()
        {
            var ic = Find("name:ImageCanvasControl");
            ic?.GetType().GetMethod("ZoomToFit", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(ic, null);
        }

        /// <summary>
        /// 대상 찾기 — 위에 뜬 창부터 메인 창 순서로 찾는다.
        /// name:X(x:Name) · tip:X(ToolTip 글자) · tipp:X(ToolTip 앞부분) · text:X(보이는 글자, #n 으로 n번째) · textp:X(글자 앞부분) ·
        /// param:X(ParameterName) · tool:X(작업 공간 도구 노드) · auto:X(AutomationProperties.Name) · cmd:X(VM 명령에 묶인 버튼) ·
        /// row:X(표의 행) · menu:X(메뉴 바 항목) · li:X(그 글자가 든 목록 항목) · tvi:X(그 글자가 든 트리 항목) ·
        /// combo:n · textbox:n · grid:n · gridrow:n(보이는 n번째) · dgrow:g|X(g번째 표의 X 행) · close:(맨 위 창의 닫기 버튼)
        /// </summary>
        private FrameworkElement? Find(string spec, bool quiet = false)
        {
            int idx = 0;
            int hash = spec.LastIndexOf('#');
            if (hash > 0 && int.TryParse(spec[(hash + 1)..], out var n)) { idx = n; spec = spec[..hash]; }
            int colon = spec.IndexOf(':');
            string kind = spec[..colon], key = spec[(colon + 1)..];
            if (kind == "tool")
            {
                var ti = _vm.DroppedTools.FirstOrDefault(t => t.Name == key);
                if (ti == null) { if (!quiet) Log("  찾기 실패 " + spec + ": 도구 없음"); return null; }
                return NodeOf(ti);
            }
            var roots = OverWindows().AsEnumerable().Reverse().Select(w => (DependencyObject)w.Content).Append(Root).ToList();
            foreach (var root in roots)
            {
                var hit = FindIn(root, kind, key, idx);
                if (hit != null) return hit;
            }
            if (!quiet && (kind == "text" || kind == "textp" || kind == "name"))
            {
                var raw = roots.SelectMany(r => FindAll<FrameworkElement>(r)).Where(e => MatchText(e, kind, key)).ToList();
                Log($"  찾기 실패 {spec}: 후보 {raw.Count} — " + string.Join(", ", raw.Take(4).Select(r => $"{r.GetType().Name} w={r.ActualWidth:F0} vis={r.IsVisible}")));
            }
            else if (!quiet) Log($"  찾기 실패 {spec}");
            return null;
        }

        private static bool MatchText(FrameworkElement e, string kind, string key) => kind switch
        {
            "name" => e.Name == key,
            "text" => (e is TextBlock tb && (tb.Text == key || InlineText(tb) == key)) || (e is ContentControl cc && cc.Content is string s && s == key),
            "textp" => e is TextBlock tb2 && (tb2.Text.StartsWith(key, StringComparison.Ordinal) || InlineText(tb2).StartsWith(key, StringComparison.Ordinal)),
            _ => false,
        };

        private FrameworkElement? FindIn(DependencyObject root, string kind, string key, int idx)
        {
            bool Vis(FrameworkElement r) => r.ActualWidth > 0 && IsShown(r);
            switch (kind)
            {
                case "combo": return FindAll<ComboBox>(root).Where(Vis).ElementAtOrDefault(int.TryParse(key, out var ci) ? ci : 0);
                case "textbox": return FindAll<TextBox>(root).Where(Vis).ElementAtOrDefault(int.TryParse(key, out var xi) ? xi : 0);
                case "grid": return FindAll<DataGrid>(root).Where(Vis).ElementAtOrDefault(int.TryParse(key, out var gdi) ? gdi : 0);
                case "gridrow": return FindAll<DataGridRow>(root).Where(Vis).ElementAtOrDefault(int.TryParse(key, out var gi) ? gi : 0);
                case "dgrow":
                {
                    var parts = key.Split('|', 2);
                    var grid = FindAll<DataGrid>(root).Where(Vis).ElementAtOrDefault(int.Parse(parts[0]));
                    return grid == null ? null : FindAll<DataGridRow>(grid).FirstOrDefault(r => Vis(r) && FindAll<TextBlock>(r).Any(t => t.Text == parts[1]));
                }
                case "row": return FindAll<DataGridRow>(root).Where(r => Vis(r) && FindAll<TextBlock>(r).Any(t => t.Text == key)).ElementAtOrDefault(idx);
                case "li": return FindAll<ListBoxItem>(root).Where(r => Vis(r) && FindAll<TextBlock>(r).Any(t => t.Text == key)).ElementAtOrDefault(idx);
                case "tvi":   // 머리글 글자가 그것인 트리 항목 — 머리글 줄(PART_Header)만 대상
                {
                    var tvi = FindAll<TreeViewItem>(root).Where(r => Vis(r)).FirstOrDefault(r =>
                        (r.Header is string hs && hs == key) || (r.Template?.FindName("PART_Header", r) is FrameworkElement ph && FindAll<TextBlock>(ph).Any(t => t.Text == key || InlineText(t) == key)));
                    if (tvi?.Template?.FindName("PART_Header", tvi) is FrameworkElement hdr && hdr.ActualWidth > 0) return hdr;
                    return tvi;
                }
                case "menu": return FindAll<MenuItem>(root).Where(Vis).FirstOrDefault(m => HeaderText(m) == key);
                case "link":       // 판정 기준 옆 Web 파라미터 연동 콤보 (link:ExpectedCount → SelectedExpectedCountCode 에 묶인 것)
                case "linkbadge":  // 그 아래 연동 상태 배지
                {
                    var pcl = FindAll<Controls.ParamCodeLink>(root).Where(Vis).FirstOrDefault(c =>
                        BindingOperations.GetBinding(c, Controls.ParamCodeLink.SelectedItemProperty)?.Path.Path == "Selected" + key + "Code");
                    if (pcl == null) return null;
                    return kind == "link" ? FindAll<ComboBox>(pcl).FirstOrDefault() : pcl.FindName("LinkBadge") as FrameworkElement;
                }
                case "help":       // 도움말 ? — help:BlobTool(도구 설명) · help:BlobTool.ExpectedCount(파라미터)
                {
                    var parts = key.Split('.', 2);
                    return FindAll<Controls.HelpIcon>(root).Where(Vis).ElementAtOrDefault(0) is null ? null
                        : FindAll<Controls.HelpIcon>(root).Where(Vis).Where(h => h.ToolType == parts[0] && (parts.Length == 1 ? string.IsNullOrEmpty(h.ParameterName) : h.ParameterName == parts[1])).ElementAtOrDefault(idx);
                }
                case "snode": return FindAll<FrameworkElement>(root).FirstOrDefault(e => e.Name == "NodeBorder" && e.DataContext is SequenceNodeItem sn && sn.Name == key && Vis(e));   // 시퀀스 캔버스 노드
                case "close": return FindAll<ButtonBase>(root).Where(Vis).FirstOrDefault(b => ReferenceEquals(b.Command, SystemCommands.CloseWindowCommand));   // 맨 위 창의 제목 표시줄 닫기 버튼
            }
            IEnumerable<FrameworkElement> all = FindAll<FrameworkElement>(root).Where(Vis);
            Func<FrameworkElement, bool> pred = kind switch
            {
                "name" or "text" or "textp" => e => MatchText(e, kind, key),
                "tip" => e => ToolTipText(e) == key,
                "tipp" => e => ToolTipText(e)?.StartsWith(key, StringComparison.Ordinal) == true,
                "auto" => e => System.Windows.Automation.AutomationProperties.GetName(e) == key,
                "param" => e => e.GetType().GetProperty("ParameterName")?.GetValue(e) as string == key,
                "cmd" => e => e is ButtonBase b && b.Command != null && IsCommandNamed(b, key),
                _ => _ => false,
            };
            var hits = all.Where(pred).ToList();
            if (kind == "text" || kind == "textp")   // 글자 TextBlock 이 버튼 · 탭 안에 있으면 그 컨트롤을 대상으로
                hits = hits.Select(h => (FrameworkElement)(FindAncestor<ButtonBase>(h) ?? (FrameworkElement?)FindAncestor<TabItem>(h) ?? h)).Distinct().ToList();
            return idx < hits.Count ? hits[idx] : null;
        }

        private bool IsCommandNamed(ButtonBase b, string name)
        {
            foreach (var src in new[] { b.DataContext, _vm })
                if (src != null && ReferenceEquals(src.GetType().GetProperty(name)?.GetValue(src), b.Command)) return true;
            return false;
        }

        private static string InlineText(TextBlock tb) =>
            string.Concat(tb.Inlines.OfType<System.Windows.Documents.Run>().Select(r => r.Text)).Trim();

        /// <summary>IsVisible 은 화면 밖 창에서 늦게 갱신될 때가 있어 조상 Visibility 를 직접 본다.</summary>
        private static bool IsShown(FrameworkElement e)
        {
            DependencyObject? d = e;
            while (d != null)
            {
                if (d is UIElement u && (u.Visibility != Visibility.Visible || u.Opacity <= 0.01)) return false;
                d = VisualTreeHelper.GetParent(d);
            }
            return true;
        }

        private static T? FindAncestor<T>(DependencyObject d) where T : DependencyObject
        {
            var p = VisualTreeHelper.GetParent(d);
            while (p != null && p is not T) p = VisualTreeHelper.GetParent(p);
            return p as T;
        }

        private static List<T> FindAll<T>(DependencyObject root) where T : DependencyObject
        {
            var list = new List<T>();
            var stack = new Stack<DependencyObject>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                var d = stack.Pop();
                if (d is T t) list.Add(t);
                int c = d is Visual || d is System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetChildrenCount(d) : 0;
                for (int i = c - 1; i >= 0; i--) stack.Push(VisualTreeHelper.GetChild(d, i));
            }
            return list;
        }

        /// <summary>시나리오 작성용 — 보이는 버튼 · 글자 · 이름 붙은 요소의 위치를 로그에 남긴다.</summary>
        private void Dump(string? filter)
        {
            var roots = OverWindows().AsEnumerable().Reverse().Select(w => (DependencyObject)w.Content).Append(Root);
            foreach (var root in roots)
                foreach (var e in FindAll<FrameworkElement>(root).Where(e => e.ActualWidth > 0 && IsShown(e)))
                {
                    string? label = e switch
                    {
                        TextBlock tb => "text=" + (string.IsNullOrEmpty(tb.Text) ? InlineText(tb) : tb.Text),
                        ButtonBase b => $"button cmd={(b.Command != null)}{(b.IsEnabled ? "" : " 비활성")} auto={System.Windows.Automation.AutomationProperties.GetName(b)} tip={ToolTipText(b)}",
                        TextBox tbx => $"textbox={tbx.Text} tip={ToolTipText(tbx)}",
                        ComboBox cbx => $"combo={cbx.SelectedItem} n={cbx.Items.Count}",
                        _ when !string.IsNullOrEmpty(e.Name) => "name=" + e.Name,
                        _ => null,
                    };
                    if (label == null || (filter != null && !label.Contains(filter, StringComparison.OrdinalIgnoreCase))) continue;
                    var r = Bounds(e);
                    Log($"  dump {e.GetType().Name} [{r.X:F0},{r.Y:F0},{r.Width:F0},{r.Height:F0}] {label.Replace('\n', ' ')}");
                }
        }

        /// <summary>
        /// 녹화용 대화상자 서비스 — 메시지 창 · 레시피 관리자 등은 원래 서비스 그대로, 파일 열기 · 폴더 · 저장은 시나리오가 준 경로를 돌려준다
        /// (OS 대화상자는 실제 화면에 떠서 녹화에 담기지 않는다). 준 경로가 없으면 대화상자를 띄우지 않고 취소로 처리한다.
        /// </summary>
        private sealed class RecordingDialogService : Interfaces.IDialogService
        {
            private readonly Interfaces.IDialogService _inner;
            private readonly Action<string> _log;
            public readonly Queue<string> Saves = new(), Opens = new(), Folders = new();
            public RecordingDialogService(Interfaces.IDialogService inner, Action<string> log) { _inner = inner; _log = log; }
            public void ShowInformation(string message, string title) => _inner.ShowInformation(message, title);
            public void ShowWarning(string message, string title) => _inner.ShowWarning(message, title);
            public void ShowError(string message, string title) => _inner.ShowError(message, title);
            public bool ShowConfirmation(string message, string title) => _inner.ShowConfirmation(message, title);
            public string? ShowOpenFileDialog(string title, string filter)
            {
                if (Opens.Count == 0) { _log("  ⚠ 열기 대화상자 경로 없음 — 취소로 처리"); return null; }
                var p = Opens.Dequeue();
                _log("  열기 대화상자 대신 경로 주입: " + p);
                return p;
            }
            public string[]? ShowOpenFilesDialog(string title, string filter)
            {
                if (Opens.Count == 0) { _log("  ⚠ 열기 대화상자 경로 없음 — 취소로 처리"); return null; }
                var p = Opens.Dequeue();
                _log("  열기(여러 파일) 대화상자 대신 경로 주입: " + p);
                return p.Split('|');
            }
            public string? ShowFolderBrowserDialog(string description)
            {
                if (Folders.Count == 0) { _log("  ⚠ 폴더 대화상자 경로 없음 — 취소로 처리"); return null; }
                var p = Folders.Dequeue();
                _log("  폴더 대화상자 대신 경로 주입: " + p);
                return p;
            }
            public string? ShowSaveFileDialog(string filter, string defaultExt, string? fileName = null)
            {
                if (Saves.Count == 0) { _log("  ⚠ 저장 대화상자 경로 없음 — 취소로 처리"); return null; }
                var p = Saves.Dequeue();
                if (fileName != null && (Directory.Exists(p) || System.IO.Path.GetExtension(p).Length == 0))
                {
                    Directory.CreateDirectory(p);
                    p = System.IO.Path.Combine(p, fileName + (System.IO.Path.HasExtension(fileName) ? "" : defaultExt));
                }
                _log($"  저장 대화상자 대신 경로 주입: {p} (제안 이름 {fileName})");
                return p;
            }
            public string? ShowRenameDialog(string currentName) => _inner.ShowRenameDialog(currentName);
            public void ShowCameraManagerDialog() => _inner.ShowCameraManagerDialog();
            public Recipe? ShowRecipeManagerDialog()
            {
                // DialogService.ShowRecipeManagerDialog 와 같되, 창 안의 Import · Export 가 이 래퍼(경로 주입)를 쓰게 한다
                var t = _inner.GetType();
                object? F(string n) => t.GetField(n, BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(_inner);
                if (F("_recipeService") is not Interfaces.IRecipeService rs || F("_cameraService") is not Interfaces.ICameraService cs)
                    return _inner.ShowRecipeManagerDialog();
                Recipe? loaded = null;
                var window = new Views.Recipe.RecipeManagerWindow(rs, cs, this, F("_parameterSyncService") as VMS.Core.Interfaces.IParameterSyncService)
                { Owner = Application.Current.MainWindow };
                window.RecipeLoaded += (_, r) => loaded = r;
                window.ShowDialog();
                return loaded;
            }
            private object? Inner(string n) => _inner.GetType().GetField(n, BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(_inner);
            public void ShowSequenceEditorDialog(IEnumerable<Services.SequenceDeviceEntry>? extraDevices = null)
            {
                // DialogService 와 같되 창 안의 가져오기 · 내보내기가 이 래퍼(경로 주입)를 쓰게 한다
                if (Inner("_recipeService") is not Interfaces.IRecipeService rs || Inner("_cameraService") is not Interfaces.ICameraService cs)
                { _inner.ShowSequenceEditorDialog(extraDevices); return; }
                new Views.Sequence.SequenceEditorWindow(rs, cs, this, extraDevices) { Owner = Application.Current.MainWindow }.ShowDialog();
            }
            public void ShowCalibrationManagerDialog()
            {
                // 캘리브레이션 창의 Load Image... 가 이 래퍼(경로 주입)를 쓰게 한다
                if (Inner("_recipeService") is not Interfaces.IRecipeService rs) { _inner.ShowCalibrationManagerDialog(); return; }
                new Views.Calibration.CalibrationManagerWindow(rs, this) { Owner = Application.Current.MainWindow }.ShowDialog();
            }
            public RecipeTemplate? ShowTemplateGalleryDialog() => _inner.ShowTemplateGalleryDialog();
            public Dictionary<string, string>? ShowCameraRemapDialog(IReadOnlyList<(string oldId, int stepCount)> unregistered,
                IReadOnlyList<VMS.Camera.Models.CameraInfo> cameras) => _inner.ShowCameraRemapDialog(unregistered, cameras);
            public OpenCvSharp.Mat? ShowTrainMaskEditorDialog(OpenCvSharp.Mat templateImage, OpenCvSharp.Mat? existingMask,
                double cannyLow = 50, double cannyHigh = 150) => _inner.ShowTrainMaskEditorDialog(templateImage, existingMask, cannyLow, cannyHigh);
            public string? ShowModelRegistryPickerDialog(string? taskType) => _inner.ShowModelRegistryPickerDialog(taskType);
        }
    }
}
#endif
