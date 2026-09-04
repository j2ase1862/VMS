using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using VMS.Services.LocalHistory;
using VMS.ViewModels;
using Xunit;

namespace VMS.Tests.ViewModels
{
    /// <summary>
    /// 생산 이력 조회 창 VM — 필터·페이징·일별 집계·NG 파레토·상세 이미지 상태·CSV 전량 내보내기.
    /// 실제 SQLite 저장소(임시 폴더)를 사용해 저장소↔VM 계약을 함께 검증.
    /// </summary>
    public class InspectionHistoryViewModelTests : IDisposable
    {
        private readonly string _tempDir;
        private readonly LocalInspectionHistoryStore _store;

        public InspectionHistoryViewModelTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), $"insp_vm_{Guid.NewGuid():N}");
            _store = new LocalInspectionHistoryStore(_tempDir);
        }

        public void Dispose()
        {
            _store.Dispose();
            try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); }
            catch { }
        }

        private void Seed(int count, Func<int, bool> pass, Func<int, string> recipe, DateTime? baseUtc = null)
        {
            var t0 = baseUtc ?? DateTime.UtcNow.AddHours(-1);
            for (int i = 0; i < count; i++)
            {
                var ok = pass(i);
                _store.Record(new LocalInspectionEntry
                {
                    InspectedAtUtc = t0.AddSeconds(i),
                    IsPass = ok,
                    RecipeName = recipe(i),
                    NgCodes = ok ? new List<string>() : new List<string> { i % 3 == 0 ? "Blob 1" : "Edge 2" },
                    CorrelationKey = $"k{i}",
                    ToolResults = new List<LocalToolResult> { new() { ToolName = "Blob 1", ToolType = "Blob", Success = ok, ExecutionTimeMs = 4 } },
                    Mode = LocalInspectionMode.Cycle
                });
            }
            Assert.True(_store.Flush(TimeSpan.FromSeconds(10)));
        }

        [Fact]
        public void Search_DefaultWeek_LoadsPage_AndAggregates()
        {
            Seed(10, i => i % 2 == 0, _ => "R1");
            var vm = new InspectionHistoryViewModel(_store);

            Assert.Equal(10, vm.TotalCount);
            Assert.Equal(10, vm.Entries.Count);
            Assert.NotNull(vm.SelectedEntry);
            Assert.True(vm.HasDetail);
            Assert.Equal(10, vm.SummaryTotal);
            Assert.Equal(5, vm.SummaryPass);
            Assert.Equal(5, vm.SummaryNg);
            Assert.Equal(50.0, vm.SummaryPassRate);
            Assert.Single(vm.DailySummaries);
            Assert.Equal(new string?[] { null, "R1" }, vm.RecipeOptions);
            Assert.Contains("10건", vm.StatusMessage);
        }

        [Fact]
        public void VerdictAndRecipeFilters_Apply()
        {
            Seed(12, i => i % 4 != 0, i => i < 6 ? "A" : "B");
            var vm = new InspectionHistoryViewModel(_store);

            vm.SelectedVerdict = InspectionHistoryViewModel.VerdictNg;
            vm.SearchCommand.Execute(null);
            Assert.Equal(3, vm.TotalCount);
            Assert.All(vm.Entries, e => Assert.False(e.IsPass));

            vm.SelectedVerdict = InspectionHistoryViewModel.VerdictAll;
            vm.SelectedRecipe = "B";
            vm.SearchCommand.Execute(null);
            Assert.Equal(6, vm.TotalCount);
            Assert.All(vm.Entries, e => Assert.Equal("B", e.RecipeName));

            vm.NgCodeText = "Edge";
            vm.SearchCommand.Execute(null);
            Assert.All(vm.Entries, e => Assert.Contains("Edge 2", e.NgCodesText));
        }

        [Fact]
        public void Paging_MovesThroughResults_Newest_First()
        {
            Seed(7, _ => true, _ => "R");
            var vm = new InspectionHistoryViewModel(_store) { PageSize = 3 };
            vm.SearchCommand.Execute(null);

            Assert.Equal(3, vm.Entries.Count);
            Assert.True(vm.CanGoNext);
            Assert.False(vm.CanGoPrev);
            Assert.Equal("k6", vm.Entries[0].CorrelationKey);   // 최신 먼저

            vm.NextPageCommand.Execute(null);
            Assert.Equal(1, vm.PageIndex);
            Assert.Equal("k3", vm.Entries[0].CorrelationKey);
            vm.NextPageCommand.Execute(null);
            Assert.Single(vm.Entries);
            Assert.False(vm.CanGoNext);
            Assert.True(vm.CanGoPrev);
            Assert.Contains("페이지 3/3", vm.PageText);

            vm.PrevPageCommand.Execute(null);
            Assert.Equal(1, vm.PageIndex);
        }

        [Fact]
        public void NgPareto_RanksCodes_WithCumulativePercent()
        {
            // i%4==0 → NG: i=0(Blob),4(Edge),8(Edge) → Edge 2건, Blob 1건
            Seed(12, i => i % 4 != 0, _ => "R");
            var vm = new InspectionHistoryViewModel(_store);

            Assert.Equal(2, vm.NgCodeRows.Count);
            Assert.Equal("Edge 2", vm.NgCodeRows[0].Code);
            Assert.Equal(2, vm.NgCodeRows[0].Count);
            Assert.Equal(66.7, vm.NgCodeRows[0].Percent);
            Assert.Equal(100.0, vm.NgCodeRows[1].CumulativePercent);
        }

        [Fact]
        public void Detail_WithoutImage_ShowsGuidance_AndMissingFileIsReported()
        {
            Seed(1, _ => false, _ => "R");
            var vm = new InspectionHistoryViewModel(_store);
            Assert.Null(vm.DetailImage);
            Assert.Contains("저장된 이미지 없음", vm.DetailImageStatus);

            _store.SetImagePath("k0", Path.Combine(_tempDir, "missing.png"), isNg: true);
            Assert.True(_store.Flush(TimeSpan.FromSeconds(10)));
            vm.SearchCommand.Execute(null);
            Assert.Contains("이미지 파일이 없습니다", vm.DetailImageStatus);
        }

        [Fact]
        public void ExportAllToCsv_WritesEveryRow_NotJustCurrentPage()
        {
            Seed(25, i => i % 5 != 0, _ => "R,1");   // 콤마 포함 레시피 → 인용 검증
            var vm = new InspectionHistoryViewModel(_store) { PageSize = 10 };
            vm.SearchCommand.Execute(null);
            Assert.Equal(10, vm.Entries.Count);

            var csv = Path.Combine(_tempDir, "out.csv");
            var written = vm.ExportAllToCsv(csv);
            Assert.Equal(25, written);

            var lines = File.ReadAllLines(csv);
            Assert.Equal(26, lines.Length);
            Assert.StartsWith("InspectedAt,Verdict,Recipe", lines[0]);
            Assert.Contains("\"R,1\"", lines[1]);
            Assert.Contains("Blob 1=NG", lines.First(l => l.Contains(",NG,")));
            // UTF-8 BOM (Excel 한글)
            var bytes = File.ReadAllBytes(csv);
            Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes.Take(3).ToArray());
        }

        [Fact]
        public void InvalidRange_ReportsWithoutThrowing()
        {
            var vm = new InspectionHistoryViewModel(_store);
            vm.FromDate = DateTime.Today;
            vm.ToDate = DateTime.Today.AddDays(-3);
            vm.SearchCommand.Execute(null);
            Assert.Contains("기간이 올바르지 않습니다", vm.StatusMessage);
        }
    }
}
