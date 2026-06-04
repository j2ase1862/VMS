namespace VMS.Models
{
    public class User
    {
        public int UserId { get; set; }
        public string Username { get; set; } = string.Empty;
        public string PasswordHash { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public UserGrade Grade { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? LastLoginAt { get; set; }

        /// <summary>
        /// 비상 local-admin 폴백 세션 표시 (SSO Migration Plan §2.3, SSO PR3).
        /// true 면 HasPermission 이 제한된 권한 집합만 허용:
        /// - 시스템 진단 / 키오스크 운영 (StartStop, ViewStatistics, RestartWebService)
        /// - 운영 데이터 변경 (ManageUsers, EditRecipe, SystemConfiguration 등) 모두 거부
        ///
        /// AuthenticateViaWebAsync 경로는 항상 false (Web 인증 사용자).
        /// 일반 로컬 Authenticate 는 username == LocalFallbackUsername 일 때만 true.
        /// </summary>
        public bool IsLocalFallback { get; set; }
    }
}
