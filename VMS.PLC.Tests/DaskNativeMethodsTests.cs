using VMS.PLC.Services.Native;
using Xunit;

namespace VMS.PLC.Tests
{
    /// <summary>
    /// ADLink DASK 상수·채널 매핑 고정.
    ///
    /// 값의 출처는 공식 PCIS-DASK 헤더(Dask64.h/Dask.h)와 공식 샘플이다. 2026-08-18 현장
    /// 실증(PCI-7432)에서 과거 값(cardType 0x11 = 실제로는 PCI_7433)이 Register_Card -13
    /// (ErrorOpenDriverFailed)을 일으켰고, 같은 PC 의 공식 x64 샘플(cardType 16)은 동작했다.
    /// 0x11 의 출처였던 PalletControl 은 DLL 미존재 시 시뮬레이션 폴백이라 검증 근거가 아니다.
    /// 이 값들로 되돌아가면 보드가 조용히 죽으므로 테스트로 잠근다.
    /// </summary>
    public class DaskNativeMethodsTests
    {
        [Fact]
        public void CardTypes_MatchOfficialDaskHeader()
        {
            // Dask64.h: #define PCI_7432 16 / PCI_7433 17 / PCI_7434 18
            Assert.Equal((ushort)16, DaskNativeMethods.PCI_7432);
            Assert.Equal((ushort)17, DaskNativeMethods.PCI_7433);
            Assert.Equal((ushort)18, DaskNativeMethods.PCI_7434);
            Assert.Equal((ushort)16, DaskNativeMethods.ModelToCardType("PCI-7432"));
            Assert.Equal((ushort)17, DaskNativeMethods.ModelToCardType("PCI-7433"));
            Assert.Equal((ushort)18, DaskNativeMethods.ModelToCardType("PCI-7434"));
        }

        [Fact]
        public void ChannelMapping_Low32IsPort0_High32IsPort1()
        {
            // 공식 샘플: 채널 0~31 = PORT_*_LOW(0), 채널 32~63 = PORT_*_HIGH(1), Line = 채널 % 32.
            // 방향(DI/DO)은 함수 이름이 구분한다 — "7432 는 DO=포트 1" 은 오답이었다.
            Assert.Equal((ushort)0, DaskNativeMethods.PortForChannel(0));
            Assert.Equal((ushort)0, DaskNativeMethods.PortForChannel(31));
            Assert.Equal((ushort)1, DaskNativeMethods.PortForChannel(32));
            Assert.Equal((ushort)1, DaskNativeMethods.PortForChannel(63));

            Assert.Equal((ushort)0, DaskNativeMethods.LineForChannel(0));
            Assert.Equal((ushort)31, DaskNativeMethods.LineForChannel(31));
            Assert.Equal((ushort)0, DaskNativeMethods.LineForChannel(32));
            Assert.Equal((ushort)31, DaskNativeMethods.LineForChannel(63));
        }

        [Fact]
        public void UnknownModel_FallsBackToPci7432()
        {
            Assert.Equal(DaskNativeMethods.PCI_7432, DaskNativeMethods.ModelToCardType("PCI-9999"));
        }

        [Fact]
        public void OnlyPci7432_IsMarkedFieldVerified()
        {
            // 7433/7434 는 헤더 값이라 신뢰도는 높지만 실기 미검증 — 연결 시 경고가 유지돼야 한다.
            Assert.True(DaskNativeMethods.IsCardTypeVerified(DaskNativeMethods.PCI_7432));
            Assert.False(DaskNativeMethods.IsCardTypeVerified(DaskNativeMethods.PCI_7433));
            Assert.False(DaskNativeMethods.IsCardTypeVerified(DaskNativeMethods.PCI_7434));
        }

        [Fact]
        public void DiagnosticsText_MentionsBitnessAndCandidates()
        {
            // 현장 진단의 핵심 — 드라이버 미설치 시 무엇을 확인해야 하는지 로그에 남아야 한다.
            var text = DaskNativeMethods.DiagnosticsText;
            Assert.Contains("bit", text);                    // 32-bit / 64-bit 표기
            Assert.True(text.Contains("PCI-Dask") || text.Contains("loaded="));
        }
    }
}
