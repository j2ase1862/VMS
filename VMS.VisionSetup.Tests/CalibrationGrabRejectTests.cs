using VMS.VisionSetup.ViewModels;
using Xunit;
using Outcome = VMS.VisionSetup.ViewModels.CalibrationManagerViewModel.VmsGrabRequestOutcome;

namespace VMS.VisionSetup.Tests
{
    /// <summary>
    /// 캘리브레이션 창 [Grab from VMS] — VMS 가 촬영을 거절했을 때 공유 메모리 프레임을 읽지 않는다
    /// (2026-09-17 v1.40.0 현장 E1: 운전 중 거절됐는데도 사이클 프레임이 들어와 거절 사유가 덮였다).
    /// 요청 채널이 없는 구버전 VMS 만 예전 읽기 동작을 유지한다.
    /// </summary>
    public class CalibrationGrabRejectTests
    {
        [Theory]
        [InlineData(Outcome.NoChannel, true)]
        [InlineData(Outcome.Success, true)]
        [InlineData(Outcome.Rejected, false)]
        [InlineData(Outcome.NoResponse, false)]
        [InlineData(Outcome.Error, false)]
        public void ShouldReadSharedFrame_OnlyWhenNoChannelOrSuccess(Outcome outcome, bool expected)
        {
            Assert.Equal(expected, CalibrationManagerViewModel.ShouldReadSharedFrame(outcome));
        }

        [Fact]
        public void Result_CarriesFrameCounterOnlyOnSuccess()
        {
            var ok = new CalibrationManagerViewModel.VmsGrabRequestResult(Outcome.Success, 42);
            Assert.Equal(42, ok.FrameCounter);

            var rejected = new CalibrationManagerViewModel.VmsGrabRequestResult(Outcome.Rejected, null);
            Assert.Null(rejected.FrameCounter);
            Assert.False(CalibrationManagerViewModel.ShouldReadSharedFrame(rejected.Outcome));
        }
    }
}
