using System;
using System.IO;
using System.Threading;
using VMS.Core.Imaging;
using VMS.Services;
using Xunit;

namespace VMS.Tests.Services
{
    /// <summary>
    /// VM-41 — 이미지 보존 기간이 VMS 시작 때(그리고 하루 한 번) 실제로 적용된다.
    /// 종전에는 정리를 부르는 곳이 --cleanup-images 하나뿐이라 아무도 지우지 않았다.
    /// </summary>
    public class ImageRetentionSchedulerTests : IDisposable
    {
        private readonly string _base = Path.Combine(Path.GetTempPath(), $"img_ret_{Guid.NewGuid():N}");

        public ImageRetentionSchedulerTests()
        {
            Directory.CreateDirectory(_base);
        }

        public void Dispose()
        {
            try { if (Directory.Exists(_base)) Directory.Delete(_base, recursive: true); } catch { }
        }

        private string MakeDayFolder(DateTime day)
        {
            var dir = Path.Combine(_base, day.ToString("yyyy-MM-dd"), "NG");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "a.png"), "x");
            return Path.GetDirectoryName(dir)!;
        }

        [Fact]
        public void StartupRun_DeletesExpiredDayFolders_KeepsRecentAndForeign()
        {
            var old = MakeDayFolder(DateTime.Today.AddDays(-40));
            var recent = MakeDayFolder(DateTime.Today.AddDays(-3));
            var foreign = Path.Combine(_base, "my-notes");
            Directory.CreateDirectory(foreign);

            using var scheduler = new ImageRetentionScheduler(
                () => new ImageSaveOptions { BaseDir = _base, RetentionDays = 30 },
                startupDelay: TimeSpan.Zero, interval: TimeSpan.FromHours(24));
            scheduler.Start();

            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (scheduler.RunCount == 0 && DateTime.UtcNow < deadline) Thread.Sleep(20);

            Assert.Equal(1, scheduler.RunCount);
            Assert.False(Directory.Exists(old));
            Assert.True(Directory.Exists(recent));
            Assert.True(Directory.Exists(foreign));   // 날짜 폴더가 아니면 건드리지 않는다
        }

        [Fact]
        public void Unlimited_DeletesNothing()
        {
            var old = MakeDayFolder(DateTime.Today.AddDays(-400));
            using var scheduler = new ImageRetentionScheduler(
                () => new ImageSaveOptions { BaseDir = _base, RetentionDays = 0 });

            var result = scheduler.RunOnce("Test");

            Assert.NotNull(result);
            Assert.Empty(result!.Deleted);
            Assert.True(Directory.Exists(old));
        }

        [Fact]
        public void Result_ListsDeletedFolderNames()
        {
            var day = DateTime.Today.AddDays(-10);
            MakeDayFolder(day);

            var result = ImageRetentionCleaner.CleanupDetailed(new ImageSaveOptions { BaseDir = _base, RetentionDays = 5 });

            Assert.Equal(new[] { day.ToString("yyyy-MM-dd") }, result.Deleted);
            Assert.Empty(result.Failed);
        }
    }
}
