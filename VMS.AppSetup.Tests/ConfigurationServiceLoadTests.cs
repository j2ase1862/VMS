using System;
using System.IO;
using VMS.AppSetup.Models;
using VMS.AppSetup.Services;
using VMS.Camera.Models;
using VMS.PLC.Models;
using Xunit;
using RobotProtocolMode = VMS.Camera.Models.RobotProtocolMode;

namespace VMS.AppSetup.Tests
{
    /// <summary>
    /// ConfigurationService 로드 경로 검증.
    /// ① 전 페이지(2~7) 필드가 Save → Load 왕복에서 손실 없이 복원되는지 —
    ///    wizard 는 "기존 설정 편집기" 컨셉이므로 어느 페이지든 미복원은 결함.
    /// ② 파싱 실패가 무통보로 삼켜지지 않는지 — LastLoadError 에 원인이 남고
    ///    원본이 .invalid.bak 으로 보존되어야 한다 (기본값 저장으로 인한 설정 유실 방지).
    /// </summary>
    public class ConfigurationServiceLoadTests : IDisposable
    {
        private readonly string _dir = Path.Combine(
            Path.GetTempPath(), $"vms_configsvc_test_{Guid.NewGuid():N}");

        public void Dispose()
        {
            if (Directory.Exists(_dir))
                Directory.Delete(_dir, recursive: true);
        }

        private static SetupConfiguration NonDefaultConfig() => new()
        {
            // Page 2
            ApplicationName = "OP102",
            SystemIpAddress = "192.168.1.102",
            ClientIndex = 2,
            WebServerUrl = "http://web-host:5292",
            VisionServerUrl = "http://vision-host:5000",
            ClientApiKey = "api-key-123",
            WebSso = new WebSsoSettings { Enabled = true, WebServerUrl = "http://web-host:5292" },

            // Page 3
            CameraMode = CameraMode.Live,
            Cameras =
            {
                new CameraConfiguration
                {
                    Name = "1",
                    IpAddress = "192.168.1.60",
                    Manufacturer = CameraManufacturer.Mech_Mind,
                    CameraType = CameraType.AreaScan3D,
                    ZRangeMax = 2000
                }
            },

            // Page 4
            PlcVendor = PlcVendor.Mitsubishi,
            CommunicationType = PlcCommunicationType.Serial,
            PlcIpAddress = "10.9.8.7",
            PlcPort = 6000,
            SerialPortName = "COM7",
            BaudRate = 9600,
            UseHeartbeat = true,
            HeartbeatAddress = "D100",
            WriteMode = PlcWriteMode.SingleShot,
            EndianMode = PlcEndianMode.BigEndian,

            // Page 5
            IsRobotEnabled = true,
            RobotVendor = RobotVendor.Doosan,
            RobotIpAddress = "10.1.2.3",
            RobotPort = 40000,
            RobotProtocolMode = RobotProtocolMode.ModbusTcp,
            RobotModbusUnitId = 7,
            RobotModbusPoseRegister = 512,

            // Page 6
            IoBoards =
            {
                new IoDeviceConfig { DeviceId = "IoBoard_9", BoardId = 3 }
            },

            // Page 7
            SecurityMode = SecurityMode.Development
        };

