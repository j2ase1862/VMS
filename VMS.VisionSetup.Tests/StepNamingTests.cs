using System.Collections.Generic;
using System.Linq;
using VMS.Camera.Models;
using VMS.VisionSetup.Models;
using VMS.VisionSetup.Services;
using Xunit;

namespace VMS.VisionSetup.Tests
{
    /// <summary>
    /// 스텝 표시 이름 파생 규칙 테스트.
    /// 이름은 저장된 문자열이 아니라 현재 카메라 레지스트리 기준으로 재계산되며,
    /// 미등록 카메라 스텝은 "?-n"으로 표시되어 등록 카메라의 새 스텝과 겹치지 않는다.
    /// </summary>
    public class StepNamingTests
    {
        private static CameraInfo Camera(string id, string name) => new() { Id = id, Name = name };

        private static InspectionStep Step(string cameraId, int sequence, string name = "") => new()
        {
            CameraId = cameraId,
            Sequence = sequence,
            Name = name
        };

        [Fact]
        public void RecomputeNames_RegisteredCamera_UsesDisplayIndexAndOrdinal()
        {
            var cameras = new List<CameraInfo> { Camera("camA", "Cam A"), Camera("camB", "Cam B") };
            var recipe = new Recipe { Name = "R" };
            recipe.Steps.Add(Step("camB", 1));
            recipe.Steps.Add(Step("camB", 2));
            recipe.Steps.Add(Step("camA", 1));

            StepNaming.RecomputeNames(recipe, cameras);

            Assert.Equal("2-1", recipe.Steps[0].Name);
            Assert.Equal("2-2", recipe.Steps[1].Name);
            Assert.Equal("1-1", recipe.Steps[2].Name);
        }

        [Fact]
        public void RecomputeNames_UnregisteredCamera_DoesNotCollideWithRegisteredCameraSteps()
        {
            // 현장 시나리오: 사무실 PC 카메라(미등록)의 "1-1" 스텝이 저장된 레시피에
            // 현장 카메라(레지스트리 1번) 스텝을 추가해도 이름이 겹치지 않아야 한다.
            var cameras = new List<CameraInfo> { Camera("fieldCam", "Field") };
            var recipe = new Recipe { Name = "A1" };
            recipe.Steps.Add(Step("officeCam", 1, name: "1-1"));
            recipe.Steps.Add(Step("fieldCam", 1));

            StepNaming.RecomputeNames(recipe, cameras);

            Assert.Equal("?-1", recipe.Steps[0].Name);
            Assert.Equal("1-1", recipe.Steps[1].Name);
            Assert.Equal(2, recipe.Steps.Select(s => s.Name).Distinct().Count());
        }

        [Fact]
        public void RecomputeNames_EmptyCameraId_UsesLegacyStepNames()
        {
            var recipe = new Recipe { Name = "R" };
            recipe.Steps.Add(Step(string.Empty, 1));
            recipe.Steps.Add(Step(string.Empty, 2));

            StepNaming.RecomputeNames(recipe, new List<CameraInfo>());

            Assert.Equal("Step 1", recipe.Steps[0].Name);
            Assert.Equal("Step 2", recipe.Steps[1].Name);
        }

        [Fact]
        public void RecomputeNames_NormalizesSequenceGapsPerCamera()
        {
            // 삭제로 생긴 시퀀스 구멍(1,3,7)은 1..n 으로 정규화된다
            var cameras = new List<CameraInfo> { Camera("camA", "Cam A") };
            var recipe = new Recipe { Name = "R" };
            recipe.Steps.Add(Step("camA", 3));
            recipe.Steps.Add(Step("camA", 1));
            recipe.Steps.Add(Step("camA", 7));

            StepNaming.RecomputeNames(recipe, cameras);

            var ordered = recipe.Steps.OrderBy(s => s.Sequence).ToList();
            Assert.Equal(new[] { 1, 2, 3 }, ordered.Select(s => s.Sequence));
            Assert.Equal(new[] { "1-1", "1-2", "1-3" }, ordered.Select(s => s.Name));
        }

        [Fact]
        public void GetUnregisteredCameraIds_ReturnsDistinctUnknownIdsOnly()
        {
            var cameras = new List<CameraInfo> { Camera("known", "Known") };
            var recipe = new Recipe { Name = "R" };
            recipe.Steps.Add(Step("known", 1));
            recipe.Steps.Add(Step("ghost1", 1));
            recipe.Steps.Add(Step("ghost1", 2));
            recipe.Steps.Add(Step("ghost2", 1));
            recipe.Steps.Add(Step(string.Empty, 1));

            var unknown = StepNaming.GetUnregisteredCameraIds(recipe, cameras);

            Assert.Equal(2, unknown.Count);
            Assert.Contains("ghost1", unknown);
            Assert.Contains("ghost2", unknown);
        }
    }
}
