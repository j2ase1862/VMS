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
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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
    /// 창 루트를 RenderTargetBitmap 으로 그려 JPEG 프레임과 시각(timeline.json)을 남긴다.
    /// 커서 · 클릭 물결 · 강조 상자는 프레임 위에 직접 그린다 — 실제 마우스는 움직이지 않는다.</para>
    ///
    /// 환경 변수: VMS_RECORD_SCENARIO = 시나리오 JSON, VMS_RECORD_DURATIONS = 구간별 내레이션 길이(초) JSON.
    /// 출력: &lt;폴더&gt;/frames/f000000.jpg …, &lt;폴더&gt;/timeline.json {frames:[{name,t}], marks:[{id,t}]}, _record.log
    /// </summary>
    public sealed class VideoRecorder
    {
        private const int W = 1920, H = 1080;
        private const double Gap = 0.7;

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
        private string? _ghost;               // 끌기 중 커서 옆 도구 이름
        private Point? _lineFrom;            // 연결 중 임시 선 시작점
        private Brush _lineBrush = Brushes.DeepSkyBlue;

        private static readonly Brush Accent = Freeze(new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x1A)));

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
            w.Left = -6000; w.Top = 0;
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
            try { await rec.RunScenarioAsync(); }
            catch (Exception ex) { rec.Log("FATAL " + ex); }
            finally { await rec.FinishAsync(); }
        }

        // ───────────────────────── 프레임 ─────────────────────────

        private static double Now() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
        private static T Freeze<T>(T f) where T : Freezable { f.Freeze(); return f; }
        private FrameworkElement Root => (FrameworkElement)_win.Content;

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
                double t = Now();
                var rtb = new RenderTargetBitmap(W, H, 96, 96, PixelFormats.Pbgra32);
                var bg = new DrawingVisual();
                using (var dc = bg.RenderOpen())
                    dc.DrawRectangle(_win.Background ?? Brushes.Black, null, new Rect(0, 0, W, H));
                rtb.Render(bg);
                rtb.Render(Root);
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
            // 연결 중 임시 선
            if (_lineFrom is Point lf)
            {
                var pen = new Pen(_lineBrush, 3) { DashStyle = DashStyles.Dash };
                dc.DrawLine(pen, lf, _cur);
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
                        foreach (var tEl in a.GetProperty("targets").EnumerateArray())
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
                    case "loadRecipe":
                    {
                        var recipe = Services.RecipeService.Instance.LoadRecipe(a.GetProperty("path").GetString()!);
                        if (recipe == null) { Log("레시피 로드 실패"); break; }
                        Services.RecipeService.Instance.SetCurrentRecipe(recipe);
                        await Settle();
                        int si = a.TryGetProperty("step", out var s) ? s.GetInt32() : 0;
                        if (si < recipe.Steps.Count) _vm.SelectedStep = recipe.Steps[si];
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
                        var tv = (TreeView?)Find("name:ToolPaletteTreeView");
                        foreach (var tvi in FindAll<TreeViewItem>(tv!))
                            if (tvi.DataContext is ToolCategory c && c.CategoryName == cat) { tvi.IsExpanded = true; tvi.BringIntoView(); }
                        await Settle();
                        break;
                    }
                    case "addTool": await AddToolAsync(a); break;
                    case "selectTool":
                    {
                        var item = Tool(a.GetProperty("tool").GetString()!);
                        await ClickVisual(NodeOf(item));
                        bool force = a.TryGetProperty("force", out var fo) && fo.GetBoolean();   // 연결 뒤 설정 패널 갱신용
                        if (force && ReferenceEquals(_vm.SelectedTool, item)) { _vm.SelectedTool = null; await Settle(); }
                        if (!ReferenceEquals(_vm.SelectedTool, item)) _vm.SelectedTool = item;
                        await Settle();
                        break;
                    }
                    case "drawRoi": await DrawRoiAsync(a); break;
                    case "set":
                    {
                        var item = a.TryGetProperty("tool", out var tn) ? Tool(tn.GetString()!) : _vm.SelectedTool!;
                        if (a.TryGetProperty("target", out var stg)) await ClickVisual(Find(stg.GetString()!), Ms(a, 700));
                        foreach (var p in a.GetProperty("values").EnumerateObject())
                            SetProp(item.VisionTool!, p.Name, p.Value);
                        await Settle();
                        break;
                    }
                    case "cmd":
                    {
                        // VM 의 ICommand 실행 (TrainPatternCommand 등) — 커서는 target 으로 먼저 이동·클릭 연출
                        if (a.TryGetProperty("target", out var tg)) await ClickVisual(Find(tg.GetString()!));
                        var prop = _vm.GetType().GetProperty(a.GetProperty("name").GetString()!);
                        if (prop?.GetValue(_vm) is ICommand c && c.CanExecute(null)) c.Execute(null);
                        else Log("  명령 실행 불가: " + a.GetProperty("name").GetString());
                        await Settle();
                        break;
                    }
                    case "connect": await ConnectAsync(a); break;
                    case "settingsSet":
                    {
                        // 선택 도구의 설정 VM 속성 (IsExpertMode 등) — 커서는 target 으로 클릭 연출
                        if (a.TryGetProperty("target", out var tg)) await ClickVisual(Find(tg.GetString()!));
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
                    case "scrollTo": Find(a.GetProperty("target").GetString()!)?.BringIntoView(); await Settle(); break;
                    case "save":
                        if (a.TryGetProperty("target", out var st)) await ClickVisual(Find(st.GetString()!));
                        _vm.SaveCurrentRecipe();
                        await Settle();
                        break;
                    case "log": Log("  status: " + _vm.StatusMessage); break;
                    default: Log("알 수 없는 op " + op); break;
                }
            }
            catch (Exception ex) { Log($"op {op} 실패: {ex}"); }
            if (op != "seg" && op != "wait" && op != "move" && op != "hl" && op != "hlClear") Log($"  {op}: {_vm.StatusMessage}");
        }

        private static int Ms(JsonElement a, int def) => a.TryGetProperty("ms", out var m) ? m.GetInt32() : def;

        private async Task Settle()
        {
            for (int i = 0; i < 2; i++)
            {
                _win.UpdateLayout();
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
            switch (fe)
            {
                case TabItem ti: ti.IsSelected = true; break;
                case ToggleButton tb: tb.IsChecked = !(tb.IsChecked ?? false); break;
                case ButtonBase b:
                    if (b.Command != null && b.Command.CanExecute(b.CommandParameter)) b.Command.Execute(b.CommandParameter);
                    else b.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, b));
                    break;
                case TreeViewItem tvi: tvi.IsSelected = true; break;
            }
            await Settle();
        }

        private Point ResolvePoint(JsonElement a)
        {
            if (a.TryGetProperty("target", out var t))
            {
                var fe = Find(t.GetString()!);
                if (fe != null) { var r = Bounds(fe); return new Point(r.X + r.Width / 2, r.Y + r.Height / 2); }
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

        private async Task ConnectAsync(JsonElement a)
        {
            var from = Tool(a.GetProperty("from").GetString()!);
            var to = Tool(a.GetProperty("to").GetString()!);
            var type = Enum.Parse<ConnectionType>(a.GetProperty("type").GetString()!);
            _lineBrush = type switch
            {
                ConnectionType.Coordinates => new SolidColorBrush(Color.FromRgb(0x3B, 0x82, 0xF6)),
                ConnectionType.Result => Accent,
                _ => new SolidColorBrush(Color.FromRgb(0x22, 0xC5, 0x5E)),
            };
            var fn = NodeOf(from); var tn = NodeOf(to);
            if (fn != null) await ClickVisual(fn, 600);
            _lineFrom = _cur;
            if (tn != null) { var r = Bounds(tn); await MoveTo(new Point(r.X + r.Width / 2, r.Y + r.Height / 2), Ms(a, 900)); }
            _clickT = Now(); _clickP = _cur;
            _lineFrom = null;
            _vm.AddConnection(from, to, type);
            await Settle();
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
                var t when t.IsEnum => Enum.Parse(t, v.GetString()!),
                _ => null,
            };
            if (val == null) { Log($"  형식 미지원: {name} {p.PropertyType}"); return; }
            p.SetValue(target, val);
        }

        // ───────────────────────── 좌표 · 찾기 ─────────────────────────

        private Rect Bounds(FrameworkElement fe) =>
            fe.TransformToAncestor(Root).TransformBounds(new Rect(0, 0, fe.ActualWidth, fe.ActualHeight));

        private Image? DisplayImage() =>
            (Find("name:ImageCanvasControl") as FrameworkElement)?.FindName("DisplayImage") as Image;

        private Point ImagePoint(double x, double y)
        {
            var img = DisplayImage();
            return img != null ? img.TranslatePoint(new Point(x, y), Root) : new Point(x, y);
        }

        private Rect ImageRect(double x, double y, double w, double h) => new(ImagePoint(x, y), ImagePoint(x + w, y + h));

        private void ZoomFit()
        {
            var ic = Find("name:ImageCanvasControl");
            ic?.GetType().GetMethod("ZoomToFit", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(ic, null);
        }

        /// <summary>
        /// 대상 찾기: name:X(x:Name) · tip:X(ToolTip 글자) · text:X(보이는 글자, #n 으로 n번째) ·
        /// param:X(ParameterName) · tool:X(작업 공간 도구 노드) · auto:X(AutomationProperties.Name)
        /// </summary>
        private FrameworkElement? Find(string spec)
        {
            int idx = 0;
            int hash = spec.LastIndexOf('#');
            if (hash > 0 && int.TryParse(spec[(hash + 1)..], out var n)) { idx = n; spec = spec[..hash]; }
            int colon = spec.IndexOf(':');
            string kind = spec[..colon], key = spec[(colon + 1)..];
            if (kind == "tool") return NodeOf(Tool(key));
            if (kind == "row")    // Run Results 등 DataGrid 의 행 — 그 행 안의 글자로 찾는다
                return FindAll<DataGridRow>(Root).FirstOrDefault(r => r.ActualWidth > 0 && FindAll<TextBlock>(r).Any(t => t.Text == key));
            IEnumerable<FrameworkElement> all = FindAll<FrameworkElement>(Root).Where(e => e.ActualWidth > 0 && IsShown(e));
            Func<FrameworkElement, bool> pred = kind switch
            {
                "name" => e => e.Name == key,
                "tip" => e => e.ToolTip is string s && s == key,
                "auto" => e => AutomationNameOf(e) == key,
                "param" => e => e.GetType().GetProperty("ParameterName")?.GetValue(e) as string == key,
                "cmd" => e => e is ButtonBase b && b.Command != null && ReferenceEquals(b.Command, _vm.GetType().GetProperty(key)?.GetValue(_vm)),
                "text" => e => (e is TextBlock tb && tb.Text == key) || (e is ContentControl cc && cc.Content is string s2 && s2 == key),
                "textp" => e => e is TextBlock tb2 && tb2.Text.StartsWith(key, StringComparison.Ordinal),
                _ => _ => false,
            };
            var hits = all.Where(pred).ToList();
            if (kind == "text")   // 글자 TextBlock 이 버튼 안에 있으면 버튼을 대상으로
                hits = hits.Select(h => (FrameworkElement)(FindAncestor<ButtonBase>(h) ?? (FrameworkElement?)FindAncestor<TabItem>(h) ?? h)).Distinct().ToList();
            if (idx >= hits.Count && kind == "text")
            {
                var raw = FindAll<FrameworkElement>(Root).Where(pred).ToList();
                Log($"  찾기 실패 {spec}: 후보 {raw.Count} — " + string.Join(", ", raw.Select(r => $"{r.GetType().Name} w={r.ActualWidth:F0} vis={r.IsVisible}")));
                foreach (var r in raw.Where(r => r.ActualWidth > 0).Take(1))
                {
                    var chain = new List<string>();
                    DependencyObject? d = r;
                    while (d != null) { if (d is UIElement u && u.Visibility != Visibility.Visible) chain.Add($"{d.GetType().Name}({(d as FrameworkElement)?.Name}):{u.Visibility}"); d = VisualTreeHelper.GetParent(d); }
                    Log("    숨김 조상: " + string.Join(" < ", chain));
                }
            }
            return idx < hits.Count ? hits[idx] : null;
        }

        /// <summary>IsVisible 은 화면 밖 창에서 늦게 갱신될 때가 있어 조상 Visibility 를 직접 본다.</summary>
        private static bool IsShown(FrameworkElement e)
        {
            DependencyObject? d = e;
            while (d != null)
            {
                if (d is UIElement u && u.Visibility != Visibility.Visible) return false;
                d = VisualTreeHelper.GetParent(d);
            }
            return true;
        }

        private static string? AutomationNameOf(DependencyObject d) => System.Windows.Automation.AutomationProperties.GetName(d);

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
    }
}
#endif
