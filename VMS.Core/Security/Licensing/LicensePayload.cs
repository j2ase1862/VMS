using System.Text.Json.Serialization;

namespace VMS.Core.Security.Licensing
{
    /// <summary>
    /// 라이선스 종류 — docs/design/license-spec.md §3.
    /// </summary>
    public enum LicenseKind
    {
        /// <summary>고객 발급분 — 지문 바인딩 필수.</summary>
        Production,
        /// <summary>사내용 (테스터/필드 엔지니어) — 지문 "*" 허용 대신 만료 필수.</summary>
        Internal,
        /// <summary>데모/평가 — 만료 필수.</summary>
        Trial
    }

    /// <summary>
    /// license.lic 의 서명 대상 본문. 날짜는 "yyyy-MM-dd" 문자열 (시간대 모호성 제거).
    /// 서명은 canonical JSON 전체에 걸리므로 구버전 검증기가 새 필드를 만나도 서명 자체는 유효하다
    /// (LicenseCanonicalJson 참조). 구버전 검증기는 schemaVersion != 1 을 거부하므로, 구버전이 무시해도
    /// 의미가 깨지지 않는 선택 필드(예: options)는 schemaVersion 을 올리지 않고 추가한다.
    /// </summary>
    public sealed class LicensePayload
    {
        [JsonPropertyName("schemaVersion")]
        public int SchemaVersion { get; init; } = 1;

        /// <summary>발급 대장 키 (예: LIC-2026-0001).</summary>
        [JsonPropertyName("licenseId")]
        public string LicenseId { get; init; } = string.Empty;

        [JsonPropertyName("customer")]
        public string Customer { get; init; } = string.Empty;

        /// <summary>Production | Internal | Trial.</summary>
        [JsonPropertyName("kind")]
        public string Kind { get; init; } = nameof(LicenseKind.Production);

        [JsonPropertyName("edition")]
        public string Edition { get; init; } = "Standard";

        /// <summary>예약 — 기능 게이팅은 후속 (spec §2).</summary>
        [JsonPropertyName("features")]
        public string[] Features { get; init; } = System.Array.Empty<string>();

        /// <summary>
        /// 확장 옵션 (<see cref="LicenseOptionKeys"/>). null = 지정 없음 → 전부 켬 — 이 필드가 생기기 전
        /// 발급분과 같은 의미다. 빈 배열 = 기본 구성 (확장 옵션 없음).
        /// schemaVersion 은 올리지 않는다: 구버전 검증기(VMS v1.47 이하 · Web 사본)는 모르는 필드를
        /// 서명에만 넣고 지나가며, 그쪽 동작(전부 켬)이 곧 옛 의미라 같은 파일을 양쪽에서 쓸 수 있다.
        /// </summary>
        [JsonPropertyName("options")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string[]? Options { get; init; }

        /// <summary>좌석 수. 단일 PC = 1.</summary>
        [JsonPropertyName("maxClients")]
        public int MaxClients { get; init; } = 1;

        /// <summary>바인딩 지문 (XXXXX-XXXXX-XXXXX). Internal 은 "*" 허용.</summary>
        [JsonPropertyName("fingerprint")]
        public string Fingerprint { get; init; } = string.Empty;

        [JsonPropertyName("issuedAt")]
        public string IssuedAt { get; init; } = string.Empty;

        /// <summary>유지보수 만료 — 이후 업데이트만 차단, 실행 유지 (영구+유지보수 모델).</summary>
        [JsonPropertyName("maintenanceUntil")]
        public string? MaintenanceUntil { get; init; }

        /// <summary>null=영구. 값 있으면 구독형 — 유예 후 실행 차단.</summary>
        [JsonPropertyName("expiresAt")]
        public string? ExpiresAt { get; init; }

        /// <summary>서명 키 식별자 — LicenseKeyring 에서 공개키 조회 (rotation 대비).</summary>
        [JsonPropertyName("keyId")]
        public string KeyId { get; init; } = string.Empty;
    }
}
