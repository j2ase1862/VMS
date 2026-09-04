using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using VMS.Core.Models.ParameterSync;
using VMS.Core.Services;
using VMS.Services;
using VMS.Services.LocalHistory;
using Xunit;

namespace VMS.Tests.Services
{
    /// <summary>
    /// InspectionService → 로컬 이력 저장소 연결 — Recent Inspections 와 같은 지점에서
    /// 사이클/수동 1건씩 기록되고(도구 결과·상관 키·사이클 ms 포함), 저장소가 없으면(옵션 off)
    /// 아무 일도 없어야 한다. 정적 상태 공유 컬렉션으로 직렬 실행.
    /// </summary>
    [Collection(InspectionServiceStaticsCollection.Name)]
    public class InspectionServiceLocalHistoryTests : IDisposable
    {
        private readonly string _tempDir;
        private readonly LocalInspectionHistoryStore _store;

        public InspectionServiceLocalHistoryTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), $"insp_svc_hist_{Guid.NewGuid():N}");
            _store = new LocalInspectionHistoryStore(_tempDir);
            InspectionService.ParameterSyncService = null;
            InspectionService.CurrentRecipeNameProvider = () => "LocalRecipe";
            InspectionService.HistoryStore = _store;
            InspectionService.SetCycleAccumulation(false);
            RecentInspectionsService.Instance.Clear();
        }

        public void Dispose()
        {
            InspectionService.SetCycleAccumulation(false);
            InspectionService.HistoryStore = null;
            InspectionService.CurrentRecipeNameProvider = null;
            RecentInspectionsService.Instance.Clear();
            _store.Dispose();
            try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); }
            catch { /* SQLite 핸들 잔존 가능 */ }
        }

        private static List<LocalToolResult> Tools(params (string name, bool ok)[] items)
        {
            var list = new List<LocalToolResult>();
            foreach (var (name, ok) in items)
                list.Add(new LocalToolResult { ToolName = name, ToolType = "Blob", Success = ok, ExecutionTimeMs = 5 });
            return list;
        }

        [Fact]
        public async Task CycleFlush_PersistsOneRow_WithAccumulatedToolsAndCycleMs()
        {
            InspectionService.SetCycleAccumulation(true);
            var key = InspectionService.CreateCorrelationKey();

            InspectionService.RecordInspectionOutcome(true, new List<string>(), new List<ParameterResultDto>(),
                new InspectionFeatureMetrics { CycleTimeMs = 30 }, key, Tools(("Blob 1", true)));
            InspectionService.RecordInspectionOutcome(false, new[] { "Edge 2" }, new List<ParameterResultDto>(),
                new InspectionFeatureMetrics { CycleTimeMs = 45 }, key, Tools(("Edge 2", false)));
            await InspectionService.FlushCycleResultAsync(overallPass: false);
            Assert.True(_store.Flush(TimeSpan.FromSeconds(10)));

            var row = Assert.Single(_store.Query(new LocalInspectionQuery()));
            Assert.False(row.IsPass);
            Assert.Equal(LocalInspectionMode.Cycle, row.Mode);
            Assert.Equal(key, row.CorrelationKey);
            Assert.Equal("LocalRecipe", row.RecipeName);
            Assert.Equal(new[] { "Edge 2" }, row.NgCodes);
            Assert.Equal(75, row.CycleTimeMs);
            Assert.Equal(2, row.ToolResults.Count);
            Assert.Equal("Blob 1", row.ToolResults[0].ToolName);
            Assert.False(row.ToolResults[1].Success);
        }

        [Fact]
        public void ManualInspection_PersistsImmediately()
        {
            InspectionService.RecordInspectionOutcome(true, new List<string>(), new List<ParameterResultDto>(),
                new InspectionFeatureMetrics { CycleTimeMs = 12 }, "manual-key", Tools(("Blob 1", true)));
            Assert.True(_store.Flush(TimeSpan.FromSeconds(10)));

            var row = Assert.Single(_store.Query(new LocalInspectionQuery()));
            Assert.True(row.IsPass);
            Assert.Equal(LocalInspectionMode.Manual, row.Mode);
            Assert.Equal("manual-key", row.CorrelationKey);
            Assert.Equal(12, row.CycleTimeMs);
            Assert.Empty(row.NgCodes);
        }

        [Fact]
        public void NoStore_StillRecordsRecentInspections_WithoutError()
        {
            InspectionService.HistoryStore = null;
            InspectionService.RecordInspectionOutcome(false, new[] { "Blob 1" }, new List<ParameterResultDto>());
            Assert.Single(RecentInspectionsService.Instance.Items);
            Assert.True(_store.Flush(TimeSpan.FromSeconds(10)));
            Assert.Equal(0, _store.Count(new LocalInspectionQuery()));
        }
    }
}
