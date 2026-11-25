using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using VMS.Services.LocalHistory;
using VMS.ViewModels;
using VMS.Views;
using Xunit;

namespace VMS.Tests.Views
{
    /// <summary>
    /// 생산 이력 조회 창 스모크 — 실제 XAML 을 VMS 앱 리소스(App.xaml)와 함께 띄워 데이터 · 세 탭 · 상세를 그려 본다.
    ///
    /// v1.28.0 은 읽기 전용 속성에 TwoWay 바인딩된 체크박스 열 때문에 창이 열리자마자 예외가 났는데,
    /// VM 테스트로는 잡히지 않고 매뉴얼 캡처(--capture-dialogs)에서야 드러났다(#418). 이 테스트는
    /// 그런 XAML 오류(바인딩 예외 · 없는 리소스 키 · 잘못된 바인딩 경로)를 CI 에서 잡는다.
    /// 창은 화면에 띄우지 않고 측정 · 배치만 한다(CI 데스크톱 없이도 돈다).
    /// </summary>
    [Collection(WpfAppCollection.Name)]
    public class InspectionHistoryWindowSmokeTests : IDisposable
    {
        private readonly string _tempDir;
        private readonly LocalInspectionHistoryStore _store;

        public InspectionHistoryWindowSmokeTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), $"insp_win_{Guid.NewGuid():N}");
            _store = new LocalInspectionHistoryStore(_tempDir);
        }

        public void Dispose()
        {
            _store.Dispose();
            try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); }
            catch { /* 이미지 · SQLite 핸들 잔존 가능 — 무시 */ }
        }

        private string WriteSamplePng()
        {
            var path = Path.Combine(_tempDir, "ng_sample.png");
            var bmp = BitmapSource.Create(8, 8, 96, 96, PixelFormats.Gray8, null, new byte[64], 8);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(bmp));
            using var fs = File.Create(path);
            enc.Save(fs);
            return path;
        }

        private void Seed()
        {
            var png = WriteSamplePng();
            var t0 = DateTime.UtcNow.AddMinutes(-30);
            for (int i = 0; i < 12; i++)
            {
                var ok = i % 3 != 0;
                _store.Record(new LocalInspectionEntry
                {
                    InspectedAtUtc = t0.AddSeconds(i * 20),
                    IsPass = ok,
                    RecipeName = i % 2 == 0 ? "A동 1호, 표준" : "R2",   // 콤마 · 한글 이름
                    NgCodes = ok ? new List<string>() : new List<string> { "Blob 1", "Edge 2" },
                    CorrelationKey = $"k{i}",
                    // NG 하나는 실제 사진, 하나는 지워진 사진(파일 없음 안내), 나머지는 이미지 없음
                    ImagePath = i == 0 ? png : i == 3 ? Path.Combine(_tempDir, "missing.png") : null,
                    ImageIsNg = !ok,
                    CycleTimeMs = 40 + i,
                    WorkOrderId = i == 5 ? 11 : null,
                    SerialNumber = i == 5 ? "SN-0005" : null,
                    ToolResults = new List<LocalToolResult>
                    {
                        new() { ToolName = "Blob 1", ToolType = "Blob", Success = ok, ExecutionTimeMs = 4.2, Message = ok ? null : "area < min" },
                        new() { ToolName = "Edge 2", ToolType = "Caliper", Success = ok, ExecutionTimeMs = 1.1 },
                    },
                    Mode = LocalInspectionMode.Cycle
                });
            }
            Assert.True(_store.Flush(TimeSpan.FromSeconds(10)));
        }

        [Fact]
        public void Window_RendersListTabs_AndDetail_WithoutXamlOrBindingErrors()
        {
            Seed();
            WpfAppHost.Invoke(() =>
            {
                using var errors = BindingErrorCollector.Start();

                var vm = new InspectionHistoryViewModel(_store);
                Assert.Equal(12, vm.TotalCount);

                var w = new InspectionHistoryWindow { DataContext = vm };
                var host = Detach(w, vm);
                try
                {
                    Layout(host);
                    var grids = FindAll<DataGrid>(host);
                    Assert.True(grids.Any(g => g.Items.Count == 12),   // 목록 표가 실제로 행을 만든다
                        $"DataGrid {grids.Count}개, 항목 수 [{string.Join(",", grids.Select(g => g.Items.Count))}]");
                    Assert.Contains(grids, g => FindAll<DataGridRow>(g).Count > 0);   // 행 · 셀(체크박스 열 포함)까지 생성

                    // 세 탭을 차례로 그려 본다 (목록 · 일별 집계 · NG 파레토)
                    var tabs = (TabControl)w.FindName("HistoryTabs");
                    Assert.Equal(3, tabs.Items.Count);
                    for (int i = 0; i < tabs.Items.Count; i++)
                    {
                        tabs.SelectedIndex = i;
                        Layout(host);
                    }
                    Assert.NotEmpty(vm.DailySummaries);
                    Assert.NotEmpty(vm.NgCodeRows);

                    // 상세 — 사진 있는 NG · 사진 파일이 없는 NG · 이미지 없는 OK
                    tabs.SelectedIndex = 0;
                    foreach (var key in new[] { "k0", "k3", "k1" })
                    {
                        vm.SelectedEntry = vm.Entries.Single(e => e.CorrelationKey == key);
                        Layout(host);
                    }
                    vm.SelectedEntry = vm.Entries.Single(e => e.CorrelationKey == "k0");
                    Layout(host);
                    Assert.True(vm.HasDetailImage);
                }
                finally
                {
                    w.Close();
                }

                Assert.True(errors.Messages.Count == 0,
                    "바인딩 오류:\n" + string.Join("\n", errors.Messages));
            });
        }

        [Fact]
        public void Window_Empty_AndWebIntegrated_Renders()
        {
            WpfAppHost.Invoke(() =>
            {
                using var errors = BindingErrorCollector.Start();
                var vm = new InspectionHistoryViewModel(_store, isWebIntegrated: true);
                var w = new InspectionHistoryWindow { DataContext = vm };
                var host = Detach(w, vm);
                try
                {
                    Layout(host);
                    Assert.Equal(0, vm.TotalCount);
                    Assert.False(vm.HasDetail);
                }
                finally { w.Close(); }
                Assert.True(errors.Messages.Count == 0,
                    "바인딩 오류:\n" + string.Join("\n", errors.Messages));
            });
        }

        /// <summary>
        /// 띄우지 않은 Window 는 Visibility 가 Collapsed 라 측정해도 내용을 그리지 않는다(Visible 로 바꾸면 Show 가 된다).
        /// 내용을 떼어 같은 크기의 Border 에 옮기고 창 리소스도 함께 옮긴다 — 화면 없이 그대로 그릴 수 있다.
        /// </summary>
        private static Border Detach(Window w, object dataContext)
        {
            var content = (UIElement)w.Content;
            w.Content = null;
            var host = new Border
            {
                Child = content,
                DataContext = dataContext,
                Width = w.Width > 0 && !double.IsNaN(w.Width) ? w.Width : 1400,
                Height = w.Height > 0 && !double.IsNaN(w.Height) ? w.Height : 900,
            };
            foreach (var md in w.Resources.MergedDictionaries) host.Resources.MergedDictionaries.Add(md);
            foreach (var key in w.Resources.Keys) host.Resources[key] = w.Resources[key];
            return host;
        }

        private static void Layout(FrameworkElement host)
        {
            var size = new Size(host.Width, host.Height);
            host.Measure(size);
            host.Arrange(new Rect(size));
            host.UpdateLayout();
            DoEvents();   // DataBind · Loaded 우선순위 작업까지 처리
            host.UpdateLayout();
        }

        private static void DoEvents()
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background,
                new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
        }

        private static List<T> FindAll<T>(DependencyObject root) where T : DependencyObject
        {
            var list = new List<T>();
            var stack = new Stack<DependencyObject>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                var cur = stack.Pop();
                if (cur is T t) list.Add(t);
                var n = VisualTreeHelper.GetChildrenCount(cur);
                for (int i = 0; i < n; i++) stack.Push(VisualTreeHelper.GetChild(cur, i));
            }
            return list;
        }
    }

    /// <summary>
    /// WPF 창 테스트가 같은 STA 스레드 · 같은 Application(VMS App.xaml 리소스)을 쓰도록 묶는다.
    /// 바인딩 오류 수집이 프로세스 전체 추적이므로 다른 테스트와 겹치지 않게 병렬 실행도 끈다.
    /// </summary>
    [CollectionDefinition(Name, DisableParallelization = true)]
    public sealed class WpfAppCollection
    {
        public const string Name = "WPF App (STA)";
    }

    /// <summary>
    /// 프로세스에 하나뿐인 VMS Application 을 전용 STA 스레드에 띄우고 그 Dispatcher 로 작업을 넘긴다.
    /// App.xaml 의 리소스만 불러오고(InitializeComponent) OnStartup 은 돌리지 않는다.
    /// </summary>
    internal static class WpfAppHost
    {
        private static readonly Lazy<Dispatcher> _dispatcher = new(Start, LazyThreadSafetyMode.ExecutionAndPublication);

        // 바인딩 예외처럼 Dispatcher 작업 안에서 난 예외 — 처리하지 않으면 테스트 호스트가 통째로 죽는다.
        // 여기서 받아 두었다가 그 작업을 넘긴 테스트의 실패로 돌려준다.
        private static Exception? _dispatcherError;

        private static Dispatcher Start()
        {
            Dispatcher? dispatcher = null;
            Exception? error = null;
            using var ready = new ManualResetEventSlim(false);
            var thread = new Thread(() =>
            {
                try
                {
                    var app = Application.Current as VMS.App ?? new VMS.App();
                    app.InitializeComponent();
                    dispatcher = Dispatcher.CurrentDispatcher;
                    dispatcher.UnhandledException += (_, e) =>
                    {
                        _dispatcherError ??= e.Exception;
                        e.Handled = true;
                    };
                }
                catch (Exception ex) { error = ex; }
                ready.Set();
                if (error == null) Dispatcher.Run();
            })
            {
                IsBackground = true,
                Name = "WpfAppHost"
            };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            ready.Wait();
            if (error != null) throw new InvalidOperationException("VMS App 리소스를 불러오지 못함", error);
            return dispatcher!;
        }

        public static void Invoke(Action action)
        {
            Exception? error = null;
            var d = _dispatcher.Value;
            d.Invoke(() => _dispatcherError = null);
            d.Invoke(() =>
            {
                try { action(); }
                catch (Exception ex) { error = ex; }
            });
            d.Invoke(() => { }, DispatcherPriority.ContextIdle);   // 뒤에 남은 바인딩 · 레이아웃 작업까지 처리
            var late = d.Invoke(() => _dispatcherError);
            if (late != null) throw new Xunit.Sdk.XunitException("Dispatcher 예외(XAML · 바인딩): " + late);
            if (error != null) throw new Xunit.Sdk.XunitException(error.ToString());
        }
    }

    /// <summary>WPF 데이터 바인딩 추적에서 오류(경로 없음 · 변환 실패 등)를 모은다.</summary>
    internal sealed class BindingErrorCollector : TraceListener
    {
        private readonly SourceLevels _previousLevel;
        public List<string> Messages { get; } = new();

        private BindingErrorCollector()
        {
            _previousLevel = PresentationTraceSources.DataBindingSource.Switch.Level;
            PresentationTraceSources.Refresh();
            PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Error;
            PresentationTraceSources.DataBindingSource.Listeners.Add(this);
        }

        public static BindingErrorCollector Start() => new();

        public override void Write(string? message) { }

        public override void WriteLine(string? message)
        {
            if (!string.IsNullOrWhiteSpace(message)) lock (Messages) Messages.Add(message);
        }

        protected override void Dispose(bool disposing)
        {
            PresentationTraceSources.DataBindingSource.Listeners.Remove(this);
            PresentationTraceSources.DataBindingSource.Switch.Level = _previousLevel;
            base.Dispose(disposing);
        }
    }
}
