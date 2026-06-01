using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using VMS.Core.Retention;
using VMS.Core.Security;
using Xunit;

namespace VMS.Core.Tests.Retention
{
    /// <summary>
    /// RetentionPreviewService 의 read-only dry-run 동작 검증.
    /// 모든 테스트는 파일이 절대 수정되지 않음을 확인 가능.
    /// </summary>
    public class RetentionPreviewServiceTests : IDisposable
    {
        private readonly string _tempBase;
        private readonly string _auditDir;
        private readonly string _backupDir;
        private readonly string _queueDir;

        public RetentionPreviewServiceTests()
        {
            _tempBase = Path.Combine(Path.GetTempPath(), $"preview_{Guid.NewGuid():N}");
            _auditDir = Path.Combine(_tempBase, "audit");
            _backupDir = Path.Combine(_tempBase, "backups");
            _queueDir = Path.Combine(_tempBase, "queue");
            Directory.CreateDirectory(_auditDir);
            Directory.CreateDirectory(_backupDir);
            Directory.CreateDirectory(_queueDir);
        }

        public void Dispose()
        {
            try { if (Directory.Exists(_tempBase)) Directory.Delete(_tempBase, recursive: true); }
            catch { /* 무시 */ }
        }

        private string MakeAuditFile(DateTime date)
        {
            var path = Path.Combine(_auditDir, $"{date:yyyy-MM-dd}.jsonl");
            File.WriteAllText(path, "{}\n");
            return path;
        }

        private string MakeQueueFile(DateTime utc)
        {
            var name = $"{utc:yyyyMMddHHmmssfff}_{Guid.NewGuid():N}.json";
            var path = Path.Combine(_queueDir, name);
            File.WriteAllText(path, "{}");
            return path;
        }

        private string MakeBackupFile(DateTime utc)
        {
            var name = $"auto-backup-{utc:yyyyMMdd-HHmmss}.zip";
            var path = Path.Combine(_backupDir, name);
            File.WriteAllText(path, "fake");
            return path;
        }

        private static string MakeLine(DateTime utc, AuditCategory cat)
        {
            return JsonSerializer.Serialize(new
            {
                timestamp = utc.ToString("o"),
                category = cat.ToString(),
                action = "x"
            });
        }

        // ─── AuditLog 전역 dry-run ────────────────────────────────

        [Fact]
        public void PreviewAuditLogCleanup_OldFile_ReportedAsAffected_NotDeleted()
        {
            var old = MakeAuditFile(DateTime.UtcNow.Date.AddDays(-500));
            var summary = RetentionPreviewService.PreviewAuditLogCleanup(_auditDir, 365);

            Assert.Equal(1, summary.FilesAffected);
            Assert.True(summary.BytesAffected > 0);
            Assert.True(File.Exists(old));  // dry-run — 미삭제
        }

        [Fact]
        public void PreviewAuditLogCleanup_TodayFile_Remaining()
        {
            MakeAuditFile(DateTime.UtcNow.Date);
            var summary = RetentionPreviewService.PreviewAuditLogCleanup(_auditDir, 7);
            Assert.Equal(0, summary.FilesAffected);
            Assert.Equal(1, summary.FilesRemaining);
        }

        [Fact]
        public void PreviewAuditLogCleanup_MixedAges_AccurateCounts()
        {
            MakeAuditFile(DateTime.UtcNow.Date.AddDays(-500));
            MakeAuditFile(DateTime.UtcNow.Date.AddDays(-100));
            MakeAuditFile(DateTime.UtcNow.Date.AddDays(-5));
            MakeAuditFile(DateTime.UtcNow.Date);

            var summary = RetentionPreviewService.PreviewAuditLogCleanup(_auditDir, 30);
            // -500 + -100 → affected, -5 + today → remaining
            Assert.Equal(2, summary.FilesAffected);
            Assert.Equal(2, summary.FilesRemaining);
        }

