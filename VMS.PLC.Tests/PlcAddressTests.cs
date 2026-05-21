using System;
using VMS.PLC.Models;
using Xunit;

namespace VMS.PLC.Tests
{
    public class PlcAddressTests
    {
        // ─── Mitsubishi ─────────────────────────────────────────────────────────

        [Theory]
        [InlineData("D100", "D", 100, -1)]   // word device
        [InlineData("R200", "R", 200, -1)]
        [InlineData("W50", "W", 50, -1)]
        [InlineData("M0", "M", 0, 0)]        // bit device → offset/16, bit%16
        [InlineData("M15", "M", 0, 15)]
        [InlineData("M16", "M", 1, 0)]
        [InlineData("X32", "X", 2, 0)]
        [InlineData("Y17", "Y", 1, 1)]
        public void Parse_Mitsubishi(string raw, string device, int offset, int bit)
        {
            var addr = PlcAddress.Parse(raw, PlcVendor.Mitsubishi);
            Assert.Equal(device, addr.DeviceCode);
            Assert.Equal(offset, addr.Offset);
            Assert.Equal(bit, addr.BitPosition);
        }

        [Fact]
        public void Parse_Mitsubishi_Invalid_Throws()
        {
            Assert.Throws<FormatException>(() => PlcAddress.Parse("INVALID@", PlcVendor.Mitsubishi));
        }

        // ─── Siemens ────────────────────────────────────────────────────────────

        [Theory]
        [InlineData("DB1.DBW0", "DBW", 1, 0, -1)]
        [InlineData("DB10.DBX5.3", "DBX", 10, 5, 3)]
        [InlineData("DB200.DBD100", "DBD", 200, 100, -1)]
        public void Parse_Siemens_DB(string raw, string device, int dbNum, int offset, int bit)
        {
            var addr = PlcAddress.Parse(raw, PlcVendor.Siemens);
            Assert.Equal(device, addr.DeviceCode);
            Assert.Equal(dbNum, addr.DbNumber);
            Assert.Equal(offset, addr.Offset);
            Assert.Equal(bit, addr.BitPosition);
        }

        [Theory]
        [InlineData("MW100", "MW", 100, -1)]
        [InlineData("M0.5", "M", 0, 5)]
        [InlineData("IW0", "IW", 0, -1)]
        public void Parse_Siemens_Direct(string raw, string device, int offset, int bit)
        {
            var addr = PlcAddress.Parse(raw, PlcVendor.Siemens);
            Assert.Equal(device, addr.DeviceCode);
            Assert.Equal(offset, addr.Offset);
            Assert.Equal(bit, addr.BitPosition);
        }

        // ─── LS XGT ─────────────────────────────────────────────────────────────

        [Theory]
        [InlineData("%DW100", "D", 100, -1)]
        [InlineData("%MX32", "M", 2, 0)]     // bit → /16
        [InlineData("%MX17", "M", 1, 1)]
        [InlineData("MW0", "M", 0, -1)]      // % 접두사 생략 가능
        public void Parse_LsXgt(string raw, string device, int offset, int bit)
        {
            var addr = PlcAddress.Parse(raw, PlcVendor.LS);
            Assert.Equal(device, addr.DeviceCode);
            Assert.Equal(offset, addr.Offset);
            Assert.Equal(bit, addr.BitPosition);
        }

        // ─── Omron ──────────────────────────────────────────────────────────────

        [Theory]
        [InlineData("D100", "D", 100, -1)]
        [InlineData("W0.15", "W", 0, 15)]
        [InlineData("CIO5.3", "CIO", 5, 3)]
        [InlineData("HR100", "HR", 100, -1)]
        public void Parse_Omron(string raw, string device, int offset, int bit)
        {
            var addr = PlcAddress.Parse(raw, PlcVendor.Omron);
            Assert.Equal(device, addr.DeviceCode);
            Assert.Equal(offset, addr.Offset);
            Assert.Equal(bit, addr.BitPosition);
        }

        // ─── Modbus ─────────────────────────────────────────────────────────────

        [Theory]
        // 명시 접두사 표기
        [InlineData("0x100", "0x", 100, -1)]
        [InlineData("1x50", "1x", 50, -1)]
        [InlineData("3x200", "3x", 200, -1)]
        [InlineData("4x100", "4x", 100, -1)]
        [InlineData("4x100.3", "4x", 100, 3)]
        // 대소문자 무관
        [InlineData("4X50", "4x", 50, -1)]
        public void Parse_Modbus_Prefixed(string raw, string device, int offset, int bit)
        {
            var addr = PlcAddress.Parse(raw, PlcVendor.Modbus);
            Assert.Equal(device, addr.DeviceCode);
            Assert.Equal(offset, addr.Offset);
            Assert.Equal(bit, addr.BitPosition);
        }

        [Theory]
        // Modicon 5/6자리 → 1-based → 0-based 변환
        [InlineData("40001", "4x", 0)]
        [InlineData("40101", "4x", 100)]
        [InlineData("400001", "4x", 0)]   // 6자리도 동일
        [InlineData("30050", "3x", 49)]
        [InlineData("10005", "1x", 4)]
        [InlineData("00001", "0x", 0)]
        public void Parse_Modbus_Modicon(string raw, string device, int offset)
        {
            var addr = PlcAddress.Parse(raw, PlcVendor.Modbus);
            Assert.Equal(device, addr.DeviceCode);
            Assert.Equal(offset, addr.Offset);
            Assert.Equal(-1, addr.BitPosition);
        }

        [Theory]
        [InlineData("invalid")]
        [InlineData("5x100")]   // 5x prefix는 표준 아님
        public void Parse_Modbus_Invalid_Throws(string raw)
        {
            Assert.Throws<FormatException>(() => PlcAddress.Parse(raw, PlcVendor.Modbus));
        }

        // ─── ToKey 일관성 ──────────────────────────────────────────────────────

        [Fact]
        public void ToKey_IsConsistent()
        {
            var a = PlcAddress.Parse("D100", PlcVendor.Mitsubishi);
            var b = PlcAddress.Parse("D100", PlcVendor.Mitsubishi);
            Assert.Equal(a.ToKey(), b.ToKey());
        }
    }
}
