using VMS.Core;
using Xunit;

namespace VMS.Core.Tests
{
    /// <summary>
    /// 제품명 · 버전 표기 — GS 인증은 설치 프로그램 · 프로그램 화면 · 매뉴얼의 제품명 · 버전이 같아야 한다.
    /// </summary>
    public class ProductInfoTests
    {
        [Fact]
        public void Display_name_is_product_name_and_version()
        {
            Assert.Equal("BODA VMS", ProductInfo.Name);
            Assert.Matches(@"^BODA VMS v\d+\.\d+\.\d+$", ProductInfo.DisplayName);
            Assert.DoesNotContain("+", ProductInfo.Version); // 빌드 메타데이터(+커밋)는 뗀다
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("BODA Vision System")]   // 제품명 변경 전 기본값
        [InlineData(" BODA Vision System ")]
        public void Old_default_or_blank_application_name_becomes_the_product_name(string? configured)
        {
            Assert.Equal("BODA VMS", ProductInfo.NormalizeApplicationName(configured));
        }

        [Fact]
        public void Custom_application_name_is_kept()
        {
            Assert.Equal("2라인 외관검사", ProductInfo.NormalizeApplicationName("2라인 외관검사"));
        }
    }
}
