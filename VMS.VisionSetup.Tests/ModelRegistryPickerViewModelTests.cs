using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VMS.Core.Services;
using VMS.VisionSetup.ViewModels;
using Xunit;

namespace VMS.VisionSetup.Tests
{
    /// <summary>
    /// 레지스트리 모델 선택 창 ViewModel — HTTP 없이 가짜 델리게이트로 목록·미리보기·확정 조건을 검증.
    /// (창의 code-behind 에 로직이 없도록 옮긴 뒤의 계약)
    /// </summary>
    public class ModelRegistryPickerViewModelTests
    {
        private static readonly Guid ModelA = Guid.Parse("11111111-1111-1111-1111-111111111111");
        private static readonly Guid ModelB = Guid.Parse("22222222-2222-2222-2222-222222222222");

        private static ModelRegistryPickerViewModel Create(string? taskType = "detection", bool failModels = false)
        {
            return new ModelRegistryPickerViewModel(
                (type, ct) =>
                {
                    if (failModels) throw new ModelRegistryException("서버에 닿지 못했습니다");
                    IReadOnlyList<RegistryModel> list = new[]
                    {
                        new RegistryModel { Id = ModelA, Name = "라인A 스크래치", TaskType = type ?? "", Classes = new[] { "good", "defect" } },
                        new RegistryModel { Id = ModelB, Name = "라인B 로고", TaskType = type ?? "", Classes = new[] { "logo" } },
                    };
                    return Task.FromResult(list);
                },
                (id, ct) =>
                {
                    IReadOnlyList<RegistryModelVersion> list = id == ModelA
                        ? new[]
                        {
                            new RegistryModelVersion { Id = Guid.NewGuid(), Number = 1, Stage = "archived", SizeBytes = 40 << 20, CreatedAt = DateTime.UtcNow.AddDays(-9) },
                            new RegistryModelVersion { Id = Guid.NewGuid(), Number = 3, Stage = "production", SizeBytes = 41 << 20, CreatedAt = DateTime.UtcNow },
                            new RegistryModelVersion { Id = Guid.NewGuid(), Number = 2, Stage = "staging", SizeBytes = 41 << 20, CreatedAt = DateTime.UtcNow.AddDays(-1) },
                        }
                        : Array.Empty<RegistryModelVersion>();
                    return Task.FromResult(list);
                },
                taskType);
        }

        [Fact]
        public async Task Loading_fills_models_selects_first_and_previews_production_stage()
        {
            var vm = Create();
            await vm.LoadModelsCommand.ExecuteAsync(null);
            if (vm.PendingVersionsLoad != null) await vm.PendingVersionsLoad;

            Assert.Equal(2, vm.Models.Count);
            Assert.Same(vm.Models[0], vm.SelectedModel);
            Assert.Equal("모델 2개", vm.Status);
            Assert.True(vm.UseStage);
            Assert.Equal($"model://{ModelA:D}@production", vm.SelectedReference);
            Assert.Equal(vm.SelectedReference, vm.PreviewText);
            Assert.True(vm.ConfirmCommand.CanExecute(null));
            Assert.Equal(new[] { 3, 2, 1 }, new[] { vm.Versions[0].Version.Number, vm.Versions[1].Version.Number, vm.Versions[2].Version.Number });
        }

        [Fact]
        public async Task Stage_change_updates_reference()
        {
            var vm = Create();
            await vm.LoadModelsCommand.ExecuteAsync(null);
            vm.SelectedStage = "staging";
            Assert.Equal($"model://{ModelA:D}@staging", vm.SelectedReference);
        }

        [Fact]
        public async Task Pinning_requires_a_version_then_builds_version_reference()
        {
            var vm = Create();
            await vm.LoadModelsCommand.ExecuteAsync(null);
            if (vm.PendingVersionsLoad != null) await vm.PendingVersionsLoad;

            vm.PinVersion = true;
            Assert.False(vm.UseStage);
            Assert.Null(vm.SelectedReference);               // 버전을 아직 안 골랐다
            Assert.False(vm.ConfirmCommand.CanExecute(null));
            Assert.Equal("—", vm.PreviewText);

            vm.SelectedVersion = vm.Versions[1];             // v2
            Assert.Equal($"model://{ModelA:D}@2", vm.SelectedReference);
            Assert.True(vm.ConfirmCommand.CanExecute(null));

            vm.UseStage = true;                              // 라디오를 되돌리면 단계 참조로
            Assert.False(vm.PinVersion);
            Assert.Equal($"model://{ModelA:D}@production", vm.SelectedReference);
        }

        [Fact]
        public async Task Selecting_other_model_reloads_versions_and_clears_pinned_version()
        {
            var vm = Create();
            await vm.LoadModelsCommand.ExecuteAsync(null);
            if (vm.PendingVersionsLoad != null) await vm.PendingVersionsLoad;
            vm.PinVersion = true;
            vm.SelectedVersion = vm.Versions[0];

            vm.SelectedModel = vm.Models[1];                 // 버전이 없는 모델
            if (vm.PendingVersionsLoad != null) await vm.PendingVersionsLoad;

            Assert.Empty(vm.Versions);
            Assert.Null(vm.SelectedVersion);
            Assert.Null(vm.SelectedReference);
        }

        [Fact]
        public async Task Confirm_and_cancel_raise_close_request()
        {
            var vm = Create();
            await vm.LoadModelsCommand.ExecuteAsync(null);
            var results = new List<bool>();
            vm.CloseRequested += (_, ok) => results.Add(ok);

            vm.ConfirmCommand.Execute(null);
            vm.CancelCommand.Execute(null);

            Assert.Equal(new[] { true, false }, results);
        }

        [Fact]
        public async Task Registry_failure_is_shown_as_status_not_exception()
        {
            var vm = Create(failModels: true);
            await vm.LoadModelsCommand.ExecuteAsync(null);
            Assert.Empty(vm.Models);
            Assert.Contains("닿지 못했습니다", vm.Status);
            Assert.False(vm.ConfirmCommand.CanExecute(null));
            Assert.False(vm.IsBusy);
        }
    }
}
