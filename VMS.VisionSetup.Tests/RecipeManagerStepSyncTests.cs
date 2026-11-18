using System.Linq;
using VMS.Camera.Models;
using VMS.VisionSetup.Models;
using VMS.VisionSetup.Services;
using VMS.VisionSetup.ViewModels;
using Xunit;

namespace VMS.VisionSetup.Tests
{
    /// <summary>
    /// VS-05: 레시피 관리자 편집기에서 더한 스텝이 창을 닫으면 메인 Steps 표에 보이고,
    /// 편집기 [Add Step] 은 메인에서 고른 카메라로 "1-n" 이름을 받는다.
    /// </summary>
    [Collection("VisionServiceSingleton")]
    public class RecipeManagerStepSyncTests
    {
        private static readonly CameraInfo CamA = new() { Id = "camA", Name = "Cam A" };
        private static readonly CameraInfo CamB = new() { Id = "camB", Name = "Cam B" };

        [Fact]
        public void EditorAddStep_UsesDefaultCamera_AndCameraOrdinalName()
        {
            var recipes = new MemoryRecipeService();
            var recipe = new Recipe { Name = "R" };
            var vm = new RecipeEditorViewModel(recipes, new ListCameraService(CamA, CamB),
                new DialogServiceRoutingTests.FakeDialogService()) { DefaultCameraId = "camB" };

            var first = vm.AddStepForDefaultCamera(recipe);
            var second = vm.AddStepForDefaultCamera(recipe);

            Assert.Equal("camB", first.CameraId);
            Assert.Equal("2-1", first.Name);
            Assert.Equal("2-2", second.Name);
        }

        [Fact]
        public void ClosingRecipeManager_RefreshesStepsOfCurrentRecipe()
        {
            var recipes = new MemoryRecipeService();
            var dialogs = new DialogServiceRoutingTests.FakeDialogService();
            var vm = new MainViewModel(VisionService.Instance, recipes, new ListCameraService(CamA, CamB),
                dialogs, () => { });
            var recipe = new Recipe { Name = "R" };
            recipes.CurrentRecipe = recipe;
            vm.SelectedCamera = vm.Cameras.First(c => c.Id == "camA");
            Assert.Empty(vm.Steps);

            string? passedCamera = null;
            dialogs.OnRecipeManager = cameraId =>
            {
                passedCamera = cameraId;
                // 편집기에서 [Add Step] · [Save] 만 하고 닫음 (목록에서 다시 Load 하지 않음)
                var editor = new RecipeEditorViewModel(recipes, new ListCameraService(CamA, CamB), dialogs)
                {
                    DefaultCameraId = cameraId
                };
                editor.AddStepForDefaultCamera(recipe);
                return null;
            };

            vm.OpenRecipeManager();

            Assert.Equal("camA", passedCamera);
            var step = Assert.Single(vm.Steps);
            Assert.Equal("1-1", step.Name);
        }
    }
}