        [Fact]
        public void PreviewAuditLogCleanup_MissingDir_ReturnsNote()
        {
            var summary = RetentionPreviewService.PreviewAuditLogCleanup(
                Path.Combine(_tempBase, "missing"), 365);
            Assert.Equal(0, summary.FilesAffected);
            Assert.Contains("디렉토리 없음", summary.Note ?? "");
        }

        // ─── UploadQueue dry-run ─────────────────────────────────

        [Fact]
        public void PreviewUploadQueueCleanup_OldQueueFile_Reported()
        {
            var old = MakeQueueFile(DateTime.UtcNow.AddDays(-60));
            var summary = RetentionPreviewService.PreviewUploadQueueCleanup(_queueDir, 30);

            Assert.Equal(1, summary.FilesAffected);
            Assert.True(File.Exists(old));
        }

        [Fact]
        public void PreviewUploadQueueCleanup_PatternMismatch_Ignored()
        {
            // 패턴 외 파일은 affected/remaining 모두 카운트 안 됨.
            File.WriteAllText(Path.Combine(_queueDir, "user-file.json"), "{}");
            var summary = RetentionPreviewService.PreviewUploadQueueCleanup(_queueDir, 30);
            Assert.Equal(0, summary.FilesAffected);
            Assert.Equal(0, summary.FilesRemaining);
        }

        // ─── AutoBackup dry-run ──────────────────────────────────

        [Fact]
        public void PreviewAutoBackupCleanup_OldZip_Reported()
        {
            var old = MakeBackupFile(DateTime.UtcNow.AddDays(-90));
            var summary = RetentionPreviewService.PreviewAutoBackupCleanup(_backupDir, 30);

            Assert.Equal(1, summary.FilesAffected);
            Assert.True(File.Exists(old));
        }

        [Fact]
        public void PreviewAutoBackupCleanup_UserZipNotMatching_Ignored()
        {
            File.WriteAllText(Path.Combine(_backupDir, "my-backup.zip"), "x");
            var summary = RetentionPreviewService.PreviewAutoBackupCleanup(_backupDir, 1);
            Assert.Equal(0, summary.FilesAffected);
        }

        // ─── 카테고리 라인 필터 dry-run ───────────────────────────

        [Fact]
        public void PreviewAuditCategoryFilter_OldSystemLine_ReportedInCategory()
        {
            var ts = DateTime.UtcNow.AddDays(-100);
            var path = Path.Combine(_auditDir, "2026-01-01.jsonl");
            File.WriteAllText(path, MakeLine(ts, AuditCategory.System) + "\n");

            var preview = RetentionPreviewService.PreviewAuditCategoryFilter(
                _auditDir, AuditCategoryRetention.Defaults);

            Assert.Equal(1, preview.TotalLinesRemoved);
            Assert.Equal(1, preview.LinesRemovedByCategory[AuditCategory.System]);
            Assert.Equal(1, preview.FilesWithRemovals);
            Assert.Equal(1, preview.FilesEmptiedAndDeletable);

            // dry-run — 파일 미수정.
            Assert.True(File.Exists(path));
            Assert.Single(File.ReadAllLines(path));
        }

        [Fact]
        public void PreviewAuditCategoryFilter_MixedCategories_AccuratePerCategory()
        {
            var ts = DateTime.UtcNow.AddDays(-200);
            var path = Path.Combine(_auditDir, "2026-01-01.jsonl");
            File.WriteAllLines(path, new[]
            {
                MakeLine(ts, AuditCategory.System),
                MakeLine(ts, AuditCategory.Security),     // 1095일 — 유지
                MakeLine(ts, AuditCategory.Authentication) // 730일 — 유지
            }, Encoding.UTF8);

            var preview = RetentionPreviewService.PreviewAuditCategoryFilter(
                _auditDir, AuditCategoryRetention.Defaults);

            Assert.Equal(1, preview.TotalLinesRemoved);
            Assert.True(preview.LinesRemovedByCategory.ContainsKey(AuditCategory.System));
            Assert.False(preview.LinesRemovedByCategory.ContainsKey(AuditCategory.Security));
        }

