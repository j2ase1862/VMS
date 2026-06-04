namespace VMS.Models
{
    public enum UserPermission
    {
        StartStop,
        ViewStatistics,
        EditRecipe,
        CameraSettings,
        LaunchVisionSetup,
        LaunchAppSetup,
        ManageUsers,
        DeleteRecipe,
        SystemConfiguration,

        /// <summary>
        /// Web Service 재시작 명령 — 비상 local-admin 폴백 세션이 Web 다운 시
        /// 복구 액션으로 사용 (SSO Migration Plan §2.3, SSO PR3).
        /// </summary>
        RestartWebService
    }
}
