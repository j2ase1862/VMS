using System.Threading;
using VMS.ViewModels;
using Xunit;

namespace VMS.Tests.ViewModels
{
    /// <summary>
    /// DashboardViewModel Tact/처리 시간 검증 — 현장 혼선(2026-08-10) 회귀:
    /// 수동 Grab+Inspect 사이의 대기 시간이 Tact(337.89s)로 표시되던 문제.
    /// Tact 는 AUTO RUN(updateTact=true) 중 검사 간 간격만 측정하고,
    /// 수동 검사(updateTact=false)는 측정 체인을 끊는다.
    /// </summary>
    public class DashboardViewModelTactTests
    {
        [Fact]
        public void ManualInspection_DoesNotUpdateTact()
        {
            var vm = new DashboardViewModel();

            vm.RecordInspectionResult(true, "Cam1", updateTact: false);
            Thread.Sleep(80);
            vm.RecordInspectionResult(true, "Cam1", updateTact: false);

            Assert.Equal(0, vm.TactTime);           // 수동 간격은 Tact 가 아니다
            Assert.False(vm.IsTactTimeExceeded);
            Assert.Equal(2, vm.TotalCount);          // 카운트는 정상 집계
        }

        [Fact]
        public void ManualInspection_BreaksChain_FirstAutoCycleDoesNotMeasureFromManual()
        {
            var vm = new DashboardViewModel();

            vm.RecordInspectionResult(true, "Cam1", updateTact: false); // 수동 — 체인 중단
            Thread.Sleep(80);
            vm.RecordInspectionResult(true, "Cam1", updateTact: true);  // 자동 첫 사이클

            // 수동 시점과의 간격(80ms+)이 Tact 로 잡히면 안 된다
            Assert.Equal(0, vm.TactTime);
        }

        [Fact]
        public void AutoRun_MeasuresIntervalBetweenInspections()
        {
            var vm = new DashboardViewModel();

            vm.RecordInspectionResult(true, "Cam1", updateTact: true);
            Thread.Sleep(80);
            vm.RecordInspectionResult(true, "Cam1", updateTact: true);

            Assert.True(vm.TactTime > 0, $"TactTime={vm.TactTime}");
            Assert.True(vm.TactTime < 5, $"TactTime={vm.TactTime}");
            Assert.False(vm.IsTactTimeExceeded);
        }

        [Fact]
        public void AutoRun_ExceededFlag_WhenOverTarget()
        {
            var vm = new DashboardViewModel { TargetTactTime = 0.01 };

            vm.RecordInspectionResult(true, "Cam1", updateTact: true);
            Thread.Sleep(80);
            vm.RecordInspectionResult(true, "Cam1", updateTact: true);

            Assert.True(vm.IsTactTimeExceeded);
        }

        [Fact]
        public void ProcessingTime_RecordedRegardlessOfTactMode()
        {
            var vm = new DashboardViewModel();

            vm.RecordInspectionResult(true, "Cam1", processingTimeMs: 123.46, updateTact: false);
            Assert.Equal(123.5, vm.ProcessingTimeMs);

            vm.RecordInspectionResult(true, "Cam1", processingTimeMs: 88.0, updateTact: true);
            Assert.Equal(88.0, vm.ProcessingTimeMs);
        }

        [Fact]
        public void ResetStatistics_ClearsProcessingTime()
        {
            var vm = new DashboardViewModel();
            vm.RecordInspectionResult(true, "Cam1", processingTimeMs: 50);

            vm.ResetStatisticsCommand.Execute(null);

            Assert.Equal(0, vm.ProcessingTimeMs);
            Assert.Equal(0, vm.TactTime);
            Assert.Equal(0, vm.TotalCount);
        }
    }
}
