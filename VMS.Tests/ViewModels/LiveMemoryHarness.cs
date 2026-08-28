using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using OpenCvSharp;
using VMS.Camera.Interfaces;
using VMS.Camera.Models;
using VMS.Interfaces;
using VMS.Models;
using VMS.PLC.Models;
using VMS.ViewModels;
using Xunit;
using Xunit.Abstractions;

namespace VMS.Tests.ViewModels
{
    /// <summary>
    /// 수동 진단 하니스 — 실증 PC 라이브 메모리 증가(2026-08-28) 원인 분리용.
    /// 실제 CameraViewModel 라이브 루프를 Basler 와 동일 규격(2448×2048 BGR, 15MB)
    /// 프레임을 뱉는 가짜 획득기로 구동해, 관리 경로(루프+표시 비트맵)만으로
    /// 메모리가 증가하는지 측정한다. 평탄하면 누수는 pylon/네이티브 계층.
    /// CI 에서는 돌지 않는다 — VMS_LIVE_HARNESS=1 로만 활성.
    /// </summary>
    public class LiveMemoryHarness
    {
        private readonly ITestOutputHelper _output;
        public LiveMemoryHarness(ITestOutputHelper output) => _output = output;

        private sealed class StubConfigService : IConfigurationService
        {
            public string ConfigDirectory => Path.GetTempPath();
            public SystemConfiguration LoadSystemConfiguration() => new();
            public bool SaveSystemConfiguration(SystemConfiguration config) => true;
            public LayoutConfiguration LoadLayoutConfiguration() => new();
            public bool SaveLayoutConfiguration(LayoutConfiguration config) => true;
            public PlcSignalConfiguration LoadPlcSignalConfiguration() => new();
            public bool SavePlcSignalConfiguration(PlcSignalConfiguration config) => true;
        }

        private sealed class NoopDialogService : IDialogService
        {
            public string? ShowSaveFileDialog(string filter, string defaultExt, string? fileName = null) => null;
            public string? ShowOpenFileDialog(string filter, string defaultExt) => null;
            public void ShowInformation(string message, string title) { }
            public void ShowWarning(string message, string title) { }
            public void ShowError(string message, string title) { }
            public bool ShowConfirmation(string message, string title) => false;
            public bool ShowLoginDialog(IUserService userService) => false;
        }

        /// <summary>Basler acA2440-20gc 와 동일 규격 프레임을 ~20fps 로 생성.</summary>
        private sealed class BigFrameAcquisition : ICameraAcquisition
        {
            public bool IsConnected { get; private set; } = true;
            public long FramesProduced;
            public event EventHandler<string>? ConnectionLost { add { } remove { } }

            public Task<bool> ConnectAsync(CameraInfo camera)
            {
                IsConnected = true;
                return Task.FromResult(true);
            }

            public Task DisconnectAsync() => Task.CompletedTask;

            public async Task<AcquisitionResult> AcquireAsync(int timeoutMs = 5000)
            {
                await Task.Delay(50); // ~20fps
                var mat = new Mat(2048, 2448, MatType.CV_8UC3, Scalar.All(64));
                Interlocked.Increment(ref FramesProduced);
                return new AcquisitionResult { Success = true, Image2D = mat };
            }

            public void Dispose() { }
        }

        [Fact]
        public async Task LiveLoop_ManagedPath_MemoryStaysFlat()
        {
            if (Environment.GetEnvironmentVariable("VMS_LIVE_HARNESS") != "1")
                return; // 수동 전용 — CI/일반 테스트에서는 no-op

            // 라이브 루프의 Dispatcher.InvokeAsync 가 실제로 펌핑되도록 WPF Application 구동
            Application? app = null;
            using var appReady = new ManualResetEventSlim(false);
            var appThread = new Thread(() =>
            {
                app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                app.Startup += (_, _) => appReady.Set();
                app.Run();
            });
            appThread.SetApartmentState(ApartmentState.STA);
            appThread.IsBackground = true;
            appThread.Start();
            Assert.True(appReady.Wait(10000), "WPF Application 기동 실패");

            var acq = new BigFrameAcquisition();
            CameraViewModel vm = null!;
            await app!.Dispatcher.InvokeAsync(() =>
            {
                vm = new CameraViewModel(new NoopDialogService(), new StubConfigService())
                {
                    Id = "cam-1",
                    Name = "HarnessCam"
                };
                vm.SetAcquisitionForTest(acq);
            });

            await vm.StartLiveGrabAsync();

            var proc = Process.GetCurrentProcess();
            const int seconds = 45;
            long firstPriv = 0, lastPriv = 0;
            for (int t = 0; t <= seconds; t += 5)
            {
                proc.Refresh();
                long priv = proc.PrivateMemorySize64 / (1024 * 1024);
                long ws = proc.WorkingSet64 / (1024 * 1024);
                long gc = GC.GetTotalMemory(false) / (1024 * 1024);
                _output.WriteLine(
                    $"t={t,3}s  frames={Interlocked.Read(ref acq.FramesProduced),5}  " +
                    $"private={priv,5}MB  workingSet={ws,5}MB  gcHeap={gc,4}MB");
                if (t == 10) firstPriv = priv;   // 워밍업(비트맵 생성 등) 이후 기준점
                lastPriv = priv;
                if (t < seconds) await Task.Delay(5000);
            }

            await vm.StopLiveGrabAsync();
            await app.Dispatcher.InvokeAsync(() => app.Shutdown());

            var growth = lastPriv - firstPriv;
            _output.WriteLine($"성장량 (t=10s 이후): {growth}MB / {seconds - 10}s");
            // 15MB/프레임 규모 누수라면 35초 동안 수 GB — 200MB 는 여유 있는 상한
            Assert.True(growth < 200,
                $"라이브 관리 경로 누수 의심: {growth}MB 증가 (35s)");
        }
    }
}