        [Fact]
        public void PreviewAuditCategoryFilter_CorruptedLine_NotCounted()
        {
            var path = Path.Combine(_auditDir, "2026-01-01.jsonl");
            File.WriteAllLines(path, new[] { "this is not json" });

            var preview = RetentionPreviewService.PreviewAuditCategoryFilter(
                _auditDir, AuditCategoryRetention.Defaults);

            Assert.Equal(0, preview.TotalLinesRemoved);
        }

        [Fact]
        public void PreviewAuditCategoryFilter_EmptyDir_ReturnsZeroCounts()
        {
            var preview = RetentionPreviewService.PreviewAuditCategoryFilter(
                _auditDir, AuditCategoryRetention.Defaults);
            Assert.Equal(0, preview.TotalLinesRemoved);
            Assert.Equal(0, preview.FilesScanned);
        }

        [Fact]
        public void PreviewAuditCategoryFilter_AllLinesRemoved_FilesEmptied()
        {
            var ts = DateTime.UtcNow.AddDays(-200);
            var path = Path.Combine(_auditDir, "2026-01-01.jsonl");
            File.WriteAllLines(path, new[]
            {
                MakeLine(ts, AuditCategory.System),
                MakeLine(ts, AuditCategory.System)
            }, Encoding.UTF8);

            var preview = RetentionPreviewService.PreviewAuditCategoryFilter(
                _auditDir, AuditCategoryRetention.Defaults);

            Assert.Equal(2, preview.TotalLinesRemoved);
            Assert.Equal(1, preview.FilesEmptiedAndDeletable);
        }

        // ─── CSV export ──────────────────────────────────────────

        [Fact]
        public void ExportToCsv_HasHeader_AndMetaRow()
        {
            var path = Path.Combine(_tempBase, "out.csv");
            RetentionPreviewService.ExportToCsv(
                new List<RetentionPreviewSummary>(),
                new CategoryFilterPreview(),
                new Dictionary<string, int>(),
                path);

            var lines = File.ReadAllLines(path);
            Assert.Equal("Section,Policy,Metric,Value", lines[0]);
            Assert.Contains(lines, l => l.StartsWith("Meta,GeneratedUtc,"));
        }

        [Fact]
        public void ExportToCsv_WritesGlobalSummaryRows()
        {
            var summary = new RetentionPreviewSummary
            {
                PolicyName = "AuditLogRetention",
                FilesAffected = 5,
                BytesAffected = 1048576,
                FilesRemaining = 30,
                OldestRemainingUtc = new DateTime(2025, 12, 1, 0, 0, 0, DateTimeKind.Utc)
            };

            var path = Path.Combine(_tempBase, "out.csv");
            RetentionPreviewService.ExportToCsv(
                new[] { summary },
                new CategoryFilterPreview(),
                new Dictionary<string, int>(),
                path);

            var content = File.ReadAllText(path);
            Assert.Contains("GlobalSummary,AuditLogRetention,FilesAffected,5", content);
            Assert.Contains("GlobalSummary,AuditLogRetention,BytesAffected,1048576", content);
            Assert.Contains("GlobalSummary,AuditLogRetention,FilesRemaining,30", content);
            Assert.Contains("GlobalSummary,AuditLogRetention,OldestRemainingUtc,2025-12-01", content);
        }

        [Fact]
        public void ExportToCsv_WritesSettingsSnapshot()
        {
            var settings = new Dictionary<string, int>
            {
                ["AuditRetentionDays"] = 365,
                ["AutoBackupRetentionDays"] = 30,
                ["UploadQueueRetentionDays"] = 30
            };
            var path = Path.Combine(_tempBase, "out.csv");
            RetentionPreviewService.ExportToCsv(
                new List<RetentionPreviewSummary>(),
                new CategoryFilterPreview(),
                settings,
                path);

            var content = File.ReadAllText(path);
            Assert.Contains("Settings,AuditRetentionDays,Days,365", content);
            Assert.Contains("Settings,AutoBackupRetentionDays,Days,30", content);
            Assert.Contains("Settings,UploadQueueRetentionDays,Days,30", content);
        }

