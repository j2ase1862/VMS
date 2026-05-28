using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VMS.Core.Security
{
    /// <summary>감사 로그 카테고리 — GS 인증 이력 추적성 분류.</summary>
    public enum AuditCategory
    {
        /// <summary>로그인 / 로그아웃 / 로그인 실패.</summary>
        Authentication,
        /// <summary>권한 부족 거부 / 역할 변경.</summary>
        Authorization,
        /// <summary>사용자 생성 / 삭제 / 변경 / 비밀번호 변경.</summary>
        UserManagement,
        /// <summary>레시피 로드 / 변경 / 저장.</summary>
        RecipeChange,
        /// <summary>시퀀스 시작 / 중지 / Reset.</summary>
        SequenceControl,
        /// <summary>검사 실행 / NG 발생.</summary>
        Inspection,
        /// <summary>시스템 설정 변경 (PLC / 카메라 / 보안 모드 등).</summary>
        Configuration,
        /// <summary>보안 정책 위반 (HTTP 사용 / cert 오류 / 비정상 입력).</summary>
        Security,
        /// <summary>분류 불명 시스템 이벤트.</summary>
        System
    }

    /// <summary>감사 이벤트 결과.</summary>
    public enum AuditOutcome
    {
        Success,
        Failure,
        Denied
    }

    /// <summary>감사 로그 1 항목 — JSONL 직렬화 대상.</summary>
    public sealed class AuditLogEntry
    {
        /// <summary>이벤트 발생 UTC 시각 (ISO 8601).</summary>
        [JsonPropertyName("timestamp")]
        public DateTime TimestampUtc { get; init; } = DateTime.UtcNow;

        [JsonPropertyName("category")]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public AuditCategory Category { get; init; }

        [JsonPropertyName("action")]
        public string Action { get; init; } = string.Empty;

        [JsonPropertyName("outcome")]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public AuditOutcome Outcome { get; init; } = AuditOutcome.Success;

        [JsonPropertyName("user")]
        public string? UserName { get; init; }

        [JsonPropertyName("source")]
        public string? Source { get; init; }

        [JsonPropertyName("details")]
        public string? Details { get; init; }
    }

    /// <summary>감사 로거 인터페이스 — 테스트 가능성 + 향후 SQLite 마이그레이션 여지.</summary>
    public interface IAuditLogger
    {
        void Log(
            AuditCategory category,
            string action,
            AuditOutcome outcome = AuditOutcome.Success,
            string? userName = null,
            string? source = null,
            string? details = null);
    }

    /// <summary>
    /// JSONL append-only 파일 기반 감사 로거 (싱글톤).
    ///
    /// 저장: %LocalAppData%\BODA VISION AI\audit\YYYY-MM-DD.jsonl — 일별 회전.
    /// 줄 단위 JSON 으로 SIEM/grep/jq 호환. append-only 라 변조 시도가 마지막 줄 이후로만 가능.
    ///
    /// 의도적으로 동기 I/O — 감사 로그는 손실 < 지연. 호출 빈도가 낮아 성능 영향 미미.
    /// 동시 쓰기는 lock 으로 직렬화.
    /// </summary>
    public sealed class AuditLogger : IAuditLogger
    {
        private static readonly Lazy<AuditLogger> _instance = new(() => new AuditLogger());
        public static AuditLogger Instance => _instance.Value;

        private readonly string _auditDir;
        private readonly object _writeLock = new();

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = false,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        private AuditLogger()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            _auditDir = Path.Combine(appData, "BODA VISION AI", "audit");
            try
            {
                Directory.CreateDirectory(_auditDir);
            }
            catch (Exception ex)
            {
                // 감사 로그 디렉토리 생성 실패 — 로깅은 best-effort, 앱 동작은 계속.
                Debug.WriteLine($"[AuditLogger] 디렉토리 생성 실패: {ex.Message}");
            }
        }

        public void Log(
            AuditCategory category,
            string action,
            AuditOutcome outcome = AuditOutcome.Success,
            string? userName = null,
            string? source = null,
            string? details = null)
        {
            if (string.IsNullOrWhiteSpace(action))
            {
                Debug.WriteLine("[AuditLogger] action 이 비어 있어 로그 skip.");
                return;
            }

            var entry = new AuditLogEntry
            {
                TimestampUtc = DateTime.UtcNow,
                Category = category,
                Action = action.Length > 256 ? action[..256] : action,
                Outcome = outcome,
                UserName = userName,
                Source = source,
                Details = details?.Length > 2048 ? details[..2048] : details
            };

            try
            {
                var json = JsonSerializer.Serialize(entry, JsonOptions);
                var line = json + Environment.NewLine;
                var path = Path.Combine(_auditDir, $"{entry.TimestampUtc:yyyy-MM-dd}.jsonl");

                lock (_writeLock)
                {
                    File.AppendAllText(path, line, Encoding.UTF8);
                }
            }
            catch (Exception ex)
            {
                // 감사 로그 쓰기 실패는 절대 호출자 흐름을 깨뜨리지 않음 — best-effort.
                Debug.WriteLine($"[AuditLogger] 쓰기 실패: {ex.Message}");
            }
        }

        /// <summary>
        /// 지정 일자(local date) 의 감사 로그를 줄 단위 JSON 으로 반환. 파일 없으면 빈 리스트.
        /// 향후 감사 조회 UI 또는 외부 도구 연동에 사용.
        /// </summary>
        public IReadOnlyList<AuditLogEntry> ReadDay(DateTime localDate)
        {
            var path = Path.Combine(_auditDir, $"{localDate:yyyy-MM-dd}.jsonl");
            if (!File.Exists(path)) return Array.Empty<AuditLogEntry>();

            var result = new List<AuditLogEntry>();
            try
            {
                foreach (var line in File.ReadLines(path))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    try
                    {
                        var entry = JsonSerializer.Deserialize<AuditLogEntry>(line, JsonOptions);
                        if (entry != null) result.Add(entry);
                    }
                    catch
                    {
                        // 손상된 줄은 skip — append-only 특성상 마지막 줄 truncation 가능.
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AuditLogger] 읽기 실패: {ex.Message}");
            }
            return result;
        }
    }
}
