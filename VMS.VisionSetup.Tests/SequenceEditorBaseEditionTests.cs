using System;
using System.Linq;
using VMS.PLC.Models;
using VMS.PLC.Models.Sequence;
using VMS.VisionSetup.Models;
using VMS.VisionSetup.Services;
using VMS.VisionSetup.ViewModels;
using Xunit;

namespace VMS.VisionSetup.Tests
{
    /// <summary>
    /// PLC 연동 확장 옵션이 없는 기본 구성(GS 인증 구성) — 시퀀스 에디터가 IO 보드만 다룬다.
    /// MainPLC 안전망 항목이 생기지 않고, 새 입출력 노드는 첫 IO 보드를 가리키며, 모니터 이름에 PLC 가 없다.
    /// </summary>
    public class SequenceEditorBaseEditionTests
    {
        private const string BoardId = "ADLink_1";

        private static SequenceEditorViewModel CreateBaseEdition(params SequenceDeviceEntry[] devices) =>
            SequenceEditorBoardTestIoTests.CreateViewModel(devices, plcEnabled: false);

        private static NodePaletteItem Palette(SequenceEditorViewModel vm, SequenceNodeType type) =>
            vm.PaletteItems.First(p => p.NodeType == type);

        [Fact]
        public void Base_edition_adds_no_MainPLC_fallback()
        {
            var vm = CreateBaseEdition();

            Assert.Empty(vm.AvailableDevices);
            Assert.Equal("I/O Monitor", vm.MonitorTitle);
            Assert.DoesNotContain("PLC", vm.MonitorPanelTitle);
        }

        [Fact]
        public void Plc_edition_keeps_the_MainPLC_fallback()
        {
            var vm = SequenceEditorBoardTestIoTests.CreateViewModel(
                Array.Empty<SequenceDeviceEntry>(), plcEnabled: true);

            Assert.Equal("MainPLC", Assert.Single(vm.AvailableDevices).DeviceId);
            Assert.Equal("PLC Monitor", vm.MonitorTitle);
        }

        [Theory]
        [InlineData(SequenceNodeType.InputCheck)]
        [InlineData(SequenceNodeType.OutputAction)]
        [InlineData(SequenceNodeType.RecipeChange)]
        [InlineData(SequenceNodeType.StepChange)]
        public void Base_edition_new_io_node_points_at_the_first_board(SequenceNodeType type)
        {
            var vm = CreateBaseEdition(new SequenceDeviceEntry(BoardId, IoDeviceType.AdLinkPci743x));

            vm.AddNodeCommand.Execute(Palette(vm, type));

            var node = vm.Nodes.Last();
            Assert.Equal(BoardId, node.DeviceId);
            Assert.True(vm.IsSelectedDeviceBoard);
            Assert.DoesNotContain("PLC", vm.AddressInputHint);
        }

        [Fact]
        public void Plc_edition_new_io_node_keeps_the_default_plc_device()
        {
            var vm = SequenceEditorBoardTestIoTests.CreateViewModel();

            vm.AddNodeCommand.Execute(Palette(vm, SequenceNodeType.InputCheck));

            Assert.Equal("MainPLC", vm.Nodes.Last().DeviceId);
        }

        [Fact]
        public void Base_edition_help_has_no_plc_wording()
        {
            foreach (var key in new[] { "SequenceNode_Start", "SequenceNode_End", "SequenceNode_InputCheck",
                         "SequenceNode_OutputAction", "SequenceNode_RecipeChange", "SequenceNode_StepChange",
                         "SequenceNode_WorkOrderComplete", "SequenceNode_Repeat", "ResultTool", "CodeReaderTool" })
            {
                var help = HelpContent.GetToolHelp(key, plcEnabled: false)!;
                Assert.DoesNotContain("PLC", help.Description);
                Assert.DoesNotContain("PLC", help.Usage);
                Assert.All(help.Parameters ?? new(), p => Assert.DoesNotContain("PLC", p.Value));
            }
            Assert.DoesNotContain("PlcMappings",
                HelpContent.GetToolHelp("ResultTool", plcEnabled: false)!.Parameters!.Keys);

            Assert.Contains("PLC", HelpContent.GetToolHelp("SequenceNode_InputCheck", plcEnabled: true)!.Description);
            Assert.Contains("PlcMappings", HelpContent.GetToolHelp("ResultTool", plcEnabled: true)!.Parameters!.Keys);
        }
    }
}
