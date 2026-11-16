using VMS.VisionSetup.Models;
using VMS.VisionSetup.Services;
using VMS.VisionSetup.ViewModels.ToolSettings;
using VMS.VisionSetup.VisionTools.Measurement;
using Xunit;

namespace VMS.VisionSetup.Tests
{
    /// <summary>
    /// VS-21 (= WB-26): Caliper EdgePair 의 기대 폭 · 허용 오차를 설정 패널에서 넣고,
    /// 레시피 저장 · 재로드까지 남으며 Web 파라미터에 연결할 수 있다.
    /// </summary>
    public class CaliperEdgePairWidthSettingsTests
    {
        [Fact]
        public void SettingsVm_Width_RoundTripsThroughSerializer()
        {
            var tool = new CaliperTool();
            var vm = new CaliperToolSettingsViewModel(tool)
            {
                ExpectedWidth = 140.2,
                WidthTolerance = 5.3
            };

            var restored = (CaliperTool)ToolSerializer.DeserializeTool(ToolSerializer.SerializeTool(tool))!;
            var vm2 = new CaliperToolSettingsViewModel(restored);

            Assert.Equal(140.2, vm2.ExpectedWidth, 6);
            Assert.Equal(5.3, vm2.WidthTolerance, 6);
        }

        [Fact]
        public void SettingsVm_WidthLinks_ToWebParameters()
        {
            var tool = new CaliperTool();
            var vm = new CaliperToolSettingsViewModel(tool);

            vm.SelectedExpectedWidthCode = new ParamCodeItem { ParamCode = 11, Value = 141 };
            vm.SelectedWidthToleranceCode = new ParamCodeItem { ParamCode = 12, Value = 5 };

            Assert.Equal(11, tool.LinkedParamCodes[nameof(CaliperTool.ExpectedWidth)]);
            Assert.Equal(12, tool.LinkedParamCodes[nameof(CaliperTool.WidthTolerance)]);
            Assert.Equal(141, tool.ExpectedWidth);
            Assert.Equal(5, tool.WidthTolerance);

            var restored = ToolSerializer.DeserializeTool(ToolSerializer.SerializeTool(tool))!;
            Assert.Equal(11, restored.LinkedParamCodes[nameof(CaliperTool.ExpectedWidth)]);
        }
    }
}
