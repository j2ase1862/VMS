using System.Text.Json;
using VMS.Core.Models.ParameterSync;
using Xunit;

namespace VMS.Core.Tests.Models
{
    /// <summary>
    /// staleWorkOrder 플래그 계약 (2026-08-19, Web #73 과 세트) — Web 에서 수동 완료된
    /// WO 로 업로드가 계속되면 서버가 이 플래그로 응답하고, VMS 는 완료 폴백
    /// (운전 정지 + 다이얼로그)을 태운다. 구버전 Web(필드 미전송)은 false 여야 한다.
    /// </summary>
    public class WorkOrderStaleFlagTests
    {
        private static readonly JsonSerializerOptions Options = new()
        {
            PropertyNameCaseInsensitive = true
        };

        [Fact]
        public void Deserialize_CamelCaseStaleFlag_Mapped()
        {
            var json = """{"id":7,"orderNo":"WO-1","status":"Completed","staleWorkOrder":true}""";

            var dto = JsonSerializer.Deserialize<WorkOrderProgressDto>(json, Options);

            Assert.NotNull(dto);
            Assert.True(dto!.StaleWorkOrder);
            Assert.Equal("Completed", dto.Status);
        }

        [Fact]
        public void Deserialize_LegacyServerWithoutFlag_DefaultsFalse()
        {
            var json = """{"id":7,"orderNo":"WO-1","status":"InProgress"}""";

            var dto = JsonSerializer.Deserialize<WorkOrderProgressDto>(json, Options);

            Assert.NotNull(dto);
            Assert.False(dto!.StaleWorkOrder);
        }

        [Fact]
        public void Sanitize_PreservesStaleFlag()
        {
            var dto = new WorkOrderProgressDto { StaleWorkOrder = true }.Sanitize();
            Assert.True(dto.StaleWorkOrder);
        }
    }
}
