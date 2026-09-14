using System.Text.Json;
using VMS.Core.Models.ParameterSync;
using Xunit;

namespace VMS.Core.Tests.Models
{
    /// <summary>
    /// unmatchedLine / mismatchedLot 플래그 계약 (2026-09-14, Web #104 와 세트).
    ///
    /// <para>VMS 는 사용자가 고른 WorkOrderId·LotId 를 들고 <b>매 사이클 그대로</b> 보낸다.
    /// Web 에서 작업지시를 다른 라인으로 재배정하면, 예전 라인이 계속 그 WO 수량을 올려
    /// 남의 실적이 부풀고 계획 도달 시 조기 완료까지 갔다. 이제 서버가 수량을 올리지 않고
    /// 이 플래그로 알려 주며, VMS 는 그 사실을 시스템 로그로 표면화한다 — 없으면 현장에서는
    /// "검사는 되는데 수량이 안 오른다" 로만 보인다.</para>
    /// </summary>
    public class WorkOrderAssignmentFlagTests
    {
        private static readonly JsonSerializerOptions Options = new()
        {
            PropertyNameCaseInsensitive = true
        };

        [Fact]
        public void Deserialize_CamelCaseFlags_Mapped()
        {
            var json = """{"id":7,"orderNo":"WO-1","status":"InProgress","unmatchedLine":true,"mismatchedLot":true}""";

            var dto = JsonSerializer.Deserialize<WorkOrderProgressDto>(json, Options);

            Assert.NotNull(dto);
            Assert.True(dto!.UnmatchedLine);
            Assert.True(dto.MismatchedLot);
        }

        /// <summary>구버전 Web 서버는 이 필드를 보내지 않는다 — 경고가 뜨면 안 된다.</summary>
        [Fact]
        public void Deserialize_LegacyServerWithoutFlags_DefaultsFalse()
        {
            var json = """{"id":7,"orderNo":"WO-1","status":"InProgress"}""";

            var dto = JsonSerializer.Deserialize<WorkOrderProgressDto>(json, Options);

            Assert.NotNull(dto);
            Assert.False(dto!.UnmatchedLine);
            Assert.False(dto.MismatchedLot);
        }

        /// <summary>정상 업로드 응답에서는 두 플래그가 모두 꺼져 있다.</summary>
        [Fact]
        public void Deserialize_NormalUpload_FlagsOff()
        {
            var json = """
                {"id":7,"orderNo":"WO-1","status":"InProgress","producedQuantity":3,
                 "unmatchedRecipe":false,"staleWorkOrder":false,
                 "unmatchedLine":false,"mismatchedLot":false}
                """;

            var dto = JsonSerializer.Deserialize<WorkOrderProgressDto>(json, Options);

            Assert.NotNull(dto);
            Assert.False(dto!.UnmatchedLine);
            Assert.False(dto.MismatchedLot);
            Assert.Equal(3, dto.ProducedQuantity);
        }

        [Fact]
        public void Sanitize_PreservesFlags()
        {
            var dto = new WorkOrderProgressDto { UnmatchedLine = true, MismatchedLot = true }.Sanitize();

            Assert.True(dto.UnmatchedLine);
            Assert.True(dto.MismatchedLot);
        }
    }
}
