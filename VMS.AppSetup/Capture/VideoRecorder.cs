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
using VMS.AppSetup.Interfaces;
using VMS.AppSetup.ViewModels;
using VMS.Core.Security.Licensing;

namespace VMS.AppSetup.Capture
{
    /// <summary>
    /// 사용 안내 동영상 녹화 (DEBUG 전용, "--record-video [폴더]") — VisionSetup · VMS 메인 녹화 모드의 설정 마법사판.
    ///
    /// <para>마법사 창(960x820)을 화면 밖에 띄우고(입력 장치 · 화면 캡처를 쓰지 않는다) 시나리오 JSON 의 동작을 VM · 비주얼 트리 수준으로
    /// 실행하면서, 1920x1080 바탕 위 가운데(origin)에 마법사 창과 그 위에 열린 창(메시지 창)을 RenderTargetBitmap 으로 합성해
    /// JPEG 프레임과 시각(timeline.json)을 남긴다. 커서 · 클릭 물결 · 강조 상자 · 툴팁 · 도움말 말풍선 · 콤보 펼침은 프레임 위에 직접 그린다.</para>
    ///
    /// <para>안전 장치 — 녹화는 이름 붙은 녹화 인스턴스(vidrec…)에서만 돈다. Web 서버 구성 · 서비스 시작 · 라이선스 설치는
    /// <see cref="Guards"/> 가 서비스 단에서 막고(UAC · 서비스 · Program Files · ProgramData 쓰기 없음), OS 파일 대화상자 · 클립보드 ·
    /// 네트워크 검색 명령은 누름 연출만 한다. [Finish] 뒤 마법사의 종료 요청은 녹화가 끝날 때까지 미룬다.</para>
    ///
    /// 환경 변수: VMS_RECORD_SCENARIO = 시나리오 JSON, VMS_RECORD_DURATIONS = 구간별 내레이션 길이(초) JSON.
    /// 출력: &lt;폴더&gt;/frames/f000000.jpg …, &lt;폴더&gt;/timeline.json {frames:[{name,t}], marks:[{id,t}]}, _record.log
    /// </summary>
    public sealed class VideoRecorder
    {
        private const int W = 1920, H = 1080;
        private const double Gap = 0.7;
        private const double OffLeft = -6000;

        /// <summary>녹화 중 — App 의 종료 요청 · 서비스 가드가 본다.</summary>
        public static bool Active { get; private set; }
        private static VideoRecorder? _current;

        // 녹화 모드에서 실행하지 않는 명령 — OS 대화상자 · 클립보드 · 네트워크 검색 · 관리자 권한 · 창 최소화/닫기
        private static readonly HashSet<string> PressOnly = new()
        {
            "CopyFingerprintCommand", "ImportLicenseCommand", "ScanForCamerasCommand", "StartWebServerCommand",
        };

        private readonly Window _win;
        private readonly SetupViewModel _vm;
        private readonly string _out;
        private readonly Dictionary<string, double> _durs;
        private readonly List<(string Name, double T)> _frames = new();
        private readonly List<(string Id, double T)> _marks = new();
        private readonly ConcurrentBag<Task> _encodes = new();
        private readonly StreamWriter _log;
        private DispatcherTimer? _timer;
        private int _frameNo;
        private bool _busy;
        private Point _origin = new(480, 24);
        // 확대 배율 — 마법사 창(960x820)을 프레임에 크게 그린다(시나리오 "scale"). 좌표 · 강조 상자 · 커서는 프레임 기준
        private double _scale = 1.0;
        private string? _segId;
        private double _segT0;

        // 덧그림 상태
        private Point _cur = new(1100, 620);
        private double _clickT = -10;
        private Point _clickP;
        private List<Rect> _hl = new();
        private double _hlT0;
        private string? _tipText;
        private Rect _tipAnchor;
        private List<string>? _list;          // 콤보 펼침 목록 (팝업은 화면 밖에서 그려지지 않는다)
        private Rect _listBox;
        private int _listHover = -1;
        private FrameworkElement? _popupEl;   // 도움말 ? 말풍선
        private Rect _popupRect;

        private VideoRecorder(Window win, SetupViewModel vm, string outDir, Dictionary<string, double> durs)
        {
            _win = win; _vm = vm; _out = outDir; _durs = durs;
            Directory.CreateDirectory(Path.Combine(outDir, "frames"));
            foreach (var f in Directory.GetFiles(Path.Combine(outDir, "frames"))) File.Delete(f);
            _log = new StreamWriter(Path.Combine(outDir, "_record.log"), false, new UTF8Encoding(false)) { AutoFlush = true };
        }

        /// <summary>Show 전에 호출 — 화면 밖, 활성화 · 작업 표시줄 없음 (크기는 마법사 고정 크기 그대로).</summary>
        public static void PrepareWindow(Window w)
        {
            Active = true;
            w.WindowStartupLocation = WindowStartupLocation.Manual;
            w.WindowState = WindowState.Normal;
            w.Left = OffLeft; w.Top = 0;
            w.ShowActivated = false;
            w.ShowInTaskbar = false;
            w.Topmost = false;
        }

        /// <summary>마법사가 [Finish] 뒤 종료를 요청 — 녹화 중에는 미루고 기록만 남긴다.</summary>
        public static void OnWizardShutdownRequested()
        {
            if (_current == null) return;
            // 실제로는 여기서 프로그램이 끝난다 — 녹화는 이어 가되 프레임에서 마법사 창을 빼서 닫힌 화면으로 보인다
            _current._closed = true;
            _current.Log("  (마법사 종료 요청 — 프레임에서 창을 빼고, 프로세스는 녹화가 끝난 뒤 닫는다)");
        }
        private bool _closed;

