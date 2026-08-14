using VMS.Interfaces;
using VMS.Models;

namespace VMS.Services
{
    /// <summary>
    /// 검사 시작/정지(AUTO RUN · Grab · Live) 의 시스템 사용자 권한 판정.
    ///
    /// 규칙 (사용자 매뉴얼 §3.5): <b>시스템 사용자가 로그인하지 않았으면 허용</b>하고,
    /// 로그인한 경우에만 등급 권한을 적용한다.
    ///
    /// 근거 — VMS 는 신원 체계가 둘이다:
    ///   • 시스템 사용자(Admin/Engineer/Operator 등급) = 설정·레시피 편집용
    ///   • 작업자 계정(사번+PIN 키오스크) = 생산 신원. 검사 이력의 OperatorId 가 이것.
    /// 생산 추적성은 작업자 로그인이 담당하므로 AUTO RUN 에 시스템 사용자 로그인까지
    /// 요구하면 현장이 이중 로그인을 해야 하고, 공용 계정 상시 로그인으로 우회되기 쉽다.
    ///
    /// MainViewModel 에 인라인으로 두면 테스트가 불가능해 별도 헬퍼로 분리했다 —
    /// 과거 <c>?? true</c> 표현이 "서비스 미주입" 만 커버해 운영 빌드에서는 사실상 로그인이
    /// 필수였고, 그 상태로 매뉴얼·툴팁과 어긋난 채 오래 방치됐다 (2026-08-14 정정).
    /// </summary>
    public static class StartStopGate
    {
        /// <summary>
        /// 시작/정지가 시스템 사용자 권한 관점에서 허용되는지.
        /// (카메라 연결·작업자 로그인·작업지시 선택은 호출 측에서 별도로 판정한다.)
        /// </summary>
        public static bool Allows(IUserService? userService)
        {
            if (userService == null) return true;          // 서비스 미주입(standalone 등) — 기존 동작
            if (!userService.IsLoggedIn) return true;      // 미로그인 — 매뉴얼 §3.5 "기본 허용"
            return userService.HasPermission(UserPermission.StartStop);
        }
    }
}
