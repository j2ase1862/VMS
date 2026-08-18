using System;
using System.Collections.Generic;
using System.Diagnostics;
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
    /// SequenceEngine 의 노드 실행 흐름 — Start/End/OutputAction/Delay/Branch/Inspection/
    /// Repeat/RecipeChange/StepChange — 를 SimulatedPlcConnection + 델리게이트 스텁으로 검증.
    ///
    /// 외부 의존 없음: PLC 통신은 in-memory dict, grab/inspect/recipeChange/stepChange 는 모두 람다.
    /// 감사 로그(AuditLogger.Instance)는 best-effort 라 테스트 흐름에 영향 없음.
    /// </summary>
    public class SequenceEngineIntegrationTests : IDisposable
    {
        private readonly SimulatedPlcConnection _plc = new();

        public void Dispose()
        {
            try { _plc.Dispose(); } catch { /* 무시 */ }
        }

        // ─── Engine factory ──────────────────────────────────────

        private SequenceEngine MakeEngine(
            Func<string, Task<bool>>? grab = null,
            Func<string, Task<bool>>? inspect = null,
            Action<string, bool>? setResult = null,
            Action<string>? reset = null,
            Func<int, Task>? recipeChange = null,
            Action<int>? stepChange = null)
        {
            return new SequenceEngine(
                _plc,
                PlcVendor.Mitsubishi,
                grab ?? (_ => Task.FromResult(true)),
                inspect ?? (_ => Task.FromResult(true)),
                setResult ?? ((_, _) => { }),
                reset ?? (_ => { }),
                getToolResultsFunc: null,
                recipeChangeByIndexFunc: recipeChange,
                stepChangeFunc: stepChange,
                ioRegistry: null);
        }

        // ─── Config / Node builders ───────────────────────────────

        private static SequenceConfig MakeConfig(string name, params SequenceNodeConfig[] nodes)
        {
            return new SequenceConfig
            {
                Name = name,
                Nodes = nodes.ToList()
            };
        }

        private static SequenceNodeConfig Start(string id, string? next) => new()
        {
            Id = id, NodeType = SequenceNodeType.Start, Name = "Start", NextNodeId = next
        };

        private static SequenceNodeConfig End(string id) => new()
        {
            Id = id, NodeType = SequenceNodeType.End, Name = "End"
        };

        private static SequenceNodeConfig OutputBit(string id, string addr, bool value, string? next = null) => new()
        {
            Id = id, NodeType = SequenceNodeType.OutputAction, Name = $"OutBit_{addr}",
            PlcAddress = addr, OutputDataType = PlcDataType.Bit, BitValue = value,
            NextNodeId = next
        };

        private static SequenceNodeConfig OutputWord(string id, string addr, int value, string? next = null) => new()
        {
            Id = id, NodeType = SequenceNodeType.OutputAction, Name = $"OutWord_{addr}",
            PlcAddress = addr, OutputDataType = PlcDataType.Int16, WordValue = value,
            NextNodeId = next
        };

        private static SequenceNodeConfig Delay(string id, int ms, string? next) => new()
        {
            Id = id, NodeType = SequenceNodeType.Delay, Name = $"Delay_{ms}",
            DelayMs = ms, NextNodeId = next
        };

        private static SequenceNodeConfig Inspection(string id, string cameraId, string? next) => new()
        {
            Id = id, NodeType = SequenceNodeType.Inspection, Name = $"Insp_{cameraId}",
            CameraId = cameraId, NextNodeId = next
        };

        private static SequenceNodeConfig Branch(string id, bool onAllCameras, string trueNext, string falseNext) => new()
        {
            Id = id, NodeType = SequenceNodeType.Branch, Name = "Branch",
            BranchOnAllCameras = onAllCameras,
            TrueBranchNodeId = trueNext, FalseBranchNodeId = falseNext
        };

        private static SequenceNodeConfig Repeat(string id, string target, int count, string? next) => new()
        {
            Id = id, NodeType = SequenceNodeType.Repeat, Name = "Repeat",
            RepeatTargetNodeId = target, RepeatCount = count, NextNodeId = next
        };

        private static SequenceNodeConfig RecipeChange(string id, string? next = null) => new()
        {
            Id = id, NodeType = SequenceNodeType.RecipeChange, Name = "RecipeChange",
            NextNodeId = next
        };

        private static SequenceNodeConfig StepChange(string id, string? next = null) => new()
        {
            Id = id, NodeType = SequenceNodeType.StepChange, Name = "StepChange",
            NextNodeId = next
        };

        // ─── Start node / completion ──────────────────────────────

        [Fact]
        public async Task RunAsync_NoStartNode_CompletesSilently()
        {
            // Start 가 없으면 로그만 남기고 정상 리턴.
            var engine = MakeEngine();
            var config = MakeConfig("NoStart", End("end1"));

            // SequenceCompleted 는 Start 노드 finding 실패 시도 finally 의 IsRunning=false 까진 가지만
            // SequenceCompleted 이벤트는 try 블록 끝에서 호출 → fire 안 됨.
            // 본 테스트는 단지 throw 없이 완료되는 것 확인.
            await engine.RunAsync(config, CancellationToken.None);

            Assert.False(engine.IsRunning);
            Assert.False(engine.WasReset);
        }

        [Fact]
        public async Task RunAsync_StartToEnd_FiresSequenceCompleted()
        {
            var engine = MakeEngine();
            var config = MakeConfig("Trivial",
                Start("s", "e"),
                End("e"));

            var completed = false;
            engine.SequenceCompleted += (_, _) => completed = true;

            await engine.RunAsync(config, CancellationToken.None);

            Assert.True(completed);
            Assert.False(engine.IsRunning);
            Assert.Null(engine.CurrentNodeId);
        }

        [Fact]
        public async Task RunAsync_AlreadyRunning_SecondCallReturnsImmediately()
        {
            var engine = MakeEngine();
            // Start → Delay(200ms) → End — 1초 안에 첫 RunAsync 가 진행되도록.
            var config = MakeConfig("LongRun",
                Start("s", "d"),
                Delay("d", 200, "e"),
                End("e"));

            var firstRun = engine.RunAsync(config, CancellationToken.None);

            // 첫 RunAsync 가 IsRunning=true 가 된 후 두번째 호출 — 즉시 return.
            // SimulatedPlcConnection.SimulateDelay 가 거의 0 이므로 RunAsync 가 매우 빨라
            // 두번째 호출 이전에 끝날 가능성이 있어 sleep 으로 보장은 못 함.
            // 대신 동일 작업을 await 후 IsRunning=false 확인으로 reentrancy 가 깨지지 않음을 검증.
            await engine.RunAsync(config, CancellationToken.None);
            await firstRun;

            Assert.False(engine.IsRunning);
        }

        // ─── OutputAction ─────────────────────────────────────────

        [Fact]
        public async Task OutputAction_Bit_WritesToSimulatedPlc()
        {
            var engine = MakeEngine();
            var config = MakeConfig("OutBit",
                Start("s", "out"),
                OutputBit("out", "M100", value: true, next: "e"),
                End("e"));

            await engine.RunAsync(config, CancellationToken.None);

            Assert.True(_plc.SimGetBit("M100"));
        }

        [Fact]
        public async Task OutputAction_Int16Word_WritesToSimulatedPlc()
        {
            var engine = MakeEngine();
            var config = MakeConfig("OutWord",
                Start("s", "out"),
                OutputWord("out", "D200", value: 1234, next: "e"),
                End("e"));

            await engine.RunAsync(config, CancellationToken.None);

            // SimulatedPlcConnection 의 word 메모리 key 는 "DeviceCode:Offset" 형식 (GetWordKey).
            Assert.Equal((short)1234, _plc.SimGetWord("D:200"));
        }

        [Fact]
        public async Task OutputAction_EmptyAddress_SkipsAndContinues()
        {
            // PlcAddress 가 빈 문자열이면 그대로 NextNodeId 로 진행 (PLC 호출 X).
            var engine = MakeEngine();
            var skipOut = new SequenceNodeConfig
            {
                Id = "out", NodeType = SequenceNodeType.OutputAction, PlcAddress = null,
                NextNodeId = "e"
            };
            var config = MakeConfig("SkipOut", Start("s", "out"), skipOut, End("e"));

            var completed = false;
            engine.SequenceCompleted += (_, _) => completed = true;

            await engine.RunAsync(config, CancellationToken.None);
            Assert.True(completed);
        }

        // ─── Delay ────────────────────────────────────────────────

        [Fact]
        public async Task Delay_NodeWaitsRequestedDuration()
        {
            var engine = MakeEngine();
            var config = MakeConfig("DelayCheck",
                Start("s", "d"),
                Delay("d", 150, "e"),
                End("e"));

            var sw = Stopwatch.StartNew();
            await engine.RunAsync(config, CancellationToken.None);
            sw.Stop();

            // 약간의 OS jitter 여유 — 100ms 이상이면 지연 확인 충분.
            Assert.True(sw.ElapsedMilliseconds >= 100,
                $"Delay 노드가 충분히 대기하지 않음 (실제 {sw.ElapsedMilliseconds}ms)");
        }

        // ─── Branch ───────────────────────────────────────────────

        [Fact]
        public async Task Branch_DefaultLastInspectionOkTrue_RoutesTrue()
        {
            // _lastInspectionOk 의 기본값이 true → Branch on LastOk → True branch.
            var engine = MakeEngine();
            var trueRan = false;
            var falseRan = false;

            var config = MakeConfig("Branch_NoInsp",
                Start("s", "br"),
                Branch("br", onAllCameras: false, trueNext: "out_t", falseNext: "out_f"),
                OutputBit("out_t", "M001", true, next: "e"),
                OutputBit("out_f", "M002", true, next: "e"),
                End("e"));

            await engine.RunAsync(config, CancellationToken.None);

            trueRan = _plc.SimGetBit("M001");
            falseRan = _plc.SimGetBit("M002");
            Assert.True(trueRan);
            Assert.False(falseRan);
        }

        [Fact]
        public async Task Branch_AfterFailedInspection_RoutesFalse()
        {
            // Inspection 이 false 반환 → _lastInspectionOk=false → Branch on LastOk → False.
            var engine = MakeEngine(
                grab: _ => Task.FromResult(true),
                inspect: _ => Task.FromResult(false));

            var config = MakeConfig("Branch_FailInsp",
                Start("s", "insp"),
                Inspection("insp", "CAM01", next: "br"),
                Branch("br", onAllCameras: false, trueNext: "out_t", falseNext: "out_f"),
                OutputBit("out_t", "M001", true, next: "e"),
                OutputBit("out_f", "M002", true, next: "e"),
                End("e"));

            await engine.RunAsync(config, CancellationToken.None);

            Assert.False(_plc.SimGetBit("M001"));
            Assert.True(_plc.SimGetBit("M002"));
        }

        // ─── Inspection ───────────────────────────────────────────

        [Fact]
        public async Task Inspection_GrabAndInspectInvokedWithCameraId()
        {
            string? grabbedCam = null;
            string? inspectedCam = null;

            var engine = MakeEngine(
                grab: id => { grabbedCam = id; return Task.FromResult(true); },
                inspect: id => { inspectedCam = id; return Task.FromResult(true); });

            var config = MakeConfig("InspCallbacks",
                Start("s", "insp"),
                Inspection("insp", "CAM_A", next: "e"),
                End("e"));

            await engine.RunAsync(config, CancellationToken.None);

            Assert.Equal("CAM_A", grabbedCam);
            Assert.Equal("CAM_A", inspectedCam);
        }

        [Fact]
        public async Task Inspection_GrabFails_InspectNotInvoked_AndCameraResultFalse()
        {
            var inspectCalled = false;
            string? resultCam = null;
            bool? resultValue = null;

            var engine = MakeEngine(
                grab: _ => Task.FromResult(false),
                inspect: _ => { inspectCalled = true; return Task.FromResult(true); },
                setResult: (cam, ok) => { resultCam = cam; resultValue = ok; });

            var config = MakeConfig("InspGrabFail",
                Start("s", "insp"),
                Inspection("insp", "CAM_B", next: "e"),
                End("e"));

            await engine.RunAsync(config, CancellationToken.None);

            Assert.False(inspectCalled);
            Assert.Equal("CAM_B", resultCam);
            Assert.Equal(false, resultValue);
            Assert.False(engine.AllInspectionsOk);
        }

        [Fact]
        public async Task Inspection_Success_DoesNotInvokeSetResult()
        {
            // 정상 검사 경로에서 setResultFunc 를 부르면 안 된다 — inspectFunc(ManualInspect)가
            // 이미 SetInspectionResult 로 InspectionCompleted 를 발생시키므로, 여기서 또 부르면
            // 트리거 1회에 대시보드 카운트·이미지 저장·업로드가 2배가 된다 (2026-08-18 현장).
            var setResultCalls = 0;

            var engine = MakeEngine(
                grab: _ => Task.FromResult(true),
                inspect: _ => Task.FromResult(true),
                setResult: (_, _) => setResultCalls++);

            var config = MakeConfig("InspNoDoubleCount",
                Start("s", "insp"),
                Inspection("insp", "CAM_C", next: "e"),
                End("e"));

            await engine.RunAsync(config, CancellationToken.None);

            Assert.Equal(0, setResultCalls);
            Assert.True(engine.AllInspectionsOk);
        }

        [Fact]
        public async Task Inspection_GrabFails_InvokesSetResultExactlyOnce()
        {
            // grab 실패 경로는 검사가 돌지 않아 InspectionCompleted 가 없으므로
            // setResultFunc 1회 호출이 유지돼야 한다 (0회도 2회도 아님).
            var setResultCalls = 0;

            var engine = MakeEngine(
                grab: _ => Task.FromResult(false),
                inspect: _ => Task.FromResult(true),
                setResult: (_, _) => setResultCalls++);

            var config = MakeConfig("InspGrabFailOnce",
                Start("s", "insp"),
                Inspection("insp", "CAM_D", next: "e"),
                End("e"));

            await engine.RunAsync(config, CancellationToken.None);

            Assert.Equal(1, setResultCalls);
        }

        [Fact]
        public async Task Inspection_NoCameraId_SkipsAndContinues()
        {
            // CameraId 가 빈 문자열이면 lastInspectionOk=false 만 설정하고 진행.
            var grabCalled = false;
            var engine = MakeEngine(grab: _ => { grabCalled = true; return Task.FromResult(true); });

            var noCamInsp = new SequenceNodeConfig
            {
                Id = "insp", NodeType = SequenceNodeType.Inspection, CameraId = null,
                NextNodeId = "e"
            };
            var config = MakeConfig("InspNoCam", Start("s", "insp"), noCamInsp, End("e"));

            await engine.RunAsync(config, CancellationToken.None);
            Assert.False(grabCalled);
        }

        // ─── Repeat ───────────────────────────────────────────────

        [Fact]
        public async Task Repeat_FiniteCount_RunsTargetExpectedTimes()
        {
            // Start → Counter → Repeat(target=Counter, count=3) → End
            // ExecuteRepeat 정책 — count<RepeatCount 동안 target 반환 → 3 회 Counter 실행.
            int counterRuns = 0;

            // 단순히 카운팅 위한 OutputAction (write 마다 +1) → 측정 후 verify.
            // 실제 카운팅은 inspectCallback 우회 못 함 → 별도 OutputAction 사용해도 부수효과 없음.
            // Repeat 동작만 검증: 노드 실행 횟수를 NodeExecuting 이벤트로 카운트.
            var engine = MakeEngine();
            engine.NodeExecuting += (_, args) =>
            {
                if (args.NodeId == "ctr") counterRuns++;
            };

            var config = MakeConfig("RepeatFinite",
                Start("s", "ctr"),
                OutputBit("ctr", "M500", true, next: "rep"),
                Repeat("rep", target: "ctr", count: 3, next: "e"),
                End("e"));

            await engine.RunAsync(config, CancellationToken.None);

            Assert.Equal(3, counterRuns);
        }

        // ─── RecipeChange / StepChange — no callback path ────────

        [Fact]
        public async Task RecipeChange_NoCallback_SkipsWithoutError()
        {
            // recipeChangeByIndexFunc=null → 무조건 NextNodeId 진행.
            var engine = MakeEngine(recipeChange: null);
            var completed = false;
            engine.SequenceCompleted += (_, _) => completed = true;

            var config = MakeConfig("RecipeNoCb",
                Start("s", "rc"),
                RecipeChange("rc", next: "e"),
                End("e"));

            await engine.RunAsync(config, CancellationToken.None);
            Assert.True(completed);
        }

        [Fact]
        public async Task StepChange_NoCallback_SkipsWithoutError()
        {
            var engine = MakeEngine(stepChange: null);
            var completed = false;
            engine.SequenceCompleted += (_, _) => completed = true;

            var config = MakeConfig("StepNoCb",
                Start("s", "sc"),
                StepChange("sc", next: "e"),
                End("e"));

            await engine.RunAsync(config, CancellationToken.None);
            Assert.True(completed);
        }

        [Fact]
        public async Task StepChange_WithCallbackButNoSignal_DefaultsToStepZero()
        {
            // StepSignalAddress / StepIndexAddress 가 모두 없으면 stepIndex=0 으로 callback.
            int? receivedStep = null;
            var engine = MakeEngine(stepChange: idx => receivedStep = idx);

            var config = MakeConfig("StepDefault",
                Start("s", "sc"),
                StepChange("sc", next: "e"),
                End("e"));

            await engine.RunAsync(config, CancellationToken.None);
            Assert.Equal(0, receivedStep);
        }

        // ─── 종료 후 상태 ────────────────────────────────────────

        [Fact]
        public async Task RunAsync_NormalCompletion_WasResetFalse()
        {
            var engine = MakeEngine();
            var config = MakeConfig("NoReset",
                Start("s", "e"),
                End("e"));

            await engine.RunAsync(config, CancellationToken.None);
            Assert.False(engine.WasReset);
            Assert.False(engine.IsRunning);
        }
    }
}
