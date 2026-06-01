using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using VMS.Core.Security;
using Xunit;

namespace VMS.Core.Tests.Security
{
    /// <summary>
    /// AuditCategoryRetention 의 카테고리별 차등 필터 동작 검증.
    /// 임시 디렉토리 격리.
    /// </summary>
    public class AuditCategoryRetentionTests : IDisposable
    {
        private readonly string _tempDir;

        public AuditCategoryRetentionTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), $"cat_retention_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); }
            catch { /* 무시 */ }
        }

        private static string MakeLine(DateTime utc, AuditCategory cat, string action = "X")
        {
            var entry = new
            {
                timestamp = utc.ToString("o"),
                category = cat.ToString(),
                action,
                outcome = "Success"
            };
            return JsonSerializer.Serialize(entry);
        }

        private string WriteFile(DateTime date, params string[] lines)
        {
            var path = Path.Combine(_tempDir, $"{date:yyyy-MM-dd}.jsonl");
            File.WriteAllLines(path, lines, Encoding.UTF8);
            return path;
        }

        // ─── Defaults / Clamp ────────────────────────────────────

        [Fact]
        public void Defaults_AllNineCategoriesPresent()
        {
            Assert.Equal(9, AuditCategoryRetention.Defaults.Count);
            foreach (AuditCategory cat in Enum.GetValues(typeof(AuditCategory)))
                Assert.True(AuditCategoryRetention.Defaults.ContainsKey(cat));
        }

        [Fact]
        public void Defaults_SystemShortest()
        {
            // System 카테고리가 가장 짧아야 함 (GS 권장).
            Assert.Equal(90, AuditCategoryRetention.Defaults[AuditCategory.System]);
        }

        [Fact]
        public void Defaults_SecurityAndUserMgmtLongest()
        {
            Assert.Equal(1095, AuditCategoryRetention.Defaults[AuditCategory.Security]);
            Assert.Equal(1095, AuditCategoryRetention.Defaults[AuditCategory.UserManagement]);
            Assert.Equal(1095, AuditCategoryRetention.Defaults[AuditCategory.Configuration]);
        }

        // ─── Per-category 필터 — boundary ────────────────────────

        [Fact]
        public void FilterFilesByCategory_SystemEntry90Days_DroppedAt100()
        {
            // System 기본 90일 → 100일 전 라인 제거.
            var oldUtc = DateTime.UtcNow.AddDays(-100);
            var line = MakeLine(oldUtc, AuditCategory.System);
            var path = WriteFile(oldUtc, line);

            var removed = AuditCategoryRetention.FilterFilesByCategory(
                _tempDir, AuditCategoryRetention.Defaults);

            Assert.Equal(1, removed);
            Assert.False(File.Exists(path));  // 모든 라인 제거 → 파일 삭제
        }

        [Fact]
        public void FilterFilesByCategory_SecurityEntry1000Days_Kept()
        {
            // Security 1095일 → 1000일 전 라인 유지.
            var ts = DateTime.UtcNow.AddDays(-1000);
            var line = MakeLine(ts, AuditCategory.Security);
            var path = WriteFile(ts, line);

            var removed = AuditCategoryRetention.FilterFilesByCategory(
                _tempDir, AuditCategoryRetention.Defaults);

            Assert.Equal(0, removed);
            Assert.True(File.Exists(path));
        }

        [Fact]
        public void FilterFilesByCategory_MixedCategoriesInOneFile_PartialRemoval()
        {
            // 같은 날 파일에 System(90일) + Security(1095일) 라인 — 둘 다 200일 전.
            // System → 제거, Security → 유지. 파일은 rewrite.
            var ts = DateTime.UtcNow.AddDays(-200);
            var path = WriteFile(ts,
                MakeLine(ts, AuditCategory.System, "old-sys"),
                MakeLine(ts, AuditCategory.Security, "old-sec"));

            var removed = AuditCategoryRetention.FilterFilesByCategory(
                _tempDir, AuditCategoryRetention.Defaults);

            Assert.Equal(1, removed);
            Assert.True(File.Exists(path));
            var remaining = File.ReadAllLines(path);
            Assert.Single(remaining);
            Assert.Contains("old-sec", remaining[0]);
        }

        // ─── Override ─────────────────────────────────────────────

        [Fact]
        public void FilterFilesByCategory_OverrideMakesShorter_RemovesMore()
        {
            // Security 기본 1095일 → override 로 30일 → 100일 전 라인이 제거됨.
            var ts = DateTime.UtcNow.AddDays(-100);
            var line = MakeLine(ts, AuditCategory.Security);
            var path = WriteFile(ts, line);

            var perCat = new System.Collections.Generic.Dictionary<AuditCategory, int>(
                AuditCategoryRetention.Defaults)
            {
                [AuditCategory.Security] = 30
            };

            var removed = AuditCategoryRetention.FilterFilesByCategory(_tempDir, perCat);

            Assert.Equal(1, removed);
            Assert.False(File.Exists(path));
        }

        // ─── 안전성 — parse 실패 / timestamp 누락 ──────────────

        [Fact]
        public void FilterFilesByCategory_CorruptedLine_Kept()
        {
            // 파싱 안 되는 라인은 keep (silent data loss 방지).
            var path = Path.Combine(_tempDir, "2026-01-01.jsonl");
            File.WriteAllLines(path, new[] { "this is not json" });

            AuditCategoryRetention.FilterFilesByCategory(
                _tempDir, AuditCategoryRetention.Defaults);

            Assert.True(File.Exists(path));
            var lines = File.ReadAllLines(path);
            Assert.Single(lines);
        }

        [Fact]
        public void FilterFilesByCategory_NoTimestamp_Kept()
        {
            // timestamp 필드 없으면 keep.
            var path = Path.Combine(_tempDir, "2026-01-01.jsonl");
            File.WriteAllText(path, "{\"category\":\"System\",\"action\":\"x\"}\n");

            AuditCategoryRetention.FilterFilesByCategory(
                _tempDir, AuditCategoryRetention.Defaults);

            Assert.True(File.Exists(path));
        }

        [Fact]
        public void FilterFilesByCategory_UnknownCategory_FallsBackToDefault()
        {
            // category 가 enum 외 값이면 System 기본 (90일) 적용.
            var ts = DateTime.UtcNow.AddDays(-100);
            var entry = $"{{\"timestamp\":\"{ts:o}\",\"category\":\"NotARealCategory\",\"action\":\"x\"}}";
            var path = Path.Combine(_tempDir, "2026-01-01.jsonl");
            File.WriteAllText(path, entry + "\n");

            var removed = AuditCategoryRetention.FilterFilesByCategory(
                _tempDir, AuditCategoryRetention.Defaults);

            Assert.Equal(1, removed);
        }

        // ─── Edge cases ───────────────────────────────────────────

        [Fact]
        public void FilterFilesByCategory_EmptyDir_NoThrow()
        {
            var removed = AuditCategoryRetention.FilterFilesByCategory(
                _tempDir, AuditCategoryRetention.Defaults);
            Assert.Equal(0, removed);
        }

        [Fact]
        public void FilterFilesByCategory_NullOrMissingDir_ReturnsZero()
        {
            Assert.Equal(0, AuditCategoryRetention.FilterFilesByCategory(
                null!, AuditCategoryRetention.Defaults));
            Assert.Equal(0, AuditCategoryRetention.FilterFilesByCategory(
                "", AuditCategoryRetention.Defaults));
            Assert.Equal(0, AuditCategoryRetention.FilterFilesByCategory(
                Path.Combine(_tempDir, "nonexistent"), AuditCategoryRetention.Defaults));
        }

        [Fact]
        public void FilterFilesByCategory_AllLinesKept_NoRewrite()
        {
            // 모두 유지되는 케이스 — 파일 modified time 변경 없음 확인은 어렵지만
            // 적어도 라인 수와 파일 존재는 그대로.
            var ts = DateTime.UtcNow.AddDays(-10);
            var path = WriteFile(ts,
                MakeLine(ts, AuditCategory.Inspection),
                MakeLine(ts, AuditCategory.Configuration));

            AuditCategoryRetention.FilterFilesByCategory(
                _tempDir, AuditCategoryRetention.Defaults);

            Assert.True(File.Exists(path));
            Assert.Equal(2, File.ReadAllLines(path).Length);
        }

        // ─── LoadDaysFromAppData ──────────────────────────────────

        [Fact]
        public void LoadDaysFromAppData_NoOverride_ReturnsDefaults()
        {
            var loaded = AuditCategoryRetention.LoadDaysFromAppData();
            Assert.Equal(AuditCategoryRetention.Defaults.Count, loaded.Count);
        }

        // ─── 정리 후 라인 시각 검증 ───────────────────────────────

        [Fact]
        public void FilterFilesByCategory_TodayLine_AlwaysKept_RegardlessOfCategory()
        {
            // 오늘 추가된 라인은 어떤 카테고리든 유지 (UtcNow - 0d ≥ threshold).
            var ts = DateTime.UtcNow.AddMinutes(-1);
            var path = WriteFile(DateTime.UtcNow.Date,
                MakeLine(ts, AuditCategory.System));

            var removed = AuditCategoryRetention.FilterFilesByCategory(
                _tempDir, AuditCategoryRetention.Defaults);

            Assert.Equal(0, removed);
            Assert.True(File.Exists(path));
        }
    }
}
