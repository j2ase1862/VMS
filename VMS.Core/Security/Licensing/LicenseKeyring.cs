using System.Collections.Generic;

namespace VMS.Core.Security.Licensing
{
    /// <summary>
    /// 신뢰하는 서명 공개키 목록 — payload.keyId 로 조회.
    ///
    /// 키 유출 시 새 keyId 로 발급 전환(rotation)하고 여기 공개키를 추가한다 —
    /// 기존 발급분은 구 keyId 로 계속 유효 (spec §3). 개인키는 절대 리포에 두지 않는다.
    /// </summary>
    public static class LicenseKeyring
    {
        /// <summary>
        /// 제품에 내장되는 신뢰 키 목록. keyId → SubjectPublicKeyInfo base64 (P-256).
        /// </summary>
        public static readonly IReadOnlyDictionary<string, string> Default = new Dictionary<string, string>
        {
            // 키 용도 구분과 개인키 보관·백업·rotation 절차는 docs/license_operations.md 가 진실.
            // dev-2026: 사내 개발/테스트(Internal) 발급 전용.
            ["dev-2026"] = "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEVZKMEOeoMzt1BJ+XUjT5HAa7LD1X7B2smRgOMBiiGVk4ffICrnWIkkDoANFPVEF8Dly2XCe4gWDcIerj7CGAcQ==",
            // prod-2026: 고객(Production/Trial) 발급용 운영 키.
            ["prod-2026"] = "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEk7OHmhZOvIxrkt/Gf6jTpwxZUVgqKVuLtZVDGZf7AD4xadeu9lJ73yfVVUsHQt7ghMEvvPCHir0l7jE89MwsAw==",
        };
    }
}
