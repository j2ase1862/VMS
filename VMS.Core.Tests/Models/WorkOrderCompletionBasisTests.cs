using VMS.Core.Models.ParameterSync;
using Xunit;

namespace VMS.Core.Tests.Models
{
    /// <summary>
    /// WO 완료 기준(CompletionBasis)에 따른 진행 수량/진척률 표시 — "양품 100개 채우기"
    /// (2026-08-18, Web #68 과 세트). 구버전 Web(필드 미전송)은 Produced 기본값으로
    /// 기존 표시가 유지돼야 한다.
    /// </summary>
    public class WorkOrderCompletionBasisTests
    {
        [Fact]
        public void WorkOrderDto_PassBasis_ProgressUsesPassQuantity()
        {
            var dto = new WorkOrderDto
            {
                CompletionBasis = "Pass",
                PlannedQuantity = 100,
                ProducedQuantity = 100,
                PassQuantity = 80,
                NgQuantity = 20
            };

            Assert.Equal(80, dto.ProgressQuantity);
            Assert.Equal(80.0, dto.Progress);
            Assert.Contains("양품 80 / 100", dto.ProgressText);
        }

        [Fact]
        public void WorkOrderDto_DefaultBasis_IsProduced_KeepsLegacyDisplay()
        {
            // 구버전 Web 서버는 CompletionBasis 를 보내지 않음 → 기본 Produced.
            var dto = new WorkOrderDto
            {
                PlannedQuantity = 100,
                ProducedQuantity = 100,
                PassQuantity = 80
            };

            Assert.Equal("Produced", dto.CompletionBasis);
            Assert.Equal(100, dto.ProgressQuantity);
            Assert.Equal("100 / 100", dto.ProgressText);
        }

        [Fact]
        public void WorkOrderProgressDto_PassBasis_ProgressUsesPassQuantity()
        {
            var dto = new WorkOrderProgressDto
            {
                CompletionBasis = "Pass",
                PlannedQuantity = 100,
                ProducedQuantity = 120,
                PassQuantity = 100,
                NgQuantity = 20
            };

            Assert.Equal(100, dto.ProgressQuantity);
            Assert.Equal(100.0, dto.Progress);
        }

        [Fact]
        public void Sanitize_InvalidBasis_TruncatedNotCrashing()
        {
            var dto = new WorkOrderDto
            {
                CompletionBasis = new string('x', 100),
                PlannedQuantity = 10
            };

            dto.Sanitize();

            // 과길이 문자열은 잘려도 Produced 경로(기본 표시)로 동작해야 한다
            Assert.True(dto.CompletionBasis.Length <= 20);
            Assert.Equal(dto.ProducedQuantity, dto.ProgressQuantity);
        }
    }
}
