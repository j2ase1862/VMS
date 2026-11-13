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
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Input;
using VMS.Camera.Interfaces;
using VMS.Camera.Models;
using VMS.ViewModels;

namespace VMS.Capture
{
    /// <summary>
    /// 사용 안내 동영상 녹화 (DEBUG 전용, "--record-video [폴더]") — VisionSetup 녹화 모드의 VMS 메인판.
    ///
    /// <para>창을 화면 밖에 띄우고(입력 장치·화면 캡처를 쓰지 않는다) 시나리오 JSON 의 동작을 VM · 비주얼 트리 수준으로
    /// 실행하면서, 메인 창 루트와 그 위에 열린 창(작업 창 · 메시지 창)을 RenderTargetBitmap 으로 합성해
    /// JPEG 프레임과 시각(timeline.json)을 남긴다. 커서 · 클릭 물결 · 강조 상자 · 툴팁은 프레임 위에 직접 그린다.</para>
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

        // 덧그림 상태
        private Point _cur = new(1100, 620);
        private double _clickT = -10;
        private Point _clickP;
        private List<Rect> _hl = new();
        private double _hlT0;
        private string? _tipText;
        private Rect _tipAnchor;
        // 드롭다운 메뉴(ContextMenu) — 화면 밖 창의 팝업은 그려지지 않으므로 항목 글자를 직접 그린다
        private ContextMenu? _menu;
        private Rect _menuAnchor;
        private int _menuHover = -1;
        // 파일 열기 · 저장 대화상자 대신 쓸 경로 (OS 대화상자는 화면 밖 녹화에 담기지 않는다)
        private RecordingDialogService? _dlg;