        public static async Task RunAsync(Window win, string outDir)
        {
            if (win.DataContext is not SetupViewModel vm) return;
            var durs = new Dictionary<string, double>();
            var dp = Environment.GetEnvironmentVariable("VMS_RECORD_DURATIONS");
            if (!string.IsNullOrEmpty(dp) && File.Exists(dp))
                foreach (var p in JsonDocument.Parse(File.ReadAllText(dp)).RootElement.EnumerateObject())
                    durs[p.Name] = p.Value.GetDouble();
            var rec = new VideoRecorder(win, vm, outDir, durs);
            _current = rec;
            // 녹화 인스턴스에서만 — 실제(기본) 인스턴스 설정 파일 · 사용자 DB 를 건드리지 않게
            var inst = VMS.Camera.Configuration.AppDataPaths.InstanceName;
            if (VMS.Camera.Configuration.AppDataPaths.IsDefaultInstance || !inst.StartsWith("vidrec", StringComparison.OrdinalIgnoreCase))
            {
                rec.Log($"FATAL 녹화 인스턴스(--instance vidrec…)가 아님: '{inst}' — 녹화하지 않는다");
                rec._log.Dispose();
                return;
            }
            rec.Log($"instance {inst} · 설정 파일 {VMS.Camera.Configuration.AppDataPaths.SystemConfigFile}");
            EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.SizeChangedEvent,
                new SizeChangedEventHandler((s, _) => { if (s is Window w && !ReferenceEquals(w, win)) rec.PlaceOver(w, "early"); }));
            try { await rec.RunScenarioAsync(); }
            catch (Exception ex) { rec.Log("FATAL " + ex); }
            finally { await rec.FinishAsync(); }
        }

        // ───────────────────────── 프레임 ─────────────────────────

        private static double Now() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
        private FrameworkElement Root => (FrameworkElement)_win.Content;

        private List<Window> OverWindows() =>
            Application.Current.Windows.OfType<Window>()
                .Where(w => !ReferenceEquals(w, _win) && w.IsVisible && w.Content is FrameworkElement fe && fe.ActualWidth > 0)
                .ToList();

