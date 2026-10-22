using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VMS.PLC.Interfaces;
using VMS.PLC.Models;
using VMS.PLC.Models.Sequence;
using VMS.PLC.Services;
using VMS.Services;
using VMS.Services.Sequence;
using Xunit;

namespace VMS.Tests.Services.Sequence
{
    /// <summary>
    /// "WO 완료 시" 분기 — WO 완료는 Web 이 판정해 운전과 무관한 시점에 알려 오고, 그 소식이 오면 운전은 사이클
    /// 경계에서 멈춘다. 종전에는 설비가 이를 몰라 계획 초과 제품을 계속 넣었고 그 제품은 검사 없이 지나갔다.
    /// 이제 시퀀스의 별도 진입점에 매단 출력·지연을 정지 직후 한 번 실행해 설비에 알린다.
    /// </summary>
    public class WorkOrderCompleteBranchTests : IDisposable
    {
        private const string BoardId = "IoBoard_1";
        private readonly SimulatedPlcConnection _plc = new();

        public void Dispose()
        {
            try { _plc.Dispose(); } catch { /* 무시 */ }
        }

        [Fact]
        public async Task Branch_writes_pulse_in_order()
        {
            var board = new FakeBoard();
            var engine = MakeEngine(board);
            var config = WithBranch(Output("on", 5, true, next: "d"), Delay("d", 10, next: "off"), Output("off", 5, false));

            Assert.True(await engine.RunWorkOrderCompleteAsync(config, CancellationToken.None));
            Assert.Equal(new[] { (5, true), (5, false) }, board.Writes);
        }

        [Fact]
        public async Task No_branch_returns_false_and_writes_nothing()
        {
            var board = new FakeBoard();
            var engine = MakeEngine(board);

            Assert.False(await engine.RunWorkOrderCompleteAsync(MainFlowOnly(), CancellationToken.None));
            Assert.Empty(board.Writes);
        }

        [Fact]
        public async Task Disallowed_node_throws_before_any_output()
        {
            // 출력이 먼저 나가고 중간에 멈추면 설비는 반쪽 신호를 받는다 — 전체를 먼저 확인한다.
            var board = new FakeBoard();
            var engine = MakeEngine(board);
            var wait = new SequenceNodeConfig { Id = "ack", NodeType = SequenceNodeType.InputCheck, Name = "Wait Ack" };
            var config = WithBranch(Output("on", 5, true, next: "ack"), wait);

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => engine.RunWorkOrderCompleteAsync(config, CancellationToken.None));
            Assert.Contains("Wait Ack", ex.Message);
            Assert.Empty(board.Writes);
        }

        [Fact]
        public async Task Main_flow_entering_branch_ends_there_without_output()
        {
            var board = new FakeBoard();
            var engine = MakeEngine(board);
            var config = WithBranch(Output("on", 5, true));
            config.Nodes.First(n => n.Id == "s").NextNodeId = "wo";   // 잘못 그린 선

            await engine.RunAsync(config, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(3));
            Assert.Empty(board.Writes);
            Assert.Contains(SequenceLinkReconciler.Reconcile(config).Problems, p => p.Contains("WO 완료 때만"));
        }

        [Fact]
        public void Reconciler_reports_branch_problems_but_not_its_end_as_dead_end()
        {
            var ok = WithBranch(Output("on", 5, true));   // 마지막 출력 뒤 연결 없음 = 분기 끝 (정상)
            Assert.Empty(SequenceLinkReconciler.Reconcile(ok).Problems);

            var two = WithBranch(Output("on", 5, true));
            two.Nodes.Add(new SequenceNodeConfig { Id = "wo2", NodeType = SequenceNodeType.WorkOrderComplete, Name = "WO Complete 2" });
            Assert.Contains(SequenceLinkReconciler.Reconcile(two).Problems, p => p.Contains("2개"));

            var loop = WithBranch(Output("on", 5, true, next: "off"), Output("off", 5, false, next: "on"));
            Assert.Contains(SequenceLinkReconciler.Reconcile(loop).Problems, p => p.Contains("되돌아가는"));
        }

        [Fact]
        public async Task AutoProcess_runs_branch_only_on_work_order_stop()
        {
            var board = new FakeBoard();
            var svc = MakeAutoProcess(board, WithBranch(Output("on", 5, true)));

            await svc.StartAsync();
            await WaitUntil(() => board.Reads > 0);   // 트리거 대기 중 (사이클 밖)
            await svc.StopAfterCycleAsync(TimeSpan.FromSeconds(5), workOrderCompleted: true);

            Assert.False(svc.IsRunning);
            Assert.Equal(new[] { (5, true) }, board.Writes);
        }

