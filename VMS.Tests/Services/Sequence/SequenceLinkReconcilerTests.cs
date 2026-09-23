using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VMS.PLC.Models;
using VMS.PLC.Models.Sequence;
using VMS.PLC.Services;
using VMS.Services.Sequence;
using Xunit;

namespace VMS.Tests.Services.Sequence
{
    /// <summary>
    /// 화면의 연결선과 엔진 참조의 불일치 (2026-09-23 실증 PC).
    /// 편집기에는 Wait Trigger → Inspection 선이 보이는데 파일의 Wait Trigger.nextNodeId 는 비어 있어,
    /// AUTO RUN 이 트리거마다 검사 없이 처음으로 돌아갔다(오류·로그 없음).
    /// </summary>
    public class SequenceLinkReconcilerTests
    {
        [Fact]
        public async Task FieldFile_TriggerWithoutNextRef_RunsInspectionOnlyAfterReconcile()
        {
            var inspections = 0;

            var broken = FieldShapedSequence();
            await MakeEngine(() => inspections++).RunAsync(broken, CancellationToken.None);
            Assert.Equal(0, inspections);                 // 결함 재현: 트리거 뒤에서 시퀀스가 끝난다

            var config = FieldShapedSequence();
            var result = SequenceLinkReconciler.Reconcile(config);
            await MakeEngine(() => inspections++).RunAsync(config, CancellationToken.None);

            Assert.Equal(1, inspections);
            Assert.Equal("insp", config.Nodes.Single(n => n.Id == "wait").NextNodeId);
            Assert.Single(result.Repairs);
            Assert.Empty(result.Problems);
        }

        [Fact]
        public void TwoNextLinesFromOneNode_AreReported_NotGuessed()
        {
            var config = FieldShapedSequence();
            config.Edges.Add(Edge("wait", "branch", "Next"));   // 선이 두 개

            var result = SequenceLinkReconciler.Reconcile(config);

            Assert.Null(config.Nodes.Single(n => n.Id == "wait").NextNodeId);
            Assert.Contains(result.Problems, p => p.Contains("Wait Trigger") && p.Contains("2개"));
        }

        [Fact]
        public void BranchWithMissingFalseRef_IsFilledFromDrawnLine()
        {
            var config = FieldShapedSequence();
            SequenceLinkReconciler.Reconcile(config);
            var branch = config.Nodes.Single(n => n.Id == "branch");
            branch.FalseBranchNodeId = null;

            var result = SequenceLinkReconciler.Reconcile(config);

            Assert.Equal("ng", branch.FalseBranchNodeId);
            Assert.Single(result.Repairs);
        }

        [Fact]
        public void DeadEndWithoutAnyLine_IsReported()
        {
            var config = FieldShapedSequence();
            config.Edges.RemoveAll(e => e.SourceNodeId == "wait");

            var result = SequenceLinkReconciler.Reconcile(config);

            Assert.Empty(result.Repairs);
            Assert.Contains(result.Problems, p => p.Contains("Wait Trigger") && p.Contains("끝납니다"));
        }

        [Fact]
        public void BuiltInSequence_IsLeftUntouched()
        {
            // 기본 시퀀스는 Repeat → End 처럼 선 없이 참조만 둔 연결을 쓴다 — 지우거나 바꾸면 안 된다.
            var config = DefaultSequenceBuilder.BuildFromSignalMap(new PlcSignalMap { CameraId = "cam1" });
            Assert.Contains(config.Nodes, n => n.NodeType == SequenceNodeType.Branch);
            Assert.Contains(config.Nodes, n => n.NodeType == SequenceNodeType.Repeat && n.NextNodeId != null);
            var before = config.Nodes.Select(n => (n.Id, n.NextNodeId, n.TrueBranchNodeId, n.FalseBranchNodeId, n.RepeatTargetNodeId)).ToList();

            var result = SequenceLinkReconciler.Reconcile(config);

            Assert.Empty(result.Repairs);
            Assert.Empty(result.Problems);
            Assert.Equal(before, config.Nodes.Select(n => (n.Id, n.NextNodeId, n.TrueBranchNodeId, n.FalseBranchNodeId, n.RepeatTargetNodeId)).ToList());
        }

        // ── helpers ──

        /// <summary>실증 PC 파일과 같은 모양 — 무한 Repeat 대신 End 로 끝나 테스트가 한 사이클에 멈춘다.</summary>
        private static SequenceConfig FieldShapedSequence()
        {
            return new SequenceConfig
            {
                Name = "Process Sequence",
                Nodes = new List<SequenceNodeConfig>
                {
                    new() { Id = "s", NodeType = SequenceNodeType.Start, Name = "Start", NextNodeId = "wait" },
                    new() { Id = "wait", NodeType = SequenceNodeType.InputCheck, Name = "Wait Trigger", NextNodeId = null },
                    new() { Id = "insp", NodeType = SequenceNodeType.Inspection, Name = "Inspection", CameraId = "cam1", NextNodeId = "branch" },
                    new() { Id = "branch", NodeType = SequenceNodeType.Branch, Name = "Result Branch", TrueBranchNodeId = "ok", FalseBranchNodeId = "ng" },
                    new() { Id = "ok", NodeType = SequenceNodeType.OutputAction, Name = "Result OK ON", NextNodeId = "e" },
                    new() { Id = "ng", NodeType = SequenceNodeType.OutputAction, Name = "Result NG ON", NextNodeId = "e" },
                    new() { Id = "e", NodeType = SequenceNodeType.End, Name = "End" },
                },
                Edges = new List<SequenceEdgeConfig>
                {
                    Edge("s", "wait", "Next"),
                    Edge("wait", "insp", "Next"),     // 화면엔 보이지만 참조는 비어 있음
                    Edge("insp", "branch", "Next"),
                    Edge("branch", "ok", "True"),
                    Edge("branch", "ng", "False"),
                    Edge("ok", "e", "Next"),
                    Edge("ng", "e", "Next"),
                }
            };
        }

        private static SequenceEdgeConfig Edge(string from, string to, string label)
            => new() { SourceNodeId = from, TargetNodeId = to, Label = label };

        private static SequenceEngine MakeEngine(System.Action onInspect)
        {
            // 주소 없는 InputCheck/OutputAction 은 엔진이 바로 통과시킨다 — 연결 흐름만 본다.
            return new SequenceEngine(
                new SimulatedPlcConnection(),
                PlcVendor.Mitsubishi,
                _ => Task.FromResult(true),
                _ => { onInspect(); return Task.FromResult(true); },
                (_, _) => { },
                _ => { });
        }
    }
}
