using System;
using VMS.PLC.Services;
using VMS.PLC.Services.Native;
using Xunit;

namespace VMS.PLC.Tests
{
    /// <summary>
    /// Advantech DAQNavi 호출의 반환 코드 검사 — SDK 없이 같은 모양의 가짜 형식으로 고정한다.
    ///
    /// 종전에는 WriteBit · WriteAny · ReadBit · ReadAny 가 ErrorCode 반환값을 버리고, 메서드를 못 찾으면
    /// `?.Invoke` 로 아무것도 하지 않아 출력 신호가 안 나가도 호출자는 성공으로 알았다 (2026-09-23 시퀀스 IO
    /// 보드 작업에서 범위 밖으로 남긴 결함).
    /// </summary>
    public class DaqNaviReflectionTests
    {
        /// <summary>DAQNavi ErrorCode 와 같은 규약 — 바탕 int, 경고 0xA… · 오류 0xE… (음수).</summary>
        public enum FakeErrorCode
        {
            Success = 0,
            WarningParamOutOfRange = unchecked((int)0xA0000002),
            ErrorHandleNotValid = unchecked((int)0xE0000000),
            ErrorDeviceIoTimeOut = unchecked((int)0xE0000011),
        }

        public sealed class FakeDoCtrl
        {
            public FakeErrorCode Next { get; set; } = FakeErrorCode.Success;
            public (int Port, int Bit, byte Data)? LastWrite { get; private set; }

            // DAQNavi 처럼 오버로드가 있어도 3인자 (int, int, data) 판을 골라야 한다
            public FakeErrorCode WriteBit(int port, int bit) => throw new NotSupportedException();
            public FakeErrorCode WriteBit(int port, int bit, byte data)
            {
                LastWrite = (port, bit, data);
                return Next;
            }

            public FakeErrorCode ReadBit(int port, int bit, out byte data)
            {
                data = 1;
                return Next;
            }

            public void Throwing(int port, int bit, byte data) => throw new InvalidCastException("드라이버 내부 오류");
        }

        [Theory]
        [InlineData(FakeErrorCode.Success, false)]
        [InlineData(FakeErrorCode.WarningParamOutOfRange, false)]
        [InlineData(FakeErrorCode.ErrorHandleNotValid, true)]
        [InlineData(FakeErrorCode.ErrorDeviceIoTimeOut, true)]
        public void IsErrorCode_FollowsDaqNaviRanges(FakeErrorCode code, bool isError)
        {
            Assert.Equal(isError, DaqNaviReflection.IsErrorCode(code, out _));
        }

        [Fact]
        public void IsErrorCode_VoidReturn_IsSuccess()
        {
            Assert.False(DaqNaviReflection.IsErrorCode(null, out _));
        }

        [Fact]
        public void FindMethod_PicksThreeArgOverload()
        {
            var m = DaqNaviReflection.FindMethod(typeof(FakeDoCtrl), "WriteBit");
            Assert.NotNull(m);
            Assert.Equal(3, m!.GetParameters().Length);
        }

        [Fact]
        public void InvokeChecked_MissingMethod_Throws()
        {
            var ex = Assert.Throws<InvalidOperationException>(() =>
                DaqNaviReflection.InvokeChecked(null, new FakeDoCtrl(), new object?[] { 0, 0, (byte)1 }, "WriteBit"));
            Assert.Contains("찾을 수 없습니다", ex.Message);
        }

        [Fact]
        public void InvokeChecked_ErrorCode_ThrowsWithCode()
        {
            var ctrl = new FakeDoCtrl { Next = FakeErrorCode.ErrorDeviceIoTimeOut };
            var m = DaqNaviReflection.FindMethod(typeof(FakeDoCtrl), "WriteBit");

            var ex = Assert.Throws<InvalidOperationException>(() =>
                DaqNaviReflection.InvokeChecked(m, ctrl, new object?[] { 1, 3, (byte)1 }, "WriteBit(port=1, bit=3, 1)"));

            Assert.Contains("ErrorDeviceIoTimeOut", ex.Message);
            Assert.Contains("0xE0000011", ex.Message);
            Assert.Equal((1, 3, (byte)1), ctrl.LastWrite);   // 호출은 실제로 갔다
        }

        [Fact]
        public void InvokeChecked_Warning_DoesNotThrow()
        {
            var ctrl = new FakeDoCtrl { Next = FakeErrorCode.WarningParamOutOfRange };
            var m = DaqNaviReflection.FindMethod(typeof(FakeDoCtrl), "WriteBit");
            DaqNaviReflection.InvokeChecked(m, ctrl, new object?[] { 0, 0, (byte)0 }, "WriteBit");
            Assert.NotNull(ctrl.LastWrite);
        }

        [Fact]
        public void InvokeChecked_OutParameter_IsFilled()
        {
            var m = DaqNaviReflection.FindMethod(typeof(FakeDoCtrl), "ReadBit");
            var args = new object?[] { 0, 2, null };
            DaqNaviReflection.InvokeChecked(m, new FakeDoCtrl(), args, "ReadBit");
            Assert.Equal((byte)1, args[2]);
        }

        [Fact]
        public void InvokeChecked_DriverException_IsUnwrapped()
        {
            var m = typeof(FakeDoCtrl).GetMethod(nameof(FakeDoCtrl.Throwing));
            var ex = Assert.Throws<InvalidOperationException>(() =>
                DaqNaviReflection.InvokeChecked(m, new FakeDoCtrl(), new object?[] { 0, 0, (byte)0 }, "WriteBit"));
            Assert.Contains("드라이버 내부 오류", ex.Message);
        }

        [Fact]
        public void Factory_Advantech_WithoutMockMarker_ReturnsNative_NotSilentMock()
        {
            // 이 PC(빌드 · CI)에는 DAQNavi 가 없다 — 종전에는 여기서 조용히 Mock 이 나왔다.
            var board = IoBoardConnectionFactory.Create(new VMS.PLC.Models.IoDeviceConfig
            {
                DeviceId = "ADV_1", Vendor = VMS.PLC.Models.IoBoardVendor.Advantech, IsEnabled = true,
                BoardId = 0, InputChannelCount = 16, OutputChannelCount = 16,
            });
            Assert.IsType<AdvantechDaqNaviConnection>(board);
        }

        [Fact]
        public async System.Threading.Tasks.Task Connect_WithoutDaqNavi_FailsWithReason()
        {
            if (DaqNaviReflection.EnsureLoaded()) return;   // DAQNavi 가 깔린 PC 에서는 이 경우를 만들 수 없다

            using var board = new AdvantechDaqNaviConnection("ADV_1", 0, 16, 16);
            Assert.False(await board.ConnectAsync());
            Assert.False(board.IsConnected);
            Assert.Contains("Automation.BDaq4.dll", board.LastError);
            await Assert.ThrowsAsync<InvalidOperationException>(() => board.WriteBitAsync(0, true));
        }

        [Fact]
        public void Factory_Advantech_MockMarker_ReturnsMock()
        {
            var board = IoBoardConnectionFactory.Create(new VMS.PLC.Models.IoDeviceConfig
            {
                DeviceId = "ADV_1", Vendor = VMS.PLC.Models.IoBoardVendor.Advantech, IsEnabled = true,
                Description = "시험 [MOCK]", InputChannelCount = 16, OutputChannelCount = 16,
            });
            Assert.IsType<AdvantechMockBoard>(board);
        }
    }
}
