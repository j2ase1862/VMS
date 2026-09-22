using System;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Xunit;

namespace VMS.VisionSetup.Tests
{
    /// <summary>
    /// WPF 컨트롤을 테스트에서 만들기 위한 STA 환경.
    ///
    /// <para>컨트롤 XAML 이 <c>{StaticResource BrushTextMuted}</c> 처럼 앱 스타일을 참조하는데,
    /// StaticResource 는 XAML 로드 시점에 해석되고 그때 컨트롤은 아직 트리에 붙기 전이라
    /// <see cref="Application"/>.Resources 말고는 찾을 곳이 없다. 그래서 Application 을 만든다.</para>
    ///
    /// <para><b>수명 주의</b> — Application 을 만들고 그냥 두면 테스트 호스트가 종료되지 못해
    /// 실행이 끝나도 프로세스가 남는다(실제로 겪었다). 전용 STA 스레드에서 Dispatcher 를 돌리고
    /// Dispose 에서 명시적으로 내린다. 스레드는 background 라 최악의 경우에도 종료를 막지 않는다.</para>
    /// </summary>
    public sealed class WpfUiFixture : IDisposable
    {
        private readonly Thread _thread;
        private Dispatcher _dispatcher = null!;

        public WpfUiFixture()
        {
            using var ready = new ManualResetEventSlim();

            _thread = new Thread(() =>
            {
                var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                foreach (var name in new[] { "MenuStyles", "ToolSettingsStyles", "WindowStyles" })
                {
                    app.Resources.MergedDictionaries.Add(new ResourceDictionary
                    {
                        Source = new Uri($"pack://application:,,,/VMS.VisionSetup;component/Styles/{name}.xaml")
                    });
                }

                _dispatcher = Dispatcher.CurrentDispatcher;
                ready.Set();
                Dispatcher.Run();
            })
            {
                IsBackground = true,
                Name = "WpfUiFixture",
            };
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start();

            Assert.True(ready.Wait(TimeSpan.FromSeconds(30)), "STA 스레드 준비 실패");
        }

        /// <summary>UI 스레드에서 실행하고 예외는 호출자에게 그대로 전달한다.</summary>
        public void Run(Action action) => _dispatcher.Invoke(action);

        public void Dispose()
        {
            _dispatcher.InvokeShutdown();
            _thread.Join(TimeSpan.FromSeconds(5));
        }
    }

    [CollectionDefinition(Name)]
    public class WpfUiCollection : ICollectionFixture<WpfUiFixture>
    {
        public const string Name = "WpfUi";
    }
}
