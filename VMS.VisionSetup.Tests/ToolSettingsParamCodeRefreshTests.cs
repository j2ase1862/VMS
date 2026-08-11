using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Messaging;
using VMS.Core.Interfaces;
using VMS.Core.Models.ParameterSync;
using VMS.VisionSetup.Models;
using VMS.VisionSetup.ViewModels.ToolSettings;
using VMS.VisionSetup.VisionTools.BlobAnalysis;
using Xunit;

namespace VMS.VisionSetup.Tests
{
    /// <summary>
    /// 툴 설정 ParamCode 콤보의 Web 캐시 추적(2026-08-11) —
    /// 기존에는 VM 생성 시 1회만 채워져, VisionSetup 이 떠 있는 동안 Web 에서
    /// 추가한 파라미터가 콤보에 나타나지 않았다. WebParamCacheUpdatedMessage 를
    /// 받으면 콤보가 재구성되고, 기존 링크 선택이 유지되어야 한다.
    /// </summary>
    [Collection("StaticSyncService")]
    public class ToolSettingsParamCodeRefreshTests : IDisposable
    {
        private sealed class FakeSyncService : IParameterSyncService
        {
            public List<RecipeParameterDto> Cache { get; } = new();

            public event Action<bool, int>? SyncCompleted;
            public event Action<int, string, int>? RecipeLoaded;
            public event Action<List<RecipeSummaryDto>>? RecipeListChanged;
            public event Action<WorkOrderProgressDto>? WorkOrderProgressed;
            public event Action<WorkOrderProgressDto>? WorkOrderCompleted;

            public DateTime? LastSyncedAt => null;
            public int CurrentRecipeId { get; set; } = 1;
            public int CachedItemCount => Cache.Count;
            public int PendingUploadCount => 0;
            public List<RecipeSummaryDto> Recipes { get; } =
                new() { new RecipeSummaryDto { Id = 1, Name = "R1" } };

            public int SyncAsyncCalls;

            public Task<bool> SyncRecipesAsync() => Task.FromResult(true);
            public Task<bool> LoadRecipeAsync(int recipeId) => Task.FromResult(true);
            public Task<bool> SyncAsync() { SyncAsyncCalls++; return Task.FromResult(true); }
            public void StartPeriodicSync(int intervalSeconds = 60) { }
            public double ResolveValue(int paramCode, double defaultValue = 0) => defaultValue;
            public bool ValidateCode(int paramCode) => Cache.Any(p => p.ParamCode == paramCode);
            public (bool exists, bool synced) CheckCodeStatus(int paramCode, double v) => (false, false);
            public List<RecipeParameterDto> GetAll() => new(Cache);
            public Task<bool> UploadResultsAsync(int recipeId, List<ParameterResultDto> results,
                InspectionFeatureMetrics? featureMetrics = null, string? correlationKey = null)
                => Task.FromResult(true);
            public int? WorkOrderId { get; set; }
            public int? LotId { get; set; }
            public int? OperatorId { get; set; }
            public string? SerialNumber { get; set; }
            public void Dispose() { }
        }

        private readonly IParameterSyncService? _prevService;
        private readonly FakeSyncService _sync = new();

        public ToolSettingsParamCodeRefreshTests()
        {
            _prevService = ToolSettingsViewModelBase.SyncService;
            ToolSettingsViewModelBase.SyncService = _sync;
        }

        public void Dispose()
        {
            ToolSettingsViewModelBase.SyncService = _prevService;
        }

        private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 3000)
        {
            var sw = Stopwatch.StartNew();
            while (!condition() && sw.ElapsedMilliseconds < timeoutMs)
                await Task.Delay(30);
        }

        [Fact]
        public async Task Message_RefreshesCombo_AndPreservesLinkedSelection()
        {
            _sync.Cache.Add(new RecipeParameterDto { ParamCode = 9, Description = "Total Area Reference" });

            var tool = new BlobTool();
            tool.LinkedParamCodes["ExpectedArea"] = 9;
            var vm = new BlobToolSettingsViewModel(tool);
            await WaitUntilAsync(() => vm.AvailableParamCodes.Any(p => p.ParamCode == 9));

            // Web 에서 파라미터 추가 → 캐시 갱신 → 메시지 발행 (App.xaml.cs 브리지와 동일 경로)
            _sync.Cache.Add(new RecipeParameterDto { ParamCode = 10, Description = "Total Area Upper Tol" });
            WeakReferenceMessenger.Default.Send(new WebParamCacheUpdatedMessage());
            await WaitUntilAsync(() => vm.AvailableParamCodes.Any(p => p.ParamCode == 10));

            Assert.Contains(vm.AvailableParamCodes, p => p.ParamCode == 10);
            // 재구성 후에도 기존 링크 선택 유지
            Assert.Equal(9, vm.SelectedExpectedAreaCode?.ParamCode);
        }

        [Fact]
        public async Task OpeningSettings_ForcesCacheSync()
        {
            // 설정을 여는 시점(VM 생성)에 SyncAsync 강제 호출 — 60초 주기 대기 없이 즉시 최신화
            _ = new BlobToolSettingsViewModel(new BlobTool());
            await WaitUntilAsync(() => _sync.SyncAsyncCalls > 0);
            Assert.True(_sync.SyncAsyncCalls > 0);
        }

        [Fact]
        public void Dispose_UnregistersMessenger()
        {
            var vm = new BlobToolSettingsViewModel(new BlobTool());
            vm.Dispose();

            // 해제 후 메시지를 보내도 예외 없이 무시되어야 함
            WeakReferenceMessenger.Default.Send(new WebParamCacheUpdatedMessage());
            Assert.False(WeakReferenceMessenger.Default.IsRegistered<WebParamCacheUpdatedMessage>(vm));
        }
    }
}
