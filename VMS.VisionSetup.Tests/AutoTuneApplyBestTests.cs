using System.Collections.Generic;
using System.ComponentModel;
using VMS.VisionSetup.Models;
using VMS.VisionSetup.Services;
using VMS.VisionSetup.Services.BatchTesting;
using VMS.VisionSetup.ViewModels;
using VMS.VisionSetup.VisionTools.BlobAnalysis;
using Xunit;

namespace VMS.VisionSetup.Tests
{
    /// <summary>
    /// 배치 테스트 Auto-tune — [Apply Best] 가 찾은 값을 레시피에 남기는지(VS-10),
    /// Best(★) 표시가 실행 뒤 갱신되는지(VS-11).
    /// </summary>
    public class AutoTuneApplyBestTests
    {
        [Fact]
        public void SyncToolParameters_WritesWorkspaceValueIntoRecipeToolConfig()
        {
            var tool = new BlobTool { Name = "Hole Blob", ThresholdValue = 128 };
            var config = ToolSerializer.SerializeTool(tool);
            config.X = 40; config.Y = 80;
            var recipe = new Recipe { Name = "R" };
            recipe.Steps.Add(new InspectionStep { Tools = new List<ToolConfig> { config } });

            // Apply Best 는 작업 공간 도구 인스턴스에 값을 넣는다
            tool.ThresholdValue = 150;

            int updated = AutoTuneViewModel.SyncToolParametersToRecipe(recipe, new[] { tool, tool });

            Assert.Equal(1, updated);
            var restored = (BlobTool)ToolSerializer.DeserializeTool(recipe.Steps[0].Tools[0])!;
            Assert.Equal(150, restored.ThresholdValue);
            // 위치는 그대로
            Assert.Equal(40, recipe.Steps[0].Tools[0].X);
            Assert.Equal(80, recipe.Steps[0].Tools[0].Y);
        }

        [Fact]
        public void CombinationResult_IsBest_RaisesPropertyChanged()
        {
            var result = new AutoTuneCombinationResult();
            var raised = new List<string?>();
            ((INotifyPropertyChanged)result).PropertyChanged += (_, e) => raised.Add(e.PropertyName);

            result.IsBest = true;

            Assert.Contains(nameof(AutoTuneCombinationResult.IsBest), raised);
        }
    }
}
