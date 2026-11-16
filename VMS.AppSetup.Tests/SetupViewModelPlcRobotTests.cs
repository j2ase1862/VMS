using System;
using System.IO;
using VMS.AppSetup.Interfaces;
using VMS.AppSetup.Models;
using VMS.AppSetup.ViewModels;
using VMS.PLC.Models;
using Xunit;
using RobotProtocolMode = VMS.Camera.Models.RobotProtocolMode;

namespace VMS.AppSetup.Tests
{
    /// <summary>
    /// 설정 마법사 4단계(PLC) · 5단계(로봇) 화면 동작 검증.
    /// VO-06 · VO-07: PLC 연결 코드가 읽지 않는 항목은 화면에서 빠지고, 저장된 값은 보존된다.
    /// VO-09: 로봇 프로토콜 전환 때 포트는 새 프로토콜 기본값으로 — 직접 바꾼 포트는 유지.
    /// </summary>
    public class SetupViewModelPlcRobotTests
    {
        private sealed class StubConfigService : IConfigurationService
        {
            public SetupConfiguration? Config;
            public SetupConfiguration? Saved;
            public string? LastLoadError { get; set; }
            public string ConfigFilePath => @"test:\system_config.json";
            public string InvalidBackupPath => @"test:\system_config.json.invalid.bak";
            public SetupConfiguration? LoadConfiguration() => Config;
            public bool SaveConfiguration(SetupConfiguration config) { Saved = config; return true; }
            public bool ConfigurationExists() => Config != null;
            public bool ExportConfiguration(SetupConfiguration config, string exportPath) => true;
        }

        private sealed class StubDialogService : IDialogService
        {
            public void ShowInformation(string message, string title) { }
            public void ShowError(string message, string title) { }
            public bool ShowConfirmation(string message, string title) => true;
            public string? ShowOpenFileDialog(string title, string filter) => null;
        }

        private static SetupViewModel CreateVm(SetupConfiguration? config = null)
            => new(new StubConfigService { Config = config }, new StubDialogService(), () => { });

        // ── VO-09 ──

        [Fact]
        public void RobotProtocol_ModbusToCustomSocket_PortReturnsToVendorDefault()
        {
            var vm = CreateVm();
            vm.IsRobotEnabled = true;
            vm.SelectedRobotVendor = RobotVendor.Doosan;
            Assert.Equal(12345, vm.RobotPort);

            vm.SelectedRobotProtocolMode = RobotProtocolMode.ModbusTcp;
            Assert.Equal(502, vm.RobotPort);

            vm.SelectedRobotProtocolMode = RobotProtocolMode.CustomSocket;
            Assert.Equal(12345, vm.RobotPort);   // 예전엔 502 가 남았다
        }

        [Fact]
        public void RobotProtocol_UserEditedPort_IsKeptOnProtocolChange()
        {
            var vm = CreateVm();
            vm.SelectedRobotVendor = RobotVendor.Doosan;
            vm.SelectedRobotProtocolMode = RobotProtocolMode.ModbusTcp;
            vm.RobotPort = 1502;                 // 사용자가 직접 바꿈

            vm.SelectedRobotProtocolMode = RobotProtocolMode.CustomSocket;
            Assert.Equal(1502, vm.RobotPort);

            vm.SelectedRobotProtocolMode = RobotProtocolMode.VendorNative;
            Assert.Equal(1502, vm.RobotPort);
        }

        [Fact]
        public void RobotProtocol_NativeToModbusAndBack_UsesDefaults()
        {
            var vm = CreateVm();
            vm.SelectedRobotVendor = RobotVendor.Doosan;
            vm.SelectedRobotProtocolMode = RobotProtocolMode.ModbusTcp;
            Assert.Equal(502, vm.RobotPort);
            vm.SelectedRobotProtocolMode = RobotProtocolMode.VendorNative;
            Assert.Equal(12345, vm.RobotPort);
        }

        [Fact]
        public void RobotVendorChange_FromModbus_ResetsToNewVendorDefault()
        {
            var vm = CreateVm();
            vm.SelectedRobotVendor = RobotVendor.Doosan;
            vm.SelectedRobotProtocolMode = RobotProtocolMode.ModbusTcp;

            vm.SelectedRobotVendor = RobotVendor.UR;
            Assert.Equal(RobotProtocolMode.VendorNative, vm.SelectedRobotProtocolMode);
            Assert.Equal(30003, vm.RobotPort);
        }

        [Theory]
        [InlineData(RobotVendor.UR, RobotProtocolMode.CustomSocket, 30003)]
        [InlineData(RobotVendor.Fanuc, RobotProtocolMode.VendorNative, 18735)]
        [InlineData(RobotVendor.Doosan, RobotProtocolMode.ModbusTcp, 502)]
        public void DefaultRobotPort_ByVendorAndProtocol(RobotVendor vendor, RobotProtocolMode mode, int expected)
            => Assert.Equal(expected, SetupViewModel.GetDefaultRobotPort(vendor, mode));

        [Fact]
        public void DefaultRobotPort_NoVendor_IsNull()
            => Assert.Null(SetupViewModel.GetDefaultRobotPort(RobotVendor.None, RobotProtocolMode.ModbusTcp));

        // ── VO-06 · VO-07 ──

        [Fact]
        public void HiddenPlcSettings_SavedValues_AreKeptForResave()
        {
            var stub = new StubConfigService
            {
                Config = new SetupConfiguration
                {
                    PlcVendor = PlcVendor.Mitsubishi,
                    CommunicationType = PlcCommunicationType.Serial,
                    SerialPortName = "COM7",
                    PollingIntervalMs = 33,
                    UseHeartbeat = true,
                    HeartbeatAddress = "D100",
                    WriteMode = PlcWriteMode.SingleShot
                }
            };
            var vm = new SetupViewModel(stub, new StubDialogService(), () => { });

            Assert.Equal(PlcCommunicationType.Serial, vm.SelectedCommunicationType);
            Assert.Equal(33, vm.PollingIntervalMs);
            Assert.Equal("D100", vm.HeartbeatAddress);
            Assert.Equal(PlcWriteMode.SingleShot, vm.WriteMode);
        }

        [Fact]
        public void PlcPage_DoesNotShowSettingsTheConnectionIgnores()
        {
            var xaml = File.ReadAllText(Path.Combine(FindRepoRoot(), "VMS.AppSetup", "MainWindow.xaml"));

            // 연결 코드가 읽지 않는 항목(VO-06 · VO-07) — 화면 바인딩이 없어야 한다.
            foreach (var binding in new[]
                     {
                         "{Binding SelectedCommunicationType", "{Binding SerialPortName", "{Binding BaudRate",
                         "{Binding DataBits", "{Binding Parity", "{Binding StopBits", "{Binding PollingIntervalMs",
                         "{Binding UseHeartbeat", "{Binding HeartbeatAddress", "{Binding WriteMode"
                     })
            {
                Assert.DoesNotContain(binding, xaml);
            }

            // 실제로 쓰이는 항목은 남는다.
            foreach (var binding in new[]
                     {
                         "{Binding PlcIpAddress", "{Binding PlcPort", "{Binding ModbusUnitId",
                         "{Binding AutoReconnect", "{Binding EndianMode"
                     })
            {
                Assert.Contains(binding, xaml);
            }
        }

        private static string FindRepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "VMS.sln")))
                dir = dir.Parent;
            return dir?.FullName ?? throw new DirectoryNotFoundException("VMS.sln 을 찾지 못했습니다.");
        }
    }
}
