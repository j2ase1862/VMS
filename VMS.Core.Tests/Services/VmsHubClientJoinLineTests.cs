using System.Reflection;
using System.Threading.Tasks;
using VMS.Core.Services;
using Xunit;

namespace VMS.Core.Tests.Services
{
    /// <summary>
    /// 설비가 <b>자기 라인 그룹</b>에 들도록 라인 번호를 들고 있는가 (Web W-009 의 VMS 쪽).
    ///
    /// <para>공개 허브는 익명이라 붙기만 하면 누구나 받는다. 서버가 전체 발신을 라인 그룹으로
    /// 좁힌 뒤에는 <c>JoinLine</c> 을 부르지 않으면 자기 라인 이벤트(WO 진행률·Lot·파라미터
    /// 변경)를 못 받는다 — 헤더 WO 칩과 Lot 콤보가 조용히 멈춘다. 연결 객체가 없을 때는
    /// 아무 일도 하지 않아야 한다(시작 전 호출·구버전 서버).</para>
    /// </summary>
    public class VmsHubClientJoinLineTests
    {
        private const string Url = "https://boda-vms.example";

        [Fact]
        public async Task Client_keeps_its_line_number()
        {
            await using var client = new VmsHubClient(Url, clientIndex: 7);

            Assert.Equal(7, LineOf(client));
        }

        [Fact]
        public async Task Line_number_is_optional_for_callers_that_only_listen()
        {
            await using var client = new VmsHubClient(Url);

            Assert.Null(LineOf(client));
        }

        [Fact]
        public async Task Join_is_a_no_op_before_the_connection_exists()
        {
            await using var client = new VmsHubClient(Url, clientIndex: 7);

            // StartAsync 전에는 연결이 없다 — 예외 없이 지나가야 한다.
            await client.JoinLineAsync();
        }

        private static int? LineOf(VmsHubClient client)
        {
            var field = typeof(VmsHubClient).GetField("_clientIndex",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field);
            return (int?)field!.GetValue(client);
        }
    }
}
