using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using VMS.PLC.Interfaces;
using VMS.PLC.Models;
using VMS.PLC.Models.Sequence;
using VMS.PLC.Services;
using VMS.Services.Sequence;
using Xunit;

namespace VMS.Tests.Services.Sequence
{
    /// <summary>
    /// 사이클 경계 정지 (2026-09-23 실증). WO 완료 응답은 사이클과 무관한 시점에 도착하는데, 종전에는
    /// 그 순간 운전을 즉시 취소해 설비에 들어와 있던 제품의 판정·출력·집계가 끊겼다(설비 NG, 화면·이력 없음).
    /// 이제 트리거 대기 중이면 바로 멈추고, 사이클 진행 중이면 그 사이클을 끝까지 수행한 뒤 멈춘다.
    /// </summary>
    public class SequenceEngineCycleBoundaryStopTests : IDisposable
    {
        private const string BoardId = "IoBoard_1";
        private readonly SimulatedPlcConnection _plc = new();

        public void Dispose()
        {
            try { _plc.Dispose(); } catch { /* 무시 */ }
        }

        [Fact]
        public async Task IdleAtBoardTrigger_StopsImmediately_WithoutInspecting()
        {
            var board = new FakeBoard();
            var inspections = 0;
            var engine = MakeEngine(board, () => { inspections++; return Task.FromResult(true); });
            var config = Cycle(trigger: BoardTrigger("wait"));

            var run = engine.RunAsync(config, CancellationToken.None);
            await WaitUntil(() => engine.CurrentNodeId == "wait");

            engine.RequestStopAtCycleBoundary();
            await run.WaitAsync(TimeSpan.FromSeconds(3));

            Assert.True(engine.StoppedAtCycleBoundary);
            Assert.Equal(0, inspections);
        }

        [Fact]
        public async Task IdleAtPlcTrigger_StopsWithoutThrowing()
        {
            // PLC 대기는 취소 시 예외를 던진다(보드 대기는 삼킨다) — 두 경로 모두 정상 종료여야 한다.
            var engine = MakeEngine(new FakeBoard(), () => Task.FromResult(true));
            var trigger = new SequenceNodeConfig
            {
                Id = "wait", NodeType = SequenceNodeType.InputCheck, Name = "Wait Trigger",
                PlcAddress = "M100", CheckMode = InputCheckMode.BitOn, TimeoutMs = -1,
            };
            var run = engine.RunAsync(Cycle(trigger), CancellationToken.None);
            await WaitUntil(() => engine.CurrentNodeId == "wait");

            engine.RequestStopAtCycleBoundary();
            await run.WaitAsync(TimeSpan.FromSeconds(3));   // OperationCanceledException 이면 여기서 실패

            Assert.True(engine.StoppedAtCycleBoundary);
        }

        [Fact]
        public async Task StopRequestedMidCycle_FinishesJudgmentOutputAndCycle_ThenStops()
        {
            var board = new FakeBoard();
            var inspecting = new TaskCompletionSource();
            var release = new TaskCompletionSource();
            var inspections = 0;
            var engine = MakeEngine(board, async () =>
            {
                inspections++;
                inspecting.TrySetResult();
                await release.Task;
                return true;
            });
            var cycles = new List<bool>();
            engine.CycleCompleted += (_, e) => cycles.Add(e.AllInspectionsOk);

            // 주소 없는 트리거 = 즉시 통과 → 곧바로 사이클 진행 중
            var trigger = new SequenceNodeConfig { Id = "wait", NodeType = SequenceNodeType.InputCheck, Name = "Wait Trigger" };
            var run = engine.RunAsync(Cycle(trigger), CancellationToken.None);
            await inspecting.Task.WaitAsync(TimeSpan.FromSeconds(3));

            engine.RequestStopAtCycleBoundary();   // WO 완료 응답이 검사 도중에 도착
            release.SetResult();
            await run.WaitAsync(TimeSpan.FromSeconds(3));

            Assert.True(board.Bits.GetValueOrDefault(0));    // OK 출력이 나갔다
            Assert.Equal(new[] { true }, cycles);            // 사이클 1건 집계(업로드 트리거)
            Assert.Equal(1, inspections);                    // 다음 트리거는 받지 않았다
            Assert.True(engine.StoppedAtCycleBoundary);
        }

