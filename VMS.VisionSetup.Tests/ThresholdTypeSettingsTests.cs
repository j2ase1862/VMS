using VMS.VisionSetup.Services;
using VMS.VisionSetup.ViewModels.ToolSettings;
using VMS.VisionSetup.VisionTools.ImageProcessing;
using Xunit;

namespace VMS.VisionSetup.Tests
{
    /// <summary>
    /// VS-20: Threshold 의 반전(ThresholdType)을 설정 패널에서 바꿀 수 있고,
    /// 바꾼 값이 레시피 저장 · 재로드까지 남는다 (설정 VM → 도구 → Serializer 왕복).
    /// </summary>
    public class ThresholdTypeSettingsTests
    {
        [Fact]
        public void SettingsVm_ThresholdType_RoundTripsThroughSerializer()
        {
            var tool = new ThresholdTool();
            var vm = new ThresholdToolSettingsViewModel(tool);

            vm.ThresholdType = ThresholdType.BinaryInv;

            var restored = (ThresholdTool)ToolSerializer.DeserializeTool(ToolSerializer.SerializeTool(tool))!;
            Assert.Equal(ThresholdType.BinaryInv, restored.ThresholdType);
            Assert.Equal(ThresholdType.BinaryInv, new ThresholdToolSettingsViewModel(restored).ThresholdType);
        }
    }
}
