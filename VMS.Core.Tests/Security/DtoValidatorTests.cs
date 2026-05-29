using VMS.Core.Models.ParameterSync;
using VMS.Core.Models.Predictive;
using VMS.Core.Security;
using Xunit;

namespace VMS.Core.Tests.Security
{
    /// <summary>
    /// DtoValidator 의 primitive sanitization 및 3개 DTO 의 Sanitize() 통합 동작 검증.
    /// AuditLogger 부수효과는 best-effort 이므로 여기서는 반환값 / 필드값만 검증.
    /// </summary>
    public class DtoValidatorTests
    {
        // ─── ClampInt ─────────────────────────────────────────────

        [Fact]
        public void ClampInt_InRange_Unchanged()
        {
            Assert.Equal(50, DtoValidator.ClampInt(50, 0, 100, "x"));
        }

        [Fact]
        public void ClampInt_BelowMin_ReturnsMin()
        {
            Assert.Equal(0, DtoValidator.ClampInt(-10, 0, 100, "x"));
        }

        [Fact]
        public void ClampInt_AboveMax_ReturnsMax()
        {
            Assert.Equal(100, DtoValidator.ClampInt(999, 0, 100, "x"));
        }

        [Fact]
        public void ClampInt_AtBoundary_Unchanged()
        {
            Assert.Equal(0, DtoValidator.ClampInt(0, 0, 100, "x"));
            Assert.Equal(100, DtoValidator.ClampInt(100, 0, 100, "x"));
        }

        // ─── ClampDouble ─────────────────────────────────────────

        [Fact]
        public void ClampDouble_InRange_Unchanged()
        {
            Assert.Equal(0.5, DtoValidator.ClampDouble(0.5, 0.0, 1.0, "x"));
        }

        [Fact]
        public void ClampDouble_NaN_ReturnsMin()
        {
            Assert.Equal(0.0, DtoValidator.ClampDouble(double.NaN, 0.0, 1.0, "x"));
        }

        [Fact]
        public void ClampDouble_PositiveInfinity_ReturnsMin()
        {
            // NaN/Inf 는 의도적으로 min 으로 매핑 — 위험한 외부 값을 안전 default 로.
            Assert.Equal(0.0, DtoValidator.ClampDouble(double.PositiveInfinity, 0.0, 1.0, "x"));
        }

        [Fact]
        public void ClampDouble_AboveMax_ReturnsMax()
        {
            Assert.Equal(1.0, DtoValidator.ClampDouble(99.0, 0.0, 1.0, "x"));
        }

        [Fact]
        public void ClampDouble_Nullable_NullPassThrough()
        {
            Assert.Null(DtoValidator.ClampDouble((double?)null, 0.0, 1.0, "x"));
        }

        [Fact]
        public void ClampDouble_Nullable_ClampsValue()
        {
            Assert.Equal(1.0, DtoValidator.ClampDouble((double?)5.0, 0.0, 1.0, "x"));
        }

        // ─── Truncate ────────────────────────────────────────────

        [Fact]
        public void Truncate_WithinLimit_Unchanged()
        {
            Assert.Equal("hello", DtoValidator.Truncate("hello", 10, "x"));
        }

        [Fact]
        public void Truncate_OverLimit_Truncated()
        {
            var result = DtoValidator.Truncate("0123456789abcdef", 5, "x");
            Assert.Equal("01234", result);
            Assert.Equal(5, result!.Length);
        }

        [Fact]
        public void Truncate_Null_PassThrough()
        {
            Assert.Null(DtoValidator.Truncate(null, 10, "x"));
        }

        [Fact]
        public void Truncate_Empty_PassThrough()
        {
            Assert.Equal("", DtoValidator.Truncate("", 10, "x"));
        }

        // ─── EnsureAllowed ───────────────────────────────────────

        [Fact]
        public void EnsureAllowed_InWhitelist_Returned()
        {
            var allowed = new[] { "A", "B", "C" };
            Assert.Equal("B", DtoValidator.EnsureAllowed("B", allowed, "A", "x"));
        }

        [Fact]
        public void EnsureAllowed_NotInWhitelist_ReturnsDefault()
        {
            var allowed = new[] { "A", "B", "C" };
            Assert.Equal("A", DtoValidator.EnsureAllowed("Hacker", allowed, "A", "x"));
        }

