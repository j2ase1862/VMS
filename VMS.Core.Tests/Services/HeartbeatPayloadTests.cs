using System.Text.Json;
using VMS.Core.Services;
using Xunit;

namespace VMS.Core.Tests.Services
{
    /// <summary>
    /// W-143 — heartbeat 본문이 규격에 맞는 JSON 인가.
    ///
    /// <para><b>왜.</b> 이 문자열은 생성자에서 한 번 만들어 5초마다 그대로 다시 보낸다. 값을
    /// 손으로 이어 붙이던 시절에는 역슬래시와 큰따옴표만 바꿨는데, JSON 문자열 안의 제어문자
    /// (줄바꿈·탭 등)는 규격 위반이라 서버가 400 으로 거절한다. <c>swName</c> 의 출처는
    /// AppSetup 에서 사람이 편집하는 값이라, 한 번 잘못 들어가면 <b>그 라인의 heartbeat 가
    /// 영원히 400</b> 이 되고 화면에는 "Web 연결 끊김" 만 뜬다 — 원인을 짚을 단서가 없다.</para>
    /// </summary>
    public class HeartbeatPayloadTests
    {
        private const string Url = "https://boda-vms.example";   // https 라 보안 모드와 무관

        private static JsonElement PayloadOf(string swName, int clientIndex = 3)
        {
            using var service = new HeartbeatService(Url, Url, clientIndex, swName: swName);
            return JsonDocument.Parse(service.HeartbeatPayload).RootElement.Clone();
        }

        [Fact]
        public void The_ordinary_payload_carries_the_three_fields_the_server_expects()
        {
            var payload = PayloadOf("BODA Vision System", clientIndex: 3);

            Assert.Equal(3, payload.GetProperty("clientIndex").GetInt32());
            Assert.Equal("BODA Vision System", payload.GetProperty("swName").GetString());
            Assert.False(string.IsNullOrWhiteSpace(payload.GetProperty("hostName").GetString()));
        }

        [Theory]
        [InlineData("line\nbreak")]        // 줄바꿈 — 예전에는 그대로 들어가 규격 위반
        [InlineData("tab\there")]
        [InlineData("carriage\rreturn")]
        [InlineData("null\0char")]
        [InlineData("bellsound")]
        public void A_control_character_in_the_name_still_produces_valid_json(string swName)
        {
            // 파싱이 되는 것 자체가 검증이다 — 예전에는 여기서 깨진 JSON 이 만들어졌다.
            var payload = PayloadOf(swName);

            Assert.Equal(swName, payload.GetProperty("swName").GetString());
        }

        [Theory]
        [InlineData("quote\"inside")]
        [InlineData(@"back\slash")]
        [InlineData("both\"and\\together")]
        public void Quotes_and_backslashes_survive_as_they_were_typed(string swName)
        {
            var payload = PayloadOf(swName);

            Assert.Equal(swName, payload.GetProperty("swName").GetString());
        }

        [Theory]
        [InlineData("한글 이름")]
        [InlineData("공장 A동 — 2호기")]
        [InlineData("émoji 🏭 line")]
        public void Non_ascii_names_come_back_unchanged(string swName)
        {
            var payload = PayloadOf(swName);

            Assert.Equal(swName, payload.GetProperty("swName").GetString());
        }

        [Fact]
        public void An_empty_name_is_still_valid_json()
        {
            var payload = PayloadOf(string.Empty);

            Assert.Equal(string.Empty, payload.GetProperty("swName").GetString());
        }

        [Fact]
        public void The_payload_never_changes_between_sends()
        {
            using var service = new HeartbeatService(Url, Url, clientIndex: 1, swName: "VMS");

            // 5초마다 같은 문자열을 보낸다 — 한 번 잘못 만들어지면 스스로 회복하지 못한다.
            Assert.Equal(service.HeartbeatPayload, service.HeartbeatPayload);
        }
    }
}
