using VMS.Services;
using Xunit;

namespace VMS.Tests.Services
{
    /// <summary>
    /// Start 노드가 없을 때의 System Log 안내 — 시퀀스는 AUTO RUN 을 시작할 때마다 다시 읽으므로
    /// "VMS 를 다시 시작하세요" 는 사실과 달랐고 사용자 매뉴얼 10.2("저장 후 다음 AUTO RUN 부터 자동으로 읽힙니다")와도
    /// 어긋났다 (GS 매뉴얼 대조 06 #21).
    /// </summary>
    public class AutoProcessMissingStartMessageTests
    {
        [Fact]
        public void Message_matches_the_manual_and_does_not_ask_for_a_restart()
        {
            var msg = AutoProcessService.MissingStartNodeMessage;

            Assert.Contains("Start 노드가 없어", msg);                               // 매뉴얼 10.2 증상 칸의 인용
            Assert.Contains("저장 후 다음 AUTO RUN 부터 자동으로 읽힙니다", msg);   // 매뉴얼 10.2 조치 칸
            Assert.DoesNotContain("다시 시작", msg);
        }
    }
}
