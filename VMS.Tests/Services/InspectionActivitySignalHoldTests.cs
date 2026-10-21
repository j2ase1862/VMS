using System;
using System.Threading.Tasks;
using VMS.PLC.Models;
using VMS.PLC.Models.Sequence;
using VMS.PLC.Services;
using VMS.Services;
using Xunit;

namespace VMS.Tests.Services
{
    /// <summary>
    /// 검사 구간(AUTO RUN · Roller 연속 검사) 동안만 "검사 중" 신호를 쥔다 — 같은 PC 의 MLOps 학습 워커가
    /// 이 신호로 학습을 멈춘다. 신호가 정지 뒤에도 남으면 워커가 영원히 멈추므로 해제 경로를 함께 본다.
    /// </summary>
    public class InspectionActivitySignalHoldTests
    {
        private sealed class FakeSignal : IDisposable
        {
            public int Acquired;
            public int Disposed;
            public bool Held => Acquired > Disposed;

            public IDisposable? Acquire()
            {
                Acquired++;
                return this;
            }

            public void Dispose() => Disposed++;
        }

        private static AutoProcessService CreateAutoProcess(PlcSignalConfiguration config, FakeSignal signal) =>
            new(
                new SimulatedPlcConnection(),
                config,
                PlcVendor.Mitsubishi,
                grabFunc: _ => Task.FromResult(true),
                inspectFunc: _ => Task.FromResult(true),
                setResultFunc: (_, _) => { },
                resetFunc: _ => { },
                processSequence: new SequenceConfig(),   // Start 노드 없음 — 운전 루프는 곧바로 끝난다
                activitySignalFactory: signal.Acquire);

        [Fact]
        public async Task AutoProcess_holds_signal_from_start_until_stop()
        {
            var signal = new FakeSignal();
            var svc = CreateAutoProcess(new PlcSignalConfiguration(), signal);

            await svc.StartAsync();
            Assert.True(svc.IsRunning);
            Assert.True(signal.Held);

            await svc.StopAsync();
            Assert.False(svc.IsRunning);
            Assert.False(signal.Held);
            Assert.Equal(1, signal.Acquired);
        }

        [Fact]
        public async Task AutoProcess_second_start_does_not_take_another_signal()
        {
            var signal = new FakeSignal();
            var svc = CreateAutoProcess(new PlcSignalConfiguration(), signal);

            await svc.StartAsync();
            await svc.StartAsync();
            await svc.StopAsync();

            Assert.Equal(1, signal.Acquired);
            Assert.False(signal.Held);
        }

        [Fact]
        public async Task AutoProcess_start_failure_releases_signal_and_running_state()
        {
            // 트리거 주소가 비어 PLC 감시 등록이 실패하는 설정 — 시작이 예외로 끝나도
            // 신호가 남으면 운전하지 않는 PC 에서 학습이 계속 막힌다.
            var config = new PlcSignalConfiguration();
            config.SignalMaps.Add(new PlcSignalMap { CameraId = "Cam1", TriggerAddress = "" });
            var signal = new FakeSignal();
            var svc = CreateAutoProcess(config, signal);

            await Assert.ThrowsAnyAsync<Exception>(() => svc.StartAsync());

            Assert.False(svc.IsRunning);
            Assert.False(signal.Held);
        }

        [Fact]
        public void Roller_holds_signal_from_start_until_stop()
        {
            var signal = new FakeSignal();
            var svc = new RollerInspectionService(activitySignalFactory: signal.Acquire);

            svc.Start();
            svc.Start();
            Assert.True(signal.Held);
            Assert.Equal(1, signal.Acquired);

            svc.Stop();
            Assert.False(signal.Held);
        }

        [Fact]
        public async Task Missing_signal_does_not_block_inspection()
        {
            // 신호를 못 만들어도(null) 검사는 시작·정지된다.
            var svc = new AutoProcessService(
                new SimulatedPlcConnection(), new PlcSignalConfiguration(), PlcVendor.Mitsubishi,
                _ => Task.FromResult(true), _ => Task.FromResult(true), (_, _) => { }, _ => { },
                processSequence: new SequenceConfig(),
                activitySignalFactory: () => null);

            await svc.StartAsync();
            Assert.True(svc.IsRunning);
            await svc.StopAsync();
            Assert.False(svc.IsRunning);

            var roller = new RollerInspectionService(activitySignalFactory: () => null);
            roller.Start();
            Assert.True(roller.IsRunning);
            roller.Stop();
            Assert.False(roller.IsRunning);
        }
    }
}
