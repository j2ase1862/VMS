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
            // dev-2026: 사내 개발/테스트 발급용. 운영 키는 발급 절차 확정 시 별도 keyId 로 추가.
            ["dev-2026"] = "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEVZKMEOeoMzt1BJ+XUjT5HAa7LD1X7B2smRgOMBiiGVk4ffICrnWIkkDoANFPVEF8Dly2XCe4gWDcIerj7CGAcQ==",
        };
    }
}
