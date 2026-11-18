using System.Collections.Generic;
using System.Text.Json;
using VMS.VisionSetup.Models;
using VMS.VisionSetup.Services;
using Xunit;

namespace VMS.VisionSetup.Tests
{
    /// <summary>
    /// VS-08: Steps 표의 Tools 열은 작업 공간에 올라와 있는 동안 그 도구 수를 따라가고,
    /// 화면 전용 값은 레시피 파일에 쓰이지 않는다.
    /// </summary>
    public class InspectionStepToolCountTests
    {
        [Fact]
        public void ToolCount_FollowsWorkspace_ThenFallsBackToSaved()
        {
            var step = new InspectionStep { Tools = new List<ToolConfig> { new(), new() } };
            var raised = new List<string?>();
            step.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

            Assert.Equal(2, step.ToolCount);

            step.WorkspaceToolCount = 4;
            Assert.Equal(4, step.ToolCount);
            Assert.Contains(nameof(InspectionStep.ToolCount), raised);

            step.WorkspaceToolCount = null;
            Assert.Equal(2, step.ToolCount);
        }

        [Fact]
        public void WorkspaceCount_IsNotSerialized()
        {
            var step = new InspectionStep { WorkspaceToolCount = 3 };
            var json = JsonSerializer.Serialize(step, RecipeJson.Options);

            Assert.DoesNotContain("workspaceToolCount", json);
            Assert.DoesNotContain("toolCount", json);
        }
    }
}
