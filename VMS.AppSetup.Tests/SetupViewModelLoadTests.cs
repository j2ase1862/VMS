using System;
using System.Collections.Generic;
using VMS.AppSetup.Interfaces;
using VMS.AppSetup.Models;
using VMS.AppSetup.ViewModels;
using VMS.Camera.Models;
using VMS.PLC.Models;
using Xunit;
using RobotProtocolMode = VMS.Camera.Models.RobotProtocolMode;

namespace VMS.AppSetup.Tests
{
    /// <summary>
    /// SetupViewModel 의 기존 설정 복원 검증 — wizard 는 "기존 설정 편집기" 컨셉.
    ///
    /// 회귀 배경 (2026-07-16 발견): SelectedRobotVendor / SelectedRobotProtocolMode 의
    /// OnChanged 핸들러가 사용자 조작용 자동 매핑으로 RobotPort 를 기본값으로 리셋하는데,
    /// LoadExistingConfiguration 이 RobotPort 를 프로토콜보다 먼저 설정해 저장 포트가
    /// 로드 때마다 유실됐다 (Doosan+ModbusTcp+40000 저장 → 재실행 시 502 로 표시).
    /// </summary>
    public class SetupViewModelLoadTests
    {
        private sealed class StubConfigService : IConfigurationService
        {
            public SetupConfiguration? Config;
            public string? LastLoadError { get; set; }
            public string ConfigFilePath => @"test:\system_config.json";
            public string InvalidBackupPath => @"test:\system_config.json.invalid.bak";
            public SetupConfiguration? LoadConfiguration() => Config;
            public bool SaveConfiguration(SetupConfiguration config) => true;
            public bool ConfigurationExists() => Config != null;
            public bool ExportConfiguration(SetupConfiguration config, string exportPath) => true;
        }

        private sealed class StubDialogService : IDialogService
        {
            public readonly List<(string Message, string Title)> Errors = new();
            public void ShowInformation(string message, string title) { }
            public void ShowError(string message, string title) => Errors.Add((message, title));
            public bool ShowConfirmation(string message, string title) => true;
            public string? ShowOpenFileDialog(string title, string filter) => null;
        }

        private static (SetupViewModel vm, StubDialogService dialog) CreateVm(
            SetupConfiguration? config, string? loadError = null)
        {
            var configService = new StubConfigService { Config = config, LastLoadError = loadError };
            var dialog = new StubDialogService();
            var vm = new SetupViewModel(configService, dialog, () => { });
            return (vm, dialog);
        }

        [Fact]
        public void RobotSettings_ModbusTcpWithCustomPort_RestoredWithoutClobbering()
        {
            var (vm, _) = CreateVm(new SetupConfiguration
            {
                IsRobotEnabled = true,
                RobotVendor = RobotVendor.Doosan,
                RobotIpAddress = "10.1.2.3",
                RobotPort = 40000,
                RobotProtocolMode = RobotProtocolMode.ModbusTcp,
                EulerConvention = EulerConvention.UR_RotationVector,
                RobotModbusUnitId = 7,
                RobotModbusPoseRegister = 512
            });

            Assert.True(vm.IsRobotEnabled);
            Assert.Equal(RobotVendor.Doosan, vm.SelectedRobotVendor);
            Assert.Equal(RobotProtocolMode.ModbusTcp, vm.SelectedRobotProtocolMode);
            Assert.Equal("10.1.2.3", vm.RobotIpAddress);
            // 핵심 회귀: OnSelectedRobotProtocolModeChanged 의 Modbus 기본 포트(502)가
            // 저장값을 덮어쓰면 안 된다.
            Assert.Equal(40000, vm.RobotPort);
            Assert.Equal(EulerConvention.UR_RotationVector, vm.SelectedEulerConvention);
            Assert.Equal((byte)7, vm.RobotModbusUnitId);
            Assert.Equal((ushort)512, vm.RobotModbusPoseRegister);
        }

        [Fact]
        public void RobotSettings_VendorNativeWithCustomPort_RestoredWithoutClobbering()
        {
            var (vm, _) = CreateVm(new SetupConfiguration
            {
                RobotVendor = RobotVendor.UR,
                RobotPort = 29999,   // 제조사 기본(30003) 이 아닌 사용자 지정 포트
                RobotProtocolMode = RobotProtocolMode.VendorNative
            });

            Assert.Equal(29999, vm.RobotPort);
        }

        [Fact]
        public void PageValues_AcrossAllPages_Restored()
        {
            var (vm, dialog) = CreateVm(new SetupConfiguration
            {
                ApplicationName = "OP102",
                ClientIndex = 2,
                WebSso = new WebSsoSettings { Enabled = true },
                CameraMode = CameraMode.Live,
                Cameras = { new CameraConfiguration
                {
                    Name = "1", IpAddress = "192.168.1.60",
                    Manufacturer = CameraManufacturer.Mech_Mind,
                    CameraType = CameraType.AreaScan3D
                } },
                PlcVendor = VMS.PLC.Models.PlcVendor.Mitsubishi,
                PlcIpAddress = "10.9.8.7",
                UseHeartbeat = true,
                IoBoards = { new IoDeviceConfig { DeviceId = "IoBoard_9" } },
                SecurityMode = SecurityMode.Development
            });

            Assert.Equal("OP102", vm.ApplicationName);
            Assert.Equal(2, vm.ClientIndex);
            Assert.True(vm.WebSsoEnabled);
            Assert.Equal(CameraMode.Live, vm.CameraMode);
            var cam = Assert.Single(vm.Cameras);
            Assert.Equal("192.168.1.60", cam.IpAddress);
            Assert.Equal(CameraManufacturer.Mech_Mind, cam.Manufacturer);
            Assert.Equal(CameraType.AreaScan3D, cam.CameraType);
            Assert.Equal(VMS.PLC.Models.PlcVendor.Mitsubishi, vm.SelectedPlcVendor);
            Assert.Equal("10.9.8.7", vm.PlcIpAddress);
            Assert.True(vm.UseHeartbeat);
            Assert.Single(vm.IoBoardItems);
            Assert.Equal(SecurityMode.Development, vm.SecurityMode);
            Assert.Empty(dialog.Errors);   // 정상 로드 — 경고 없음
        }

        [Fact]
        public void LoadFailure_ShowsErrorDialog_WithCauseAndBackupPath()
        {
            var (_, dialog) = CreateVm(config: null, loadError: "잘못된 enum 값: NoSuchVendor");

            var (message, title) = Assert.Single(dialog.Errors);
            Assert.Equal("설정 로드 실패", title);
            Assert.Contains("기본값으로 시작", message);
            Assert.Contains("NoSuchVendor", message);
            Assert.Contains(".invalid.bak", message);
        }

        [Fact]
        public void FreshInstall_NoConfigFile_NoDialog()
        {
            var (_, dialog) = CreateVm(config: null, loadError: null);

            Assert.Empty(dialog.Errors);
        }
    }
}
