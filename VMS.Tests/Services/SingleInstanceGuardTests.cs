using System;
using System.Threading;
using VMS.Services;
using VMS.ViewModels;
using Xunit;

namespace VMS.Tests.Services
{
    /// <summary>
    /// VM-43 — 같은 이름 인스턴스는 한 번만 뜨고, 창 제목에 인스턴스 이름이 보인다.
    /// 뮤텍스는 같은 스레드에서는 재진입되므로, "다른 프로세스" 는 별도 스레드로 흉내 낸다.
    /// </summary>
    public class SingleInstanceGuardTests
    {
        [Fact]
        public void SecondAcquire_WhileHeldElsewhere_Fails_ThenSucceedsAfterRelease()
        {
            var name = $@"Local\BODA_VMS_Test_{Guid.NewGuid():N}";
            using var acquired = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            using var released = new ManualResetEventSlim();

            var holder = new Thread(() =>
            {
                var guard = SingleInstanceGuard.TryAcquire(name, TimeSpan.Zero);
                Assert.NotNull(guard);
                acquired.Set();
                release.Wait();
                guard!.Dispose();
                released.Set();
            });
            holder.Start();
            Assert.True(acquired.Wait(TimeSpan.FromSeconds(5)));

            Assert.Null(SingleInstanceGuard.TryAcquire(name, TimeSpan.FromMilliseconds(100)));

            release.Set();
            Assert.True(released.Wait(TimeSpan.FromSeconds(5)));
            holder.Join();

            using var again = SingleInstanceGuard.TryAcquire(name, TimeSpan.FromSeconds(1));
            Assert.NotNull(again);
        }

        [Fact]
        public void DifferentInstanceNames_DoNotBlockEachOther()
        {
            using var a = SingleInstanceGuard.TryAcquire($@"Local\BODA_VMS_Test_{Guid.NewGuid():N}", TimeSpan.Zero);
            using var b = SingleInstanceGuard.TryAcquire($@"Local\BODA_VMS_Test_{Guid.NewGuid():N}", TimeSpan.Zero);
            Assert.NotNull(a);
            Assert.NotNull(b);
        }

        [Theory]
        [InlineData("BODA VMS", "", "BODA VMS")]
        [InlineData("BODA VMS", null, "BODA VMS")]
        [InlineData("BODA VMS", "line2", "BODA VMS [line2]")]
        public void Title_ShowsInstanceName_OnlyForNamedInstance(string title, string? instance, string expected)
            => Assert.Equal(expected, MainViewModel.WithInstanceName(title, instance));
    }
}
