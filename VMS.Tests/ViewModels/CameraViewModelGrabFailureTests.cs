using System;
using System.IO;
using System.Threading.Tasks;
using VMS.Camera.Interfaces;
using VMS.Camera.Models;
using VMS.Interfaces;
using VMS.Models;
using VMS.PLC.Models;
using VMS.ViewModels;
using Xunit;

namespace VMS.Tests.ViewModels
{
    /// <summary>
    /// GrabOnceAsync / ManualInspect 의 grab 실패 전달 검증 —
    /// 현장 사고(2026-08-19) 회귀: Auto Run grabFunc 가 무조건 true 를 반환해
    /// 카메라 케이블이 뽑힌 상태에서도 직전 프레임으로 검사가 계속되고,
    /// 이미지가 아예 없으면 OK 판정까지 나갔다.
    /// </summary>
    public class CameraViewModelGrabFailureTests
    {
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

        private sealed class FakeAcquisition : ICameraAcquisition
        {
            public bool IsConnected { get; set; }
            public bool ConnectResult { get; set; }
            public AcquisitionResult AcquireResult { get; set; } = new() { Success = false };
            public int AcquireCallCount { get; private set; }
            public int ConnectCallCount { get; private set; }

            public event EventHandler<string>? ConnectionLost;

            public void RaiseConnectionLost(string reason)
            {
                IsConnected = false;
                ConnectionLost?.Invoke(this, reason);
            }

            public Task<bool> ConnectAsync(CameraInfo camera)
            {
                ConnectCallCount++;
                IsConnected = ConnectResult;
                return Task.FromResult(ConnectResult);
            }

            public Task DisconnectAsync() => Task.CompletedTask;

            public Task<AcquisitionResult> AcquireAsync(int timeoutMs = 5000)
            {
                AcquireCallCount++;
                return Task.FromResult(AcquireResult);
            }

            public void Dispose() { }
        }

        private static CameraViewModel MakeVm(FakeAcquisition acquisition)
        {
            var vm = new CameraViewModel(new NoopDialogService(), new StubConfigService())
            {
                Id = "cam-1",
                Name = "Cam1"
            };
            vm.SetAcquisitionForTest(acquisition);
            return vm;
        }

        [Fact]
        public async Task GrabOnce_AcquireFails_ReturnsFalse()
        {
            var acq = new FakeAcquisition
            {
                IsConnected = true,
                AcquireResult = new AcquisitionResult { Success = false, Message = "획득 오류: 케이블 분리" }
            };
            var vm = MakeVm(acq);

            var ok = await vm.GrabOnceAsync();

            Assert.False(ok);
            Assert.Equal(1, acq.AcquireCallCount);
            Assert.Contains("케이블", vm.ResultMessage);
        }

        [Fact]
        public async Task GrabOnce_ConnectFails_ReturnsFalse()
        {
            var acq = new FakeAcquisition { IsConnected = false, ConnectResult = false };
            var vm = MakeVm(acq);

            var ok = await vm.GrabOnceAsync();

            Assert.False(ok);
            Assert.Equal(0, acq.AcquireCallCount);
            Assert.False(vm.IsConnected);
        }

        [Fact]
        public async Task GrabOnce_AcquireSucceeds_ReturnsTrue()
        {
            // Image2D/PointCloud 없이 Success 만 — 변환 경로 없이 반환값만 검증
            var acq = new FakeAcquisition
            {
                IsConnected = true,
                AcquireResult = new AcquisitionResult { Success = true }
            };
            var vm = MakeVm(acq);

            var ok = await vm.GrabOnceAsync();

            Assert.True(ok);
        }

        [Fact]
        public async Task ConnectionLost_DropsIsConnected_AndNextGrabReconnects()
        {
            // 현장 사고(2026-08-19) 회귀: 케이블이 뽑혀도 IsConnected 가 true 로 남아
            // GrabAsync 의 재연결 분기(!IsConnected)에 영원히 진입하지 못했다.
            var acq = new FakeAcquisition
            {
                IsConnected = true,
                ConnectResult = true,
                AcquireResult = new AcquisitionResult { Success = true }
            };
            var vm = MakeVm(acq);
            vm.IsConnected = true;

            acq.RaiseConnectionLost("케이블 분리");

            Assert.False(vm.IsConnected);
            Assert.Contains("연결 끊김", vm.ResultMessage);

            // 케이블 재연결 후 다음 grab — 플래그가 내려가 있으므로 자동 재연결되어야 한다
            var ok = await vm.GrabOnceAsync();

            Assert.True(ok);
            Assert.Equal(1, acq.ConnectCallCount);
            Assert.True(vm.IsConnected);
        }

        [Fact]
        public async Task ManualInspect_NoImage_SetsNg()
        {
            // 이미지가 한 번도 획득되지 않은 상태에서 검사 → 과거엔 OK, 이제 NG
            var vm = MakeVm(new FakeAcquisition());

            await vm.ManualInspectCommand.ExecuteAsync(null);

            Assert.False(vm.InspectionOk);
            Assert.True(vm.IsInspected);
            Assert.Equal("No image to inspect", vm.ResultMessage);
        }
    }
}
