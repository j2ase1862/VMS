using System;
using System.IO;
using System.Linq;
using Boda.LicGen;
using Xunit;

namespace VMS.Core.Tests.LicGen
{
    /// <summary>
    /// VO-14: LicGen 백업 배너의 미반영 발급 건수는 매체마다 센다.
    /// 예전엔 대장 줄 수가 가장 많은 매체 하나로만 세어, 두 매체를 한 번씩 등록한 뒤로는
    /// USB 하나만 갱신해도 '미반영 0건' 으로 보였다.
    /// 실제 키 폴더(%USERPROFILE%\.boda-licgen)는 쓰지 않고 임시 폴더에 대장 · 마커만 만든다.
    /// </summary>
    public sealed class BackupServiceStatusTests : IDisposable
    {
        private readonly string _keyDir = Path.Combine(Path.GetTempPath(), "vo14-licgen-" + Guid.NewGuid().ToString("N"));

        public BackupServiceStatusTests()
        {
            Assert.DoesNotContain(".boda-licgen", _keyDir, StringComparison.OrdinalIgnoreCase);
            Directory.CreateDirectory(_keyDir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_keyDir, recursive: true); } catch (IOException) { }
        }

        private void WriteLedger(int lines) =>
            File.WriteAllLines(LedgerStore.PathFor(_keyDir), Enumerable.Range(1, lines).Select(i => "{\"line\":" + i + "}"));

        private void WriteMarker(params (string Key, string Utc, int Lines)[] targets)
        {
            var marker = new BackupMarker();
            foreach (var (key, utc, lines) in targets)
                marker.Targets[key] = new BackupMarker.Entry(utc, lines);
            marker.Save(_keyDir);
        }

        [Fact]
        public void SecondMediaBehind_IsReportedPerMedia_AndWarns()
        {
            WriteLedger(5);
            // USB-A 는 5건까지, USB-B 는 2건까지만 담김 (A 만 갱신한 상태)
            WriteMarker(("USB-A [AAAA0001]", "2026-11-16T05:00:00.0000000Z", 5),
                        ("USB-B [BBBB0002]", "2026-11-10T05:00:00.0000000Z", 2));

            var status = BackupService.GetStatus(_keyDir);

            Assert.Equal(2, status.MediaCount);
            Assert.Equal(1, status.UpToDateCount);
            Assert.True(status.NeedsBackup);                 // 예전엔 '0건' · 경고 없음
            Assert.Equal(3, status.PendingIssues);           // 2부를 갖추려면 3건 더 반영
            Assert.Equal("USB-A [AAAA0001] 0건 · USB-B [BBBB0002] 3건", status.DescribeMedia());
            Assert.Equal("2026-11-16T05:00:00.0000000Z", status.NewestUtc);
        }

        [Fact]
        public void BothMediaUpToDate_NoWarning()
        {
            WriteLedger(5);
            WriteMarker(("USB-A [AAAA0001]", "2026-11-16T05:00:00.0000000Z", 5),
                        ("USB-B [BBBB0002]", "2026-11-16T06:00:00.0000000Z", 5));

            var status = BackupService.GetStatus(_keyDir);

            Assert.False(status.NeedsBackup);
            Assert.Equal(0, status.PendingIssues);
            Assert.StartsWith("USB-B", status.DescribeMedia());   // 최근 백업 순
        }

        [Fact]
        public void SingleMedia_UpToDate_StillNeedsSecondCopy()
        {
            WriteLedger(3);
            WriteMarker(("USB-A [AAAA0001]", "2026-11-16T05:00:00.0000000Z", 3));

            var status = BackupService.GetStatus(_keyDir);

            Assert.True(status.NeedsBackup);
            Assert.Equal(3, status.PendingIssues);   // 두 번째 매체엔 아무것도 없음
        }

        [Fact]
        public void RetiredThirdMedia_DoesNotWarn_WhenTwoAreUpToDate()
        {
            WriteLedger(8);
            WriteMarker(("USB-A [AAAA0001]", "2026-11-16T05:00:00.0000000Z", 8),
                        ("USB-B [BBBB0002]", "2026-11-16T06:00:00.0000000Z", 8),
                        ("OLD [CCCC0003]", "2026-01-01T00:00:00.0000000Z", 1));

            var status = BackupService.GetStatus(_keyDir);

            Assert.False(status.NeedsBackup);
            Assert.Equal(0, status.PendingIssues);
            Assert.EndsWith("OLD [CCCC0003] 7건", status.DescribeMedia());
        }

        [Fact]
        public void NoMarker_HasNoRecord()
        {
            WriteLedger(2);

            var status = BackupService.GetStatus(_keyDir);

            Assert.False(status.HasRecord);
            Assert.Null(status.NewestUtc);
            Assert.True(status.NeedsBackup);
        }
    }
}