        [Fact]
        public void EnsureAllowed_Null_ReturnsDefault()
        {
            var allowed = new[] { "A", "B" };
            Assert.Equal("A", DtoValidator.EnsureAllowed(null, allowed, "A", "x"));
        }

        // ─── WorkOrderProgressDto.Sanitize ───────────────────────

        [Fact]
        public void WorkOrderProgressDto_Sanitize_NegativeQuantities_ClampedToZero()
        {
            var dto = new WorkOrderProgressDto
            {
                Id = -1,
                PlannedQuantity = -100,
                ProducedQuantity = -50,
                PassQuantity = -10,
                NgQuantity = -5,
                OrderNo = "WO-1",
                Status = "InProgress"
            };
            dto.Sanitize();

            Assert.Equal(0, dto.Id);
            Assert.Equal(0, dto.PlannedQuantity);
            Assert.Equal(0, dto.ProducedQuantity);
            Assert.Equal(0, dto.PassQuantity);
            Assert.Equal(0, dto.NgQuantity);
        }

        [Fact]
        public void WorkOrderProgressDto_Sanitize_AbsurdQuantities_ClampedToMax()
        {
            var dto = new WorkOrderProgressDto
            {
                PlannedQuantity = 5_000_000,
                ProducedQuantity = int.MaxValue
            };
            dto.Sanitize();

            Assert.Equal(1_000_000, dto.PlannedQuantity);
            Assert.Equal(1_000_000, dto.ProducedQuantity);
        }

        [Fact]
        public void WorkOrderProgressDto_Sanitize_OverlongStrings_Truncated()
        {
            var dto = new WorkOrderProgressDto
            {
                OrderNo = new string('A', 500),
                Status = new string('B', 200)
            };
            dto.Sanitize();

            Assert.Equal(100, dto.OrderNo.Length);
            Assert.Equal(50, dto.Status.Length);
        }

        [Fact]
        public void WorkOrderProgressDto_Sanitize_ReturnsSelf_ForChaining()
        {
            var dto = new WorkOrderProgressDto();
            Assert.Same(dto, dto.Sanitize());
        }

        // ─── OperatorSessionDto.Sanitize ─────────────────────────

        [Fact]
        public void OperatorSessionDto_Sanitize_InvalidRole_DefaultsToOperator()
        {
            var dto = new OperatorSessionDto { Role = "Admin" };  // 화이트리스트 밖
            dto.Sanitize();
            Assert.Equal(OperatorRoles.Operator, dto.Role);
        }

        [Theory]
        [InlineData("Operator")]
        [InlineData("Lead")]
        [InlineData("Supervisor")]
        public void OperatorSessionDto_Sanitize_ValidRole_Preserved(string role)
        {
            var dto = new OperatorSessionDto { Role = role };
            dto.Sanitize();
            Assert.Equal(role, dto.Role);
        }

        [Fact]
        public void OperatorSessionDto_Sanitize_NullRole_DefaultsToOperator()
        {
            var dto = new OperatorSessionDto { Role = null! };
            dto.Sanitize();
            Assert.Equal(OperatorRoles.Operator, dto.Role);
        }

        [Fact]
        public void OperatorSessionDto_Sanitize_OverlongOperatorName_Truncated()
        {
            var dto = new OperatorSessionDto
            {
                OperatorName = new string('X', 1000),
                Role = OperatorRoles.Operator
            };
            dto.Sanitize();
            Assert.Equal(200, dto.OperatorName.Length);
        }

        [Fact]
        public void OperatorSessionDto_Sanitize_NegativeIds_ClampedToZero()
        {
            var dto = new OperatorSessionDto
            {
                Id = -1,
                OperatorId = -50,
                ClientId = -100,
                ClientIndex = -1,
                Role = OperatorRoles.Operator
            };
            dto.Sanitize();
            Assert.Equal(0, dto.Id);
            Assert.Equal(0, dto.OperatorId);
            Assert.Equal(0, dto.ClientId);
            Assert.Equal(0, dto.ClientIndex);
        }

        // ─── PredictionCurrentDto.Sanitize ───────────────────────

        [Fact]
        public void PredictionCurrentDto_Sanitize_NgRateAbove1_ClampedTo1()
        {
            var dto = new PredictionCurrentDto { PredictedNgRate = 2.5 };
            dto.Sanitize();
            Assert.Equal(1.0, dto.PredictedNgRate);
        }

