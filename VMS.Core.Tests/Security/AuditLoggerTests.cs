using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using VMS.Core.Security;
using Xunit;

namespace VMS.Core.Tests.Security
{
    public class AuditLoggerTests
    {
        // AuditLogger 싱글톤 자체는 %LocalAppData% 하드코딩이라 단위 테스트에서 격리 불가.
        // 부작용 없는 static 메서드(ExportToCsv) 와 AuditLogEntry JSON 라운드트립만 검증.

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        // ─── AuditLogEntry JSON round-trip ─────────────────────────

        [Fact]
        public void Entry_JsonRoundTrip_PreservesAllFields()
        {
            var original = new AuditLogEntry
            {
                TimestampUtc = new DateTime(2026, 5, 28, 9, 30, 0, DateTimeKind.Utc),
                Category = AuditCategory.Authentication,
                Action = "OperatorLogin",
                Outcome = AuditOutcome.Success,
                UserName = "EMP-001",
                Source = "OperatorAuthService",
                Details = "Role=Operator"
            };

            var json = JsonSerializer.Serialize(original, JsonOptions);
            var restored = JsonSerializer.Deserialize<AuditLogEntry>(json, JsonOptions);

            Assert.NotNull(restored);
            Assert.Equal(original.TimestampUtc, restored!.TimestampUtc);
            Assert.Equal(original.Category, restored.Category);
            Assert.Equal(original.Action, restored.Action);
            Assert.Equal(original.Outcome, restored.Outcome);
            Assert.Equal(original.UserName, restored.UserName);
            Assert.Equal(original.Source, restored.Source);
            Assert.Equal(original.Details, restored.Details);
        }

        [Fact]
        public void Entry_JsonSerialization_NullsOmitted()
        {
            var entry = new AuditLogEntry
            {
                Category = AuditCategory.System,
                Action = "Test",
                Outcome = AuditOutcome.Success
                // UserName / Source / Details = null
            };
            var json = JsonSerializer.Serialize(entry, JsonOptions);

            // null 필드는 JSON 에서 생략 — 가독성 + 디스크 사용량.
            Assert.DoesNotContain("\"user\":", json);
            Assert.DoesNotContain("\"source\":", json);
            Assert.DoesNotContain("\"details\":", json);
        }

        [Fact]
        public void Entry_EnumSerialized_AsString()
        {
            var entry = new AuditLogEntry
            {
                Category = AuditCategory.Inspection,
                Action = "Test",
                Outcome = AuditOutcome.Denied
            };
            var json = JsonSerializer.Serialize(entry, JsonOptions);

            // JsonStringEnumConverter 적용 — 사람이 읽을 수 있는 카테고리/outcome 명.
            Assert.Contains("\"Inspection\"", json);
            Assert.Contains("\"Denied\"", json);
            // 정수형 enum 시리얼라이즈 패턴이 아님을 검증.
            Assert.DoesNotContain("\"category\":5", json);
        }

        // ─── ExportToCsv ────────────────────────────────────────

        [Fact]
        public void ExportToCsv_WritesHeader()
        {
            var path = NewTempPath();
            try
            {
                AuditLogger.ExportToCsv(Array.Empty<AuditLogEntry>(), path);
                var lines = File.ReadAllLines(path);
                Assert.Single(lines);
                Assert.Equal("Timestamp(UTC),Category,Action,Outcome,User,Source,Details", lines[0]);
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public void ExportToCsv_WritesAllEntries()
        {
            var entries = new List<AuditLogEntry>
            {
                new() { Category = AuditCategory.Authentication, Action = "Login", Outcome = AuditOutcome.Success, UserName = "user1" },
                new() { Category = AuditCategory.Inspection, Action = "NG", Outcome = AuditOutcome.Failure },
            };
            var path = NewTempPath();
            try
            {
                AuditLogger.ExportToCsv(entries, path);
                var lines = File.ReadAllLines(path);
                Assert.Equal(3, lines.Length);  // header + 2 entries
                Assert.Contains("Login", lines[1]);
                Assert.Contains("user1", lines[1]);
                Assert.Contains("NG", lines[2]);
                Assert.Contains("Failure", lines[2]);
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public void ExportToCsv_EscapesCommaInDetails()
        {
            var entries = new[]
            {
                new AuditLogEntry
                {
                    Category = AuditCategory.RecipeChange,
                    Action = "Save",
                    Outcome = AuditOutcome.Success,
                    Details = "Id=1, Name='test'"
                }
            };
            var path = NewTempPath();
            try
            {
                AuditLogger.ExportToCsv(entries, path);
                var lines = File.ReadAllLines(path);
                // 콤마 포함 필드는 따옴표로 감싸야 RFC 4180 준수.
                Assert.Contains("\"Id=1, Name='test'\"", lines[1]);
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public void ExportToCsv_EscapesQuoteInDetails()
        {
            var entries = new[]
            {
                new AuditLogEntry
                {
                    Category = AuditCategory.System,
                    Action = "Test",
                    Outcome = AuditOutcome.Success,
                    Details = "He said \"hello\""
                }
            };
            var path = NewTempPath();
            try
            {
                AuditLogger.ExportToCsv(entries, path);
                var lines = File.ReadAllLines(path);
                // 내부 따옴표는 두 개로 escape: He said ""hello"" → "He said """"hello"""".
                Assert.Contains("\"He said \"\"hello\"\"\"", lines[1]);
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public void ExportToCsv_EscapesNewlineInDetails()
        {
            var entries = new[]
            {
                new AuditLogEntry
                {
                    Category = AuditCategory.System,
                    Action = "Multi",
                    Outcome = AuditOutcome.Success,
                    Details = "Line1\nLine2"
                }
            };
            var path = NewTempPath();
            try
            {
                AuditLogger.ExportToCsv(entries, path);
                var content = File.ReadAllText(path);
                // 개행이 포함된 필드는 따옴표로 감싸야 함 — 그래야 다음 행과 구분 가능.
                Assert.Contains("\"Line1\nLine2\"", content);
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public void ExportToCsv_NullDetails_WritesEmpty()
        {
            var entries = new[]
            {
                new AuditLogEntry
                {
                    Category = AuditCategory.System,
                    Action = "Test",
                    Outcome = AuditOutcome.Success
                    // UserName / Source / Details = null
                }
            };
            var path = NewTempPath();
            try
            {
                AuditLogger.ExportToCsv(entries, path);
                var lines = File.ReadAllLines(path);
                // null 필드는 빈 문자열로 — 끝부분 ',,,' 패턴.
                Assert.EndsWith(",,,", lines[1]);
            }
            finally { File.Delete(path); }
        }

        private static string NewTempPath()
            => Path.Combine(Path.GetTempPath(), $"audit_test_{Guid.NewGuid():N}.csv");
    }
}
