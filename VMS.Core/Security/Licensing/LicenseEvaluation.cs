namespace VMS.Core.Security.Licensing
{
    /// <summary>
    /// 라이선스 상태 — spec §5 상태 전이표. 심각도 오름차순 정렬
    /// (Valid < 경고류 < 차단류 — UI/헬스체크가 비교 연산으로 분류 가능).
    /// </summary>
    public enum LicenseStatus
    {
        /// <summary>정상 (로컬 license.lic).</summary>
        Valid,
        /// <summary>Web 서버 좌석 임대로 유효 (spec §5b) — 서버 라이선스의 좌석을 사용 중.
        /// 서버 미도달 시 임대 캐시 만료 시각까지 유예 운전.</summary>
        SeatLeased,
        /// <summary>유지보수 만료 30일 전 — 비차단 경고.</summary>
        MaintenanceExpiring,
        /// <summary>유지보수 만료 — 실행 유지, 업데이트만 차단 대상.</summary>
        MaintenanceExpired,
        /// <summary>구독 만료 30일 전 — 비차단 경고.</summary>
        Expiring,
        /// <summary>구독 만료 후 유예 14일 이내 — 매 기동 경고.</summary>
        ExpiredGrace,
        /// <summary>구독 만료 + 유예 경과 — 강제 모드에서 기동 차단.</summary>
        Expired,
        /// <summary>지문 2/3 미달 — 재발급 필요.</summary>
        FingerprintMismatch,
        /// <summary>서명/형식 불일치.</summary>
        Invalid,
        /// <summary>license.lic 없음 — 전환기 호환 모드에서는 경고만 (spec §9).</summary>
        Missing
    }

    /// <summary>검증 + 상태 평가 결과.</summary>
    public sealed class LicenseEvaluation
    {
        public LicenseStatus Status { get; init; }
        /// <summary>서명이 유효한 경우에만 non-null (Invalid/Missing 은 null).</summary>
        public LicensePayload? Payload { get; init; }
        /// <summary>사람이 읽는 사유 — 감사로그/헬스체크 메시지.</summary>
        public string Message { get; init; } = string.Empty;

        /// <summary>강제 모드에서 기동을 차단해야 하는 상태인가 (전환기에는 경고로 강등).</summary>
        public bool IsBlocking => Status is LicenseStatus.Expired
                                           or LicenseStatus.FingerprintMismatch
                                           or LicenseStatus.Invalid
                                           or LicenseStatus.Missing;

        /// <summary>정상(로컬 유효/좌석 임대) 외 모든 상태 — 운영자에게 표면화할 대상.</summary>
        public bool NeedsAttention => Status is not (LicenseStatus.Valid or LicenseStatus.SeatLeased);
    }
}
