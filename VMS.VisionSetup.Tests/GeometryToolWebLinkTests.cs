using System.Collections.Generic;
using VMS.VisionSetup.Models;
using VMS.VisionSetup.Services;
using VMS.VisionSetup.ViewModels.ToolSettings;
using VMS.VisionSetup.VisionTools.Measurement;
using Xunit;

namespace VMS.VisionSetup.Tests
{
    /// <summary>
    /// GeometryTool 판정 파라미터의 Web 파라미터 연동 —
    /// Web Dimension 프리셋(Reference Value / Upper·Lower Tolerance)과 1:1 대응.
    /// </summary>
    public class GeometryToolWebLinkTests
    {
        [Fact]
        public void SettingsViewModel_LinkSetters_UpdateToolLinkedParamCodes()
        {
            var tool = new GeometryTool();
            var vm = new GeometryToolSettingsViewModel(tool);

            vm.SelectedExpectedValueCode = new ParamCodeItem { ParamCode = 1 };
            vm.SelectedTolerancePlusCode = new ParamCodeItem { ParamCode = 3 };
            vm.SelectedToleranceMinusCode = new ParamCodeItem { ParamCode = 2 };

            Assert.Equal(1, tool.LinkedParamCodes["ExpectedValue"]);
            Assert.Equal(3, tool.LinkedParamCodes["TolerancePlus"]);
            Assert.Equal(2, tool.LinkedParamCodes["ToleranceMinus"]);

            // 연동 해제 (None 선택)
            vm.SelectedExpectedValueCode = new ParamCodeItem { ParamCode = null };
            Assert.False(tool.LinkedParamCodes.ContainsKey("ExpectedValue"));
        }

        [Fact]
        public void Serializer_RoundTrips_JudgmentLinks()
        {
            var original = new GeometryTool();
            original.LinkedParamCodes = new Dictionary<string, int>
            {
                ["ExpectedValue"] = 1,
                ["ToleranceMinus"] = 2,
                ["TolerancePlus"] = 3
            };

            var config = ToolSerializer.SerializeTool(original);
            var restored = ToolSerializer.DeserializeTool(config);

            Assert.NotNull(restored);
            Assert.Equal(1, restored.LinkedParamCodes["ExpectedValue"]);
            Assert.Equal(2, restored.LinkedParamCodes["ToleranceMinus"]);
            Assert.Equal(3, restored.LinkedParamCodes["TolerancePlus"]);
        }
    }
}
