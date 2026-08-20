namespace VMS.Core.Models.Updates
{
    /// <summary>
    /// 인앱 업데이트 다운로드 진행 스냅샷. IProgress&lt;T&gt; 로 UI 스레드에 전달된다.
    /// TotalBytes 가 0 이면(서버가 크기 미제공) Percent 는 0 — UI 는 불확정 진행바로 표시.
    /// </summary>
    public readonly record struct UpdateDownloadProgress(long BytesReceived, long TotalBytes)
    {
        public double Percent => TotalBytes > 0
            ? (double)BytesReceived / TotalBytes * 100.0
            : 0.0;
    }

    /// <summary>
    /// 다운로드 결과. Ok=true 면 FilePath 에 검증 완료된 MSI 경로.
    /// 실패 시 Error 에 사용자 표시용 메시지 (해시 불일치·네트워크 오류 등).
    /// </summary>
    public sealed record UpdateDownloadResult(bool Ok, string? FilePath, string? Error)
    {
        public static UpdateDownloadResult Success(string filePath) => new(true, filePath, null);
        public static UpdateDownloadResult Failure(string error) => new(false, null, error);
    }

    /// <summary>
    /// 설치 부트스트래퍼 실행 결과. UacDeclined 는 사용자가 관리자 승인을 거부한 경우 —
    /// 오류가 아닌 취소로 안내해야 한다.
    /// </summary>
    public sealed record UpdateInstallLaunchResult(bool Ok, bool UacDeclined, string? Error)
    {
        public static UpdateInstallLaunchResult Success() => new(true, false, null);
        public static UpdateInstallLaunchResult Declined() => new(false, true, null);
        public static UpdateInstallLaunchResult Failure(string error) => new(false, false, error);
    }
}
