using System;
using VMS.Core.Models.ParameterSync;
using Xunit;

namespace VMS.Core.Tests.Models
{
    /// <summary>
    /// VM-34 — Work Orders 창 Planned Start 는 현지 시각으로 보여야 한다.
    /// Web 은 UTC 로 저장해 보낸다(시간대 표시가 빠진 채 올 수도 있다).
    /// </summary>
    public class WorkOrderPlannedStartLocalTests
    {
        [Fact]
        public void Utc_IsConvertedToLocal()
        {
            var utc = new DateTime(2026, 11, 13, 0, 0, 0, DateTimeKind.Utc);
            var dto = new WorkOrderDto { PlannedStartAt = utc };
            Assert.Equal(utc.ToLocalTime(), dto.PlannedStartAtLocal);
        }

        [Fact]
        public void Unspecified_IsTreatedAsUtc()
        {
            var raw = new DateTime(2026, 11, 13, 4, 0, 0, DateTimeKind.Unspecified);
            var expected = DateTime.SpecifyKind(raw, DateTimeKind.Utc).ToLocalTime();
            Assert.Equal(expected, WorkOrderDto.ToLocalDisplayTime(raw));
        }

        [Fact]
        public void Local_IsKept_AndNullStaysNull()
        {
            var local = new DateTime(2026, 11, 13, 9, 0, 0, DateTimeKind.Local);
            Assert.Equal(local, WorkOrderDto.ToLocalDisplayTime(local));
            Assert.Null(new WorkOrderDto().PlannedStartAtLocal);
        }
    }
}
