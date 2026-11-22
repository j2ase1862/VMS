using VMS.ViewModels;
using Xunit;

namespace VMS.Tests.ViewModels
{
    /// <summary>
    /// Recipe 패널 ↻ 의 상태바 요약 — 사용자 매뉴얼 4.6 은 "Web 레시피 3개 — 새 항목 1, 이름 연결 1" 처럼 요약되고
    /// 0 개면 라인 번호가 맞는지 확인하라는 힌트가 붙는다고 적는다. 종전 문구는 "… 3개 동기화 (ClientIndex n) — …" 라
    /// 예와 글자가 달랐고 '라인 번호' 라는 말이 없었다 (GS 매뉴얼 대조 02 #22).
    /// </summary>
    public class MainViewModelWebRecipeSyncTextTests
    {
        [Fact]
        public void Summary_starts_with_the_manual_example()
        {
            var text = MainViewModel.DescribeWebRecipeSync(success: true, total: 3, added: 1, linkedByName: 1, clientIndex: 2);

            Assert.StartsWith("Web 레시피 3개 — 새 항목 1, 이름 연결 1", text);
            Assert.Contains("라인 번호 2", text);
        }

        [Fact]
        public void Name_link_part_is_omitted_when_there_is_none()
        {
            var text = MainViewModel.DescribeWebRecipeSync(success: true, total: 2, added: 0, linkedByName: 0, clientIndex: 1);

            Assert.StartsWith("Web 레시피 2개 — 새 항목 0", text);
            Assert.DoesNotContain("이름 연결", text);
        }

        [Fact]
        public void Zero_recipes_hints_at_the_line_number()
        {
            var text = MainViewModel.DescribeWebRecipeSync(success: true, total: 0, added: 0, linkedByName: 0, clientIndex: 5);

            Assert.StartsWith("Web 레시피 0개", text);
            Assert.Contains("라인 번호 5", text);
            Assert.Contains("Client Index", text);   // 설정 마법사 칸 이름
        }

        [Fact]
        public void Failure_names_the_line_number_too()
        {
            var text = MainViewModel.DescribeWebRecipeSync(success: false, total: 0, added: 0, linkedByName: 0, clientIndex: 5);

            Assert.Contains("동기화 실패", text);
            Assert.Contains("라인 번호(Client Index 5)", text);
        }
    }
}
