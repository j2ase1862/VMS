using VMS.ViewModels;
using Xunit;

namespace VMS.Tests.ViewModels
{
    /// <summary>
    /// WO 종결 상태 판정 — Web 수동 완료 폴백(OnWorkOrderProgressed)이 운전 정지를
    /// 발동시키는 조건. 구버전 Web(staleWorkOrder 미전송)에서도 Status 문자열만으로
    /// 종결을 인지해야 한다 (2026-08-19).
    /// </summary>
    public class WorkOrderFinishedStatusTests
    {
        [Theory]
        [InlineData("Completed")]
        [InlineData("Closed")]
        public void FinishedStatuses_True(string status)
            => Assert.True(MainViewModel.IsFinishedWorkOrderStatus(status));

        [Theory]
        [InlineData("Planned")]
        [InlineData("InProgress")]
        [InlineData("")]
        [InlineData(null)]
        [InlineData("completed")]   // 서버는 정확한 대소문자 상수만 전송 — 오타/변조는 미판정
        public void OtherStatuses_False(string? status)
            => Assert.False(MainViewModel.IsFinishedWorkOrderStatus(status));
    }
}