        // ── helpers ──

        /// <summary>Start → trigger → Inspection → Branch → OK ON(ch0)/NG ON(ch1) → Repeat(무한, trigger 로).</summary>
        private static SequenceConfig Cycle(SequenceNodeConfig trigger)
        {
            trigger.NextNodeId = "insp";
            return new SequenceConfig
            {
                Nodes = new List<SequenceNodeConfig>
                {
                    new() { Id = "s", NodeType = SequenceNodeType.Start, Name = "Start", NextNodeId = trigger.Id },
                    trigger,
                    new() { Id = "insp", NodeType = SequenceNodeType.Inspection, Name = "Inspection", CameraId = "cam1", NextNodeId = "branch" },
                    new() { Id = "branch", NodeType = SequenceNodeType.Branch, Name = "Result Branch", TrueBranchNodeId = "ok", FalseBranchNodeId = "ng" },
                    BoardOutput("ok", "Result OK ON", 0),
                    BoardOutput("ng", "Result NG ON", 1),
                    new() { Id = "rep", NodeType = SequenceNodeType.Repeat, Name = "Repeat", RepeatCount = -1, RepeatTargetNodeId = trigger.Id, NextNodeId = "e" },
                    new() { Id = "e", NodeType = SequenceNodeType.End, Name = "End" },
                }
            };
        }

        private static SequenceNodeConfig BoardTrigger(string id) => new()
        {
            Id = id, NodeType = SequenceNodeType.InputCheck, Name = "Wait Trigger",
            DeviceId = BoardId, PlcAddress = "0", CheckMode = InputCheckMode.BitRisingEdge, TimeoutMs = -1,
        };

        private static SequenceNodeConfig BoardOutput(string id, string name, int channel) => new()
        {
            Id = id, NodeType = SequenceNodeType.OutputAction, Name = name,
            DeviceId = BoardId, PlcAddress = channel.ToString(), OutputDataType = PlcDataType.Bit, BitValue = true,
            NextNodeId = "rep",
        };

        private SequenceEngine MakeEngine(FakeBoard board, Func<Task<bool>> inspect)
        {
            var registry = new IoDeviceRegistry();
            registry.Register(board);
            return new SequenceEngine(
                _plc, PlcVendor.Mitsubishi,
                _ => Task.FromResult(true),
                _ => inspect(),
                (_, _) => { },
                _ => { },
                ioRegistry: registry);
        }

        private static async Task WaitUntil(Func<bool> condition)
        {
            var sw = Stopwatch.StartNew();
            while (!condition())
            {
                if (sw.Elapsed > TimeSpan.FromSeconds(3)) throw new TimeoutException("조건 대기 시간 초과");
                await Task.Delay(10);
            }
            await Task.Delay(100);   // 대기 노드 안으로 들어갈 시간
        }

        private sealed class FakeBoard : IIoBoardConnection
        {
            public Dictionary<int, bool> Bits { get; } = new();
            public string DeviceId => BoardId;
            public IoDeviceType DeviceType => IoDeviceType.AdLinkPci743x;
            public bool IsConnected => true;
            public int InputChannelCount => 32;
            public int OutputChannelCount => 32;
            public event EventHandler<IoBitChangedEventArgs>? BitChanged { add { } remove { } }
            public Task<bool> ConnectAsync() => Task.FromResult(true);
            public Task DisconnectAsync() => Task.CompletedTask;
            public Task<bool> ReadBitAsync(int channel) => Task.FromResult(false);   // 트리거는 오지 않는다
            public Task<uint> ReadPortAsync(int portNo) => Task.FromResult(0u);
            public Task WriteBitAsync(int channel, bool value) { Bits[channel] = value; return Task.CompletedTask; }
            public Task WritePortAsync(int portNo, uint value) => Task.CompletedTask;
            public Task StartMonitoringAsync(int channel, int pollingIntervalMs = 50) => Task.CompletedTask;
            public Task StopMonitoringAsync(int channel) => Task.CompletedTask;
            public Task StopAllMonitoringAsync() => Task.CompletedTask;
            public void Dispose() { }
        }
    }
}
