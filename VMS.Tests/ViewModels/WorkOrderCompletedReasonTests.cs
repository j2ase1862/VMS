using VMS.Core.Models.ParameterSync;
using VMS.VisionSetup.Views;
using Xunit;

namespace VMS.Tests.ViewModels
{
    /// <summary>
    /// VM-37 — 작업지시 완료 창 문구가 자동 완료(계획 수량 도달)와 Web 수동 완료를 구분한다.
    /// </summary>
    public class WorkOrderCompletedReasonTests
    {
        [Fact]
        public void ReachedPlan_SaysAutoCompleted()
        {
            var p = new WorkOrderProgressDto { PlannedQuantity = 6, ProducedQuantity = 6, PassQuantity = 5 };
            Assert.Contains("자동 완료", WorkOrderCompletedDialog.CompletionReason(p));
        }

        [Fact]
        public void ManualCompleteBeforePlan_SaysWebCompleted()
        {
            var p = new WorkOrderProgressDto { PlannedQuantity = 6, ProducedQuantity = 2, PassQuantity = 2 };
            Assert.Contains("Web 에서", WorkOrderCompletedDialog.CompletionReason(p));
        }

        [Fact]
        public void PassBasis_UsesPassQuantity()
        {
            // 양품 기준이면 총 생산이 계획을 넘어도 양품이 모자라면 자동 완료가 아니다
            var p = new WorkOrderProgressDto
            {
                PlannedQuantity = 6, ProducedQuantity = 7, PassQuantity = 4, CompletionBasis = "Pass"
            };
            Assert.Contains("Web 에서", WorkOrderCompletedDialog.CompletionReason(p));
        }
    }
}
