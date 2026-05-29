using System;
using System.IO;
using System.Linq;
using VMS.Core.Security;
using Xunit;

namespace VMS.Core.Tests.Security
{
    /// <summary>
    /// 임시 디렉토리에서 격리된 AuditLogger 인스턴스로 실제 JSONL 파일 쓰기/읽기/필터링/
    /// 일별 회전을 검증하는 통합 테스트. 싱글톤(Instance)이 아닌 internal ctor 사용.
    ///
    /// 각 테스트는 IDisposable 의 Dispose 에서 임시 디렉토리를 정리 — 테스트 간 격리 보장.
    /// </summary>
    public class AuditLoggerIntegrationTests : IDisposable
    {
        private readonly string _tempDir;
        private readonly AuditLogger _logger;

        public AuditLoggerIntegrationTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), $"audit_int_{Guid.NewGuid():N}");
            _logger = new AuditLogger(_tempDir);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_tempDir))
                    Directory.Delete(_tempDir, recursive: true);
            }
            catch
            {
                // 테스트 정리 실패는 무시 — 다른 테스트 영향 없음.
            }
        }

        // ─── 기본 Log → ReadDay round-trip ──────────────────────────

        [Fact]
        public void Log_WritesEntry_ReadableViaReadDay()
        {
            _logger.Log(
                AuditCategory.Authentication, "OperatorLogin", AuditOutcome.Success,
                userName: "EMP-001", source: "TestSrc", details: "RoundTrip");

            var entries = _logger.ReadDay(DateTime.Today);
            Assert.Single(entries);

            var e = entries[0];
            Assert.Equal(AuditCategory.Authentication, e.Category);
            Assert.Equal("OperatorLogin", e.Action);
            Assert.Equal(AuditOutcome.Success, e.Outcome);
            Assert.Equal("EMP-001", e.UserName);
            Assert.Equal("TestSrc", e.Source);
            Assert.Equal("RoundTrip", e.Details);
        }

        [Fact]
        public void Log_MultipleEntries_AllReadable_InOrder()
        {
            _logger.Log(AuditCategory.Authentication, "First", AuditOutcome.Success);
            _logger.Log(AuditCategory.Authentication, "Second", AuditOutcome.Failure);
            _logger.Log(AuditCategory.System, "Third", AuditOutcome.Denied);

            var entries = _logger.ReadDay(DateTime.Today);
            Assert.Equal(3, entries.Count);
            Assert.Equal("First", entries[0].Action);
            Assert.Equal("Second", entries[1].Action);
            Assert.Equal("Third", entries[2].Action);
        }

        [Fact]
        public void Log_EmptyOrWhitespaceAction_SkipsWrite()
        {
            _logger.Log(AuditCategory.System, "", AuditOutcome.Success);
            _logger.Log(AuditCategory.System, "   ", AuditOutcome.Success);
            _logger.Log(AuditCategory.System, "Valid", AuditOutcome.Success);

            var entries = _logger.ReadDay(DateTime.Today);
            Assert.Single(entries);
            Assert.Equal("Valid", entries[0].Action);
        }

        [Fact]
        public void Log_TruncatesLongAction_TruncatesLongDetails()
        {
            var longAction = new string('A', 300);   // 256 상한 초과
            var longDetails = new string('D', 3000); // 2048 상한 초과

            _logger.Log(AuditCategory.System, longAction, AuditOutcome.Success, details: longDetails);

            var entries = _logger.ReadDay(DateTime.Today);
            Assert.Single(entries);
            Assert.Equal(256, entries[0].Action.Length);
            Assert.Equal(2048, entries[0].Details?.Length);
        }

        // ─── ReadDay 빈 결과 ────────────────────────────────────

        [Fact]
        public void ReadDay_NoFile_ReturnsEmpty()
        {
            var entries = _logger.ReadDay(DateTime.Today.AddDays(-30));
            Assert.Empty(entries);
        }

        // ─── ReadRange 필터링 ──────────────────────────────────

        [Fact]
        public void ReadRange_FilterByCategory()
        {
            _logger.Log(AuditCategory.Authentication, "Auth1", AuditOutcome.Success);
            _logger.Log(AuditCategory.System, "Sys1", AuditOutcome.Success);
            _logger.Log(AuditCategory.Authentication, "Auth2", AuditOutcome.Failure);

            var auth = _logger.ReadRange(DateTime.Today, DateTime.Today,
                category: AuditCategory.Authentication);
            Assert.Equal(2, auth.Count);
            Assert.All(auth, e => Assert.Equal(AuditCategory.Authentication, e.Category));
        }

        [Fact]
        public void ReadRange_FilterByOutcome()
        {
            _logger.Log(AuditCategory.Authentication, "Login1", AuditOutcome.Success);
            _logger.Log(AuditCategory.Authentication, "Login2", AuditOutcome.Failure);
            _logger.Log(AuditCategory.Authentication, "Login3", AuditOutcome.Denied);

            var denied = _logger.ReadRange(DateTime.Today, DateTime.Today,
                outcome: AuditOutcome.Denied);
            Assert.Single(denied);
            Assert.Equal("Login3", denied[0].Action);
        }

        [Fact]
        public void ReadRange_FilterByCategoryAndOutcome()
        {
            _logger.Log(AuditCategory.Authentication, "A", AuditOutcome.Success);
            _logger.Log(AuditCategory.Authentication, "B", AuditOutcome.Failure);
            _logger.Log(AuditCategory.System, "C", AuditOutcome.Failure);

            var result = _logger.ReadRange(DateTime.Today, DateTime.Today,
                category: AuditCategory.Authentication, outcome: AuditOutcome.Failure);
            Assert.Single(result);
            Assert.Equal("B", result[0].Action);
        }

        [Fact]
        public void ReadRange_FilterBySearchText_MatchesAction()
        {
            _logger.Log(AuditCategory.Authentication, "LoginAttempt", AuditOutcome.Success);
            _logger.Log(AuditCategory.Authentication, "LogoutAction", AuditOutcome.Success);
            _logger.Log(AuditCategory.System, "WorkOrderSelected", AuditOutcome.Success);

            // case-insensitive substring 검색 — "login" 이 LoginAttempt 만 매치
            var result = _logger.ReadRange(DateTime.Today, DateTime.Today, searchText: "login");
            Assert.Single(result);
            Assert.Equal("LoginAttempt", result[0].Action);
        }

        [Fact]
        public void ReadRange_FilterBySearchText_MatchesUserOrSourceOrDetails()
        {
            _logger.Log(AuditCategory.Authentication, "Login", AuditOutcome.Success,
                userName: "EMP-001", source: "OperatorAuth", details: "Role=Operator");
            _logger.Log(AuditCategory.Authentication, "Login", AuditOutcome.Success,
                userName: "EMP-099", source: "OperatorAuth", details: "Role=Supervisor");

            // user 매치
            var byUser = _logger.ReadRange(DateTime.Today, DateTime.Today, searchText: "001");
            Assert.Single(byUser);

            // source 매치 (둘 다)
            var bySource = _logger.ReadRange(DateTime.Today, DateTime.Today, searchText: "OperatorAuth");
            Assert.Equal(2, bySource.Count);

            // details 매치
            var byDetails = _logger.ReadRange(DateTime.Today, DateTime.Today, searchText: "Supervisor");
            Assert.Single(byDetails);
        }

        [Fact]
        public void ReadRange_SwapsFromToWhenReversed()
        {
            _logger.Log(AuditCategory.System, "T1", AuditOutcome.Success);
            // from > to 인 호출도 정상 동작해야 — 내부에서 swap.
            var result = _logger.ReadRange(DateTime.Today.AddDays(1), DateTime.Today.AddDays(-1));
            Assert.Single(result);
        }

        // ─── 베스트-에포트 동작 검증 ─────────────────────────────

        [Fact]
        public void Log_WriteToInvalidDir_DoesNotThrow()
        {
            // 존재하지 않는 디렉토리 경로 — ctor 의 CreateDirectory 실패 또는 Append 실패 가능성.
            // 어쨌든 호출자에게 예외 전파 금지 (best-effort).
            // 잘못된 경로 (null character 등) 로 인스턴스화해도 예외 안 나는지.
            var invalidLogger = new AuditLogger(@"Z:\does\not\exist\nope");
            // Log 호출이 예외 던지면 안 됨 — best-effort.
            invalidLogger.Log(AuditCategory.System, "Test", AuditOutcome.Success);
        }

        // ─── 일별 회전 ─────────────────────────────────────────

        [Fact]
        public void Log_CreatesFilePerDate()
        {
            _logger.Log(AuditCategory.System, "Today", AuditOutcome.Success);

            // 오늘 일자 파일이 생겼는지 확인.
            var today = DateTime.UtcNow.Date;
            var path = Path.Combine(_tempDir, $"{today:yyyy-MM-dd}.jsonl");
            Assert.True(File.Exists(path));

            // JSONL 형식: 1 라인 = 1 entry.
            var lines = File.ReadAllLines(path);
            Assert.Single(lines);
            Assert.Contains("Today", lines[0]);
        }

        [Fact]
        public void Log_AppendsToExistingFile_NotOverwrite()
        {
            _logger.Log(AuditCategory.System, "First", AuditOutcome.Success);
            _logger.Log(AuditCategory.System, "Second", AuditOutcome.Success);

            var today = DateTime.UtcNow.Date;
            var path = Path.Combine(_tempDir, $"{today:yyyy-MM-dd}.jsonl");
            var lines = File.ReadAllLines(path);
            Assert.Equal(2, lines.Length);
        }

        // ─── ReadDay 손상된 줄 skip ─────────────────────────────

        [Fact]
        public void ReadDay_SkipsCorruptedLine_ContinuesRest()
        {
            // 정상 1개 + 손상 1줄 + 정상 1개 수동 작성.
            var today = DateTime.UtcNow.Date;
            var path = Path.Combine(_tempDir, $"{today:yyyy-MM-dd}.jsonl");
            File.WriteAllLines(path, new[]
            {
                "{\"timestamp\":\"2026-05-29T01:00:00Z\",\"category\":\"System\",\"action\":\"A\",\"outcome\":\"Success\"}",
                "{ malformed JSON",
                "{\"timestamp\":\"2026-05-29T01:00:01Z\",\"category\":\"System\",\"action\":\"B\",\"outcome\":\"Success\"}"
            });

            var entries = _logger.ReadDay(today);
            Assert.Equal(2, entries.Count);  // 손상 줄 skip
            Assert.Equal("A", entries[0].Action);
            Assert.Equal("B", entries[1].Action);
        }
    }
}
