using VMS.PLC.Services.Native;
using Xunit;

namespace VMS.PLC.Tests
{
    /// <summary>
    /// ADLink DASK 상수·포트 매핑 고정.
    ///
    /// 2026-08-14 현장 실증(PCI-7432)에서 드러난 값들이다. 검증된 레퍼런스
    /// (PalletizingSystem/PalletControl · Pci7432Device.cs)와 대조해 확정했고,
    /// 과거 값(cardType 0x37, DO 포트 0)으로 되돌아가면 보드가 조용히 죽으므로 테스트로 잠근다.
    /// </summary>
    public class DaskNativeMethodsTests
    {
        [Fact]
        public void Pci7432_CardType_MatchesFieldProvenValue()
        {
            // PalletControl Pci7432Device.cs: private const ushort PCI_7432 = 0x11;
            Assert.Equal((ushort)0x11, DaskNativeMethods.PCI_7432);
            Assert.Equal((ushort)0x11, DaskNativeMethods.ModelToCardType("PCI-7432"));
        }

        [Fact]
        public void Pci7432_SeparatesInputAndOutputPorts()
        {
            // PalletControl: DI_PORT = 0, DO_PORT = 1 — 둘 다 0 이면 출력이 나가지 않는다.
            Assert.Equal((ushort)0, DaskNativeMethods.DiPortFor(DaskNativeMethods.PCI_7432));
            Assert.Equal((ushort)1, DaskNativeMethods.DoPortFor(DaskNativeMethods.PCI_7432));
        }

        [Fact]
        public void Pci7434_OutputOnlyCard_UsesPortZeroForOutput()
        {
            Assert.Equal((ushort)0, DaskNativeMethods.DoPortFor(DaskNativeMethods.PCI_7434));
        }

        [Fact]
        public void UnknownModel_FallsBackToPci7432()
        {
            Assert.Equal(DaskNativeMethods.PCI_7432, DaskNativeMethods.ModelToCardType("PCI-9999"));
        }

        [Fact]
        public void OnlyPci7432_IsMarkedFieldVerified()
        {
            // 7433/7434 상수는 미검증 — 연결 실패 시 이 값을 먼저 의심하라는 신호가 유지돼야 한다.
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
