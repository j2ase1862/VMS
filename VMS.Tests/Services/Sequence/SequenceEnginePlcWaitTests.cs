using System.Collections.Generic;
using System.Diagnostics;
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
    /// VM-06 — plc_signals.json 에 없는 주소를 InputCheck 로 쓰는 시퀀스가 신호를 받는지.
    /// 종전에는 엔진이 한 번 읽고 BitChanged 만 기다렸는데, 그 주소는 아무도 감시하지 않아
    /// 이벤트가 오지 않았다 → 타임아웃(없으면 영원히)까지 멈춤.
    /// </summary>
    public class SequenceEnginePlcWaitTests : System.IDisposable
    {
        private readonly SimulatedPlcConnection _plc = new();

        public void Dispose()
        {
            try { _plc.Dispose(); } catch { /* 무시 */ }
        }

        private SequenceEngine MakeEngine() => new(
            _plc,
            PlcVendor.Mitsubishi,
            _ => Task.FromResult(true),
            _ => Task.FromResult(true),
            (_, _) => { },
            _ => { });

        private static SequenceConfig WaitConfig(string address, InputCheckMode mode, int timeoutMs) => new()
        {
            Name = "PlcWait",
            Nodes = new List<SequenceNodeConfig>
            {
                new() { Id = "s", NodeType = SequenceNodeType.Start, Name = "Start", NextNodeId = "wait" },
                new()
                {
                    Id = "wait", NodeType = SequenceNodeType.InputCheck, Name = "Wait Trigger",
                    PlcAddress = address, CheckMode = mode, TimeoutMs = timeoutMs, NextNodeId = "e"
                },
                new() { Id = "e", NodeType = SequenceNodeType.End, Name = "End" }
            }
        };

        [Fact]
        public async Task BitOn_UnmonitoredAddress_PassesSoonAfterSignal()
        {
            await _plc.ConnectAsync(new PlcConnectionConfig { Vendor = PlcVendor.Mitsubishi });
            var engine = MakeEngine();

            var sw = Stopwatch.StartNew();
            var run = engine.RunAsync(WaitConfig("M300", InputCheckMode.BitOn, timeoutMs: 15_000), CancellationToken.None);

            await Task.Delay(300);
            _plc.SimSetBit("M300", true);   // 이벤트를 직접 올리지 않는다 — 실제 PLC 처럼 값만 바뀐다

            await run;
            sw.Stop();

            Assert.True(sw.ElapsedMilliseconds < 5_000,
                $"신호가 들어온 뒤에도 타임아웃까지 기다렸습니다 (경과 {sw.ElapsedMilliseconds}ms)");
        }

        [Fact]
        public async Task RisingEdge_UnmonitoredAddress_PassesAfterPulse()
        {
            await _plc.ConnectAsync(new PlcConnectionConfig { Vendor = PlcVendor.Mitsubishi });
            var engine = MakeEngine();

            var sw = Stopwatch.StartNew();
            var run = engine.RunAsync(WaitConfig("M301", InputCheckMode.BitRisingEdge, timeoutMs: 15_000), CancellationToken.None);

            await Task.Delay(300);
            _plc.SimSetBit("M301", true);

            await run;
            sw.Stop();

            Assert.True(sw.ElapsedMilliseconds < 5_000,
                $"상승 에지를 받지 못하고 타임아웃까지 기다렸습니다 (경과 {sw.ElapsedMilliseconds}ms)");
        }

        [Fact]
        public async Task BitOn_NoSignal_StillTimesOut()
        {
            // 재확인 회전이 타임아웃을 새로 잡지 않는다 — 신호가 없으면 정한 시간에 끝나야 한다
            await _plc.ConnectAsync(new PlcConnectionConfig { Vendor = PlcVendor.Mitsubishi });
            var engine = MakeEngine();

            var sw = Stopwatch.StartNew();
            await engine.RunAsync(WaitConfig("M302", InputCheckMode.BitOn, timeoutMs: 700), CancellationToken.None);
            sw.Stop();

            Assert.InRange(sw.ElapsedMilliseconds, 600, 5_000);
        }
    }
}
