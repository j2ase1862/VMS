using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using VMS.Services.LocalHistory;
using Xunit;

namespace VMS.Tests.Services
{
    /// <summary>
    /// 로컬 검사 이력 SQLite 저장소 — 큐→배치 기록 왕복, 이미지 경로 후속 갱신(순서 무관·NG 우선),
    /// 조회 필터, 일별 집계, NG 코드 집계, 보존 정리. 인스턴스별 임시 폴더로 격리.
    /// </summary>
    public class LocalInspectionHistoryStoreTests : IDisposable
    {
        private readonly string _tempDir;
        private readonly LocalInspectionHistoryStore _store;
        private static readonly TimeSpan FlushTimeout = TimeSpan.FromSeconds(10);

        public LocalInspectionHistoryStoreTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), $"insp_hist_{Guid.NewGuid():N}");
            _store = new LocalInspectionHistoryStore(_tempDir);
        }

        public void Dispose()
        {
            _store.Dispose();
            try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); }
            catch { /* SQLite 핸들 잔존 가능 — 무시 */ }
        }

        private static LocalInspectionEntry Entry(bool pass, string? key = null, DateTime? atUtc = null,
            string recipe = "R1", params string[] ngCodes) => new()
        {
            InspectedAtUtc = atUtc ?? DateTime.UtcNow,
            IsPass = pass,
            RecipeName = recipe,
            NgCodes = ngCodes.ToList(),
            CorrelationKey = key,
            ToolResults = new List<LocalToolResult>
            {
                new() { ToolName = "Blob 1", ToolType = "Blob", Success = pass, ExecutionTimeMs = 12.5, Message = pass ? null : "area < min" }
            },
            CycleTimeMs = 40,
            Mode = LocalInspectionMode.Cycle
        };

        [Fact]
        public void Record_ThenQuery_RoundTripsAllFields()
        {
            var at = new DateTime(2026, 9, 4, 1, 2, 3, 456, DateTimeKind.Utc);
            _store.Record(new LocalInspectionEntry
            {
                InspectedAtUtc = at, IsPass = false, RecipeId = 7, RecipeName = "Cap",
                NgCodes = new() { "Blob 1", "Edge 2" }, CorrelationKey = "k1",
                WorkOrderId = 11, LotId = 22, SerialNumber = "SN-1", CycleTimeMs = 123,
                Mode = LocalInspectionMode.Cycle,
                ToolResults = new() { new LocalToolResult { ToolName = "Blob 1", ToolType = "Blob", Success = false, ExecutionTimeMs = 3.2, Message = "x" } }
            });
            Assert.True(_store.Flush(FlushTimeout));

            var rows = _store.Query(new LocalInspectionQuery());
            var r = Assert.Single(rows);
            Assert.Equal(at, r.InspectedAtUtc);
            Assert.Equal(DateTimeKind.Utc, r.InspectedAtUtc.Kind);
            Assert.False(r.IsPass);
            Assert.Equal(7, r.RecipeId);
            Assert.Equal("Cap", r.RecipeName);
            Assert.Equal(new[] { "Blob 1", "Edge 2" }, r.NgCodes);
            Assert.Equal("k1", r.CorrelationKey);
            Assert.Equal(11, r.WorkOrderId);
            Assert.Equal(22, r.LotId);
            Assert.Equal("SN-1", r.SerialNumber);
            Assert.Equal(123, r.CycleTimeMs);
            Assert.Equal(LocalInspectionMode.Cycle, r.Mode);
            var t = Assert.Single(r.ToolResults);
            Assert.Equal("Blob 1", t.ToolName);
            Assert.False(t.Success);
            Assert.Equal("x", t.Message);
            Assert.True(File.Exists(_store.DbPath));
        }

        [Fact]
        public void ImagePath_ArrivingAfterInsert_IsAttached()
        {
            _store.Record(Entry(false, key: "k-after"));
            _store.SetImagePath("k-after", @"D:\img\NG\a.png", isNg: true);
            Assert.True(_store.Flush(FlushTimeout));

            var r = Assert.Single(_store.Query(new LocalInspectionQuery()));
            Assert.Equal(@"D:\img\NG\a.png", r.ImagePath);
            Assert.True(r.ImageIsNg);
        }

        [Fact]
        public void ImagePath_ArrivingBeforeInsert_IsHeldAndAttached()
        {
            // AUTO RUN: 이미지는 검사마다 저장되지만 행은 사이클 끝에 삽입된다.
            _store.SetImagePath("k-before", @"D:\img\OK\1.png", isNg: false);
            _store.SetImagePath("k-before", @"D:\img\NG\2.png", isNg: true);
            _store.SetImagePath("k-before", @"D:\img\OK\3.png", isNg: false);   // NG 를 덮지 않음
            _store.Record(Entry(false, key: "k-before"));
            Assert.True(_store.Flush(FlushTimeout));

            var r = Assert.Single(_store.Query(new LocalInspectionQuery()));
            Assert.Equal(@"D:\img\NG\2.png", r.ImagePath);
            Assert.True(r.ImageIsNg);
        }

        [Fact]
        public void ImagePath_NgReplacesOk_ButOkNeverReplacesNg()
        {
            _store.Record(Entry(false, key: "k-mix"));
            _store.SetImagePath("k-mix", "ok.png", isNg: false);
            _store.SetImagePath("k-mix", "ng.png", isNg: true);
            _store.SetImagePath("k-mix", "ok2.png", isNg: false);
            Assert.True(_store.Flush(FlushTimeout));

            var r = Assert.Single(_store.Query(new LocalInspectionQuery()));
            Assert.Equal("ng.png", r.ImagePath);
        }

        [Fact]
        public void Query_Filters_ByVerdict_Recipe_NgCode_AndRange()
        {
            var t0 = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
            _store.Record(Entry(true, atUtc: t0.AddHours(1), recipe: "A"));
            _store.Record(Entry(false, atUtc: t0.AddHours(2), recipe: "A", ngCodes: "Blob 1"));
            _store.Record(Entry(false, atUtc: t0.AddHours(3), recipe: "B", ngCodes: "Edge_2"));
            _store.Record(Entry(true, atUtc: t0.AddDays(5), recipe: "B"));
            Assert.True(_store.Flush(FlushTimeout));

            Assert.Equal(4, _store.Count(new LocalInspectionQuery()));
            Assert.Equal(2, _store.Count(new LocalInspectionQuery { IsPass = false }));
            Assert.Equal(2, _store.Count(new LocalInspectionQuery { RecipeName = "A" }));
            Assert.Equal(1, _store.Count(new LocalInspectionQuery { NgCodeContains = "Edge_2" }));
            Assert.Equal(0, _store.Count(new LocalInspectionQuery { NgCodeContains = "Edge%2" }));   // LIKE 와일드카드 이스케이프

            var ranged = _store.Query(new LocalInspectionQuery
            {
                FromLocal = t0.ToLocalTime(),
                ToLocalExclusive = t0.AddDays(1).ToLocalTime()
            });
            Assert.Equal(3, ranged.Count);
            // 최신 순
            Assert.True(ranged[0].InspectedAtUtc > ranged[1].InspectedAtUtc);

            var paged = _store.Query(new LocalInspectionQuery { Limit = 2, Offset = 2 });
            Assert.Equal(2, paged.Count);
        }

        [Fact]
        public void DailySummary_And_NgCodeCounts_Aggregate()
        {
            var day = new DateTime(2026, 9, 2, 12, 0, 0, DateTimeKind.Local);
            var utc = day.ToUniversalTime();
            _store.Record(Entry(true, atUtc: utc));
            _store.Record(Entry(false, atUtc: utc.AddMinutes(1), ngCodes: new[] { "Blob 1", "Edge 2" }));
            _store.Record(Entry(false, atUtc: utc.AddMinutes(2), ngCodes: "Blob 1"));
            _store.Record(Entry(true, atUtc: utc.AddDays(1)));
            Assert.True(_store.Flush(FlushTimeout));

            var summary = _store.GetDailySummary(day.Date, day.Date.AddDays(2));
            Assert.Equal(2, summary.Count);
            var d1 = summary[0];
            Assert.Equal(DateOnly.FromDateTime(day), d1.Date);
            Assert.Equal(3, d1.Total);
            Assert.Equal(1, d1.Pass);
            Assert.Equal(2, d1.Ng);
            Assert.Equal(33.3, d1.PassRate);

            var codes = _store.GetNgCodeCounts(day.Date, day.Date.AddDays(2), top: 10);
            Assert.Equal("Blob 1", codes[0].Code);
            Assert.Equal(2, codes[0].Count);
            Assert.Equal("Edge 2", codes[1].Code);
            Assert.Equal(1, codes[1].Count);
        }

        [Fact]
        public void PurgeOlderThan_DeletesOnlyExpiredRows_AndPreviewMatches()
        {
            var now = DateTime.UtcNow;
            _store.Record(Entry(true, atUtc: now.AddDays(-100)));
            _store.Record(Entry(true, atUtc: now.AddDays(-91)));
            _store.Record(Entry(true, atUtc: now.AddDays(-10)));
            _store.Record(Entry(true, atUtc: now));
            Assert.True(_store.Flush(FlushTimeout));

            Assert.Equal(2, _store.CountOlderThan(90));
            var preview = LocalInspectionHistoryStore.PreviewCleanup(_tempDir, 90);
            Assert.Equal(2, preview.FilesAffected);
            Assert.Equal(2, preview.FilesRemaining);
            Assert.NotNull(preview.OldestRemainingUtc);

            Assert.Equal(2, _store.PurgeOlderThan(90));
            Assert.Equal(2, _store.Count(new LocalInspectionQuery()));
            Assert.Equal(0, _store.CountOlderThan(90));
        }

        [Fact]
        public void PreviewCleanup_WithoutDbFile_DoesNotCreateFile()
        {
            var emptyDir = Path.Combine(Path.GetTempPath(), $"insp_hist_none_{Guid.NewGuid():N}");
            Directory.CreateDirectory(emptyDir);
            try
            {
                var p = LocalInspectionHistoryStore.PreviewCleanup(emptyDir, 30);
                Assert.Contains("없음", p.Note);
                Assert.False(File.Exists(Path.Combine(emptyDir, LocalInspectionHistoryStore.DbFileName)));
            }
            finally { Directory.Delete(emptyDir, true); }
        }

        [Fact]
        public void Record_AfterDispose_IsIgnored()
        {
            _store.Record(Entry(true));
            Assert.True(_store.Flush(FlushTimeout));
            _store.Dispose();
            _store.Record(Entry(true));   // 예외 없이 무시
            Assert.True(_store.Flush(FlushTimeout));
        }

        [Fact]
        public void ManyRecords_AreBatched_AndAllPersisted()
        {
            for (int i = 0; i < 1000; i++)
                _store.Record(Entry(i % 5 != 0, key: $"k{i}"));
            Assert.True(_store.Flush(FlushTimeout));
            Assert.Equal(1000, _store.Count(new LocalInspectionQuery()));
            Assert.Equal(200, _store.Count(new LocalInspectionQuery { IsPass = false }));
        }
    }
}
