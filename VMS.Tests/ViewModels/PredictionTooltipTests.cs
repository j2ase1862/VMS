using System;
using VMS.Core.Models.Predictive;
using VMS.ViewModels;
using Xunit;

namespace VMS.Tests.ViewModels
{
    /// <summary>
    /// WB-28 (VMS 쪽) — 예측 칩 툴팁은 '다음 1시간' 이 마지막 검사가 있었던 시간 기준임을 말하고,
    /// 검사 건수는 그 마지막 집계 시간의 건수라고 적는다.
    /// </summary>
    public class PredictionTooltipTests
    {
        private static PredictionCurrentDto Dto(DateTime windowStartUtc) => new()
        {
            Status = "ok",
            PredictedNgRate = 0.034,
            WindowStart = windowStartUtc,
            ModelName = "ngrate",
            ModelVersion = "3",
            InspectionCountThisHour = 12
        };

        [Fact]
        public void States_LastInspectionBasis_AndCountWording()
        {
            var start = DateTime.UtcNow.AddMinutes(10);
            var text = MainViewModel.FormatPredictionTooltip(Dto(start), DateTime.Now);

            Assert.Contains("마지막 검사가 있었던 시간의 다음 1시간", text);
            Assert.Contains("마지막 집계 시간의 검사: 12건", text);
            Assert.Contains("3.40%", text);
            Assert.DoesNotContain("이번 시간", text);
            Assert.DoesNotContain("이미 지난 시간", text);
        }

        [Fact]
        public void PastWindow_IsFlagged()
        {
            var start = DateTime.UtcNow.AddHours(-3);
            var text = MainViewModel.FormatPredictionTooltip(Dto(start), DateTime.Now);
            Assert.Contains("이미 지난 시간", text);
        }
    }
}
