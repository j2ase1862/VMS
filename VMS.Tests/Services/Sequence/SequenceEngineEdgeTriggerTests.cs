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
    /// IO 보드 트리거의 에지 판정 검증 — 현장 사고(2026-08-19): InputCheck(BitOn) 이
    /// 레벨 감지라 스위치를 누르고 있는 동안 사이클 시간마다 트리거가 반복 성립,
    /// 검사·WO 수량이 눌린 시간에 비례해 부풀었다.
    /// BitRisingEdge 는 "비활성 레벨 관측 후의 전환"만 통과시켜 1회 누름 = 1회 트리거를 고정한다.
    /// </summary>
    public class SequenceEngineEdgeTriggerTests : IDisposable
    {
        private const string BoardId = "ADLink_1";

        private readonly SimulatedPlcConnection _plc = new();

        public void Dispose()
        {
            try { _plc.Dispose(); } catch { /* 무시 */ }
        }

        private SequenceEngine MakeEngine(IIoBoardConnection board)
        {
            var registry = new IoDeviceRegistry();
            registry.Register(board);
            return new SequenceEngine(
                _plc,
                PlcVendor.Mitsubishi,
                _ => Task.FromResult(true),
                _ => Task.FromResult(true),
                (_, _) => { },
                _ => { },
                getToolResultsFunc: null,
                recipeChangeByIndexFunc: null,
                stepChangeFunc: null,
                ioRegistry: registry);
        }

        private static SequenceConfig TriggerConfig(SequenceNodeConfig inputCheck)
        {
            inputCheck.Id = "wait";
            inputCheck.NextNodeId = "e";
            return new SequenceConfig
            {
                Name = "EdgeTrigger",
                Nodes = new List<SequenceNodeConfig>
                {
                    new() { Id = "s", NodeType = SequenceNodeType.Start, Name = "Start", NextNodeId = "wait" },
                    inputCheck,
                    new() { Id = "e", NodeType = SequenceNodeType.End, Name = "End" }
                }
            };
        }

        private static SequenceNodeConfig EdgeCheck(int channel, InputCheckMode mode, int timeoutMs = 5000, int debounceMs = 0) => new()
        {
            NodeType = SequenceNodeType.InputCheck,
            Name = "Wait Trigger",
            DeviceId = BoardId,
            PlcAddress = channel.ToString(),
            CheckMode = mode,
            TimeoutMs = timeoutMs,
            DebounceMs = debounceMs
        };

        [Fact]
        public async Task RisingEdge_SignalHeldOnEntry_PassesOnlyAfterReleaseAndRepress()
        {
            // 스위치를 누른 채 노드 진입 → ON 만으로는 통과 금지, OFF 관측 후의 ON 에서 통과
            var board = new ScriptedBoard(true, true, false, true);
            var engine = MakeEngine(board);

            SequenceErrorEventArgs? error = null;
            engine.SequenceError += (_, e) => error = e;

            await engine.RunAsync(TriggerConfig(EdgeCheck(4, InputCheckMode.BitRisingEdge)), CancellationToken.None);

            Assert.Null(error);
            // 읽기 순서: ON(대기) → ON(대기) → OFF(무장) → ON(통과) = 정확히 4회
            Assert.Equal(4, board.ReadCount);
        }

        [Fact]
        public async Task RisingEdge_SignalHeldForever_DoesNotRetrigger()
        {
            // 레벨 모드였다면 즉시 통과 — 에지 모드는 해제될 때까지 통과하지 않고 타임아웃까지 대기
            var board = new ScriptedBoard(true);   // 계속 ON
            var engine = MakeEngine(board);

            var sw = Stopwatch.StartNew();
            await engine.RunAsync(TriggerConfig(EdgeCheck(4, InputCheckMode.BitRisingEdge, timeoutMs: 400)), CancellationToken.None);
            sw.Stop();

            Assert.True(sw.ElapsedMilliseconds >= 350,
                $"홀드 중 즉시 통과했습니다 (경과 {sw.ElapsedMilliseconds}ms) — 레벨 감지로 퇴행");
        }

        [Fact]
        public async Task BitOn_LevelSemantics_Unchanged()
        {
            // 기존 BitOn 은 레벨 검사 그대로 — 첫 읽기에서 즉시 통과 (상태 확인용 노드 보호)
            var board = new ScriptedBoard(true);
            var engine = MakeEngine(board);

            await engine.RunAsync(TriggerConfig(EdgeCheck(4, InputCheckMode.BitOn)), CancellationToken.None);

            Assert.Equal(1, board.ReadCount);
        }

        [Fact]
        public async Task RisingEdge_DebounceRejectsGlitch()
        {
            // OFF(무장) → ON(후보) → [디바운스 재확인] OFF = 글리치 기각
            // → OFF → ON(후보) → [재확인] ON = 통과
            var board = new ScriptedBoard(false, true, false, false, true, true);
            var engine = MakeEngine(board);

            await engine.RunAsync(
                TriggerConfig(EdgeCheck(4, InputCheckMode.BitRisingEdge, debounceMs: 60)), CancellationToken.None);

            Assert.Equal(6, board.ReadCount);
        }

        [Fact]
        public async Task FallingEdge_PassesOnOnToOffTransition()
        {
            var board = new ScriptedBoard(false, true, false);
            var engine = MakeEngine(board);

            SequenceErrorEventArgs? error = null;
            engine.SequenceError += (_, e) => error = e;

            await engine.RunAsync(TriggerConfig(EdgeCheck(4, InputCheckMode.BitFallingEdge)), CancellationToken.None);

            Assert.Null(error);
            // OFF(대기) → ON(무장) → OFF(통과) = 3회
            Assert.Equal(3, board.ReadCount);
        }

        [Fact]
        public async Task BoardInputCheck_NonNumericChannel_RaisesSequenceError()
        {
            // 종전: 파싱 실패 → 조용히 통과 = Wait Trigger 가 스위치와 무관하게 무한 사이클.
            // 이제 SequenceError 로 표면화되어 AutoProcessService 가 로그 + 재시도 지연을 건다.
            var board = new ScriptedBoard(false);
            var engine = MakeEngine(board);

            var check = EdgeCheck(0, InputCheckMode.BitOn);
            check.PlcAddress = "X0";    // PLC 식 주소를 보드 노드에 잘못 입력한 경우

            SequenceErrorEventArgs? error = null;
            engine.SequenceError += (_, e) => error = e;

            await engine.RunAsync(TriggerConfig(check), CancellationToken.None);

            Assert.NotNull(error);
            Assert.IsType<InvalidOperationException>(error!.Error);
            Assert.Equal(0, board.ReadCount);
        }

        // ─── 스텁 ────────────────────────────────────────────────

        /// <summary>
        /// ReadBitAsync 가 정해진 순서의 값을 돌려주는 보드 스텁 — 마지막 값은 유지된다.
        /// 읽기 횟수로 에지 판정이 어느 시점에 통과했는지 정확히 검증한다.
        /// </summary>
        private sealed class ScriptedBoard : IIoBoardConnection
        {
            private readonly bool[] _script;
            private int _index;

            public ScriptedBoard(params bool[] script) => _script = script;

            public int ReadCount => _index;

            public string DeviceId => BoardId;
            public IoDeviceType DeviceType => IoDeviceType.AdLinkPci743x;
            public bool IsConnected => true;
            public int InputChannelCount => 32;
            public int OutputChannelCount => 32;

            public event EventHandler<IoBitChangedEventArgs>? BitChanged { add { } remove { } }

            public Task<bool> ConnectAsync() => Task.FromResult(true);
            public Task DisconnectAsync() => Task.CompletedTask;

            public Task<bool> ReadBitAsync(int channel)
            {
                var value = _script[Math.Min(_index, _script.Length - 1)];
                _index++;
                return Task.FromResult(value);
            }

            public Task<uint> ReadPortAsync(int portNo) => Task.FromResult(0u);
            public Task WriteBitAsync(int channel, bool value) => Task.CompletedTask;
            public Task WritePortAsync(int portNo, uint value) => Task.CompletedTask;
            public Task StartMonitoringAsync(int channel, int pollingIntervalMs = 50) => Task.CompletedTask;
            public Task StopMonitoringAsync(int channel) => Task.CompletedTask;
            public Task StopAllMonitoringAsync() => Task.CompletedTask;
            public void Dispose() { }
        }
    }
}