        private VideoRecorder(Window win, MainViewModel vm, string outDir, Dictionary<string, double> durs)
        {
            _win = win; _vm = vm; _out = outDir; _durs = durs;
            Directory.CreateDirectory(Path.Combine(outDir, "frames"));
            foreach (var f in Directory.GetFiles(Path.Combine(outDir, "frames"))) File.Delete(f);
            _log = new StreamWriter(Path.Combine(outDir, "_record.log"), false, new UTF8Encoding(false)) { AutoFlush = true };
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
            var hwnd = new System.Windows.Interop.WindowInteropHelper(w).Handle;
            Log($"  창 위치 보정({when}): {w.GetType().Name} ({w.Left:F0},{w.Top:F0} {ww:F0}x{wh:F0} {w.WindowStartupLocation} hwnd보임={(hwnd != IntPtr.Zero && IsWindowVisible(hwnd))})");
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
                    // 창 클라이언트 영역 = 창 템플릿 루트(여백 없음). 콘텐츠 자체는 Margin 이 있을 수 있어
                    // VisualBrush 원점이 어긋나므로 템플릿 루트를 그린다
                    var content = VisualTreeHelper.GetChildrenCount(w) > 0 && VisualTreeHelper.GetChild(w, 0) is FrameworkElement tr
                        ? tr : (FrameworkElement)w.Content;
                    Point o;
                    try { o = Root.PointFromScreen(content.PointToScreen(new Point(0, 0))); }
                    catch { o = OffsetOf(w); }
                    var r = new Rect(o.X, o.Y, content.ActualWidth, content.ActualHeight);
                    var dv = new DrawingVisual();
                    using (var dc = dv.RenderOpen())
                    {
                        // OS 제목 표시줄이 있는 창(WindowStyle 기본)은 RenderTargetBitmap 에 비클라이언트 영역이 안 담기므로
                        // Windows 밝은 테마 기본 모양(흰 바탕 · 검은 제목 · 최소화/최대화/닫기)으로 그린다
                        bool chrome = w.WindowStyle != WindowStyle.None;
                        var full = chrome ? new Rect(r.X, r.Y - 31, r.Width, r.Height + 31) : r;
                        // 창 그림자 — 화면 밖 창은 OS 그림자가 없어 경계가 묻힌다 (투명 창은 자체 그림자가 있다)
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
                        dc.DrawRectangle(w.Background ?? Brushes.Black, null, r);
                        // Viewbox 를 콘텐츠 배치 사각형으로 고정 — 그림자 효과가 여백 밖으로 번지는 창(메시지 창)도 좌표가 어긋나지 않게
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
                string path = Path.Combine(_out, "frames", name);
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
            // 커서 (화살표 24 → 30px)
            var g = Geometry.Parse("M4,2.5 L10.6,20.7 L13.2,13.3 L20.5,10.6 Z").Clone();
            g.Transform = new TransformGroup { Children = { new ScaleTransform(1.25, 1.25), new TranslateTransform(_cur.X - 5, _cur.Y - 3.5) } };
            dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(70, 0, 0, 0)), null, Shift(g, 1.5, 2));
            dc.DrawGeometry(new SolidColorBrush(Color.FromRgb(0x11, 0x11, 0x11)), new Pen(Brushes.White, 2) { LineJoin = PenLineJoin.Round }, g);
        }

        // ───────────────────────── 드롭다운 메뉴 ─────────────────────────

        private const double MenuItemH = 34, MenuSepH = 9, MenuW = 250;

        /// <summary>메뉴 항목 사각형 (메뉴가 열린 자리 기준, 구분선 포함 순서 그대로).</summary>
        private List<(object Item, Rect R)> MenuLayout()
        {
            var list = new List<(object, Rect)>();
            if (_menu == null) return list;
            double x = Math.Clamp(_menuAnchor.Right - MenuW, 8, W - MenuW - 8), y = _menuAnchor.Bottom + 2 + 4;
            foreach (var it in _menu.Items)
            {
                double h = it is Separator ? MenuSepH : MenuItemH;
                list.Add((it, new Rect(x + 4, y, MenuW - 8, h)));
                y += h;
            }
            return list;
        }

        private void DrawMenu(DrawingContext dc)
        {
            var lay = MenuLayout();
            if (lay.Count == 0) return;
            var box = new Rect(lay[0].R.X - 4, lay[0].R.Y - 4, MenuW, lay[^1].R.Bottom - lay[0].R.Y + 8);
            var sh = box; sh.Offset(0, 4);
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(80, 0, 0, 0)), null, sh, 4, 4);
            dc.DrawRectangle(_menu!.Background ?? new SolidColorBrush(Color.FromRgb(0x2A, 0x2F, 0x3A)),
                new Pen(_menu.BorderBrush ?? Brushes.Gray, 1), box);
            var fg = _menu.Foreground ?? Brushes.White;
            for (int i = 0; i < lay.Count; i++)
            {
                var (it, r) = lay[i];
                if (it is Separator)
                {
                    dc.DrawRectangle(_menu.BorderBrush ?? Brushes.Gray, null, new Rect(r.X + 6, r.Y + r.Height / 2, r.Width - 12, 1));
                    continue;
                }
                if (it is not MenuItem mi) continue;
                if (i == _menuHover)
                    dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(48, 0xFF, 0xFF, 0xFF)), null, r, 3, 3);
                if (mi.Icon is TextBlock ic)
                {
                    var g = new FormattedText(ic.Text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                        new Typeface(ic.FontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), ic.FontSize,
                        ic.Foreground ?? fg, 1.0);
                    dc.DrawText(g, new Point(r.X + 12, r.Y + (r.Height - g.Height) / 2));
                }
                var t = new FormattedText(mi.Header?.ToString() ?? "", CultureInfo.GetCultureInfo("ko-KR"), FlowDirection.LeftToRight,
                    new Typeface("Segoe UI"), 13, fg, 1.0);
                dc.DrawText(t, new Point(r.X + 42, r.Y + (r.Height - t.Height) / 2));
            }
        }

        private static Geometry Shift(Geometry g, double dx, double dy)
        {
            var c = g.Clone();
            c.Transform = new TransformGroup { Children = { g.Transform, new TranslateTransform(dx, dy) } };
            return c;
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
            File.WriteAllText(Path.Combine(_out, "timeline.json"), sb.ToString());
            Log($"frames {_frames.Count}, marks {_marks.Count}");
            // 녹화 중 연 창은 닫고 끝낸다 (모달이 남아 있으면 종료가 막힌다)
            foreach (var w in OverWindows()) { try { w.Close(); } catch { } }
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
            await Settle();
            await Task.Delay(500);
            if (root.TryGetProperty("setup", out var setup))
                foreach (var a in setup.EnumerateArray()) await DoAsync(a);
            await Settle();
            StartFrames();
            await Task.Delay(1200);
            foreach (var a in root.GetProperty("actions").EnumerateArray()) await DoAsync(a);
        }

        private CameraViewModel Cam(JsonElement a) =>
            _vm.Cameras[a.TryGetProperty("cam", out var c) ? c.GetInt32() : 0];

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
                        else Log("  tip: " + _tipText);
                        break;
                    }
                    case "tipClear": _tipText = null; break;
                    case "seedUser":
                    {
                        // 녹화 인스턴스 사용자 DB 에 계정 만들기 (준비 단계용) — admin 은 설정 마법사와 같은 SeedInitialAdmin
                        var us = typeof(MainViewModel).GetField("_userService", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(_vm);
                        string user = a.GetProperty("user").GetString()!, pw = a.GetProperty("password").GetString()!;
                        string name = a.TryGetProperty("name", out var nm) ? nm.GetString()! : user;
                        object? ok;
                        if (string.Equals(user, "admin", StringComparison.OrdinalIgnoreCase))
                            ok = us?.GetType().GetMethod("SeedInitialAdmin")?.Invoke(us, new object[] { pw, name });
                        else
                            ok = us?.GetType().GetMethod("CreateUser")?.Invoke(us, new object[] { user, pw, name,
                                Enum.Parse<Models.UserGrade>(a.TryGetProperty("grade", out var g2) ? g2.GetString()! : "Operator") });
                        Log($"  seedUser {user}: {ok}");
                        break;
                    }
                    case "login":
                    {
                        // 시스템 사용자 로그인 상태 주입 (로그인 창은 화면 가운데에 뜨므로 쓰지 않는다)
                        //   password 가 있으면 사용자 DB 로 실제 인증(감사 로그에도 남음), 없으면 admin 상태만 주입
                        var us = typeof(MainViewModel).GetField("_userService", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(_vm);
                        if (a.TryGetProperty("password", out var lpw))
                        {
                            var ok = us?.GetType().GetMethod("Authenticate")?.Invoke(us, new object[] { a.GetProperty("user").GetString()!, lpw.GetString()! });
                            typeof(MainViewModel).GetMethod("UpdateUserDisplay", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(_vm, null);
                            if (ok is true)
                                _vm.LogService?.Log($"User logged in: {(us?.GetType().GetProperty("CurrentUser")?.GetValue(us) as Models.User)?.DisplayName}",
                                    Interfaces.LogLevel.Success, "Auth");
                            Log($"  login(실제 인증) {a.GetProperty("user").GetString()}: {ok} → {_vm.CurrentUserDisplay}");
                            await Settle();
                            break;
                        }
                        var grade = Enum.Parse<Models.UserGrade>(a.TryGetProperty("grade", out var gr) ? gr.GetString()! : "Admin");
                        var user = new Models.User
                        {
                            Username = a.TryGetProperty("user", out var un) ? un.GetString()! : "admin",
                            DisplayName = a.TryGetProperty("name", out var dn) ? dn.GetString()! : "Administrator",
                            Grade = grade,
                            LastLoginAt = DateTime.Now,
                        };
                        us?.GetType().GetProperty("CurrentUser")?.SetValue(us, user);
                        typeof(MainViewModel).GetMethod("UpdateUserDisplay", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(_vm, null);
                        _vm.LogService?.Log($"User logged in: {user.DisplayName}", Interfaces.LogLevel.Success, "Auth");
                        await Settle();
                        break;
                    }
                    case "panel":
                    {
                        if (a.TryGetProperty("target", out var tg)) await ClickVisual(Find(tg.GetString()!));
                        _vm.IsSidePanelOpen = a.GetProperty("open").GetBoolean();
                        await Task.Delay(320);   // 펼침 애니메이션 0.2 s
                        await Settle();
                        break;
                    }
                    case "loadRecipe":
                    {
                        string name = a.GetProperty("name").GetString()!;
                        var info = _vm.RecipeList.FirstOrDefault(r => r.Name == name);
                        if (info == null)
                        {
                            Log($"  레시피 없음: {name} — 목록: {string.Join(", ", _vm.RecipeList.Select(r => r.Name))}");
                            break;
                        }
                        if (a.TryGetProperty("pick", out var pk))   // 목록 행 클릭 연출
                            await ClickVisual(Find(pk.GetString()!));
                        _vm.SelectedRecipeInfo = info;
                        await Settle();
                        if (a.TryGetProperty("target", out var tg)) await ClickVisual(Find(tg.GetString()!));
                        _vm.LoadRecipeCommand.Execute(null);
                        await Settle();
                        Log($"  recipe: {_vm.CurrentRecipeName}");
                        break;
                    }
                    case "selectRecipe":
                    {
                        string name = a.GetProperty("name").GetString()!;
                        var info = _vm.RecipeList.FirstOrDefault(r => r.Name == name);
                        if (a.TryGetProperty("target", out var tg)) await ClickVisual(Find(tg.GetString()!));
                        if (info != null) _vm.SelectedRecipeInfo = info; else Log("  레시피 없음: " + name);
                        await Settle();
                        break;
                    }
                    case "openImage":
                    {
                        var cam = Cam(a);
                        if (a.TryGetProperty("target", out var tg)) await ClickVisual(Find(tg.GetString()!));
                        cam.LoadImageFile(a.GetProperty("path").GetString()!);
                        await Settle();
                        break;
                    }
                    case "inspect":
                    {
                        var cam = Cam(a);
                        if (a.TryGetProperty("target", out var tg)) await ClickVisual(Find(tg.GetString()!));
                        await cam.ManualInspectCommand.ExecuteAsync(null);
                        await Settle();
                        Log($"  inspect: {(cam.InspectionOk ? "OK" : "NG")} | {cam.ResultMessage}");
                        break;
                    }
                    case "feedImages":
                    {
                        // AUTO RUN 시연 — 카메라 대신 이미지 파일을 차례로 내주는 취득기 (녹화 전용)
                        var cam = Cam(a);
                        var files = a.GetProperty("paths").EnumerateArray().Select(p => p.GetString()!).ToList();
                        var acq = new FileImageAcquisition(files);
                        await acq.ConnectAsync(new CameraInfo());
                        cam.SetAcquisitionForTest(acq);
                        cam.IsConnected = true;
                        await Settle();
                        Log($"  feedImages: {files.Count}장");
                        break;
                    }
                    case "trigger":
                    {
                        // 설비 트리거 신호 대신 시뮬레이션 PLC 비트를 켠다 — 사이클 한 번이 끝날 때까지 기다린다
                        string addr = a.TryGetProperty("address", out var ad) ? ad.GetString()! : "M100";
                        var sim = SimPlc();
                        if (sim == null) { Log("  시뮬레이션 PLC 없음"); break; }
                        int before = _vm.Dashboard.TotalCount;
                        sim.SimSetBit(addr.ToUpperInvariant(), true);
                        var sw = Stopwatch.StartNew();
                        while (_vm.Dashboard.TotalCount == before && sw.ElapsedMilliseconds < 8000) await Task.Delay(30);
                        sim.SimSetBit(addr.ToUpperInvariant(), false);
                        await Settle();
                        Log($"  trigger: total {before}→{_vm.Dashboard.TotalCount} ({sw.ElapsedMilliseconds} ms) {_vm.SystemStatus}");
                        break;
                    }
                    case "set":
                    {
                        // on: "cam" = 카메라 VM, "win" = 맨 위에 뜬 창의 DataContext, 없으면 메인 VM
                        string onv = a.TryGetProperty("on", out var on) ? on.GetString() ?? "" : "";
                        object target = onv == "cam" ? Cam(a) : onv == "win" ? (OverWindows().LastOrDefault()?.DataContext ?? _vm) : _vm;
                        if (a.TryGetProperty("target", out var stg)) await ClickVisual(Find(stg.GetString()!), Ms(a, 700));
                        foreach (var p in a.GetProperty("values").EnumerateObject())
                            SetProp(target, p.Name, p.Value);
                        await Settle();
                        break;
                    }
                    case "cmd":
                    {
                        // VM 의 ICommand 실행 — on:"cam" 이면 카메라 VM. 동기 명령은 디스패처로 넘겨 모달 창이 떠도 흐름이 막히지 않게 한다.
                        object target = a.TryGetProperty("on", out var on) && on.GetString() == "cam" ? Cam(a) : _vm;
                        if (a.TryGetProperty("target", out var tg)) await ClickVisual(Find(tg.GetString()!));
                        string name = a.GetProperty("name").GetString()!;
                        var c = target.GetType().GetProperty(name)?.GetValue(target) as ICommand;
                        if (c == null || !c.CanExecute(null)) { Log("  명령 실행 불가: " + name); break; }
                        if (c is IAsyncRelayCommand ac && !(a.TryGetProperty("modal", out var md) && md.GetBoolean()))
                            await ac.ExecuteAsync(null);
                        else
                            _ = _win.Dispatcher.BeginInvoke(() => c.Execute(null));
                        await Settle();
                        await Task.Delay(150);
                        await Settle();
                        break;
                    }
                    case "closeWindow":
                    {
                        var w = OverWindows().LastOrDefault();
                        if (a.TryGetProperty("target", out var tg)) await ClickVisual(Find(tg.GetString()!));
                        if (w != null) { w.Close(); Log("  closeWindow: " + w.GetType().Name); }
                        else Log("  닫을 창 없음");
                        await Settle();
                        break;
                    }
                    case "tab":
                    {
                        // TabControl 탭 고르기 — 머리글 글자로
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
                        if (fe is PasswordBox pb)
                        {
                            // 암호 칸 — 점으로 한 글자씩. 로그인 창은 마우스를 뗄 때 암호를 VM 에 옮기므로(LoginWindow.OnPreviewMouseLeftButtonUp)
                            // VM 에 Password 속성이 있으면 같은 값을 넣어 둔다. 사용자 관리 창은 버튼 Click 처리기가 옮긴다(click onclick).
                            pb.Password = "";
                            for (int i = 1; i <= text.Length; i++) { pb.Password = text[..i]; await Task.Delay(per); }
                            var pp = pb.DataContext?.GetType().GetProperty("Password");
                            if (pp != null && pp.CanWrite && pp.PropertyType == typeof(string)) pp.SetValue(pb.DataContext, text);
                            await Settle();
                            break;
                        }
                        if (fe is not TextBox tb) { Log("  글자 칸 아님: " + a.GetProperty("target").GetString()); break; }
                        tb.Text = "";
                        for (int i = 1; i <= text.Length; i++)
                        {
                            tb.Text = text[..i];
                            tb.CaretIndex = i;
                            await Task.Delay(per);
                        }
                        tb.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
                        await Settle();
                        break;
                    }
                    case "combo":
                    {
                        // 콤보 상자 항목 고르기 — 항목 글자(ToString / 표시 경로)로
                        var fe = Find(a.GetProperty("target").GetString()!);
                        await ClickVisual(fe, Ms(a, 700));
                        var cb = fe as ComboBox ?? (fe != null ? FindAncestor<ComboBox>(fe) : null);
                        if (cb == null) { Log("  콤보 아님: " + a.GetProperty("target").GetString()); break; }
                        string want = a.GetProperty("item").GetString()!;
                        object? hit = null;
                        foreach (var it in cb.Items)
                        {
                            string s = it?.ToString() ?? "";
                            if (!string.IsNullOrEmpty(cb.DisplayMemberPath))
                                s = it?.GetType().GetProperty(cb.DisplayMemberPath)?.GetValue(it)?.ToString() ?? s;
                            if (s == want) { hit = it; break; }
                        }
                        if (hit == null) Log($"  콤보 항목 없음: {want} — " + string.Join(", ", cb.Items.Cast<object>().Select(o => o?.ToString())));
                        else cb.SelectedItem = hit;
                        await Settle();
                        break;
                    }
                    case "touch":
                    {
                        // 파일을 같은 내용으로 다시 쓴다 — 외부 프로그램(VisionSetup)의 저장을 대신해 파일 감시를 깨운다
                        string p = a.GetProperty("path").GetString()!;
                        var bytes = File.ReadAllBytes(p);
                        File.WriteAllBytes(p, bytes);
                        File.SetLastWriteTime(p, DateTime.Now);
                        Log("  touch: " + p);
                        await Task.Delay(400);
                        await Settle();
                        break;
                    }
                    case "gridShow":
                    {
                        // 표(grid:n)에서 속성 값이 그 글자인 항목까지 스크롤 — 아래쪽에 숨은 행을 보이게 한다
                        var grid = Find(a.GetProperty("target").GetString()!) as DataGrid;
                        string want = a.GetProperty("text").GetString()!;
                        var item = grid?.Items.Cast<object>().FirstOrDefault(o => o != null && o.GetType().GetProperties()
                            .Any(p => p.GetIndexParameters().Length == 0 && p.PropertyType == typeof(string) && (p.GetValue(o) as string) == want));
                        if (grid == null || item == null) { Log("  gridShow 대상 없음: " + want); break; }
                        grid.ScrollIntoView(item);
                        await Settle();
                        break;
                    }
                    case "menu":
                    {
                        // 버튼의 드롭다운 메뉴(ContextMenu)를 그려 보이고 항목을 고른다 — 항목 명령은 디스패처로 넘겨 모달 창이 떠도 막히지 않게
                        var btn = Find(a.GetProperty("target").GetString()!);
                        if (btn?.ContextMenu == null) { Log("  메뉴 없음: " + a.GetProperty("target").GetString()); break; }
                        await ClickVisual(btn, Ms(a, 700));
                        _menu = btn.ContextMenu; _menuAnchor = Bounds(btn); _menuHover = -1;
                        await Task.Delay(a.TryGetProperty("show", out var shw) ? (int)(shw.GetDouble() * 1000) : 700);
                        if (!a.TryGetProperty("pick", out var pk)) break;   // 펼쳐 보이기만 (다음 menuClose 까지)
                        var lay = MenuLayout();
                        int idx = lay.FindIndex(l => l.Item is MenuItem m && m.Header?.ToString() == pk.GetString());
                        if (idx < 0) { Log("  메뉴 항목 없음: " + pk.GetString()); _menu = null; break; }
                        var r = lay[idx].R;
                        await MoveTo(new Point(r.X + 70, r.Y + r.Height / 2), 600);
                        _menuHover = idx;
                        await Task.Delay(450);
                        _clickT = Now(); _clickP = _cur;
                        await Task.Delay(220);
                        _menu = null; _menuHover = -1;
                        var mi = (MenuItem)lay[idx].Item;
                        var cmd = mi.Command;
                        if (cmd == null && BindingOperations.GetBinding(mi, MenuItem.CommandProperty) is { } bd)   // 열린 적 없는 메뉴는 DataContext 가 없다
                            cmd = _vm.GetType().GetProperty(bd.Path.Path)?.GetValue(_vm) as ICommand;
                        if (cmd == null || !cmd.CanExecute(mi.CommandParameter)) { Log("  메뉴 명령 실행 불가: " + pk.GetString()); break; }
                        var prm = mi.CommandParameter;
                        _ = _win.Dispatcher.BeginInvoke(() => cmd.Execute(prm));
                        await Settle();
                        await Task.Delay(200);
                        await Settle();
                        break;
                    }
                    case "menuClose": _menu = null; _menuHover = -1; break;
                    case "resizeWin":
                    {
                        // 맨 위 창 아래 가장자리를 끌어 높이를 키우는 연출 — 창 Height 를 커서와 함께 늘린다 (사용자가 테두리를 끄는 것과 같은 결과)
                        var w = OverWindows().LastOrDefault();
                        if (w == null) { Log("  ⚠ resizeWin 창 없음"); break; }
                        double h0 = w.ActualHeight, h1 = a.GetProperty("h").GetDouble();
                        int ms = Ms(a, 900);
                        var o = OffsetOf(w);
                        double cx = o.X + ((FrameworkElement)w.Content).ActualWidth / 2;
                        double top = w.Top - _win.Top;
                        await MoveTo(new Point(cx, top + h0 - 2), 600);
                        await Task.Delay(150);
                        _clickT = Now(); _clickP = _cur;
                        var sw = Stopwatch.StartNew();
                        while (sw.ElapsedMilliseconds < ms)
                        {
                            double k = sw.ElapsedMilliseconds / (double)ms;
                            double e = k < .5 ? 2 * k * k : 1 - Math.Pow(-2 * k + 2, 2) / 2;
                            w.Height = h0 + (h1 - h0) * e;
                            _cur = new Point(cx, top + w.Height - 2);
                            await Task.Delay(15);
                        }
                        w.Height = h1;
                        _cur = new Point(cx, top + h1 - 2);
                        await Settle();
                        Log($"  resizeWin: {w.GetType().Name} {h0:F0} → {h1:F0}");
                        break;
                    }
                    case "fileDialog":
                    {
                        // 다음 파일 저장 · 열기 대화상자 대신 돌려줄 경로 — MainViewModel 의 IDialogService 를 녹화용 래퍼로 감싼다
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
                        break;
                    }
                    case "saveAs":
                    {
                        // 맨 위 창의 [Run Backup...] · [Run Export...] — VM 이 직접 띄우는 OS 저장 대화상자 대신 경로를 주고
                        // 같은 서비스 호출 · 같은 결과 표시를 한다 (BackupRestoreViewModel.RunBackup · SupportPackageViewModel.RunExport 와 동일)
                        var dc = OverWindows().LastOrDefault()?.DataContext;
                        if (a.TryGetProperty("target", out var tg)) await ClickVisual(Find(tg.GetString()!), Ms(a, 700));
                        string dir = a.GetProperty("dir").GetString()!;
                        Directory.CreateDirectory(dir);
                        var root = VMS.Camera.Configuration.AppDataPaths.Root;
                        if (dc is BackupRestoreViewModel b)
                        {
                            var p = Path.Combine(dir, b.BackupFileName);
                            var r = VMS.Core.Backup.BackupRestoreService.CreateBackup(root, p,
                                new VMS.Core.Backup.BackupOptions { IncludeAudit = b.IncludeAudit, ProductVersion = b.ProductVersion });
                            if (r.Success)
                            {
                                b.BackupStatus = "Success"; b.BackupStatusColor = "#10B981";
                                b.LastBackupSummary = $"{r.FileCount} 파일 · {r.BytesWritten / 1024.0:F1} KB → {r.ZipPath}";
                            }
                            else { b.BackupStatus = "Failed"; b.BackupStatusColor = "#EF4444"; b.LastBackupSummary = r.ErrorMessage ?? "(원인 불명)"; }
                            Log($"  saveAs(백업, 대화상자 대신 경로 주입) {p}: {b.BackupStatus} {b.LastBackupSummary}");
                        }
                        else if (dc is SupportPackageViewModel s)
                        {
                            var p = Path.Combine(dir, s.DefaultFileName);
                            var r = VMS.Core.SupportPackage.SupportPackageService.Export(root, p, new VMS.Core.SupportPackage.SupportPackageOptions
                            {
                                AuditDays = s.AuditDays, IncludeEnvironment = s.IncludeEnvironment, IncludeBackupIndex = s.IncludeBackupIndex,
                                IncludeMachineName = s.IncludeMachineName, ProductVersion = s.ProductVersion,
                            });
                            if (r.Success)
                            {
                                s.Status = "Success"; s.StatusColor = "#10B981";
                                s.SummaryLine = $"{r.FileCount} 항목 · {r.BytesWritten / 1024.0:F1} KB → {r.ZipPath}";
                            }
                            else { s.Status = "Failed"; s.StatusColor = "#EF4444"; s.SummaryLine = r.ErrorMessage ?? "(원인 불명)"; }
                            Log($"  saveAs(지원 패키지, 대화상자 대신 경로 주입) {p}: {s.Status} {s.SummaryLine}");
                        }
                        else Log("  ⚠ saveAs 대상 창 아님: " + dc?.GetType().Name);
                        await Settle();
                        break;
                    }
                    case "scrollTo": Find(a.GetProperty("target").GetString()!)?.BringIntoView(); await Settle(); break;
                    case "dump": Dump(a.TryGetProperty("filter", out var fl) ? fl.GetString() : null); break;
                    case "log": Log("  status: " + _vm.SystemStatus); break;
                    case "logTail":
                        foreach (var en in (_vm.LogService?.LogEntries.TakeLast(a.TryGetProperty("n", out var ln) ? ln.GetInt32() : 12) ?? Enumerable.Empty<Interfaces.LogEntry>()).ToList())
                            Log($"  syslog [{en.Source}] {en.Message}");
                        break;
                    case "scrollLog":
                    {
                        // System Log 를 맨 아래(최신)로 — 목록이 새 줄을 따라 내려가지 않아 사용자가 스크롤하는 동작
                        var lb = Find("name:LogListBox") as ListBox;
                        if (lb == null || lb.Items.Count == 0) break;
                        if (a.TryGetProperty("move", out var mv) && mv.GetBoolean()) await MoveTo(Center(Bounds(lb)), 600);
                        lb.UpdateLayout();
                        lb.ScrollIntoView(lb.Items[^1]);
                        await Settle();
                        FindAll<ScrollViewer>(lb).FirstOrDefault()?.ScrollToEnd();
                        await Settle();
                        break;
                    }
                    default: Log("알 수 없는 op " + op); break;
                }
            }
            catch (Exception ex) { Log($"op {op} 실패: {ex}"); }
            if (op != "seg" && op != "wait" && op != "move" && op != "hl" && op != "hlClear" && op != "tip" && op != "tipClear")
                Log($"  {op}: {_vm.SystemStatus}");
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        private static int Ms(JsonElement a, int def) => a.TryGetProperty("ms", out var m) ? m.GetInt32() : def;
        private static Point Center(Rect r) => new(r.X + r.Width / 2, r.Y + r.Height / 2);

        private async Task Settle()
        {
            for (int i = 0; i < 2; i++)
            {
                _win.UpdateLayout();
                foreach (var w in OverWindows()) w.UpdateLayout();
                await _win.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            }
        }

        private VMS.PLC.Services.SimulatedPlcConnection? SimPlc()
        {
            object? plc = typeof(MainViewModel).GetField("_plcConnection", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(_vm);
            for (int i = 0; i < 3 && plc != null && plc is not VMS.PLC.Services.SimulatedPlcConnection; i++)
                plc = plc.GetType().GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
                    .FirstOrDefault(f => typeof(VMS.PLC.Interfaces.IPlcConnection).IsAssignableFrom(f.FieldType))?.GetValue(plc);
            return plc as VMS.PLC.Services.SimulatedPlcConnection;
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
            bool defer = a.TryGetProperty("defer", out var df) && df.GetBoolean();
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
            switch (fe)
            {
                case TabItem ti: ti.IsSelected = true; break;
                case ToggleButton tb when tb.Command == null && defer:   // 켜면 모달(로그인 창)이 뜨는 토글
                    _ = _win.Dispatcher.BeginInvoke(() => tb.IsChecked = !(tb.IsChecked ?? false)); break;
                case ToggleButton tb when tb.Command == null: tb.IsChecked = !(tb.IsChecked ?? false); break;
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
                    if (grid != null) grid.SelectedItem = dgr.Item; else dgr.IsSelected = true;
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
                if (fe != null) { var r = Bounds(fe); return new Point(r.X + r.Width / 2, r.Y + r.Height / 2); }
            }
            if (a.TryGetProperty("rect", out var rr))
                return new Point(rr[0].GetDouble() + rr[2].GetDouble() / 2, rr[1].GetDouble() + rr[3].GetDouble() / 2);
            if (a.TryGetProperty("x", out var x)) return new Point(x.GetDouble(), a.GetProperty("y").GetDouble());
            Log("  이동 대상 없음");
            return _cur;
        }

        private void SetProp(object target, string name, JsonElement v)
        {
            var p = target.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (p == null || !p.CanWrite) { Log($"  속성 없음: {target.GetType().Name}.{name}"); return; }
            object? val = p.PropertyType switch
            {
                var t when t == typeof(bool) => v.GetBoolean(),
                var t when t == typeof(int) => v.GetInt32(),
                var t when t == typeof(double) => v.GetDouble(),
                var t when t == typeof(string) => v.GetString(),
                var t when t == typeof(DateTime) || t == typeof(DateTime?) => DateTime.Parse(v.GetString()!, CultureInfo.InvariantCulture),
                var t when t.IsEnum => Enum.Parse(t, v.GetString()!),
                _ => null,
            };
            if (val == null) { Log($"  형식 미지원: {name} {p.PropertyType}"); return; }
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

        /// <summary>
        /// 대상 찾기 — 위에 뜬 창부터 메인 창 순서로 찾는다.
        /// name:X(x:Name) · tip:X(ToolTip 글자) · tipp:X(ToolTip 앞부분) · auto:X(AutomationProperties.Name) ·
        /// text:X(보이는 글자, #n 으로 n번째) · textp:X(글자 앞부분) · cmd:X(VM 명령 속성 이름에 묶인 버튼) · row:X(표의 행)
        /// </summary>
        private FrameworkElement? Find(string spec)
        {
            int idx = 0;
            int hash = spec.LastIndexOf('#');
            if (hash > 0 && int.TryParse(spec[(hash + 1)..], out var n)) { idx = n; spec = spec[..hash]; }
            int colon = spec.IndexOf(':');
            string kind = spec[..colon], key = spec[(colon + 1)..];
            var roots = OverWindows().AsEnumerable().Reverse().Select(w => (DependencyObject)w.Content).Append(Root).ToList();
            foreach (var root in roots)
            {
                var hit = FindIn(root, kind, key, idx);
                if (hit != null) return hit;
            }
            if (kind == "text" || kind == "textp" || kind == "name")
            {
                var raw = roots.SelectMany(r => FindAll<FrameworkElement>(r)).Where(e => MatchText(e, kind, key)).ToList();
                Log($"  찾기 실패 {spec}: 후보 {raw.Count} — " + string.Join(", ", raw.Take(4).Select(r => $"{r.GetType().Name} w={r.ActualWidth:F0} vis={r.IsVisible}")));
            }
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
            if (kind == "section")     // 설정 패널 섹션 카드 — 머리글 글자를 감싼 Border
            {
                var head = FindAll<TextBlock>(root).FirstOrDefault(t => t.ActualWidth > 0 && IsShown(t) && (t.Text == key || InlineText(t) == key));
                return head == null ? null : FindAncestor<Border>(head);
            }
            if (kind == "ngitem")      // NG History 썸네일 n 번째 (0 = 최신)
                return ItemContainer(root, _vm.Dashboard.NgImageHistory, int.TryParse(key, out var ni) ? ni : 0, null);
            if (kind == "recipeitem")  // 설정 패널 레시피 목록의 그 이름 행
                return ItemContainer(root, _vm.RecipeList, -1, o => o is VMS.VisionSetup.Models.RecipeInfo ri && ri.Name == key);
            if (kind == "combo")       // n 번째 보이는 콤보 상자 (0 부터)
                return FindAll<ComboBox>(root).Where(r => r.ActualWidth > 0 && IsShown(r)).ElementAtOrDefault(int.TryParse(key, out var ci) ? ci : 0);
            if (kind == "textbox")     // n 번째 보이는 글자 칸 (0 부터)
                return FindAll<TextBox>(root).Where(r => r.ActualWidth > 0 && IsShown(r)).ElementAtOrDefault(int.TryParse(key, out var xi) ? xi : 0);
            if (kind == "dgrow")       // "g|글자" — g 번째 보이는 표에서 그 글자가 든 행
            {
                var parts = key.Split('|', 2);
                var grid = FindAll<DataGrid>(root).Where(r => r.ActualWidth > 0 && IsShown(r)).ElementAtOrDefault(int.Parse(parts[0]));
                return grid == null ? null : FindAll<DataGridRow>(grid).FirstOrDefault(r => r.ActualWidth > 0 && IsShown(r) && FindAll<TextBlock>(r).Any(t => t.Text == parts[1]));
            }
            if (kind == "grid")        // n 번째 보이는 표
                return FindAll<DataGrid>(root).Where(r => r.ActualWidth > 0 && IsShown(r)).ElementAtOrDefault(int.TryParse(key, out var gdi) ? gdi : 0);
            if (kind == "gridrow")     // 표의 n 번째 보이는 행 (0 부터)
                return FindAll<DataGridRow>(root).Where(r => r.ActualWidth > 0 && IsShown(r)).ElementAtOrDefault(int.TryParse(key, out var gi) ? gi : 0);
            if (kind == "row")
                return FindAll<DataGridRow>(root).FirstOrDefault(r => r.ActualWidth > 0 && IsShown(r) && FindAll<TextBlock>(r).Any(t => t.Text == key));
            IEnumerable<FrameworkElement> all = FindAll<FrameworkElement>(root).Where(e => e.ActualWidth > 0 && IsShown(e));
            Func<FrameworkElement, bool> pred = kind switch
            {
                "name" or "text" or "textp" => e => MatchText(e, kind, key),
                "tip" => e => ToolTipText(e) == key,
                "tipp" => e => ToolTipText(e)?.StartsWith(key, StringComparison.Ordinal) == true,
                "auto" => e => System.Windows.Automation.AutomationProperties.GetName(e) == key,
                "cmd" => e => e is ButtonBase b && b.Command != null && IsCommandNamed(b, key),
                _ => _ => false,
            };
            var hits = all.Where(pred).ToList();
            if (kind == "text" || kind == "textp")   // 글자 TextBlock 이 버튼 · 탭 안에 있으면 그 컨트롤을 대상으로
                hits = hits.Select(h => (FrameworkElement)(FindAncestor<ButtonBase>(h) ?? (FrameworkElement?)FindAncestor<TabItem>(h) ?? h)).Distinct().ToList();
            return idx < hits.Count ? hits[idx] : null;
        }

        private static FrameworkElement? ItemContainer(DependencyObject root, System.Collections.IEnumerable source, int index, Func<object, bool>? match)
        {
            var ic = FindAll<ItemsControl>(root).FirstOrDefault(c => ReferenceEquals(c.ItemsSource, source) && c.ActualWidth > 0);
            if (ic == null) return null;
            object? item = match != null ? ic.Items.Cast<object>().FirstOrDefault(match) : (index < ic.Items.Count ? ic.Items[index] : null);
            return item == null ? null : ic.ItemContainerGenerator.ContainerFromItem(item) as FrameworkElement;
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
        /// 녹화용 대화상자 서비스 — 메시지 창 · 로그인 창은 원래 서비스 그대로, 파일 저장 · 열기는 시나리오가 준 경로를 돌려준다
        /// (OS 파일 대화상자는 실제 화면에 떠서 녹화에 담기지 않는다). 준 경로가 없으면 대화상자를 띄우지 않고 취소로 처리한다.
        /// </summary>
        private sealed class RecordingDialogService : Interfaces.IDialogService
        {
            private readonly Interfaces.IDialogService _inner;
            private readonly Action<string> _log;
            public readonly Queue<string> Saves = new(), Opens = new();
            public RecordingDialogService(Interfaces.IDialogService inner, Action<string> log) { _inner = inner; _log = log; }
            public void ShowInformation(string message, string title) => _inner.ShowInformation(message, title);
            public void ShowWarning(string message, string title) => _inner.ShowWarning(message, title);
            public void ShowError(string message, string title) => _inner.ShowError(message, title);
            public bool ShowConfirmation(string message, string title) => _inner.ShowConfirmation(message, title);
            public bool ShowLoginDialog(Interfaces.IUserService userService) => _inner.ShowLoginDialog(userService);
            public string? ShowSaveFileDialog(string filter, string defaultExt, string? fileName = null)
            {
                if (Saves.Count == 0) { _log("  ⚠ 저장 대화상자 경로 없음 — 취소로 처리"); return null; }
                var p = Saves.Dequeue();
                if (fileName != null && (Directory.Exists(p) || Path.GetExtension(p).Length == 0))   // 폴더만 주면 제품이 제안한 파일 이름
                {
                    Directory.CreateDirectory(p);
                    p = Path.Combine(p, fileName);
                }
                _log($"  저장 대화상자 대신 경로 주입: {p} (제안 이름 {fileName})");
                return p;
            }
            public string? ShowOpenFileDialog(string filter, string defaultExt)
            {
                if (Opens.Count == 0) { _log("  ⚠ 열기 대화상자 경로 없음 — 취소로 처리"); return null; }
                var p = Opens.Dequeue();
                _log("  열기 대화상자 대신 경로 주입: " + p);
                return p;
            }
        }

        /// <summary>녹화 전용 취득기 — 카메라 대신 이미지 파일을 차례로 내준다 (AUTO RUN 시연).</summary>
        private sealed class FileImageAcquisition : ICameraAcquisition
        {
            private readonly List<string> _files;
            private int _next;
            public FileImageAcquisition(List<string> files) => _files = files;
            public bool IsConnected { get; private set; }
            public Task<bool> ConnectAsync(CameraInfo camera) { IsConnected = true; return Task.FromResult(true); }
            public Task DisconnectAsync() { IsConnected = false; return Task.CompletedTask; }
            public Task<AcquisitionResult> AcquireAsync(int timeoutMs = 5000)
            {
                if (_files.Count == 0) return Task.FromResult(new AcquisitionResult { Success = false, Message = "이미지 없음" });
                var mat = OpenCvSharp.Cv2.ImRead(_files[_next++ % _files.Count]);
                return Task.FromResult(new AcquisitionResult { Success = !mat.Empty(), Image2D = mat, Message = "file" });
            }
            public void Dispose() { }
        }
    }
}
#endif
