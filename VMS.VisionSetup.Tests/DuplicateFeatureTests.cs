using System.Collections.Generic;
using System.Linq;
using OpenCvSharp;
using VMS.VisionSetup.Models;
using VMS.VisionSetup.Services;
using VMS.VisionSetup.VisionTools.PatternMatching;
using Xunit;

namespace VMS.VisionSetup.Tests
{
    /// <summary>
    /// 복사 기능 테스트 — 스텝 복제(깊은 복사 + ID 재발급 + 연결 재매핑 + 삽입 위치)와
    /// 도구 복제의 핵심 경로(직렬화 왕복이 학습 데이터까지 별도 인스턴스로 복제).
    /// 레시피 복제의 ID 재발급도 같은 RegenerateStepIds 를 쓰므로 스텝 테스트가 겸한다.
    /// </summary>
    public class DuplicateFeatureTests
    {
        private static Recipe BuildRecipe()
        {
            var recipe = new Recipe { Id = "r1", Name = "Test Recipe" };

            var step1 = new InspectionStep
            {
                Id = "s1",
                Name = "1-1",
                Sequence = 1,
                CameraId = "camA",
                Exposure = 1234,
                Gain = 2.5,
                Tools = new List<ToolConfig>
                {
                    new() { Id = "t1", ToolType = "GrayscaleTool", Name = "Gray #1", Sequence = 1 },
                    new()
                    {
                        Id = "t2", ToolType = "BlobTool", Name = "Blob #1", Sequence = 2,
                        Parameters = new Dictionary<string, object> { ["MinArea"] = 100.0 },
                        Connections = new List<ToolConnectionConfig>
                        {
                            new() { SourceToolId = "t1", ConnectionType = "Image" }
                        }
                    }
                }
            };

            var step2 = new InspectionStep
            {
                Id = "s2", Name = "1-2", Sequence = 2, CameraId = "camA",
                Tools = new List<ToolConfig>()
            };

            recipe.Steps.Add(step1);
            recipe.Steps.Add(step2);
            recipe.UsedCameraIds.Add("camA");
            return recipe;
        }

        [Fact]
        public void DuplicateStep_DeepCopies_RegeneratesIds_RemapsConnections()
        {
            var recipe = BuildRecipe();

            var copy = RecipeService.Instance.DuplicateStep(recipe, "s1");

            Assert.NotNull(copy);
            Assert.Equal(3, recipe.Steps.Count);
            Assert.NotEqual("s1", copy!.Id);

            // 설정 값 복사
            Assert.Equal(1234, copy.Exposure);
            Assert.Equal(2.5, copy.Gain);
            Assert.Equal("camA", copy.CameraId);
            Assert.Equal(2, copy.Tools.Count);

            // 툴 ID 재발급 + 연결 재매핑
            var newGray = copy.Tools.First(t => t.ToolType == "GrayscaleTool");
            var newBlob = copy.Tools.First(t => t.ToolType == "BlobTool");
            Assert.NotEqual("t1", newGray.Id);
            Assert.NotEqual("t2", newBlob.Id);
            Assert.Equal(newGray.Id, newBlob.Connections.Single().SourceToolId);

            // 깊은 복사 — 복사본 파라미터 수정이 원본에 영향 없음
            newBlob.Parameters["MinArea"] = 999.0;
            var origBlob = recipe.Steps.First(s => s.Id == "s1").Tools.First(t => t.Id == "t2");
            Assert.Equal(100.0, origBlob.Parameters["MinArea"]);
        }

        [Fact]
        public void DuplicateStep_InsertsRightAfterOriginal()
        {
            var recipe = BuildRecipe();

            var copy = RecipeService.Instance.DuplicateStep(recipe, "s1");

            Assert.NotNull(copy);
            var ordered = recipe.Steps
                .Where(s => s.CameraId == "camA")
                .OrderBy(s => s.Sequence)
                .Select(s => s.Id)
                .ToList();
            Assert.Equal(new[] { "s1", copy!.Id, "s2" }, ordered);
            Assert.Equal(new[] { 1, 2, 3 },
                recipe.Steps.OrderBy(s => s.Sequence).Select(s => s.Sequence).ToArray());
        }

        [Fact]
        public void DuplicateStep_UnknownStep_ReturnsNull()
        {
            var recipe = BuildRecipe();
            Assert.Null(RecipeService.Instance.DuplicateStep(recipe, "no-such-step"));
            Assert.Equal(2, recipe.Steps.Count);
        }

        [Fact]
        public void ToolDuplication_SerializerRoundTrip_CopiesTrainedModelAsSeparateInstance()
        {
            // MainViewModel.DuplicateTool 의 핵심 경로: Serialize → Deserialize → 새 ID
            using var template = new Mat(100, 100, MatType.CV_8UC1, Scalar.Black);
            Cv2.Rectangle(template, new Rect(25, 25, 50, 50), Scalar.White, -1);

            var tool = new FeatureMatchTool { UseROI = true, ROI = new Rect(10, 20, 100, 100) };
            Assert.True(tool.TrainPattern(template));

            var config = ToolSerializer.SerializeTool(tool);
            var copy = ToolSerializer.DeserializeTool(config) as FeatureMatchTool;
            Assert.NotNull(copy);
            copy!.Id = System.Guid.NewGuid().ToString();

            Assert.NotEqual(tool.Id, copy.Id);
            Assert.True(copy.UseROI);
            Assert.Equal(new Rect(10, 20, 100, 100), copy.ROI);

            // 학습 데이터가 별도 인스턴스로 복제됨 — 원본 해제해도 복사본 유효
            var copiedModel = copy.Models.Last();
            Assert.True(copiedModel.IsTrained);
            Assert.NotSame(tool.Models.Last().TemplateImage, copiedModel.TemplateImage);
        }
    }
}