        [Fact]
        public void PredictionCurrentDto_Sanitize_NgRateNegative_ClampedTo0()
        {
            var dto = new PredictionCurrentDto { PredictedNgRate = -0.1 };
            dto.Sanitize();
            Assert.Equal(0.0, dto.PredictedNgRate);
        }

        [Fact]
        public void PredictionCurrentDto_Sanitize_NgRateNaN_ClampedTo0()
        {
            var dto = new PredictionCurrentDto { PredictedNgRate = double.NaN };
            dto.Sanitize();
            Assert.Equal(0.0, dto.PredictedNgRate);
        }

        [Fact]
        public void PredictionCurrentDto_Sanitize_NgRateNull_PassThrough()
        {
            var dto = new PredictionCurrentDto { PredictedNgRate = null, Status = "no_data" };
            dto.Sanitize();
            Assert.Null(dto.PredictedNgRate);
        }

        [Fact]
        public void PredictionCurrentDto_Sanitize_ValidRate_Preserved()
        {
            var dto = new PredictionCurrentDto { PredictedNgRate = 0.05 };
            dto.Sanitize();
            Assert.Equal(0.05, dto.PredictedNgRate);
        }

        [Fact]
        public void PredictionCurrentDto_Sanitize_OverlongStrings_Truncated()
        {
            var dto = new PredictionCurrentDto
            {
                RecipeName = new string('R', 500),
                ModelName = new string('M', 500),
                ModelVersion = new string('V', 200),
                Status = new string('S', 100),
                Message = new string('M', 1000)
            };
            dto.Sanitize();
            Assert.Equal(200, dto.RecipeName!.Length);
            Assert.Equal(200, dto.ModelName!.Length);
            Assert.Equal(100, dto.ModelVersion!.Length);
            Assert.Equal(50, dto.Status.Length);
            Assert.Equal(500, dto.Message!.Length);
        }

        // ─── WorkOrderDto.Sanitize (Phase 3c) ─────────────────────

        [Fact]
        public void WorkOrderDto_Sanitize_NegativeIds_ClampedToZero()
        {
            var dto = new WorkOrderDto
            {
                Id = -1, ProductId = -2, ClientId = -3, RecipeId = -4
            };
            dto.Sanitize();
            Assert.Equal(0, dto.Id);
            Assert.Equal(0, dto.ProductId);
            Assert.Equal(0, dto.ClientId);
            Assert.Equal(0, dto.RecipeId);
        }

        [Fact]
        public void WorkOrderDto_Sanitize_NullableClientIndex_NullPassThrough()
        {
            var dto = new WorkOrderDto { ClientIndex = null };
            dto.Sanitize();
            Assert.Null(dto.ClientIndex);
        }

        [Fact]
        public void WorkOrderDto_Sanitize_NullableClientIndex_OverMax_Clamped()
        {
            var dto = new WorkOrderDto { ClientIndex = 5_000_000 };
            dto.Sanitize();
            Assert.Equal(999_999, dto.ClientIndex);
        }

        [Fact]
        public void WorkOrderDto_Sanitize_OverlongStrings_Truncated()
        {
            var dto = new WorkOrderDto
            {
                OrderNo = new string('A', 200),
                ProductCode = new string('B', 200),
                ProductName = new string('C', 500),
                ClientName = new string('D', 500),
                RecipeName = new string('E', 500),
                Status = new string('F', 100),
                Note = new string('G', 1000)
            };
            dto.Sanitize();
            Assert.Equal(100, dto.OrderNo.Length);
            Assert.Equal(100, dto.ProductCode!.Length);
            Assert.Equal(200, dto.ProductName!.Length);
            Assert.Equal(200, dto.ClientName!.Length);
            Assert.Equal(200, dto.RecipeName!.Length);
            Assert.Equal(50, dto.Status.Length);
            Assert.Equal(500, dto.Note!.Length);
        }

        [Fact]
        public void WorkOrderDto_Sanitize_NullStatus_DefaultsToPlanned()
        {
            var dto = new WorkOrderDto { Status = null! };
            dto.Sanitize();
            Assert.Equal("Planned", dto.Status);
        }

        // ─── LotDto.Sanitize (Phase 3c) ───────────────────────────

