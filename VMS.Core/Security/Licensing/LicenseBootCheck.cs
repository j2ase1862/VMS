using System;
using System.Diagnostics;

namespace VMS.Core.Security.Licensing
{
    /// <summary>
    /// 앱 기동 시 라이선스 1회 평가 — 전환기(호환 모드)에서는 어떤 상태여도 차단하지 않고
    /// 감사로그 + StartupHealthCheck 로 표면화만 한다 (spec §9 1단계).
    /// 강제 모드 전환 시 App.xaml.cs 호출부에서 IsBlocking 분기를 활성화한다.
    /// </summary>
    public static class LicenseBootCheck
    {
        /// <summary>마지막 Run 결과 — StartupHealthCheck / UI 상태 표시가 참조.</summary>
        public static LicenseEvaluation? Current { get; private set; }

        /// <summary>
        /// 기본 경로 + 실제 머신 지문 + 오늘 날짜로 평가 후 감사 기록.
        /// 어떤 예외도 전파하지 않는다 — 라이선스 검사가 기동을 깨서는 안 됨.
        /// </summary>
        public static LicenseEvaluation Run()
        {
            LicenseEvaluation result;
            try
            {
                result = Evaluate(
                    licensePath: null,
                    machineFingerprint: MachineFingerprint.GetCode(),
                    today: DateOnly.FromDateTime(DateTime.Now));
            }
            catch (Exception ex)
            {
                result = new LicenseEvaluation
                {
                    Status = LicenseStatus.Invalid,
                    Message = $"라이선스 검사 자체 실패: {ex.GetType().Name} {ex.Message}"
                };
            }

            Current = result;

            try
            {
                // 차단류도 전환기에는 Success 로 기록 — 운영 실패가 아니라 도입기 정보성 이벤트.
                // Failure 는 서명 불일치(변조 의심)에만 사용해 감사 리뷰에서 눈에 띄게 한다.
                var outcome = result.Status == LicenseStatus.Invalid
                    ? AuditOutcome.Failure
                    : AuditOutcome.Success;
                AuditLogger.Instance.Log(
                    AuditCategory.System, "LicenseCheck", outcome,
                    source: nameof(LicenseBootCheck),
                    details: $"Status={result.Status}, {result.Message}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LicenseBootCheck] 감사 로그 실패: {ex.Message}");
            }

            return result;
        }

        /// <summary>경로/지문/날짜 주입 평가 — 테스트 및 헬스체크 격리용 (감사 기록 없음).</summary>
        public static LicenseEvaluation Evaluate(string? licensePath, string machineFingerprint, DateOnly today,
            string? leaseCachePath = null, DateTime? utcNow = null)
        {
            var (json, error) = LicenseFileStore.TryRead(licensePath);
            if (error != null)
            {
                return new LicenseEvaluation
                {
                    Status = LicenseStatus.Invalid,
                    Message = $"license.lic 읽기 실패: {error}"
                };
            }
            if (json == null)
            {
                // 로컬 파일 없음 → Web 서버 좌석 임대 캐시 폴백 (spec §5b) —
                // 서버-다수 클라이언트 구성의 클라이언트 PC 는 이 경로가 정상 상태다.
                var lease = SeatLeaseCache.TryLoad(leaseCachePath);
                var now = utcNow ?? DateTime.UtcNow;
                if (lease != null && lease.IsValidAt(now))
                {
                    var capacity = lease.OverCapacity
                        ? $" ⚠ 좌석 초과 ({lease.SeatsUsed}/{lease.MaxClients}) — 계약 좌석 확인 필요"
                        : $" (좌석 {lease.SeatsUsed}/{lease.MaxClients})";
                    return new LicenseEvaluation
                    {
                        Status = LicenseStatus.SeatLeased,
                        Message = $"서버 좌석 임대 — {lease.Customer} ({lease.LicenseId}), " +
                                  $"서버 미도달 시 {lease.ValidUntilUtc.ToLocalTime():yyyy-MM-dd HH:mm} 까지 유예{capacity}"
                    };
                }

                var leaseHint = lease != null
                    ? $" (좌석 임대 유예 만료: {lease.ValidUntilUtc.ToLocalTime():yyyy-MM-dd HH:mm} — Web 서버 연결 확인 필요)"
                    : string.Empty;
                return new LicenseEvaluation
                {
                    Status = LicenseStatus.Missing,
                    Message = $"license.lic 없음 ({licensePath ?? LicenseFileStore.DefaultPath}) — " +
                              $"전환기 호환 모드로 동작, 라이선스 발급/설치 필요{leaseHint}"
                };
            }

            return LicenseValidator.Validate(json, machineFingerprint, today);
        }
    }
}
