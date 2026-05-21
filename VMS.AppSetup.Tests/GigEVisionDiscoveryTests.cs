using System;
using System.Text;
using VMS.AppSetup.Services;
using Xunit;

namespace VMS.AppSetup.Tests
{
    /// <summary>
    /// GigE Vision DISCOVERY_ACK 248-byte 응답 파싱 검증.
    /// 표준 layout: header(8) + payload(240) — MAC@16, IP@44, manufacturer@72,
    /// model@104, version@136, info@168, serial@216, user-name@232.
    /// </summary>
    public class GigEVisionDiscoveryTests
    {
        [Fact]
        public void Parse_ValidAck_ExtractsAllFields()
        {
            var data = BuildAckPacket(
                mac: new byte[] { 0x00, 0x11, 0x1C, 0x2D, 0x3E, 0x4F },
                ip: new byte[] { 192, 168, 0, 65 },
                manufacturer: "Hikrobot",
                model: "MV-CA050-10GC",
                version: "V1.6.3",
                serial: "01234567",
                userName: "TopCam");

            var device = GigEVisionDiscovery.ParseDiscoveryAck(data);

            Assert.NotNull(device);
            Assert.Equal("00:11:1C:2D:3E:4F", device!.MacAddress);
            Assert.Equal("192.168.0.65", device.IpAddress);
            Assert.Equal("Hikrobot", device.Manufacturer);
            Assert.Equal("MV-CA050-10GC", device.Model);
            Assert.Equal("V1.6.3", device.DeviceVersion);
            Assert.Equal("01234567", device.SerialNumber);
            Assert.Equal("TopCam", device.UserDefinedName);
        }

        [Fact]
        public void Parse_NonAckCommand_ReturnsNull()
        {
            var data = BuildAckPacket(
                mac: new byte[] { 0, 0, 0, 0, 0, 0 },
                ip: new byte[] { 0, 0, 0, 0 },
                manufacturer: "", model: "", version: "", serial: "", userName: "");

            // command code를 ACK(0x0003)에서 다른 값으로 변조
            data[2] = 0x00;
            data[3] = 0x99;

            var device = GigEVisionDiscovery.ParseDiscoveryAck(data);
            Assert.Null(device);
        }

        [Fact]
        public void Parse_TooShort_ReturnsNull()
        {
            var data = new byte[10];   // 248보다 훨씬 짧음
            data[2] = 0x00;
            data[3] = 0x03;
            var device = GigEVisionDiscovery.ParseDiscoveryAck(data);
            Assert.Null(device);
        }

        [Fact]
        public void Parse_TruncatedStrings_ReadsUntilNull()
        {
            // 짧은 manufacturer "Basler" 만 채우고 나머지 zero
            var data = BuildAckPacket(
                mac: new byte[] { 0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF },
                ip: new byte[] { 10, 0, 0, 200 },
                manufacturer: "Basler",
                model: "acA2500-14gc",
                version: "",
                serial: "SN-001",
                userName: "");

            var device = GigEVisionDiscovery.ParseDiscoveryAck(data);
            Assert.NotNull(device);
            Assert.Equal("Basler", device!.Manufacturer);   // NULL termination 후 trim
            Assert.Equal("acA2500-14gc", device.Model);
            Assert.Equal("", device.DeviceVersion);
            Assert.Equal("SN-001", device.SerialNumber);
            Assert.Equal("", device.UserDefinedName);
            Assert.Equal("10.0.0.200", device.IpAddress);
        }

        // ─── Test helpers ──────────────────────────────────────────────────────

        /// <summary>
        /// GVCP DISCOVERY_ACK 248-byte 패킷 합성 — 테스트용.
        /// </summary>
        private static byte[] BuildAckPacket(
            byte[] mac, byte[] ip,
            string manufacturer, string model, string version,
            string serial, string userName)
        {
            var data = new byte[248];

            // Header: status(0x0000) + cmd(0x0003) + length(0x00F0) + ackId
            data[0] = 0x00; data[1] = 0x00;     // status = success
            data[2] = 0x00; data[3] = 0x03;     // DISCOVERY_ACK
            data[4] = 0x00; data[5] = 0xF0;     // payload length 240
            data[6] = 0x00; data[7] = 0x01;     // ack id

            // MAC (offset 16, 6 bytes)
            Array.Copy(mac, 0, data, 16, 6);

            // Current IP (offset 44, 4 bytes BE)
            Array.Copy(ip, 0, data, 44, 4);

            // Strings
            WriteAscii(data, 72, 32, manufacturer);
            WriteAscii(data, 104, 32, model);
            WriteAscii(data, 136, 32, version);
            // 168-215: manufacturer info (skip)
            WriteAscii(data, 216, 16, serial);
            WriteAscii(data, 232, 16, userName);

            return data;
        }

        private static void WriteAscii(byte[] data, int offset, int maxLen, string value)
        {
            var bytes = Encoding.ASCII.GetBytes(value);
            int n = System.Math.Min(bytes.Length, maxLen - 1);   // leave room for null
            Array.Copy(bytes, 0, data, offset, n);
            // rest already zero
        }
    }
}