        /// <summary>창 콘텐츠 왼쪽 위의 프레임 좌표 (마법사 창은 origin).</summary>
        private Point OffsetOf(Window w)
        {
            if (ReferenceEquals(w, _win)) return _origin;
            Point o;
            try { o = Root.PointFromScreen(((Visual)w.Content).PointToScreen(new Point(0, 0))); }
            catch { o = new Point(w.Left - _win.Left, w.Top - _win.Top); }
            return new Point(o.X * _scale + _origin.X, o.Y * _scale + _origin.Y);
        }

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
            Log($"  창 위치 보정({when}): {w.GetType().Name} ({w.Left:F0},{w.Top:F0} {ww:F0}x{wh:F0})");
            w.WindowStartupLocation = WindowStartupLocation.Manual;
            w.ShowActivated = false;
            w.Left = _win.Left + (_win.ActualWidth - ww) / 2;
            w.Top = _win.Top + Math.Max(0, (_win.ActualHeight - wh) / 2);
        }

        private void StartFrames()
        {
            _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(50) };
            _timer.Tick += (_, _) => CaptureFrame();
            _timer.Start();
        }

        private static readonly Brush Desk = new LinearGradientBrush(Color.FromRgb(0x23, 0x28, 0x31), Color.FromRgb(0x12, 0x15, 0x1A), 90);

        private void DrawWindow(DrawingContext dc, Window w, FrameworkElement content, Rect r)
        {
            for (int i = 1; i <= 5; i++)   // 창 그림자 — 화면 밖 창은 OS 그림자가 없어 경계가 묻힌다
            {
                var sr = r; sr.Inflate(i * 3, i * 3); sr.Offset(0, i * 2);
                dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb((byte)(40 - i * 7), 0, 0, 0)), null, sr, 6 + i * 3, 6 + i * 3);
            }
            dc.DrawRectangle(w.Background ?? Brushes.Black, null, r);
            dc.DrawRectangle(new VisualBrush(content)
            {
                Stretch = Stretch.Fill, ViewboxUnits = BrushMappingMode.Absolute,
                Viewbox = new Rect(0, 0, content.ActualWidth, content.ActualHeight),
            }, null, r);
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
                var dv0 = new DrawingVisual();
                using (var dc = dv0.RenderOpen())
                {
                    dc.DrawRectangle(Desk, null, new Rect(0, 0, W, H));
                    if (!_closed) DrawWindow(dc, _win, Root, new Rect(_origin.X, _origin.Y, Root.ActualWidth * _scale, Root.ActualHeight * _scale));
                    foreach (var w in _closed ? new List<Window>() : OverWindows())
                    {
                        var content = VisualTreeHelper.GetChildrenCount(w) > 0 && VisualTreeHelper.GetChild(w, 0) is FrameworkElement tr
                            ? tr : (FrameworkElement)w.Content;
                        Point o;
                        try
                        {
                            var p = Root.PointFromScreen(content.PointToScreen(new Point(0, 0)));
                            o = new Point(p.X * _scale + _origin.X, p.Y * _scale + _origin.Y);
                        }
                        catch { o = OffsetOf(w); }
                        DrawWindow(dc, w, content, new Rect(o.X, o.Y, content.ActualWidth * _scale, content.ActualHeight * _scale));
                    }
                    DrawOverlay(dc, t);
                }
                rtb.Render(dv0);
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
            if (_hl.Count > 0)
            {
                double a = Math.Clamp((t - _hlT0) / 0.35, 0, 1);
                var r = _hl.Aggregate(Rect.Union);
                r.Inflate(6, 6);
                var full = new RectangleGeometry(new Rect(0, 0, W, H));
                var hole = new RectangleGeometry(r, 10, 10);
                var dim = new CombinedGeometry(GeometryCombineMode.Exclude, full, hole);
                dc.DrawGeometry(new SolidColorBrush(Color.FromArgb((byte)(46 * a), 15, 23, 42)), null, dim);
                dc.DrawRoundedRectangle(null, new Pen(new SolidColorBrush(Color.FromArgb((byte)(90 * a), 0xFF, 0x6B, 0x1A)), 10), r, 10, 10);
                dc.DrawRoundedRectangle(null, new Pen(new SolidColorBrush(Color.FromArgb((byte)(255 * a), 0xFF, 0x6B, 0x1A)), 3.5), r, 10, 10);
            }
            if (_list != null) DrawList(dc);
            if (_popupEl != null)
            {
                var sh = _popupRect; sh.Offset(3, 5);
                dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(90, 0, 0, 0)), null, sh, 6, 6);
                dc.DrawRectangle(new VisualBrush(_popupEl)
                {
                    Stretch = Stretch.Fill, ViewboxUnits = BrushMappingMode.Absolute,
                    Viewbox = new Rect(0, 0, _popupEl.ActualWidth, _popupEl.ActualHeight),
                }, null, _popupRect);
            }
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
            double k = (t - _clickT) / 0.55;
            if (k >= 0 && k <= 1)
            {
                double rad = 20 * (1 + 0.6 * k);
                dc.DrawEllipse(new SolidColorBrush(Color.FromArgb((byte)(46 * (1 - k)), 0xFF, 0x6B, 0x1A)),
                    new Pen(new SolidColorBrush(Color.FromArgb((byte)(230 * (1 - k)), 0xFF, 0x6B, 0x1A)), 3), _clickP, rad, rad);
            }
            var g = Geometry.Parse("M4,2.5 L10.6,20.7 L13.2,13.3 L20.5,10.6 Z").Clone();
            g.Transform = new TransformGroup { Children = { new ScaleTransform(1.2, 1.2), new TranslateTransform(_cur.X - 4.8, _cur.Y - 3) } };
            dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(70, 0, 0, 0)), null, Shift(g, 1.5, 2));
            dc.DrawGeometry(new SolidColorBrush(Color.FromRgb(0x11, 0x11, 0x11)), new Pen(Brushes.White, 2) { LineJoin = PenLineJoin.Round }, g);
        }

        private double ListItemH => 28 * _scale;

        private List<Rect> ListLayout()
        {
            var list = new List<Rect>();
            if (_list == null) return list;
            double y = _listBox.Y + 3;
            foreach (var _ in _list) { list.Add(new Rect(_listBox.X + 3, y, _listBox.Width - 6, ListItemH)); y += ListItemH; }
            return list;
        }

        private void DrawList(DrawingContext dc)
        {
            var lay = ListLayout();
            if (lay.Count == 0) return;
            var box = new Rect(_listBox.X, _listBox.Y, _listBox.Width, lay.Count * ListItemH + 6);
            var sh = box; sh.Offset(2, 4);
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(80, 0, 0, 0)), null, sh, 4, 4);
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x2D, 0x2D, 0x30)), new Pen(new SolidColorBrush(Color.FromRgb(0x55, 0x5A, 0x63)), 1), box);
            for (int i = 0; i < lay.Count; i++)
            {
                if (i == _listHover) dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(0x09, 0x47, 0x71)), null, lay[i], 3, 3);
                var st = new FormattedText(_list![i], CultureInfo.GetCultureInfo("ko-KR"), FlowDirection.LeftToRight, new Typeface("Segoe UI"), 13 * _scale, Brushes.White, 1.0);
                dc.DrawText(st, new Point(lay[i].X + 9, lay[i].Y + (lay[i].Height - st.Height) / 2));
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
            if (root.TryGetProperty("origin", out var og)) _origin = new Point(og[0].GetDouble(), og[1].GetDouble());
            if (root.TryGetProperty("scale", out var sc)) _scale = sc.GetDouble();
            await Settle();
            await Task.Delay(500);
            if (root.TryGetProperty("setup", out var setup))
                foreach (var a in setup.EnumerateArray()) await DoAsync(a);
            await Settle();
            StartFrames();
            await Task.Delay(1200);
            foreach (var a in root.GetProperty("actions").EnumerateArray()) await DoAsync(a);
        }

        /// <summary>set 대상 — vm(기본) · sel(선택한 IO 보드) · cam:n(n 번째 카메라)</summary>
        private object? Target(JsonElement a)
        {
            string on = a.TryGetProperty("on", out var o) ? o.GetString() ?? "" : "";
            if (on == "sel") return _vm.SelectedIoBoard;
            if (on.StartsWith("cam:")) return _vm.Cameras.ElementAtOrDefault(int.Parse(on[4..]));
            return _vm;
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
                        Log($"[{id}] start (page {_vm.CurrentPage})");
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
                        // 이 구간 내레이션의 f 지점(0~1)까지 기다린다 — 자막 문장과 화면 동작을 맞춘다. 이미 지났으면 바로
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
                        _hl = rects; _hlT0 = Now();
                        break;
                    }
                    case "hlClear": _hl = new(); break;
                    case "ripple": _clickT = Now(); _clickP = _cur; await Task.Delay(250); break;
                    case "tip":
                    {
                        var fe = Find(a.GetProperty("target").GetString()!);
                        if (fe == null) { Log("  tip 대상 없음: " + a.GetProperty("target").GetString()); break; }
                        _tipAnchor = Bounds(fe);
                        _tipText = a.TryGetProperty("text", out var tt) ? tt.GetString() : ToolTipText(fe);
                        if (string.IsNullOrEmpty(_tipText)) { Log("  tip 글자 없음"); _tipText = null; }
                        break;
                    }
                    case "tipClear": _tipText = null; break;
                    case "popup":
                    {
                        // 도움말 ? 의 말풍선(Popup) 내용을 열지 않고 배치해 오른쪽(모자라면 왼쪽)에 그린다 — 열면 실제 화면에 뜬다
                        var host = Find(a.GetProperty("target").GetString()!);
                        if (host == null) { Log("  popup 대상 없음"); break; }
                        var pop = host.FindName("HelpPopup") as Popup
                                  ?? FindAll<FrameworkElement>(host).Select(e => e.FindName("HelpPopup") as Popup).FirstOrDefault(x => x != null)
                                  ?? LogicalTreeHelper.GetChildren(host).OfType<Popup>().FirstOrDefault();
                        if (pop == null && host is UserControl uc && uc.Content is DependencyObject ucc)
                            pop = LogicalTreeHelper.GetChildren(ucc).OfType<Popup>().FirstOrDefault();
                        if (pop?.Child is not FrameworkElement child) { Log("  popup 없음"); break; }
                        var hr = Bounds(host);
                        if (!(a.TryGetProperty("hover", out var ph) && !ph.GetBoolean())) await MoveTo(new Point(hr.X + hr.Width / 2, hr.Y + hr.Height / 2), Ms(a, 600));
                        child.Measure(new Size(a.TryGetProperty("w", out var pw) ? pw.GetDouble() : 420, 900));
                        child.Arrange(new Rect(child.DesiredSize));
                        child.UpdateLayout();
                        double cw = child.ActualWidth * _scale, ch = child.ActualHeight * _scale;
                        double x = hr.Right + 8, y = hr.Top - 6;
                        if (x + cw > W - 8) x = hr.Left - cw - 8;
                        y = Math.Clamp(y, 8, H - ch - 8);
                        _popupEl = child; _popupRect = new Rect(x, y, cw, ch);
                        Log($"  popup {cw:F0}x{ch:F0} at {x:F0},{y:F0}");
                        break;
                    }
                    case "popupClear": _popupEl = null; break;
                    case "set":
                    {
                        var target = Target(a);
                        if (a.TryGetProperty("target", out var stg)) await ClickVisual(Find(stg.GetString()!), Ms(a, 700));
                        if (target == null) { Log("  ⚠ set 대상 없음"); break; }
                        foreach (var p in a.GetProperty("values").EnumerateObject()) SetProp(target, p.Name, p.Value);
                        await Settle();
                        break;
                    }
                    case "cmd":
                    {
                        if (a.TryGetProperty("target", out var tg)) await ClickVisual(Find(tg.GetString()!));
                        string name = a.GetProperty("name").GetString()!;
                        if (PressOnly.Contains(name)) { Log("  누름 연출만(녹화 모드 차단): " + name); break; }
                        var c = _vm.GetType().GetProperty(name)?.GetValue(_vm) as ICommand;
                        if (c == null || !c.CanExecute(null)) { Log("  명령 실행 불가: " + name); break; }
                        if (c is IAsyncRelayCommand ac && !(a.TryGetProperty("modal", out var md) && md.GetBoolean())) await ac.ExecuteAsync(null);
                        else _ = _win.Dispatcher.BeginInvoke(() => c.Execute(null));
                        await Settle(); await Task.Delay(150); await Settle();
                        break;
                    }
                    case "closeWindow":
                    {
                        var w = OverWindows().LastOrDefault();
                        if (a.TryGetProperty("target", out var tg)) await ClickVisual(Find(tg.GetString()!), Ms(a, 700));
                        if (w != null) { w.Close(); Log("  closeWindow: " + w.GetType().Name); }
                        else Log("  ⚠ 닫을 창 없음");
                        await Settle();
                        break;
                    }
                    case "type":
                    {
                        var fe = Find(a.GetProperty("target").GetString()!);
                        await ClickVisual(fe, Ms(a, 700));
                        string text = a.TryGetProperty("textEnv", out var te)
                            ? Environment.GetEnvironmentVariable(te.GetString()!) ?? ""
                            : a.GetProperty("text").GetString()!;
                        if (text.Length == 0 && !(a.TryGetProperty("clear", out var clr) && clr.GetBoolean())) Log("  ⚠ type: 넣을 글자 없음");
                        int per = a.TryGetProperty("perChar", out var pc) ? pc.GetInt32() : 45;
                        if (fe is PasswordBox pb)
                        {
                            // 암호 칸 — 점으로 한 글자씩. PasswordChanged 처리기(코드비하인드)가 VM 으로 옮긴다. 값은 로그에 쓰지 않는다.
                            pb.Password = "";
                            for (int i = 1; i <= text.Length; i++) { pb.Password = text[..i]; await Task.Delay(per); }
                            Log($"  type(암호 {text.Length}자): {a.GetProperty("target").GetString()}");
                            await Settle();
                            break;
                        }
                        if (fe is not TextBox tb) { Log("  ⚠ 글자 칸 아님: " + a.GetProperty("target").GetString()); break; }
                        if (!(a.TryGetProperty("append", out var ap) && ap.GetBoolean())) tb.Text = "";
                        string start = tb.Text;
                        for (int i = 1; i <= text.Length; i++)
                        {
                            tb.Text = start + text[..i];
                            tb.CaretIndex = tb.Text.Length;
                            await Task.Delay(per);
                        }
                        tb.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
                        await Settle();
                        break;
                    }
                    case "combo":
                    {
                        var fe = Find(a.GetProperty("target").GetString()!);
                        await ClickVisual(fe, Ms(a, 700));
                        var cb = fe as ComboBox ?? (fe != null ? FindAncestor<ComboBox>(fe) : null);
                        if (cb == null) { Log("  ⚠ 콤보 아님: " + a.GetProperty("target").GetString()); break; }
                        string want = a.GetProperty("item").GetString()!;
                        var texts = cb.Items.Cast<object>().Select(o => ComboText(cb, o)).ToList();
                        int hi = texts.IndexOf(want);
                        if (hi < 0) { Log($"  ⚠ 콤보 항목 없음: {want} — " + string.Join(", ", texts)); break; }
                        if (a.TryGetProperty("show", out var cshow))   // 펼친 목록을 그려 보이고 그 항목을 짚어 고른다
                        {
                            var cbr = Bounds(cb);
                            double lw = Math.Max(cbr.Width, texts.Max(t => new FormattedText(t, CultureInfo.GetCultureInfo("ko-KR"), FlowDirection.LeftToRight, new Typeface("Segoe UI"), 13 * _scale, Brushes.Black, 1.0).Width) + 30);
                            _listBox = new Rect(cbr.X, cbr.Bottom + 1, lw, 0);
                            _list = texts;
                            _listHover = texts.IndexOf(ComboText(cb, cb.SelectedItem));
                            await Task.Delay((int)(cshow.GetDouble() * 1000));
                            var lay = ListLayout();
                            await MoveTo(new Point(lay[hi].X + 50, lay[hi].Y + lay[hi].Height / 2), 600);
                            _listHover = hi;
                            await Task.Delay(400);
                            _clickT = Now(); _clickP = _cur;
                            await Task.Delay(200);
                            _list = null; _listHover = -1;
                        }
                        cb.SelectedItem = cb.Items[hi];
                        await Settle();
                        break;
                    }
                    case "check":
                    {
                        // 체크 상자 · 라디오 · 토글 켜기/끄기 (value 없으면 반전)
                        var fe = Find(a.GetProperty("target").GetString()!);
                        await ClickVisual(fe, Ms(a, 700));
                        var tb = fe as ToggleButton ?? (fe != null ? FindAncestor<ToggleButton>(fe) : null);
                        if (tb == null) { Log("  ⚠ 토글 아님: " + a.GetProperty("target").GetString()); break; }
                        if (!tb.IsEnabled) { Log("  ⚠ 토글 비활성: " + a.GetProperty("target").GetString()); break; }
                        tb.IsChecked = a.TryGetProperty("value", out var v) ? v.GetBoolean() : !(tb.IsChecked ?? false);
                        tb.GetBindingExpression(ToggleButton.IsCheckedProperty)?.UpdateSource();
                        await Settle();
                        break;
                    }
                    case "scrollTo":
                    {
                        // top:true 면 대상이 감싼 ScrollViewer 맨 위(여백 offset)로 · end:true 면 맨 아래로 · home:true 면 맨 위로 · 아니면 BringIntoView
                        var fe = Find(a.GetProperty("target").GetString()!);
                        if (fe == null) break;
                        var sv = fe as ScrollViewer ?? FindAncestor<ScrollViewer>(fe);
                        if (a.TryGetProperty("end", out var se) && se.GetBoolean() && sv != null) sv.ScrollToEnd();
                        else if (a.TryGetProperty("home", out var sh) && sh.GetBoolean() && sv != null) sv.ScrollToHome();
                        else if (a.TryGetProperty("top", out var tp) && tp.GetBoolean() && sv != null)
                        {
                            double off = a.TryGetProperty("offset", out var of) ? of.GetDouble() : 8;
                            double y = fe.TransformToAncestor(sv).Transform(new Point(0, 0)).Y;
                            await SmoothScroll(sv, Math.Max(0, sv.VerticalOffset + y - off), Ms(a, 500));
                        }
                        else fe.BringIntoView();
                        await Settle();
                        break;
                    }
                    case "waitFor":
                    {
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
                    case "webState":
                    {
                        // Web 서버 카드 상태 — dir 의 Web 폴더(BODA.VMS.Web.exe · appsettings.Production.json 유무)로 판정하는 같은 서비스를 녹화 가드로 감싸 바꿔 끼운다.
                        //   구성 · 서비스 시작은 가드가 막는다(UAC · 서비스 · 파일 쓰기 없음). 서비스 상태 조회만 읽기.
                        string dir = a.GetProperty("dir").GetString()!;
                        string svc = a.TryGetProperty("service", out var sv) ? sv.GetString()! : "BodaVmsWeb_vidrec_none";
                        var inner = new Services.WebServerSetupService(dir, Path.Combine(dir, "_data"), svc);
                        var f = typeof(SetupViewModel).GetField("_webServerSetup", BindingFlags.NonPublic | BindingFlags.Instance);
                        f!.SetValue(_vm, new Guards.Web(inner, Log));
                        _vm.RefreshWebServerStatusCommand.Execute(null);
                        await Settle();
                        Log($"  webState {Path.GetFileName(dir)}: 설치={_vm.IsWebServerInstalled} 구성={_vm.IsWebServerConfigured} '{_vm.WebServerServiceStatus}'");
                        break;
                    }
                    case "vmLog":
                    {
                        foreach (var p in a.GetProperty("props").EnumerateArray())
                        {
                            string n = p.GetString()!;
                            var v = _vm.GetType().GetProperty(n)?.GetValue(_vm);
                            bool secret = n.Contains("Password") || n.Contains("Token") || n.Contains("ApiKey") || n.Contains("Fingerprint");
                            Log($"  vm {n} = {(secret ? $"({(v as string)?.Length ?? 0}자)" : v)}");
                        }
                        break;
                    }
                    case "fileLog":
                    {
                        // 저장된 설정 파일 확인 — 키 목록과 몇몇 값(비밀 값 제외)
                        var p = VMS.Camera.Configuration.AppDataPaths.SystemConfigFile;
                        if (!File.Exists(p)) { Log("  설정 파일 없음: " + p); break; }
                        using var cfg = JsonDocument.Parse(File.ReadAllText(p));
                        foreach (var kv in cfg.RootElement.EnumerateObject())
                        {
                            bool secret = kv.Name.Contains("Token", StringComparison.OrdinalIgnoreCase) || kv.Name.Contains("ApiKey", StringComparison.OrdinalIgnoreCase);
                            string val = kv.Value.ValueKind switch
                            {
                                JsonValueKind.Array => $"[{kv.Value.GetArrayLength()}]",
                                JsonValueKind.Object => "{…}",
                                _ when secret => $"({kv.Value.ToString().Length}자)",
                                _ => kv.Value.ToString(),
                            };
                            Log($"  cfg {kv.Name} = {val}");
                        }
                        break;
                    }
                    case "hide":
                    {
                        // 녹화 환경 표시 숨기기 — 녹화 인스턴스 배지("인스턴스: vidrecsetup")처럼 실제 현장 PC(기본 인스턴스)에는 없는 것. ancestor 로 감싼 컨트롤까지
                        var fe = Find(a.GetProperty("target").GetString()!);
                        FrameworkElement? hit = fe;
                        if (fe != null && a.TryGetProperty("ancestor", out var an) && an.GetString() == "Border") hit = FindAncestor<Border>(fe);
                        if (hit == null) { Log("  ⚠ hide 대상 없음"); break; }
                        hit.Visibility = Visibility.Collapsed;
                        await Settle();
                        Log("  hide: " + a.GetProperty("target").GetString());
                        break;
                    }
                    case "dump": Dump(a.TryGetProperty("filter", out var fl) ? fl.GetString() : null); break;
                    case "log": Log($"  page {_vm.CurrentPage} '{_vm.PageTitle}' step {_vm.DisplayStepNumber}/{_vm.DisplayStepTotal}"); break;
                    default: Log("  ⚠ 알 수 없는 op " + op); break;
                }
            }
            catch (Exception ex) { Log($"  ⚠ op {op} 실패: {ex}"); }
        }

        private static int Ms(JsonElement a, int def) => a.TryGetProperty("ms", out var m) ? m.GetInt32() : def;

        private async Task Settle()
        {
            for (int i = 0; i < 2; i++)
            {
                _win.UpdateLayout();
                foreach (var w in OverWindows()) w.UpdateLayout();
                await _win.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            }
        }

        private static async Task SmoothScroll(ScrollViewer sv, double to, int ms)
        {
            double from = sv.VerticalOffset;
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < ms)
            {
                double k = sw.ElapsedMilliseconds / (double)ms;
                double e = k < .5 ? 2 * k * k : 1 - Math.Pow(-2 * k + 2, 2) / 2;
                sv.ScrollToVerticalOffset(from + (to - from) * e);
                await Task.Delay(15);
            }
            sv.ScrollToVerticalOffset(to);
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
            if (fe == null) { Log("  ⚠ 클릭 대상 없음"); return; }
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
            string spec = a.GetProperty("target").GetString()!;
            var fe = Find(spec);
            await ClickVisual(fe, Ms(a, 700));
            if (fe == null || (a.TryGetProperty("invoke", out var inv) && !inv.GetBoolean())) return;
            if (fe is ButtonBase bb && bb.Command != null)
            {
                string? cname = CommandName(bb);
                if (bb.Command == SystemCommands.CloseWindowCommand || bb.Command == SystemCommands.MinimizeWindowCommand || (cname != null && PressOnly.Contains(cname)))
                {
                    Log($"  누름 연출만(녹화 모드 차단): {cname ?? "창 단추"}");
                    return;
                }
            }
            switch (fe)
            {
                case TabItem ti: ti.IsSelected = true; break;
                case RadioButton rb: rb.IsChecked = true; break;
                case ToggleButton tb when tb.Command == null: tb.IsChecked = !(tb.IsChecked ?? false); break;
                case ButtonBase b:
                    if (!b.IsEnabled) { Log("  ⚠ 버튼 비활성: " + spec); break; }
                    if (b.Command != null)
                    {
                        if (b.Command.CanExecute(b.CommandParameter))
                        {
                            var cmd = b.Command; var prm = b.CommandParameter;
                            _ = _win.Dispatcher.BeginInvoke(() => cmd.Execute(prm));
                        }
                        else Log("  ⚠ 버튼 비활성: " + spec);
                    }
                    else _ = _win.Dispatcher.BeginInvoke(() => b.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, b)));
                    break;
                case ListBoxItem li: li.IsSelected = true; break;
            }
            await Settle();
            await Task.Delay(120);
            await Settle();
        }

        private string? CommandName(ButtonBase b)
        {
            foreach (var p in typeof(SetupViewModel).GetProperties())
                if (typeof(ICommand).IsAssignableFrom(p.PropertyType) && ReferenceEquals(p.GetValue(_vm), b.Command)) return p.Name;
            return null;
        }

        private Point ResolvePoint(JsonElement a)
        {
            if (a.TryGetProperty("target", out var t))
            {
                var fe = Find(t.GetString()!);
                if (fe != null)
                {
                    var r = Bounds(fe);
                    double ax = a.TryGetProperty("ax", out var axe) ? axe.GetDouble() : 0.5;
                    double ay = a.TryGetProperty("ay", out var aye) ? aye.GetDouble() : 0.5;
                    double dx = a.TryGetProperty("dx", out var dxe) ? dxe.GetDouble() : 0;
                    double dy = a.TryGetProperty("dy", out var dye) ? dye.GetDouble() : 0;
                    return new Point(r.X + r.Width * ax + dx, r.Y + r.Height * ay + dy);
                }
            }
            if (a.TryGetProperty("x", out var x)) return new Point(x.GetDouble(), a.GetProperty("y").GetDouble());
            Log("  ⚠ 이동 대상 없음");
            return _cur;
        }

        private void SetProp(object target, string name, JsonElement v)
        {
            var p = target.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (p == null || !p.CanWrite) { Log($"  ⚠ 속성 없음: {target.GetType().Name}.{name}"); return; }
            object? val = p.PropertyType switch
            {
                var t when t == typeof(bool) => v.GetBoolean(),
                var t when t == typeof(int) => v.GetInt32(),
                var t when t == typeof(byte) => (byte)v.GetInt32(),
                var t when t == typeof(ushort) => (ushort)v.GetInt32(),
                var t when t == typeof(double) => v.GetDouble(),
                var t when t == typeof(string) => v.GetString(),
                var t when t.IsEnum => Enum.Parse(t, v.GetString()!),
                _ => null,
            };
            if (val == null) { Log($"  ⚠ 형식 미지원: {name} {p.PropertyType}"); return; }
            p.SetValue(target, val);
        }

        // ───────────────────────── 좌표 · 찾기 ─────────────────────────

        /// <summary>요소의 프레임 기준 사각형 — 마법사 창 안이면 origin 을, 다른 창 안이면 그 창의 위치를 더한다.</summary>
        private Rect Bounds(FrameworkElement fe)
        {
            var w = Window.GetWindow(fe);
            if (w == null || ReferenceEquals(w, _win))
            {
                var r0 = fe.TransformToAncestor(Root).TransformBounds(new Rect(0, 0, fe.ActualWidth, fe.ActualHeight));
                return new Rect(_origin.X + r0.X * _scale, _origin.Y + r0.Y * _scale, r0.Width * _scale, r0.Height * _scale);
            }
            var r = fe.TransformToAncestor((Visual)w.Content).TransformBounds(new Rect(0, 0, fe.ActualWidth, fe.ActualHeight));
            var o = OffsetOf(w);
            return new Rect(o.X + r.X * _scale, o.Y + r.Y * _scale, r.Width * _scale, r.Height * _scale);
        }

        private static string? ToolTipText(FrameworkElement fe) => fe.ToolTip switch
        {
            string s => s,
            ToolTip tt when tt.Content is string s2 => s2,
            TextBlock tb => tb.Text,
            null => null,
            var o => o.ToString(),
        };

        private static string ComboText(ComboBox cb, object? it)
        {
            string s = it is ContentControl cci ? cci.Content?.ToString() ?? "" : it?.ToString() ?? "";
            if (!string.IsNullOrEmpty(cb.DisplayMemberPath))
                s = it?.GetType().GetProperty(cb.DisplayMemberPath)?.GetValue(it)?.ToString() ?? s;
            return s;
        }

        /// <summary>
        /// 대상 찾기 — 위에 뜬 창부터 마법사 창 순서로. "A &gt; B" 는 A 안에서 B 를 찾는다.
        /// name:X · text:X(#n) · textp:X · tip:X · cmd:X(VM 명령에 묶인 버튼) · help:X(도움말 ? 의 제목) · field:X(그 라벨 바로 뒤 입력 컨트롤) ·
        /// label:X(라벨 TextBlock 자체) · combo:n · textbox:n · li:X(목록 항목) · cam:n(카메라 카드) · sv:n(n 번째 보이는 ScrollViewer) · page:(지금 페이지 ScrollViewer)
        /// </summary>
        private FrameworkElement? Find(string spec, bool quiet = false)
        {
            var parts = spec.Split(" > ");
            var roots = OverWindows().AsEnumerable().Reverse().Select(w => (DependencyObject)w.Content).Append(Root).ToList();
            FrameworkElement? hit = null;
            for (int pi = 0; pi < parts.Length; pi++)
            {
                string part = parts[pi];
                int idx = 0;
                int hash = part.LastIndexOf('#');
                if (hash > 0 && int.TryParse(part[(hash + 1)..], out var n)) { idx = n; part = part[..hash]; }
                int colon = part.IndexOf(':');
                string kind = part[..colon], key = part[(colon + 1)..];
                var scope = pi == 0 ? roots : new List<DependencyObject> { hit! };
                hit = null;
                foreach (var root in scope)
                {
                    hit = FindIn(root, kind, key, idx);
                    if (hit != null) break;
                }
                if (hit == null)
                {
                    if (!quiet) Log($"  찾기 실패 {spec} (단계 '{parts[pi]}')");
                    return null;
                }
            }
            return hit;
        }

        private static bool MatchText(FrameworkElement e, string kind, string key) => kind switch
        {
            "name" => e.Name == key,
            "text" => (e is TextBlock tb && (tb.Text == key || InlineText(tb) == key)) || (e is ContentControl cc && cc.Content is string s && s == key),
            "textp" => (e is TextBlock tb2 && (tb2.Text.StartsWith(key, StringComparison.Ordinal) || InlineText(tb2).StartsWith(key, StringComparison.Ordinal)))
                       || (e is ContentControl cc2 && cc2.Content is string s2 && s2.StartsWith(key, StringComparison.Ordinal)),
            _ => false,
        };

        private static bool Vis(FrameworkElement e) => e.ActualWidth > 0 && IsShown(e);

        private FrameworkElement? FindIn(DependencyObject root, string kind, string key, int idx)
        {
            switch (kind)
            {
                case "combo": return FindAll<ComboBox>(root).Where(Vis).ElementAtOrDefault(int.TryParse(key, out var ci) ? ci : 0);
                case "textbox": return FindAll<TextBox>(root).Where(Vis).ElementAtOrDefault(int.TryParse(key, out var xi) ? xi : 0);
                case "check": return FindAll<CheckBox>(root).Where(Vis).ElementAtOrDefault(int.TryParse(key, out var ki) ? ki : 0);
                case "sv": return FindAll<ScrollViewer>(root).Where(Vis).ElementAtOrDefault(int.TryParse(key, out var si) ? si : 0);
                case "page":   // 지금 보이는 페이지의 바깥 ScrollViewer (가장 큰 것)
                    return FindAll<ScrollViewer>(root).Where(Vis).OrderByDescending(s => s.ActualHeight * s.ActualWidth).FirstOrDefault();
                case "li": return FindAll<ListBoxItem>(root).Where(r => Vis(r) && FindAll<TextBlock>(r).Any(t => t.Text == key || InlineText(t) == key)).ElementAtOrDefault(idx);
                case "cam":    // n 번째 카메라 카드
                {
                    var ic = FindAll<ItemsControl>(root).FirstOrDefault(c => ReferenceEquals(c.ItemsSource, _vm.Cameras) && c.ActualWidth > 0);
                    int ni = int.TryParse(key, out var cn) ? cn : 0;
                    return ic == null || ni >= ic.Items.Count ? null : ic.ItemContainerGenerator.ContainerFromIndex(ni) as FrameworkElement;
                }
                case "help":   // 도움말 ? — 제목(CustomTitle)으로
                    return FindAll<VMS.VisionSetup.Controls.HelpIcon>(root).Where(Vis).Where(h => h.CustomTitle == key).ElementAtOrDefault(idx);
                case "label":
                    return FindAll<TextBlock>(root).Where(t => Vis(t) && (t.Text == key || InlineText(t) == key)).ElementAtOrDefault(idx);
                case "field":  // 라벨 TextBlock 다음 형제(입력 컨트롤 · 그것을 감싼 패널 안의 첫 입력 컨트롤)
                {
                    var lab = FindAll<TextBlock>(root).Where(t => Vis(t) && (t.Text == key || InlineText(t) == key)).ElementAtOrDefault(idx);
                    if (lab == null || VisualTreeHelper.GetParent(lab) is not Panel panel) return null;
                    int at = panel.Children.IndexOf(lab);
                    for (int i = at + 1; i < panel.Children.Count; i++)
                    {
                        if (panel.Children[i] is not FrameworkElement sib || !Vis(sib)) continue;
                        if (sib is Control) return sib;
                        var inner = FindAll<Control>(sib).FirstOrDefault(c => Vis(c) && c is TextBox or PasswordBox or ComboBox or CheckBox);
                        if (inner != null) return inner;
                    }
                    return null;
                }
            }
            IEnumerable<FrameworkElement> all = FindAll<FrameworkElement>(root).Where(Vis);
            Func<FrameworkElement, bool> pred = kind switch
            {
                "name" or "text" or "textp" => e => MatchText(e, kind, key),
                "tip" => e => ToolTipText(e) == key,
                "cmd" => e => e is ButtonBase b && b.Command != null && ReferenceEquals(_vm.GetType().GetProperty(key)?.GetValue(_vm), b.Command),
                _ => _ => false,
            };
            var hits = all.Where(pred).ToList();
            if (kind == "text" || kind == "textp")   // 글자 TextBlock 이 버튼 안에 있으면 그 버튼을 대상으로
                hits = hits.Select(h => (FrameworkElement)(FindAncestor<ButtonBase>(h) ?? h)).Distinct().ToList();
            return idx < hits.Count ? hits[idx] : null;
        }

        private static string InlineText(TextBlock tb) =>
            string.Concat(tb.Inlines.OfType<System.Windows.Documents.Run>().Select(r => r.Text)).Trim();

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
                int c = d is Visual ? VisualTreeHelper.GetChildrenCount(d) : 0;
                for (int i = c - 1; i >= 0; i--) stack.Push(VisualTreeHelper.GetChild(d, i));
            }
            return list;
        }

        /// <summary>시나리오 작성용 — 보이는 버튼 · 글자 · 입력 칸의 프레임 위치를 로그에 남긴다 (암호 칸 값은 남기지 않음).</summary>
        private void Dump(string? filter)
        {
            var roots = OverWindows().AsEnumerable().Reverse().Select(w => (DependencyObject)w.Content).Append(Root);
            foreach (var root in roots)
                foreach (var e in FindAll<FrameworkElement>(root).Where(Vis))
                {
                    string? label = e switch
                    {
                        TextBlock tb => "text=" + (string.IsNullOrEmpty(tb.Text) ? InlineText(tb) : tb.Text),
                        CheckBox chk => $"check={chk.IsChecked}{(chk.IsEnabled ? "" : " 비활성")} content={chk.Content}",
                        TextBox tbx => $"textbox={tbx.Text}{(tbx.IsEnabled ? "" : " 비활성")}",
                        RadioButton rdo => $"radio={rdo.IsChecked} content={rdo.Content} tip={ToolTipText(rdo)}",
                        ButtonBase b => $"button cmd={(b.Command != null ? CommandName(b) ?? "?" : "-")}{(b.IsEnabled ? "" : " 비활성")} content={(b.Content as string)} tip={ToolTipText(b)}",
                        PasswordBox pbx => $"passwordbox name={pbx.Name} len={pbx.Password.Length} enabled={pbx.IsEnabled}",
                        ComboBox cbx => $"combo={cbx.SelectedItem} n={cbx.Items.Count}",
                        VMS.VisionSetup.Controls.HelpIcon h => "help=" + h.CustomTitle,
                        _ when !string.IsNullOrEmpty(e.Name) => "name=" + e.Name,
                        _ => null,
                    };
                    if (label == null || (filter != null && !label.Contains(filter, StringComparison.OrdinalIgnoreCase))) continue;
                    var r = Bounds(e);
                    Log($"  dump {e.GetType().Name} [{r.X:F0},{r.Y:F0},{r.Width:F0},{r.Height:F0}] {label.Replace('\n', ' ')}");
                }
        }

        /// <summary>
        /// 녹화 모드 서비스 가드 — App 이 녹화 인자로 뜰 때 원래 서비스를 감싼다.
        /// 되돌릴 수 없는 동작(관리자 권한 재실행 · Windows 서비스 · Program Files · ProgramData 쓰기 · OS 파일 대화상자)은 실행하지 않는다.
        /// </summary>
        public static class Guards
        {
            public sealed class Web : IWebServerSetupService
            {
                private readonly IWebServerSetupService _inner;
                private readonly Action<string>? _log;
                public Web(IWebServerSetupService inner, Action<string>? log = null) { _inner = inner; _log = log; }
                public WebServerStatus GetStatus() => _inner.GetStatus();
                public Task<WebServerConfigureResult> ConfigureAsync(string adminPassword)
                {
                    (_log ?? (s => _current?.Log(s)))("  ⚠ 녹화 모드 — Web 서버 구성 실행 차단");
                    return Task.FromResult(new WebServerConfigureResult(false, "녹화 모드 — 실행하지 않음"));
                }
                public Task<WebServerConfigureResult> StartServiceAsync()
                {
                    (_log ?? (s => _current?.Log(s)))("  ⚠ 녹화 모드 — Web 서비스 시작 차단");
                    return Task.FromResult(new WebServerConfigureResult(false, "녹화 모드 — 실행하지 않음"));
                }
                public Task<bool> SmokeTestAsync() => Task.FromResult(false);
            }

            public sealed class License : ILicenseSetupService
            {
                private readonly ILicenseSetupService _inner;
                public License(ILicenseSetupService inner) => _inner = inner;
                public string GetMachineFingerprint() => _inner.GetMachineFingerprint();
                public LicenseEvaluation GetStatus() => _inner.GetStatus();
                public LicenseEvaluation ValidateCandidate(string filePath) => _inner.ValidateCandidate(filePath);
                public Task<LicenseImportResult> ImportAsync(string sourcePath)
                {
                    _current?.Log("  ⚠ 녹화 모드 — 라이선스 설치 차단");
                    return Task.FromResult(new LicenseImportResult(false, "녹화 모드 — 실행하지 않음"));
                }
            }

            public sealed class Dialogs : IDialogService
            {
                private readonly IDialogService _inner;
                public Dialogs(IDialogService inner) => _inner = inner;
                public void ShowInformation(string message, string title) { _current?.Log($"  메시지 창(정보) '{title}'"); _inner.ShowInformation(message, title); }
                public void ShowError(string message, string title) { _current?.Log($"  메시지 창(오류) '{title}'"); _inner.ShowError(message, title); }
                public bool ShowConfirmation(string message, string title) => _inner.ShowConfirmation(message, title);
                public string? ShowOpenFileDialog(string title, string filter)
                {
                    _current?.Log("  ⚠ 녹화 모드 — 파일 열기 대화상자를 띄우지 않음(취소로 처리)");
                    return null;
                }
            }
        }
    }
}
#endif