        [Fact]
        public void SaveThenLoad_AllPages_RoundTripsWithoutLoss()
        {
            var svc = new ConfigurationService(_dir);
            Assert.True(svc.SaveConfiguration(NonDefaultConfig()));

            var loaded = new ConfigurationService(_dir).LoadConfiguration();

            Assert.NotNull(loaded);

            // Page 2
            Assert.Equal("OP102", loaded!.ApplicationName);
            Assert.Equal("192.168.1.102", loaded.SystemIpAddress);
            Assert.Equal(2, loaded.ClientIndex);
            Assert.Equal("http://web-host:5292", loaded.WebServerUrl);
            Assert.Equal("api-key-123", loaded.ClientApiKey);
            Assert.True(loaded.WebSso.Enabled);

            // Page 3
            Assert.Equal(CameraMode.Live, loaded.CameraMode);
            var cam = Assert.Single(loaded.Cameras);
            Assert.Equal("1", cam.Name);
            Assert.Equal("192.168.1.60", cam.IpAddress);
            Assert.Equal(CameraManufacturer.Mech_Mind, cam.Manufacturer);
            Assert.Equal(CameraType.AreaScan3D, cam.CameraType);
            Assert.Equal(2000, cam.ZRangeMax);

            // Page 4
            Assert.Equal(PlcVendor.Mitsubishi, loaded.PlcVendor);
            Assert.Equal(PlcCommunicationType.Serial, loaded.CommunicationType);
            Assert.Equal("10.9.8.7", loaded.PlcIpAddress);
            Assert.Equal(6000, loaded.PlcPort);
            Assert.Equal("COM7", loaded.SerialPortName);
            Assert.Equal(9600, loaded.BaudRate);
            Assert.True(loaded.UseHeartbeat);
            Assert.Equal("D100", loaded.HeartbeatAddress);
            Assert.Equal(PlcWriteMode.SingleShot, loaded.WriteMode);
            Assert.Equal(PlcEndianMode.BigEndian, loaded.EndianMode);

            // Page 5
            Assert.True(loaded.IsRobotEnabled);
            Assert.Equal(RobotVendor.Doosan, loaded.RobotVendor);
            Assert.Equal("10.1.2.3", loaded.RobotIpAddress);
            Assert.Equal(40000, loaded.RobotPort);
            Assert.Equal(RobotProtocolMode.ModbusTcp, loaded.RobotProtocolMode);
            Assert.Equal((byte)7, loaded.RobotModbusUnitId);
            Assert.Equal((ushort)512, loaded.RobotModbusPoseRegister);

            // Page 6
            var board = Assert.Single(loaded.IoBoards);
            Assert.Equal("IoBoard_9", board.DeviceId);
            Assert.Equal(3, board.BoardId);

            // Page 7
            Assert.Equal(SecurityMode.Development, loaded.SecurityMode);
        }

        [Fact]
        public void MissingFile_ReturnsNull_WithoutError()
        {
            var svc = new ConfigurationService(_dir);

            Assert.Null(svc.LoadConfiguration());
            Assert.Null(svc.LastLoadError);
            Assert.False(File.Exists(svc.InvalidBackupPath));
        }

        [Fact]
        public void UnknownEnumValue_ReturnsNull_SetsErrorAndPreservesOriginal()
        {
            var svc = new ConfigurationService(_dir);
            Assert.True(svc.SaveConfiguration(NonDefaultConfig()));

            // 구버전/수동 편집으로 현재 enum 에 없는 값이 들어간 상황 재현
            var json = File.ReadAllText(svc.ConfigFilePath)
                .Replace("\"Mech_Mind\"", "\"NoSuchVendor\"");
            File.WriteAllText(svc.ConfigFilePath, json);

            var loaded = svc.LoadConfiguration();

            Assert.Null(loaded);
            Assert.NotNull(svc.LastLoadError);
            Assert.True(File.Exists(svc.InvalidBackupPath));
            Assert.Contains("NoSuchVendor", File.ReadAllText(svc.InvalidBackupPath));
        }

        [Fact]
        public void MalformedJson_ReturnsNull_SetsErrorAndPreservesOriginal()
        {
            var svc = new ConfigurationService(_dir);
            File.WriteAllText(svc.ConfigFilePath, "{ not valid json !!");

            Assert.Null(svc.LoadConfiguration());
            Assert.NotNull(svc.LastLoadError);
            Assert.Equal("{ not valid json !!", File.ReadAllText(svc.InvalidBackupPath));
        }

        [Fact]
        public void LoadAfterFailure_Succeeds_ClearsError()
        {
            var svc = new ConfigurationService(_dir);
            File.WriteAllText(svc.ConfigFilePath, "broken");
            Assert.Null(svc.LoadConfiguration());
            Assert.NotNull(svc.LastLoadError);

            Assert.True(svc.SaveConfiguration(NonDefaultConfig()));
            Assert.NotNull(svc.LoadConfiguration());
            Assert.Null(svc.LastLoadError);
        }
    }
}
