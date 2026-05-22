# Changelog — BODA VMS

## v1.1.0 (2026-05-22)

### 🚀 운영 워크플로 (Phase 7)

- **Stage 1 — Operator Login**: Web `/api/kiosk/*` 활용. `OperatorAuthService` + `OperatorLoginDialog`. 헤더 Operator chip.
- **Stage 2 — Work Order 목록 + 자동 Recipe 로드**: Web `/api/workorders/by-client/{idx}` 익명 endpoint. `WorkOrderListWindow` (chromeless 다크, DataGrid + 상태 필터). WO 선택 시 `WO.RecipeName` 로 로컬 레시피 자동 로드 → 카메라 전파 → Web 파라미터 동기화. AUTO RUN 활성화 조건 = 카메라 연결 + Operator 로그인 + WO 선택.
- **Stage 3 — 진행률 실시간 + 계획 수량 알람**:
  - Web `/api/parameters/results` 응답에 `workOrder` dict (Planned→InProgress, Completed 자동 전이).
  - `IParameterSyncService` 에 `WorkOrderProgressed` / `WorkOrderCompleted` 이벤트.
  - `WorkOrderCompletedDialog` (560×500 chromeless, KPI 카드, SystemSounds.Asterisk).
  - 헤더 WO 칩에 ProgressBar 시각화 (커스텀 Template, #14B8A6 인디케이터).

### 🔧 운영 흐름 UX (B 시리즈)

- **B1 — 활성 Lot 자동 채움**: WO 선택 시 Web 에서 활성 Open Lot fetch → `LotIdText` 자동 채움.
- **B2 — Completed 후 다음 WO 흐름**: `CanStartStop` 에 WO Status (Planned/InProgress) 조건. 완료 다이얼로그에서 "다음 작업지시 선택" → 자동 호출.
- **B3 — 알람 UX 개선**: 기본 MessageBox → `WorkOrderCompletedDialog` 큰 다이얼로그 + 사운드.
- **B4 — 진행률 ProgressBar**: 헤더 WO 칩에 80×6 ProgressBar.

### ⚙️ 인프라 / 안정성 (C 시리즈)

- **C5 — SignalR 실시간 푸시**: Web `VmsPublicHub` 익명 + `/hubs/vms-public`. VMS `Microsoft.AspNetCore.SignalR.Client` + `VmsHubClient` (자동재연결). 다중 클라이언트 동시 갱신.
- **C6 — 업로드 큐/자동 재시도**: `%LocalAppData%/BODA VISION AI/upload_queue/{timestamp}_{guid}.json` 디스크 큐 + 5s 주기 retry timer. 첫 실패 시 중단 (순서 보존). 프로세스 재시작 시 자동 복구.

### 🎨 UI/UX 정리

- 헤더 운영 흐름 단순화: Operator → Work Orders → WO 칩 → Recipe 칩 → Ctx → AUTO RUN.
- 도구 액션 (Grab/Live Start·Stop/Roller) → Settings 사이드 패널 "Camera Control" 섹션.
- 헤더 칩 높이 통일 (32px) + AUTO RUN 강조 (34px Bold).
- Recipe 칩 신설 (파란색 보더).
- 사이드 패널 Recent Inspections 섹션 — 3-cell 통계 + ListBox.

### 👥 권한 (D 시리즈)

- **D8 — VMS 자체 검사 히스토리**: `RecentInspectionsService` 순환 버퍼 (MaxItems=200). 사이드 패널 ListBox + Clear 버튼.
- **D10 — Operator 등급(Role) 기반 메뉴 가시성**: Web 측 `Operator.Role` 필드 + 마이그레이션. VMS 측 `OperatorSessionDto.Role` + `CanLeadOrAbove` / `CanSupervisor`. 헤더 Role 뱃지 (Lead=파랑, Supervisor=주황). External Tools 섹션 Supervisor 전용.

### 📦 배포

- MSI 인스톨러: `installer/VMS.Installer.wixproj` (WiX v5). `BODA-VMS-1.1.0.msi`.
- 공용 버전 관리: 솔루션 루트 `Directory.Build.props`.

### 🐛 버그 수정

- WO Completed 후 SelectedWorkOrder 가 남아 AUTO RUN 이 다시 활성되는 모호함 해소.
- ComboBox SelectedItem 타입 불일치 (활성/전체 필터가 동작 안 함) → SelectedValue + SelectedValuePath.
- 알림 배지가 AppBar 모서리에 잘림 → Origin.TopLeft + 커스텀 CSS.

### 🔗 통합 정렬

- VMS ↔ Web DTO: `WorkOrderDto` / `LotDto` / `OperatorSessionDto` (Role 포함) / `WorkOrderProgressDto` 양방향 호환.
- 모든 VMS 측 endpoint 익명 — JWT 없이 운영 가능.

---

## v1.0.0

초기 릴리스. Phase 1~6 (E2E heartbeat / 레시피 동기화 / 추적성 4필드 / Self-register fallback / NG 알람 SignalR / 레시피 변경 전파).
