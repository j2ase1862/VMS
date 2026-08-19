using VMS.VisionSetup.Controls;
using Xunit;

namespace VMS.VisionSetup.Tests
{
    /// <summary>
    /// ParamCodeLink 배지의 로컬↔Web 값 불일치 판정 검증 —
    /// 현장 혼선(2026-08-19): 링크된 파라미터를 VisionSetup 텍스트박스에서 고쳐도
    /// 운전은 Web 값을 쓰는데 이를 알려주는 UI 가 없었다. 배지 승격 조건을 고정한다.
    /// </summary>
    public class ParamCodeLinkMismatchTests
    {
        [Theory]
        [InlineData(128, 128)]        // 동일 정수
        [InlineData(3.5, 3.5)]        // 동일 실수
        [InlineData(0.0, 0.0)]
        public void SameValue_NoMismatch(double local, double web)
            => Assert.False(ParamCodeLink.IsMismatch(local, web));

        [Theory]
        [InlineData(128, 129)]        // 정수 1 차이
        [InlineData(128, 128.4)]      // Web 이 소수 값 — 로컬과 다름을 알려야 함
        [InlineData(3.5, 3.6)]        // 실수 소수 차이
        [InlineData(100, 3.5)]
        [InlineData(0.001, 0.002)]
        public void DifferentValue_Mismatch(double local, double web)
            => Assert.True(ParamCodeLink.IsMismatch(local, web));

        [Fact]
        public void UnboundLocal_NaN_NeverMismatch()
            => Assert.False(ParamCodeLink.IsMismatch(double.NaN, 3.5));
    }
}
