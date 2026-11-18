using VMS.VisionSetup.ViewModels;
using Xunit;

namespace VMS.Tests.ViewModels
{
    /// <summary>
    /// VM-39 — Work Orders 창 상태 줄 건수는 필터로 보이는 수를 기준으로 한다.
    /// </summary>
    public class WorkOrderListCountTests
    {
        [Fact]
        public void AllShown_ShowsSingleCount()
            => Assert.Equal("3건", WorkOrderListViewModel.FormatCount(3, 3));

        [Fact]
        public void Filtered_ShowsShownAndTotal()
            => Assert.Equal("2건 (전체 3건)", WorkOrderListViewModel.FormatCount(2, 3));
    }
}
