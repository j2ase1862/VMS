using System;

namespace VMS.Core.Models.Updates
{
    /// <summary>
    /// GitHub Releases 조회 결과 + 현재 버전 비교 정보.
    /// IUpdateService.CheckAsync 반환 값. 비교 결과가 명확하지 않은 경우(파싱 실패 등)
    /// 서비스는 null 반환 — 호출 측은 null 을 "정보 없음 / 통신 실패" 로 해석.
    /// </summary>
    public sealed class UpdateInfo
    {
        /// <summary>현재 실행 중인 어셈블리 버전 (예: 1.1.0).</summary>
        public Version CurrentVersion { get; init; } = new Version(0, 0, 0);

        /// <summary>GitHub Release 의 tag_name 에서 'v' 접두사 제거 후 파싱된 버전 (예: 1.1.0).</summary>
        public Version LatestVersion { get; init; } = new Version(0, 0, 0);

        /// <summary>tag_name 원본 (예: v1.1.0). UI 표시용.</summary>
        public string LatestTagName { get; init; } = string.Empty;

        /// <summary>LatestVersion &gt; CurrentVersion 여부. UI 배지 표시 조건.</summary>
        public bool IsUpdateAvailable => LatestVersion > CurrentVersion;

        /// <summary>MSI 자산 직접 다운로드 URL (없으면 string.Empty).</summary>
        public string DownloadUrl { get; init; } = string.Empty;

        /// <summary>MSI 자산 파일명 (예: VMS-1.8.0.msi). 다운로드 저장 파일명으로 사용.</summary>
        public string DownloadFileName { get; init; } = string.Empty;

        /// <summary>MSI 자산 크기(바이트). 진행률 표시용 — API 미제공 시 0.</summary>
        public long DownloadSizeBytes { get; init; }

        /// <summary>
        /// MSI 자산 SHA-256 (소문자 hex, "sha256:" 접두사 제거됨).
        /// GitHub asset digest 필드 유래 — 미제공 시 string.Empty (검증 생략).
        /// </summary>
        public string DownloadSha256 { get; init; } = string.Empty;

        /// <summary>Release HTML 페이지 URL — 브라우저로 열어 사용자에게 보여줄 URL.</summary>
        public string ReleaseUrl { get; init; } = string.Empty;

        /// <summary>릴리스 노트(GitHub body 필드, Markdown). 다이얼로그에 plain-text 로 표시.</summary>
        public string ReleaseNotes { get; init; } = string.Empty;

        /// <summary>릴리스 게시 시각(UTC). 표시용.</summary>
        public DateTime PublishedAt { get; init; }
    }
}