        [Fact]
        public void ExportToCsv_WritesCategoryRows_OrderedByCount()
        {
            var cat = new CategoryFilterPreview
            {
                FilesScanned = 5,
                FilesWithRemovals = 2,
                FilesEmptiedAndDeletable = 1,
                TotalLinesRemoved = 30,
                LinesRemovedByCategory = new Dictionary<AuditCategory, int>
                {
                    [AuditCategory.System] = 20,
                    [AuditCategory.Inspection] = 10
                }
            };
            var path = Path.Combine(_tempBase, "out.csv");
            RetentionPreviewService.ExportToCsv(
                new List<RetentionPreviewSummary>(),
                cat,
                new Dictionary<string, int>(),
                path);

            var lines = File.ReadAllLines(path);
            Assert.Contains(lines, l => l == "CategoryFilter,Total,FilesScanned,5");
            Assert.Contains(lines, l => l == "CategoryFilter,Total,LinesRemoved,30");

            // System (20) 이 Inspection (10) 보다 먼저 (DESC).
            var sysIdx = Array.FindIndex(lines, l => l == "CategoryFilter,System,LinesRemoved,20");
            var inspIdx = Array.FindIndex(lines, l => l == "CategoryFilter,Inspection,LinesRemoved,10");
            Assert.True(sysIdx >= 0 && inspIdx >= 0 && sysIdx < inspIdx);
        }

        [Fact]
        public void ExportToCsv_RFC4180Escape_CommaAndQuote()
        {
            var summary = new RetentionPreviewSummary
            {
                PolicyName = "Test,Policy",  // 콤마 포함
                Note = "value with \"quote\""  // 따옴표 포함
            };
            var path = Path.Combine(_tempBase, "out.csv");
            RetentionPreviewService.ExportToCsv(
                new[] { summary },
                new CategoryFilterPreview(),
                new Dictionary<string, int>(),
                path);

            var content = File.ReadAllText(path);
            Assert.Contains("\"Test,Policy\"", content);
            Assert.Contains("\"value with \"\"quote\"\"\"", content);
        }

        [Fact]
        public void ExportToCsv_NullCollections_NoThrow()
        {
            var path = Path.Combine(_tempBase, "out.csv");
            RetentionPreviewService.ExportToCsv(null!, null!, null!, path);

            var lines = File.ReadAllLines(path);
            Assert.Equal("Section,Policy,Metric,Value", lines[0]);
            Assert.Contains(lines, l => l.StartsWith("Meta,GeneratedUtc,"));
        }

        [Fact]
        public void ExportToCsv_EmptyOutputPath_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
                RetentionPreviewService.ExportToCsv(
                    new List<RetentionPreviewSummary>(),
                    new CategoryFilterPreview(),
                    new Dictionary<string, int>(),
                    ""));
        }

        // ─── 모든 메서드 read-only 확인 ───────────────────────────

        [Fact]
        public void AllPreviewMethods_AreNonDestructive()
        {
            MakeAuditFile(DateTime.UtcNow.Date.AddDays(-500));
            MakeQueueFile(DateTime.UtcNow.AddDays(-60));
            MakeBackupFile(DateTime.UtcNow.AddDays(-90));

            var beforeAudit = Directory.EnumerateFiles(_auditDir).Count();
            var beforeQueue = Directory.EnumerateFiles(_queueDir).Count();
            var beforeBackup = Directory.EnumerateFiles(_backupDir).Count();

            RetentionPreviewService.PreviewAuditLogCleanup(_auditDir, 30);
            RetentionPreviewService.PreviewUploadQueueCleanup(_queueDir, 7);
            RetentionPreviewService.PreviewAutoBackupCleanup(_backupDir, 7);
            RetentionPreviewService.PreviewAuditCategoryFilter(
                _auditDir, AuditCategoryRetention.Defaults);

            Assert.Equal(beforeAudit, Directory.EnumerateFiles(_auditDir).Count());
            Assert.Equal(beforeQueue, Directory.EnumerateFiles(_queueDir).Count());
            Assert.Equal(beforeBackup, Directory.EnumerateFiles(_backupDir).Count());
        }
    }
}
