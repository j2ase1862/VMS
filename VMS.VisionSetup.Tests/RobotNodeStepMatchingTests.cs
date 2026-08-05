using System.Text.Json;
using System.Text.Json.Serialization;
using VMS.VisionSetup.Models;
using VMS.VisionSetup.Services;
using Xunit;

namespace VMS.VisionSetup.Tests
{
    /// <summary>
    /// 로봇 노드 번호 → 스텝 매칭 및 별칭/노드 번호 직렬화 테스트.
    /// 매칭 규칙: RobotNodeIndex 명시 스텝 우선, 없으면 카메라별 순번(Sequence) 폴백.
    /// </summary>
    public class RobotNodeStepMatchingTests
    {
        // RecipeService.JsonOptions 와 동일 구성
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        private static InspectionStep Step(string cameraId, int sequence, int? nodeIndex = null) => new()
        {
            CameraId = cameraId,
            Sequence = sequence,
            RobotNodeIndex = nodeIndex
        };

        [Fact]
        public void FindStepByRobotNode_ExplicitNodeIndex_TakesPriorityOverSequence()
        {
            var recipe = new Recipe { Name = "R" };
            recipe.Steps.Add(Step("cam", 1));
            recipe.Steps.Add(Step("cam", 2));
            recipe.Steps.Add(Step("cam", 3, nodeIndex: 2)); // 노드 2 를 명시적으로 3번째 스텝에 배정

            var found = RecipeService.Instance.FindStepByRobotNode(recipe, 2);

            Assert.NotNull(found);
            Assert.Equal(3, found!.Sequence); // 순번 2 스텝이 아니라 명시 스텝
        }

        [Fact]
        public void FindStepByRobotNode_NoExplicitIndex_FallsBackToSequence()
        {
            var recipe = new Recipe { Name = "R" };
            recipe.Steps.Add(Step("cam", 1));
            recipe.Steps.Add(Step("cam", 2));
            recipe.Steps.Add(Step("cam", 3));

            var found = RecipeService.Instance.FindStepByRobotNode(recipe, 3);

            Assert.NotNull(found);
            Assert.Equal(3, found!.Sequence);
        }

        [Fact]
        public void FindStepByRobotNode_CameraFilter_LimitsCandidates()
        {
            var recipe = new Recipe { Name = "R" };
            recipe.Steps.Add(Step("camA", 1, nodeIndex: 5));
            recipe.Steps.Add(Step("camB", 1, nodeIndex: 5));

            var found = RecipeService.Instance.FindStepByRobotNode(recipe, 5, cameraId: "camB");

            Assert.NotNull(found);
            Assert.Equal("camB", found!.CameraId);
        }

        [Fact]
        public void FindStepByRobotNode_NoMatch_ReturnsNull()
        {
            var recipe = new Recipe { Name = "R" };
            recipe.Steps.Add(Step("cam", 1));

            Assert.Null(RecipeService.Instance.FindStepByRobotNode(recipe, 99));
        }

        [Fact]
        public void RoundTrip_PreservesAliasAndRobotNodeIndex()
        {
            var step = new InspectionStep
            {
                Name = "1-3",
                Description = "Node 3",
                RobotNodeIndex = 3
            };

            var json = JsonSerializer.Serialize(step, JsonOptions);
            var restored = JsonSerializer.Deserialize<InspectionStep>(json, JsonOptions)!;

            Assert.Equal("Node 3", restored.Description);
            Assert.Equal(3, restored.RobotNodeIndex);
        }

        [Fact]
        public void Deserialize_LegacyJsonWithoutRobotNodeIndex_DefaultsToNull()
        {
            var json = """{ "name": "1-1", "sequence": 1, "cameraId": "cam" }""";

            var restored = JsonSerializer.Deserialize<InspectionStep>(json, JsonOptions)!;

            Assert.Null(restored.RobotNodeIndex);
            Assert.Equal(string.Empty, restored.Description);
        }

        [Fact]
        public void DisplayName_CombinesDerivedNameAndAlias()
        {
            var step = new InspectionStep { Name = "1-3" };
            Assert.Equal("1-3", step.DisplayName);

            step.Description = "Node 3";
            Assert.Equal("1-3 — Node 3", step.DisplayName);
        }
    }
}
