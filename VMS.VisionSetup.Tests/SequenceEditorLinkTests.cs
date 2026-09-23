using System.Collections.Generic;
using System.Linq;
using VMS.PLC.Models.Sequence;
using VMS.VisionSetup.Models;
using Xunit;

namespace VMS.VisionSetup.Tests
{
    /// <summary>
    /// 편집기가 화면의 선과 실행 참조를 어긋나게 만들던 경로 (2026-09-23 실증 PC).
    /// 한 노드에서 Next 선을 두 번 그리면 선이 두 개 남고 참조는 마지막 선만 가리켰다. 그 선을 지우면
    /// 참조가 비어, 남은 선은 화면에만 있고 AUTO RUN 은 거기서 시퀀스를 끝냈다.
    /// </summary>
    public class SequenceEditorLinkTests
    {
        [Fact]
        public void SecondNextConnection_ReplacesFirst_SoDeletingItCannotLeaveGhostLine()
        {
            var vm = SequenceEditorBoardTestIoTests.CreateViewModel();
            vm.Nodes.Clear();
            vm.Edges.Clear();
            var wait = Add(vm, SequenceNodeType.InputCheck, "Wait Trigger");
            var insp = Add(vm, SequenceNodeType.Inspection, "Inspection");
            var other = Add(vm, SequenceNodeType.Delay, "Delay");

            Connect(vm, wait, insp);
            Connect(vm, wait, other);

            var fromWait = vm.Edges.Where(e => e.SourceNode == wait).ToList();
            var only = Assert.Single(fromWait);
            Assert.Same(other, only.TargetNode);
            Assert.Equal(other.Id, wait.Config.NextNodeId);

            // 과거 결함 경로: 두 번째 선을 지우면 첫 선만 화면에 남고 참조는 비었다
            vm.DeleteEdgeCommand.Execute(only);
            Assert.DoesNotContain(vm.Edges, e => e.SourceNode == wait);
            Assert.Null(wait.Config.NextNodeId);
        }

        [Fact]
        public void LoadingFileWithGhostLine_RepairsReferenceAndSaysSo()
        {
            var vm = SequenceEditorBoardTestIoTests.CreateViewModel();
            var config = new SequenceConfig
            {
                Nodes = new List<SequenceNodeConfig>
                {
                    new() { Id = "s", NodeType = SequenceNodeType.Start, Name = "Start", NextNodeId = "wait" },
                    new() { Id = "wait", NodeType = SequenceNodeType.InputCheck, Name = "Wait Trigger" },
                    new() { Id = "e", NodeType = SequenceNodeType.End, Name = "End" },
                },
                Edges = new List<SequenceEdgeConfig>
                {
                    new() { SourceNodeId = "s", TargetNodeId = "wait", Label = "Next" },
                    new() { SourceNodeId = "wait", TargetNodeId = "e", Label = "Next" },
                }
            };

            var note = vm.FromConfig(config);

            Assert.Equal("e", vm.Nodes.Single(n => n.Id == "wait").Config.NextNodeId);
            Assert.Contains("연결 1건", note);
        }

        private static SequenceNodeItem Add(VMS.VisionSetup.ViewModels.SequenceEditorViewModel vm,
            SequenceNodeType type, string name)
        {
            var item = new SequenceNodeItem(new SequenceNodeConfig { NodeType = type, Name = name });
            vm.Nodes.Add(item);
            return item;
        }

        private static void Connect(VMS.VisionSetup.ViewModels.SequenceEditorViewModel vm,
            SequenceNodeItem from, SequenceNodeItem to)
        {
            vm.StartConnectionCommand.Execute(from);
            vm.CompleteConnectionCommand.Execute(to);
        }
    }
}
