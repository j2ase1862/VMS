using System;
using System.Threading;
using VMS.Core.Services;
using Xunit;

namespace VMS.Core.Tests.Services
{
    /// <summary>
    /// 검사 중 신호 — VMS 가 핸들을 쥐는 동안만 같은 PC 의 학습 워커가 "검사 중" 으로 본다 (MLOps 개발 문서 §5.6).
    /// 실제 규약 이름은 이 PC 에서 도는 VMS 와 부딪히므로, 시험은 실행마다 고유한 Global\ 이름을 쓴다.
    /// </summary>
    public class InspectionActivitySignalTests
    {
        private static string UniqueName() => $@"Global\BODA.VMS.InspectionActive.test-{Guid.NewGuid():N}";

        [Fact]
        public void MutexName_is_the_contract_value_shared_with_the_train_worker()
        {
            // 워커가 이 문자열로 찾는다 — 바꾸면 워커가 검사 중을 못 본다.
            Assert.Equal(@"Global\BODA.VMS.InspectionActive", InspectionActivitySignal.MutexName);
        }

        [Fact]
        public void Not_active_when_nobody_holds_the_signal()
        {
            Assert.False(InspectionActivitySignal.IsActive(UniqueName()));
        }

        [Fact]
        public void Active_while_held_and_gone_after_dispose()
        {
            var name = UniqueName();
            var handle = InspectionActivitySignal.TryAcquire(name, out var error);

            Assert.NotNull(handle);
            Assert.Null(error);
            Assert.True(InspectionActivitySignal.IsActive(name));

            handle!.Dispose();
            Assert.False(InspectionActivitySignal.IsActive(name));
        }

        [Fact]
        public void Signal_stays_until_the_last_holder_releases()
        {
            // 한 PC 의 여러 인스턴스(또는 AUTO RUN + Roller) — 먼저 끝난 쪽이 신호를 지우면 안 된다.
            var name = UniqueName();
            var first = InspectionActivitySignal.TryAcquire(name, out _);
            var second = InspectionActivitySignal.TryAcquire(name, out _);
            Assert.NotNull(first);
            Assert.NotNull(second);

            first!.Dispose();
            Assert.True(InspectionActivitySignal.IsActive(name));

            second!.Dispose();
            Assert.False(InspectionActivitySignal.IsActive(name));
        }

        [Fact]
        public void Checking_does_not_keep_the_signal_alive()
        {
            // 워커가 확인 후 핸들을 닫지 않으면 VMS 가 정지해도 신호가 남는다.
            var name = UniqueName();
            var handle = InspectionActivitySignal.TryAcquire(name, out _);
            Assert.True(InspectionActivitySignal.IsActive(name));
            Assert.True(InspectionActivitySignal.IsActive(name));

            handle!.Dispose();
            Assert.False(InspectionActivitySignal.IsActive(name));
        }

        [Fact]
        public void Acquire_failure_returns_null_with_reason_instead_of_throwing()
        {
            // 같은 이름의 다른 종류 커널 객체가 있으면 Mutex 로 열 수 없다 — 검사를 막지 않고 사유만 돌려준다.
            var name = UniqueName();
            using var squatter = new EventWaitHandle(false, EventResetMode.ManualReset, name);

            var handle = InspectionActivitySignal.TryAcquire(name, out var error);

            Assert.Null(handle);
            Assert.False(string.IsNullOrEmpty(error));
        }
    }
}
