namespace VMS.AppSetup.Interfaces
{
    /// <summary>MSI 동봉 로컬 Web 서버(BODA.VMS.Web)의 설치·구성 상태.</summary>
    public enum WebServerInstallState
    {
        /// <summary>이 PC 에 Web 서버 파일이 없음 (MSI 에서 Feature 제외 또는 원격 서버 구성).</summary>
        NotInstalled,
        /// <summary>파일은 있으나 초기 구성 전 — 앱은 구성 없이는 부팅을 거부한다.</summary>
        NotConfigured,
        /// <summary>appsettings.Production.json 존재 — 구성 완료.</summary>
        Configured,
    }

    /// <param name="ServiceStatus">Windows Service 상태 표시 문자열 ("Running" 등). 서비스 미등록이면 null.</param>
    public sealed record WebServerStatus(
        WebServerInstallState State,
        string? ServiceStatus,
        string WebInstallPath);

    public sealed record WebServerConfigureResult(bool Success, string? Error);

    /// <summary>
    /// MSI 가 등록만 해 둔(demand, 미시작) 로컬 Web 서버의 초기 구성을 담당.
    /// 구성 = admin 비밀번호 시드 + Jwt:Key 생성 → appsettings.Production.json 작성
    /// → 서비스 auto 전환 + 시작. Program Files 쓰기와 서비스 제어는 관리자 권한이
    /// 필요하므로 적용 단계는 AppSetup 자신을 UAC 상승 재실행해 수행한다.
    /// </summary>
    public interface IWebServerSetupService
    {
        WebServerStatus GetStatus();

        /// <summary>초기 구성 적용 (UAC 프롬프트 발생). 이미 구성된 경우 실패를 반환한다.</summary>
        Task<WebServerConfigureResult> ConfigureAsync(string adminPassword);

        /// <summary>
        /// 구성은 완료됐지만 서비스가 내려간 경우(마이그레이션 §13.4 / 수동 정지)의
        /// 서비스 시작 (UAC 프롬프트 발생). 자동시작(auto) 전환도 함께 보장한다.
        /// 이미 Running 이면 상승 없이 성공을 반환한다.
        /// </summary>
        Task<WebServerConfigureResult> StartServiceAsync();

        /// <summary>로컬 /health 응답 확인 (첫 부팅 마이그레이션 고려해 일정 시간 재시도).</summary>
        Task<bool> SmokeTestAsync();
    }
}