        [Fact]
        public async Task AutoProcess_manual_stop_does_not_run_branch()
        {
            var board = new FakeBoard();
            var svc = MakeAutoProcess(board, WithBranch(Output("on", 5, true)));

            await svc.StartAsync();
            await WaitUntil(() => board.Reads > 0);
            await svc.StopAsync();

            Assert.Empty(board.Writes);
        }

        // ── helpers ──

        /// <summary>Start → 보드 트리거 대기(오지 않음) → Repeat. 검사·출력 없음.</summary>
        private static SequenceConfig MainFlowOnly() => new()
        {
            Nodes = new List<SequenceNodeConfig>
            {
                new() { Id = "s", NodeType = SequenceNodeType.Start, Name = "Start", NextNodeId = "wait" },
                new()
                {
                    Id = "wait", NodeType = SequenceNodeType.InputCheck, Name = "Wait Trigger", NextNodeId = "rep",
                    DeviceId = BoardId, PlcAddress = "0", CheckMode = InputCheckMode.BitOn, TimeoutMs = -1,
                },
                new() { Id = "rep", NodeType = SequenceNodeType.Repeat, Name = "Repeat", RepeatCount = -1, RepeatTargetNodeId = "wait" },
            }
        };

        private static SequenceConfig WithBranch(params SequenceNodeConfig[] chain)
        {
            var config = MainFlowOnly();
            config.Nodes.Add(new SequenceNodeConfig
            {
                Id = "wo", NodeType = SequenceNodeType.WorkOrderComplete, Name = "WO Complete", NextNodeId = chain[0].Id,
            });
            config.Nodes.AddRange(chain);
            return config;
        }

        private static SequenceNodeConfig Output(string id, int channel, bool value, string? next = null) => new()
        {
            Id = id, NodeType = SequenceNodeType.OutputAction, Name = $"WO Done {(value ? "ON" : "OFF")}",
            DeviceId = BoardId, PlcAddress = channel.ToString(), OutputDataType = PlcDataType.Bit, BitValue = value,
            NextNodeId = next,
        };

        private static SequenceNodeConfig Delay(string id, int ms, string? next = null) => new()
        {
            Id = id, NodeType = SequenceNodeType.Delay, Name = "Pulse Width", DelayMs = ms, NextNodeId = next,
        };

        private SequenceEngine MakeEngine(FakeBoard board)
        {
            var registry = new IoDeviceRegistry();
            registry.Register(board);
            return new SequenceEngine(
                _plc, PlcVendor.Mitsubishi,
                _ => Task.FromResult(true), _ => Task.FromResult(true), (_, _) => { }, _ => { },
                ioRegistry: registry);
        }

        private AutoProcessService MakeAutoProcess(FakeBoard board, SequenceConfig sequence)
        {
            var registry = new IoDeviceRegistry();
            registry.Register(board);
            return new AutoProcessService(
                _plc, new PlcSignalConfiguration(), PlcVendor.Mitsubishi,
                _ => Task.FromResult(true), _ => Task.FromResult(true), (_, _) => { }, _ => { },
                processSequence: sequence,
                ioRegistry: registry,
                activitySignalFactory: () => null);
        }

        private static async Task WaitUntil(Func<bool> condition)
        {
            var sw = Stopwatch.StartNew();
            while (!condition())
            {
                if (sw.Elapsed > TimeSpan.FromSeconds(10)) throw new TimeoutException("조건 대기 시간 초과");   // 콜드 스타트·CI 러너 여유
                await Task.Delay(10);
            }
        }

        private sealed class FakeBoard : IIoBoardConnection
        {
            private int _reads;
            public List<(int Channel, bool Value)> Writes { get; } = new();
            public int Reads => Volatile.Read(ref _reads);
            public string DeviceId => BoardId;
            public IoDeviceType DeviceType => IoDeviceType.AdLinkPci743x;
            public bool IsConnected => true;
            public int InputChannelCount => 32;
            public int OutputChannelCount => 32;
            public event EventHandler<IoBitChangedEventArgs>? BitChanged { add { } remove { } }
            public Task<bool> ConnectAsync() => Task.FromResult(true);
            public Task DisconnectAsync() => Task.CompletedTask;
            public Task<bool> ReadBitAsync(int channel) { Interlocked.Increment(ref _reads); return Task.FromResult(false); }
            public Task<uint> ReadPortAsync(int portNo) => Task.FromResult(0u);
            public Task WriteBitAsync(int channel, bool value) { lock (Writes) Writes.Add((channel, value)); return Task.CompletedTask; }
            public Task WritePortAsync(int portNo, uint value) => Task.CompletedTask;
            public Task StartMonitoringAsync(int channel, int pollingIntervalMs = 50) => Task.CompletedTask;
            public Task StopMonitoringAsync(int channel) => Task.CompletedTask;
            public Task StopAllMonitoringAsync() => Task.CompletedTask;
            public void Dispose() { }
        }
    }
}
