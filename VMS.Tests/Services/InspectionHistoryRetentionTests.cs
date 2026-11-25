using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using VMS.Core.Retention;
using VMS.Services.LocalHistory;
using Xunit;

namespace VMS.Tests.Services
{
    /// <summary>
    /// 로컬 검사 이력 보존 정리 — 행 수 상한(inspectionHistory.maxRows), 묶음 삭제, 미리보기,
    /// 시작 2 분 뒤 + 1 시간마다 도는 스케줄러. 종전에는 VMS 시작 때 보존 일수로만 한 번 지워
    /// 재시작 없이 오래 도는 라인에서 DB 가 끝없이 커졌다.
    /// </summary>
    public class InspectionHistoryRetentionTests : IDisposable
    {
        private readonly string _tempDir;
        private readonly LocalInspectionHistoryStore _store;
        private static readonly TimeSpan FlushTimeout = TimeSpan.FromSeconds(30);

        public InspectionHistoryRetentionTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), $"insp_ret_{Guid.NewGuid():N}");
            _store = new LocalInspectionHistoryStore(_tempDir);
        }

        public void Dispose()
        {
            _store.Dispose();
            try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); }
            catch { /* SQLite 핸들 잔존 가능 — 무시 */ }
        }

        /// <summary>startUtc 부터 1 초 간격으로 count 건. 키는 prefix+번호.</summary>
        private void Seed(int count, DateTime startUtc, string prefix)
        {
            for (int i = 0; i < count; i++)
            {
                _store.Record(new LocalInspectionEntry
                {
                    InspectedAtUtc = startUtc.AddSeconds(i),
                    IsPass = true,
                    RecipeName = "R1",
                    CorrelationKey = $"{prefix}{i}",
                    Mode = LocalInspectionMode.Cycle
                });
            }
            Assert.True(_store.Flush(FlushTimeout));
        }

        private List<string> AllKeysOldestFirst()
            => _store.Query(new LocalInspectionQuery { Limit = int.MaxValue })
                .OrderBy(e => e.InspectedAtUtc).ThenBy(e => e.Id)
                .Select(e => e.CorrelationKey!).ToList();

        // ─── 저장소 ─────────────────────────────────────────────

        [Fact]
        public void ApplyRetention_MaxRows_DeletesOldestBeyondCap()
        {
            Seed(30, DateTime.UtcNow.AddHours(-1), "k");

            var r = _store.ApplyRetention(90, maxRows: 10, trigger: "Test", auditWhenNothingDeleted: false);

            Assert.Equal(0, r.DeletedByAge);
            Assert.Equal(20, r.DeletedByMaxRows);
            Assert.Equal(10, r.Remaining);
            Assert.False(r.Failed);
            // 가장 최근 10 건(k20 ~ k29)만 남는다
            Assert.Equal(Enumerable.Range(20, 10).Select(i => $"k{i}"), AllKeysOldestFirst());
        }

        [Fact]
        public void ApplyRetention_AgeFirst_ThenCap_AcrossSeveralChunks()
        {
            // 보존 일수 밖 6,000 건(묶음 5,000 을 넘김) + 안쪽 6,000 건, 상한 1,000
            var now = DateTime.UtcNow;
            Seed(6000, now.AddDays(-120), "old");
            Seed(6000, now.AddHours(-3), "new");
            Assert.True(6000 > LocalInspectionHistoryStore.PurgeChunk);

            var r = _store.ApplyRetention(90, maxRows: 1000, trigger: "Test", auditWhenNothingDeleted: false);

            Assert.Equal(6000, r.DeletedByAge);
            Assert.Equal(5000, r.DeletedByMaxRows);
            Assert.Equal(1000, r.Remaining);
            Assert.Equal(1000, _store.Count(new LocalInspectionQuery()));
            var keys = AllKeysOldestFirst();
            Assert.Equal("new5000", keys.First());
            Assert.Equal("new5999", keys.Last());
        }

        [Fact]
        public void ApplyRetention_UnderCap_DeletesNothing_AndRecordingKeepsWorking()
        {
            Seed(5, DateTime.UtcNow.AddMinutes(-5), "k");

            var r = _store.ApplyRetention(90, maxRows: 10, trigger: "Test", auditWhenNothingDeleted: false);
            Assert.Equal(0, r.Deleted);
            Assert.Equal(5, r.Remaining);

            // 정리 뒤에도 큐 기록이 계속 된다
            Seed(3, DateTime.UtcNow, "after");
            Assert.Equal(8, _store.Count(new LocalInspectionQuery()));
        }

        [Fact]
        public void ApplyRetention_WithoutCap_IsAgeOnly_LikePurgeOlderThan()
        {
            var now = DateTime.UtcNow;
            Seed(3, now.AddDays(-100), "old");
            Seed(4, now.AddHours(-1), "new");

            var r = _store.ApplyRetention(90, maxRows: null, trigger: "Test", auditWhenNothingDeleted: false);

            Assert.Equal(3, r.DeletedByAge);
            Assert.Equal(0, r.DeletedByMaxRows);
            Assert.Equal(4, r.Remaining);
        }

        [Fact]
        public void PreviewCleanup_WithCap_CountsCapDeletions_AndMatchesActualRun()
        {
            var now = DateTime.UtcNow;
            Seed(5, now.AddDays(-100), "old");
            Seed(20, now.AddHours(-2), "new");

            var p = LocalInspectionHistoryStore.PreviewCleanup(_tempDir, 90, maxRows: 8);
            Assert.Equal(5 + 12, p.FilesAffected);
            Assert.Equal(8, p.FilesRemaining);
            Assert.Null(p.Note);
            Assert.NotNull(p.OldestRemainingUtc);

            // 미리보기 "정리 후 가장 오래된" = 실제로 남는 가장 오래된 행
            _store.ApplyRetention(90, maxRows: 8, trigger: "Test", auditWhenNothingDeleted: false);
            var oldestKept = _store.Query(new LocalInspectionQuery { Limit = int.MaxValue }).Min(e => e.InspectedAtUtc);
            Assert.Equal(oldestKept, p.OldestRemainingUtc!.Value);
            Assert.Equal(8, _store.Count(new LocalInspectionQuery()));

            // 상한을 안 주면 종전과 같이 보존 일수만
            var ageOnly = LocalInspectionHistoryStore.PreviewCleanup(_tempDir, 90);
            Assert.Equal(0, ageOnly.FilesAffected);
        }

        // ─── 스케줄러 ───────────────────────────────────────────

        [Fact]
        public void Scheduler_RunOnce_AppliesCurrentOptions()
        {
            Seed(15, DateTime.UtcNow.AddMinutes(-30), "k");
            var options = new InspectionHistoryOptions { Enabled = true, RetentionDays = 90, MaxRows = 6 };
            using var s = new InspectionHistoryRetentionScheduler(() => options, () => _store,
                startupDelay: TimeSpan.FromHours(1), interval: TimeSpan.FromHours(1));

            var r = s.RunOnce("Startup");

            Assert.NotNull(r);
            Assert.Equal(9, r!.DeletedByMaxRows);
            Assert.Equal(6, _store.Count(new LocalInspectionQuery()));
            Assert.Equal(1, s.RunCount);

            // 설정을 매 회차 다시 읽는다 — 상한을 줄이면 다음 회차에 반영
            options = new InspectionHistoryOptions { Enabled = true, RetentionDays = 90, MaxRows = 2 };
            Assert.Equal(4, s.RunOnce("Hourly")!.DeletedByMaxRows);
            Assert.Equal(2, s.RunCount);
        }

        [Fact]
        public void Scheduler_Disabled_DoesNotTouchStore()
        {
            var touched = false;
            using var s = new InspectionHistoryRetentionScheduler(
                () => new InspectionHistoryOptions { Enabled = false },
                () => { touched = true; return _store; },
                startupDelay: TimeSpan.FromHours(1), interval: TimeSpan.FromHours(1));

            Assert.Null(s.RunOnce("Startup"));
            Assert.False(touched);
            Assert.Equal(0, s.RunCount);
        }

        [Fact]
        public void Scheduler_Start_RunsAfterStartupDelay()
        {
            Seed(12, DateTime.UtcNow.AddMinutes(-10), "k");
            using var s = new InspectionHistoryRetentionScheduler(
                () => new InspectionHistoryOptions { Enabled = true, RetentionDays = 90, MaxRows = 5 },
                () => _store, startupDelay: TimeSpan.FromMilliseconds(50), interval: TimeSpan.FromHours(1));

            s.Start();
            var deadline = DateTime.UtcNow.AddSeconds(15);
            while (s.RunCount == 0 && DateTime.UtcNow < deadline)
                System.Threading.Thread.Sleep(50);

            Assert.Equal(1, s.RunCount);
            Assert.Equal(5, _store.Count(new LocalInspectionQuery()));
        }

        // ─── 설정 ───────────────────────────────────────────────

        [Theory]
        [InlineData(null, InspectionHistoryOptions.DefaultMaxRows)]            // 키 없음 → 기본
        [InlineData("500000", 500_000)]
        [InlineData("10", InspectionHistoryOptions.MinMaxRows)]                // 너무 작으면 하한
        [InlineData("900000000", InspectionHistoryOptions.MaxMaxRows)]         // 너무 크면 상한
        [InlineData("\"abc\"", InspectionHistoryOptions.DefaultMaxRows)]       // 숫자가 아니면 기본
        public void Options_MaxRows_ParsedAndClamped(string? json, int expected)
        {
            var path = Path.Combine(_tempDir, "system_config.json");
            var section = json == null
                ? "{ \"enabled\": true, \"retentionDays\": 30 }"
                : $"{{ \"enabled\": true, \"retentionDays\": 30, \"maxRows\": {json} }}";
            File.WriteAllText(path, $"{{ \"inspectionHistory\": {section} }}");

            var o = InspectionHistoryOptions.LoadFromFile(path);

            Assert.Equal(expected, o.MaxRows);
            Assert.Equal(30, o.RetentionDays);   // 다른 값은 그대로
        }

        [Fact]
        public void Options_NoFile_UsesDefaults()
        {
            var o = InspectionHistoryOptions.LoadFromFile(Path.Combine(_tempDir, "none.json"));
            Assert.True(o.Enabled);
            Assert.Equal(InspectionHistoryOptions.DefaultRetentionDays, o.RetentionDays);
            Assert.Equal(InspectionHistoryOptions.DefaultMaxRows, o.MaxRows);
        }
    }
}
