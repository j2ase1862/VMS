using VMS.Services;
using Xunit;

namespace VMS.Tests.Services
{
    /// <summary>
    /// 생산 관리 게이트 — Web 연동 라인에서 작업자 로그인 · 작업지시 선택을 운전 조건으로 요구할지.
    /// 예전에는 Web 주소만 넣으면 둘 다 필수여서, Web 의 이력 · 품질 · 설비 관리만 쓰려는 라인이 운전할 수 없었다.
    /// </summary>
    public class ProductionGateTests
    {
        [Fact]
        public void Default_web_line_requires_login_and_work_order()
        {
            var gate = new ProductionGate(WebIntegrated: true, RequireOperatorLogin: true, RequireWorkOrder: true);

            Assert.False(gate.AllowsRun(operatorLoggedIn: false, workOrderStatus: null));
            Assert.False(gate.AllowsRun(operatorLoggedIn: true, workOrderStatus: null));
            Assert.True(gate.AllowsRun(operatorLoggedIn: true, workOrderStatus: "InProgress"));
            Assert.False(gate.AllowsCamera(operatorLoggedIn: false));
        }

        [Fact]
        public void Standalone_ignores_both_switches()
        {
            var gate = new ProductionGate(WebIntegrated: false, RequireOperatorLogin: true, RequireWorkOrder: true);

            Assert.False(gate.LoginRequired);
            Assert.True(gate.AllowsRun(operatorLoggedIn: false, workOrderStatus: null));
            Assert.True(gate.AllowsCamera(operatorLoggedIn: false));
        }

        [Fact]
        public void Login_only_runs_without_work_order()
        {
            var gate = new ProductionGate(WebIntegrated: true, RequireOperatorLogin: true, RequireWorkOrder: false);

            Assert.False(gate.AllowsRun(operatorLoggedIn: false, workOrderStatus: null));
            Assert.True(gate.AllowsRun(operatorLoggedIn: true, workOrderStatus: null));
        }

        [Fact]
        public void Both_off_runs_without_login_or_work_order()
        {
            var gate = new ProductionGate(WebIntegrated: true, RequireOperatorLogin: false, RequireWorkOrder: false);

            Assert.True(gate.AllowsRun(operatorLoggedIn: false, workOrderStatus: null));
            Assert.True(gate.AllowsCamera(operatorLoggedIn: false));
            Assert.False(gate.LoginRequired);   // 작업자 역할 메뉴 제한도 걸지 않는다
        }

        [Fact]
        public void Work_order_switch_needs_login_switch()
        {
            // WO 목록은 작업자 로그인으로 조회한다 — 로그인 없이 WO 필수는 성립하지 않는다.
            var gate = new ProductionGate(WebIntegrated: true, RequireOperatorLogin: false, RequireWorkOrder: true);

            Assert.False(gate.WorkOrderRequired);
            Assert.True(gate.AllowsRun(operatorLoggedIn: false, workOrderStatus: null));
        }

        [Theory]
        [InlineData("Completed")]
        [InlineData("Closed")]
        public void Finished_work_order_blocks_even_when_optional(string status)
        {
            // 완료 · 마감 WO 로 올리면 수량이 오르지 않는다 — 골랐다면 필수 여부와 관계없이 막는다.
            var gate = new ProductionGate(WebIntegrated: true, RequireOperatorLogin: false, RequireWorkOrder: false);

            Assert.False(gate.AllowsRun(operatorLoggedIn: true, workOrderStatus: status));
            Assert.True(gate.AllowsRun(operatorLoggedIn: true, workOrderStatus: "Planned"));
        }
    }
}