        [Fact]
        public void LotDto_Sanitize_NegativeCounts_ClampedToZero()
        {
            var dto = new LotDto
            {
                Id = -1, WorkOrderId = -2, Sequence = -3,
                Quantity = -100, PassCount = -50, NgCount = -10
            };
            dto.Sanitize();
            Assert.Equal(0, dto.Id);
            Assert.Equal(0, dto.WorkOrderId);
            Assert.Equal(0, dto.Sequence);
            Assert.Equal(0, dto.Quantity);
            Assert.Equal(0, dto.PassCount);
            Assert.Equal(0, dto.NgCount);
        }

        [Fact]
        public void LotDto_Sanitize_AbsurdQuantity_ClampedToMax()
        {
            var dto = new LotDto { Quantity = int.MaxValue, NgCount = 999_999_999 };
            dto.Sanitize();
            Assert.Equal(1_000_000, dto.Quantity);
            Assert.Equal(1_000_000, dto.NgCount);
        }

        [Fact]
        public void LotDto_Sanitize_OverlongStrings_Truncated()
        {
            var dto = new LotDto
            {
                LotNumber = new string('L', 200),
                WorkOrderNo = new string('W', 200),
                Status = new string('S', 100),
                Note = new string('N', 1000)
            };
            dto.Sanitize();
            Assert.Equal(100, dto.LotNumber.Length);
            Assert.Equal(100, dto.WorkOrderNo!.Length);
            Assert.Equal(50, dto.Status.Length);
            Assert.Equal(500, dto.Note!.Length);
        }

        [Fact]
        public void LotDto_Sanitize_NullStatus_DefaultsToOpen()
        {
            var dto = new LotDto { Status = null! };
            dto.Sanitize();
            Assert.Equal("Open", dto.Status);
        }

        // ─── RecipeSummaryDto.Sanitize (Phase 3c) ─────────────────

        [Fact]
        public void RecipeSummaryDto_Sanitize_NegativeId_ClampedToZero()
        {
            var dto = new RecipeSummaryDto { Id = -1, Name = "Recipe-A" };
            dto.Sanitize();
            Assert.Equal(0, dto.Id);
            Assert.Equal("Recipe-A", dto.Name);
        }

        [Fact]
        public void RecipeSummaryDto_Sanitize_OverlongName_Truncated()
        {
            var dto = new RecipeSummaryDto
            {
                Name = new string('N', 500),
                Description = new string('D', 3000)
            };
            dto.Sanitize();
            Assert.Equal(200, dto.Name.Length);
            Assert.Equal(2000, dto.Description.Length);
        }

        // ─── RecipeParameterDto.Sanitize (Phase 3c) ───────────────

        [Fact]
        public void RecipeParameterDto_Sanitize_NegativeIds_ClampedToZero()
        {
            var dto = new RecipeParameterDto
            {
                Id = -1, RecipeId = -2, ParamCode = -3, ParamValue = 0.5
            };
            dto.Sanitize();
            Assert.Equal(0, dto.Id);
            Assert.Equal(0, dto.RecipeId);
            Assert.Equal(0, dto.ParamCode);
        }

        [Fact]
        public void RecipeParameterDto_Sanitize_ParamValueNaN_ClampedToMin()
        {
            var dto = new RecipeParameterDto { ParamValue = double.NaN };
            dto.Sanitize();
            Assert.Equal(-1e9, dto.ParamValue);
        }

        [Fact]
        public void RecipeParameterDto_Sanitize_ParamValueAbsurd_ClampedToMax()
        {
            var dto = new RecipeParameterDto { ParamValue = 1e15 };
            dto.Sanitize();
            Assert.Equal(1e9, dto.ParamValue);
        }

        [Fact]
        public void RecipeParameterDto_Sanitize_ParamValueValid_Preserved()
        {
            var dto = new RecipeParameterDto { ParamValue = 42.5 };
            dto.Sanitize();
            Assert.Equal(42.5, dto.ParamValue);
        }

        [Fact]
        public void RecipeParameterDto_Sanitize_OverlongStrings_Truncated()
        {
            var dto = new RecipeParameterDto
            {
                Description = new string('D', 1000),
                Category = new string('C', 500),
                Unit = new string('U', 200)
            };
            dto.Sanitize();
            Assert.Equal(500, dto.Description.Length);
            Assert.Equal(100, dto.Category.Length);
            Assert.Equal(50, dto.Unit.Length);
        }
    }
}
