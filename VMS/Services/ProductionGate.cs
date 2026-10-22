namespace VMS.Services
{
    /// <summary>
    /// Web 연동 라인의 생산 관리 게이트 — 작업자 로그인 · 작업지시(WO) 선택을 운전 조건으로 요구할지.
    ///
    /// 예전에는 Web 주소를 넣는 순간(작업자 로그인 서비스가 생기는 순간) 둘 다 필수였다. 그래서 "레시피 파라미터 ·
    /// 생산 이력 · 품질 · 설비 관리만 Web 으로 쓰고 작업자 · WO 는 안 쓰는" 라인은 운전할 수 없었다. 업로드 경로와
    /// Web API 는 WO · 작업자 없이도 받으므로 막는 곳은 이 게이트 하나였다.
    ///
    /// 설정(system_config <c>requireOperatorLogin</c> · <c>requireWorkOrder</c>, 둘 다 기본 켬 — 종전 동작)으로
    /// 끌 수 있다. WO 목록은 작업자 로그인으로 조회하므로 WO 필수는 로그인 필수일 때만 의미가 있다.
    /// 단독 모드(Web 미연동)는 둘 다 무시한다.
    ///
    /// 로그인 필수가 꺼지면 작업자 역할(Lead · Supervisor) 메뉴 제한도 적용하지 않는다 — 단독 모드처럼
    /// 시스템 사용자 권한(<see cref="StartStopGate"/>)만 본다. 작업자는 원하면 로그인할 수 있고,
    /// 그때는 결과에 작업자가 실린다.
    /// </summary>
    public readonly record struct ProductionGate(bool WebIntegrated, bool RequireOperatorLogin, bool RequireWorkOrder)
    {
        /// <summary>작업자 로그인이 운전 · 카메라 조작 조건인가.</summary>
        public bool LoginRequired => WebIntegrated && RequireOperatorLogin;

        /// <summary>작업지시 선택이 운전 조건인가.</summary>
        public bool WorkOrderRequired => LoginRequired && RequireWorkOrder;

        /// <summary>Grab · Live 등 카메라 조작 — 로그인 조건만 본다.</summary>
        public bool AllowsCamera(bool operatorLoggedIn) => !LoginRequired || operatorLoggedIn;

        /// <summary>
        /// AUTO RUN · Roller 검사 — 로그인 조건 + WO 조건. <paramref name="workOrderStatus"/> 는 선택한 WO 의 상태,
        /// 선택하지 않았으면 null. WO 를 골랐다면 필수 여부와 관계없이 Planned · InProgress 여야 한다
        /// (완료 · 마감 WO 로 올리면 수량이 오르지 않는다).
        /// </summary>
        public bool AllowsRun(bool operatorLoggedIn, string? workOrderStatus)
        {
            if (!AllowsCamera(operatorLoggedIn)) return false;
            if (workOrderStatus == null) return !WorkOrderRequired;
            return workOrderStatus is "Planned" or "InProgress";
        }
    }
}
