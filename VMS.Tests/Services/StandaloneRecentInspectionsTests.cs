using System.Collections.Generic;
using System.Threading.Tasks;
using VMS.Core.Models;
using VMS.Core.Models.ParameterSync;
using VMS.Core.Services;
using VMS.Services;
using Xunit;

namespace VMS.Tests.Services
{
    /// <summary>
    /// 단독 모드 Recent Inspections 공백 회귀 (2026-09-04) —
    /// 로컬 이력 push 가 "Web 동기화 서비스 + Web 레시피 ID" 가드 안쪽에 있어
    /// 단독 모드(ParameterSyncService == null)에서는 사이드 패널이 항상 비어 있었다.
    /// 로컬 이력은 Web 연동 여부와 무관하게 남아야 하며, NG 코드 자리에는
    /// 실패한 도구 이름이, 레시피 이름은 로컬 레시피 이름이 들어가야 한다.
    /// InspectionService 정적 상태·RecentInspectionsService 싱글톤을 공유하므로
    /// 같은 컬렉션의 테스트와 직렬 실행.
    /// </summary>
    [Collection(InspectionServiceStaticsCollection.Name)]
    public class StandaloneRecentInspectionsTests
    {
        private static void EnterStandalone(string? recipeName = "LocalRecipe")
        {
            InspectionService.ParameterSyncService = null;
            InspectionService.CurrentRecipeNameProvider = () => recipeName;
            InspectionService.SetCycleAccumulation(false);
            RecentInspectionsService.Instance.Clear();
        }

        private static void Leave()
        {
            InspectionService.SetCycleAccumulation(false);
            InspectionService.CurrentRecipeNameProvider = null;
            RecentInspectionsService.Instance.Clear();
        }

        [Fact]
        public async Task Standalone_CycleFlush_RecordsLocally_WithFailedToolsAsNgCodes()
        {
            EnterStandalone();
            try
            {
                InspectionService.SetCycleAccumulation(true);

                // 사이클 내 검사 2건 — 누적 중에는 패널에 push 되지 않는다.
                InspectionService.RecordInspectionOutcome(true, new List<string>(), new List<ParameterResultDto>());
                InspectionService.RecordInspectionOutcome(false, new[] { "Blob 1", "Blob 1", "Edge 2" }, new List<ParameterResultDto>());
                Assert.Empty(RecentInspectionsService.Instance.Items);

                var uploaded = await InspectionService.FlushCycleResultAsync(overallPass: false);

                Assert.False(uploaded);   // Web 업로드는 없음 (단독 모드)
                var rec = Assert.Single(RecentInspectionsService.Instance.Items);
                Assert.False(rec.IsPass);
                Assert.Equal(new[] { "Blob 1", "Edge 2" }, rec.NgCodes);
                Assert.Equal("LocalRecipe", rec.RecipeName);
                Assert.Equal(0, rec.RecipeId);
                Assert.Null(rec.WorkOrderId);
                Assert.Equal("LocalRecipe", rec.DisplayLabel);
                Assert.Equal(1, RecentInspectionsService.Instance.NgCount);
            }
            finally { Leave(); }
        }

        [Fact]
        public async Task Standalone_CycleFlush_ClearsFailedTools_BetweenCycles()
        {
            EnterStandalone();
            try
            {
                InspectionService.SetCycleAccumulation(true);
                InspectionService.RecordInspectionOutcome(false, new[] { "Blob 1" }, new List<ParameterResultDto>());
                await InspectionService.FlushCycleResultAsync(overallPass: false);

                // 다음 사이클 OK — 이전 사이클의 실패 도구가 새어 나오면 안 된다.
                InspectionService.RecordInspectionOutcome(true, new List<string>(), new List<ParameterResultDto>());
                await InspectionService.FlushCycleResultAsync(overallPass: true);

                Assert.Equal(2, RecentInspectionsService.Instance.Items.Count);
                var latest = RecentInspectionsService.Instance.Items[0];
                Assert.True(latest.IsPass);
                Assert.Empty(latest.NgCodes);
                Assert.Equal(1, RecentInspectionsService.Instance.PassCount);
                Assert.Equal(1, RecentInspectionsService.Instance.NgCount);
            }
            finally { Leave(); }
        }

        [Fact]
        public void Standalone_ManualInspection_RecordsImmediately()
        {
            EnterStandalone();
            try
            {
                InspectionService.RecordInspectionOutcome(true, new List<string>(), new List<ParameterResultDto>());
                InspectionService.RecordInspectionOutcome(false, new[] { "Pattern 3" }, new List<ParameterResultDto>());

                Assert.Equal(2, RecentInspectionsService.Instance.Items.Count);
                Assert.True(RecentInspectionsService.Instance.Items[1].IsPass);
                Assert.False(RecentInspectionsService.Instance.Items[0].IsPass);
                Assert.Equal("Pattern 3", RecentInspectionsService.Instance.Items[0].NgCodesText);
            }
            finally { Leave(); }
        }

        [Fact]
        public void Standalone_NoRecipeName_DisplayLabelIsNotRecipeZero()
        {
            EnterStandalone(recipeName: null);
            try
            {
                var rec = InspectionService.RecordLocalInspection(true, new List<ParameterResultDto>(), new List<string>());
                Assert.DoesNotContain("Recipe#0", rec.DisplayLabel);
                Assert.False(string.IsNullOrWhiteSpace(rec.DisplayLabel));
            }
            finally { Leave(); }
        }

        [Fact]
        public void Standalone_RecipeNameProviderThrows_StillRecords()
        {
            EnterStandalone();
            InspectionService.CurrentRecipeNameProvider = () => throw new System.InvalidOperationException("boom");
            try
            {
                var rec = InspectionService.RecordLocalInspection(false, new List<ParameterResultDto>(), new[] { "Blob 1" });
                Assert.Single(RecentInspectionsService.Instance.Items);
                Assert.Null(rec.RecipeName);
            }
            finally { Leave(); }
        }
    }

    /// <summary>InspectionService 정적 상태를 만지는 테스트 클래스들의 직렬화 컬렉션.</summary>
    [CollectionDefinition(Name)]
    public class InspectionServiceStaticsCollection
    {
        public const string Name = "InspectionServiceStatics";
    }
}
