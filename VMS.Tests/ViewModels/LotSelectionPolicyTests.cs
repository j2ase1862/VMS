using System.Collections.Generic;
using VMS.ViewModels;
using Xunit;

namespace VMS.Tests.ViewModels
{
    /// <summary>
    /// 멀티 Lot 콤보 선택 정책 — 현재 Lot 이 Open 목록에 남아 있으면 유지,
    /// 없고 목록이 1개면 자동 선택(기존 B1 단일 Lot 경험 보존), 여러 개면 작업자 선택 대기.
    /// </summary>
    public class LotSelectionPolicyTests
    {
        [Fact]
        public void KeepsCurrentSelection_WhenStillOpen()
        {
            Assert.Equal(5, MainViewModel.ResolveLotSelection(5, new List<int> { 3, 5, 7 }));
        }

        [Fact]
        public void AutoSelects_WhenSingleOpenLot()
        {
            // 현재 선택 없음 / 마감된 Lot 선택 중 — 목록이 1개면 자동
            Assert.Equal(9, MainViewModel.ResolveLotSelection(null, new List<int> { 9 }));
            Assert.Equal(9, MainViewModel.ResolveLotSelection(4, new List<int> { 9 }));
        }

        [Fact]
        public void WaitsForOperator_WhenMultipleAndNoneMatching()
        {
            // 선택 중이던 Lot 이 마감되어 목록에 없고 여러 개 남음 — 자동 선택하지 않음
            Assert.Null(MainViewModel.ResolveLotSelection(4, new List<int> { 3, 5 }));
            Assert.Null(MainViewModel.ResolveLotSelection(null, new List<int> { 3, 5 }));
        }

        [Fact]
        public void Empty_WhenNoOpenLots()
        {
            Assert.Null(MainViewModel.ResolveLotSelection(5, new List<int>()));
            Assert.Null(MainViewModel.ResolveLotSelection(null, new List<int>()));
        }
    }
}
