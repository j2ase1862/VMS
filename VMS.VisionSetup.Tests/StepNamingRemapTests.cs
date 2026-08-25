using System.Collections.Generic;
using System.Linq;
using VMS.Camera.Models;
using VMS.VisionSetup.Models;
using VMS.VisionSetup.Services;
using Xunit;

namespace VMS.VisionSetup.Tests
{
    /// <summary>
    /// StepNaming.RemapCameras — 다른 PC 레시피의 미등록 카메라 재연결 (2026-08-25 현장 실증).
    /// 카메라 ID 는 등록 시 발급되는 GUID 라 재등록으로는 해결되지 않는다는 전제의 정식 해결 경로.
    /// </summary>
    public class StepNamingRemapTests
    {
        private static Recipe MakeRecipe(params (string cameraId, int seq)[] steps)
        {
            var recipe = new Recipe { Name = "이식 레시피" };
            foreach (var (cameraId, seq) in steps)
                recipe.Steps.Add(new InspectionStep { CameraId = cameraId, Sequence = seq });
            return recipe;
        }

        [Fact]
        public void Remap_replaces_only_mapped_ids_and_returns_count()
        {
            var recipe = MakeRecipe(("old-a", 1), ("old-a", 2), ("old-b", 1), ("", 1));
            var mapping = new Dictionary<string, string> { ["old-a"] = "new-1" };

            int remapped = StepNaming.RemapCameras(recipe, mapping);

            Assert.Equal(2, remapped);
            Assert.Equal(2, recipe.Steps.Count(s => s.CameraId == "new-1"));
            Assert.Single(recipe.Steps, s => s.CameraId == "old-b");   // 미매핑은 유지
            Assert.Single(recipe.Steps, s => s.CameraId == "");        // 카메라 미지정도 유지
        }

        [Fact]
        public void Remap_then_recompute_removes_question_mark_names()
        {
            var recipe = MakeRecipe(("old-a", 1), ("old-a", 2));
            var cameras = new List<CameraInfo> { new() { Id = "new-1", Name = "Cam1" } };

            StepNaming.RecomputeNames(recipe, cameras);
            Assert.All(recipe.Steps, s => Assert.StartsWith("?-", s.Name));  // 이식 직후 상태

            StepNaming.RemapCameras(recipe, new Dictionary<string, string> { ["old-a"] = "new-1" });
            StepNaming.RecomputeNames(recipe, cameras);

            Assert.Equal(new[] { "1-1", "1-2" }, recipe.Steps.OrderBy(s => s.Sequence).Select(s => s.Name));
            Assert.Empty(StepNaming.GetUnregisteredCameraIds(recipe, cameras));
        }

        [Fact]
        public void Remap_merging_two_old_cameras_into_one_renumbers_sequences()
        {
            // 서로 다른 구 카메라 2대를 이 PC 카메라 1대로 합치는 경우 — 순번 충돌 없이 재부여
            var recipe = MakeRecipe(("old-a", 1), ("old-b", 1));
            var cameras = new List<CameraInfo> { new() { Id = "new-1", Name = "Cam1" } };

            StepNaming.RemapCameras(recipe, new Dictionary<string, string>
            {
                ["old-a"] = "new-1",
                ["old-b"] = "new-1",
            });
            StepNaming.RecomputeNames(recipe, cameras);

            Assert.Equal(new[] { "1-1", "1-2" }, recipe.Steps.OrderBy(s => s.Sequence).Select(s => s.Name));
            Assert.Equal(new[] { 1, 2 }, recipe.Steps.OrderBy(s => s.Sequence).Select(s => s.Sequence));
        }

        [Fact]
        public void Remap_with_identity_mapping_is_noop()
        {
            var recipe = MakeRecipe(("cam-1", 1));
            int remapped = StepNaming.RemapCameras(recipe, new Dictionary<string, string> { ["cam-1"] = "cam-1" });
            Assert.Equal(0, remapped);
        }
    }
}
