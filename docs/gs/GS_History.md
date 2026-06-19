# VMS (BODA Vision AI) — GS 인증 대응 작업 히스토리

**문서 버전**: 1.1
**작성일**: 2026-06-04 (v1.0) / **개정**: 2026-06-10 (v1.1 — SSO / Option C / 매뉴얼 매트릭스 반영)
**대상**: VMS 솔루션 (WPF 데스크탑) — `VMS / VMS.AppSetup / VMS.VisionSetup / VMS.DeepLearning / VMS.Core / VMS.PLC / VMS.Camera / VMS.MasterSetup`
**기준**: 한국 TTA GS(Good Software) 인증, ISO/IEC 25051 (소비자용 소프트웨어 품질 요구사항)

> 본 문서는 VMS 솔루션이 GS 인증 신청 가능 상태에 도달하기까지 수행한 모든 PR 의 **시간순 히스토리** 입니다.
> 각 PR 의 상세 매핑(ISO/IEC 25051 항목별 구현 위치, 검증 절차) 은 `gs_compliance_overview_v1.0.md` 와 동행 문서들을 참조하세요.
> 짝 작업으로 진행한 BODA.VMS.Web (웹 서버) 측 히스토리는 `D:\Project\BODA.VMS.Web\docs\GS_Certification_Baseline.md` 입니다.

---

## 1. 개요

### 1.1 작업 범위
| 카테고리 | PR 수 | 비고 |
|----------|-------|------|
| 보안 (인증/입력 검증/감사 로깅/보존 정책) | 17 | PR1~3, 4~6, 9, 19~20, 22, 27, 29, 31, 34, 37, 39, 42, 43 |
| 테스트 인프라 (단위/통합 테스트) | 8 | PR7~8, 12~15, 18, 23 |
| UI / UX 개선 (audit viewer / backup UI / health UI / token-based design) | 11 | PR6, 10, 28, 30, 32, 33, 35, 36, 38, 40, 41 |
| CI/MSI 빌드 | 2 | PR16, 44 |
| 문서화 (GS 종합 / SIEM / MSI 서명 / 배포 정책 / 운영 회귀 가이드) | 6 | PR11, 17, 21, 24, 25, 26 |
| Web 솔루션 짝 작업 (X-API-Key 헤더 송신) | 1 | PR119 |
| 외부 검토 대응 (Tiffany P1~P5) | 6 | PR122~127 |
| SSO Migration (단일 사용자 인증 흐름) | 5 | PR128~132 |
| Option C (디폴트 admin 시드 제거) | 2 | PR133~134 |
| 매뉴얼 일관성 (User Manual UserGrade 매트릭스 + 회귀) | 3 | PR135~137 |
| **합계** | **58 PR** | PR1 ~ PR44 + PR119 + PR122~137 |

> v1.0 (2026-06-04) 은 PR1~44 + PR119(45 PR)까지 기록했으며, v1.1 (2026-06-10) 에서 외부검토 대응·SSO·Option C·매뉴얼 매트릭스(PR122~137)를 Phase 15~17 로 추가했다.

### 1.2 산출물 (6 GS 문서 + 코드 변경)
- `docs/gs/gs_compliance_overview_v1.0.md` (PR21, v1.1 개정) — ISO/IEC 25051 항목별 매핑 (§3.1 후속 PR / §6 인증 아키텍처 포함)
- `docs/gs/gs_msi_code_signing_guide.md` (PR24) — MSI 코드 서명 운영 가이드
- `docs/gs/gs_audit_siem_integration_guide.md` (PR25) — 감사 로그 SIEM 외부 전송
- `docs/gs/gs_distribution_policy.md` (PR26) — 라이선스 / NOTICE / 배포 정책
- `docs/gs/SSO_Migration_Plan.md` (PR128) — VMS↔Web SSO 설계
- `docs/gs/GS_History.md` (본 문서) — 시간순 히스토리

---

## 2. Phase 별 진행 흐름

GS 인증 작업을 13 phase 로 나눠 시간순으로 정리. 한 phase 안에서 PR 들이 인터리브된 경우(예: 보존 정책 phase 의 service ↔ UI 짝) 도 같은 묶음.

### Phase 1: 보안 기초 (PR1~3)
**목적**: 모든 외부 통신 / 사용자 입력 / 파일 시스템 접근의 출발선 정의.

| PR | 내용 |
|----|------|
| **PR1** | `feat(security): 통합 HttpClient 정책 + SecurityOptions 모드 (Development/Production)` — Development 는 HTTP 허용 / self-signed 인증서 허용, Production 은 HTTPS 강제 + 엄격한 인증서 검증. `system_config.json` 의 `securityMode` 로 전환. |
| **PR2** | `feat(security): 자격증명 입력 검증 + 메모리 처리 + DB 파일 ACL` — 사번/PIN 입력 sanitization, `SecureString` 사용, DB 파일에 적절한 ACL 부여. |
| **PR3** | `feat(security): 입력 검증 표준화 — Path/Extension/PLC offset/HTTP size` — 외부 입력 4 종 (파일 경로 정규화, 확장자 화이트리스트, PLC 주소 범위, HTTP 응답 크기 제한). |

---

### Phase 2: 감사 로깅 인프라 (PR4~6, PR9)
**목적**: ISO/IEC 25051 보안성/책임 추적성 항목 — 누가 언제 무엇을 했는지 기록.

| PR | 내용 |
|----|------|
| **PR4** | `feat(security): 감사 로깅 인프라 + 주요 이벤트 적용` — `AuditLogger` 코어, 로그인 / 권한 변경 / 시스템 설정 변경 이벤트. |
| **PR5** | `feat(security): 감사 로깅 확장 — Recipe 변경 + Inspection NG` — 레시피 수정 / 검사 NG 발생 기록. |
| **PR6** | `feat(security): 감사 로그 조회 UI (Audit Log Viewer)` — 필터 + 정렬 + DataGrid + 검색. |
| **PR9** | `feat(security): 감사 로그 보완 — 권한 거부 / WO / Configuration` — 빈 영역 보강. |

---

### Phase 3: 테스트 인프라 (PR7~8, PR12~15, PR18, PR23)
**목적**: ISO/IEC 25051 유지보수성/신뢰성 — 회귀 자동 검출.

| PR | 내용 | 누적 테스트 |
|----|------|-------------|
| **PR7** | `test: VMS.Core.Tests 신설 + 보안 헬퍼 단위 테스트 67건` | +67 |
| **PR8** | `test: 보안 정책 분기 + PLC 주소 검증 테스트 +29건` | +29 |
| **PR12** | `test: AuditLogger 통합 테스트 +15건 (임시 디렉토리 격리)` | +15 |
| **PR13** | `test: VMS.Tests 신설 + RecipeService 통합 테스트 +17건` | +17 |
| **PR14** | `test: ConfigurationService 통합 테스트 +15건` | +15 |
| **PR15** | `test: UserService 통합 테스트 +28건` | +28 |
| **PR18** | `test: InspectionService 통합 테스트 +15건` | +15 |
| **PR23** | `test: SequenceEngine 통합 테스트 +17건` | +17 |

---

### Phase 4: UX / 도움말 (PR10~11)
**목적**: ISO/IEC 25051 사용성 — 운영자가 막히지 않고 작업하도록 in-app 도움말 확장.

| PR | 내용 |
|----|------|
| **PR10** | `feat(ux): Sequence 노드 + Common Settings 에 in-app 도움말 노출` |
| **PR11** | `docs: Sequence 노드 나머지 5개 Parameters 보강` |

---

### Phase 5: CI / MSI (PR16~17)
**목적**: 배포 자동화 + 운영 회귀 가이드.

| PR | 내용 |
|----|------|
| **PR16** | `ci: MSI 인스톨러 artifact 업로드 + Build Summary 강화` |
| **PR17** | `docs: 운영 환경 회귀 테스트 가이드 v1.2` |

---

### Phase 6: 입력 검증 Phase 3b/3c (PR19~20)
**목적**: 외부 JSON DTO (Web → VMS 응답) sanitization — 신뢰 경계 외부 데이터 검증.

| PR | 내용 |
|----|------|
| **PR19** | `feat(security): 입력 검증 Phase 3b — 외부 JSON DTO sanitization` |
| **PR20** | `feat(security): 입력 검증 Phase 3c — 외부 JSON DTO sanitization 확장` |

---

### Phase 7: GS 문서화 (PR21, PR24~26)
**목적**: 심사관 / SI 인계 시 일관된 보안 정책 문서 패키지 제공.

| PR | 산출물 |
|----|--------|
| **PR21** | `docs/gs/gs_compliance_overview_v1.0.md` (385 줄) — ISO/IEC 25051 항목별 PR1~20 매핑 |
| **PR24** | `docs/gs/gs_msi_code_signing_guide.md` — MSI 코드 서명 운영 가이드 |
| **PR25** | `docs/gs/gs_audit_siem_integration_guide.md` — 감사 로그 SIEM 외부 전송 |
| **PR26** | `docs/gs/gs_distribution_policy.md` + `LICENSE` + `NOTICE` — 배포 정책 + OSS 라이선스 표기 |

---

### Phase 8: 시작 헬스 체크 (PR27~28)
**목적**: ISO/IEC 25051 신뢰성 — 운영 환경 자기 진단으로 잠재 장애 조기 발견.

| PR | 내용 |
|----|------|
| **PR27** | `feat(security): 시작 헬스 체크 — 환경 자기 진단` — 시작시 DB 연결 / config 무결성 / 라이선스 / TLS 인증서 / 시간 동기 등 체크 |
| **PR28** | `feat(ui): 시작 헬스 체크 UI 윈도우` — 결과 시각화 |

---

### Phase 9: 백업 / 복원 + 자동 백업 (PR29~32)
**목적**: ISO/IEC 25051 데이터 무결성 / 복구성 — 운영 데이터 손실 방어.

| PR | 내용 |
|----|------|
| **PR29** | `feat(security): 백업 / 복원 서비스` — DB / config / 레시피 일괄 백업, 무결성 검증 (SHA-256) |
| **PR30** | `feat(ui): 백업 / 복원 UI 윈도우` |
| **PR31** | `feat(security): 자동 백업 스케줄러` — IHostedService 패턴, 주기 설정 |
| **PR32** | `feat(ui): 자동 백업 설정 UI 윈도우` |

---

### Phase 10: 윈도우 디자인 통일 (chromeless) (PR33, PR36)
**목적**: ISO/IEC 25051 사용성 — 솔루션 전체 UI 일관성.

| PR | 내용 |
|----|------|
| **PR33** | `style(ui): 독립 윈도우 4종 디자인 통일 — VMS.VisionSetup WindowStyles 적용` |
| **PR36** | `feat(ui): Admin 윈도우 chromeless 통일 + 헤더 5 아이콘 단일 드롭다운` |

---

### Phase 11: 지원 패키지 export (PR34~35)
**목적**: ISO/IEC 25051 유지보수성 — 운영 사고시 운영팀이 진단 자료 한 번에 추출.

| PR | 내용 |
|----|------|
| **PR34** | `feat(security): 지원 패키지 export 서비스` — 시스템 정보 / 감사 로그 / 설정 (비밀 제외) / 진단 출력 압축 |
| **PR35** | `feat(ui): 지원 패키지 export UI 윈도우` |

---

### Phase 12: 보존 정책 (audit log retention) (PR37~43)
**목적**: ISO/IEC 25051 / 개인정보보호법 — 감사 로그 보존 기간 정책 자동화.

| PR | 내용 |
|----|------|
| **PR22** | `feat(security): 감사 로그 보존 정책 자동화` — 초기 보존 정책 (단순 일수 컷오프) |
| **PR37 (M)** | `feat(security): upload_queue 보존 정책` |
| **PR38** | `feat(ui): 보존 정책 통합 설정 UI` |
| **PR39 (S)** | `feat(security): 감사 로그 카테고리별 차등 보존` — 9 카테고리(Security / UserManagement / Configuration / Authentication / Authorization / RecipeChange / SequenceControl / Inspection / System) 별 보존 기간 차등 |
| **PR40 (V)** | `feat(ui): 보존 정책 UI 에 카테고리별 차등 편집 추가` |
| **PR41 (W)** | `feat(ui): 보존 정책 빠른 프리셋 — Conservative / Standard / Minimal` |
| **PR42 (X)** | `feat(security): 보존 정책 dry-run 미리보기` — 실제 삭제 전 영향 받을 행 수 확인 |
| **PR43 (Y)** | `feat(security): 보존 정책 dry-run CSV export` — 영향 행 목록 외부 검토 가능 |

---

### Phase 13: MSI 빌드 가이드 (PR44)
**목적**: 인증 받은 빌드 산출물 재현 가능성 확보.

| PR | 산출물 |
|----|--------|
| **PR44** | `docs: MSI 빌드 가이드 v1.0` |

---

### Phase 14: Web 솔루션 짝 작업 — X-API-Key 헤더 송신 (PR119)
**목적**: BODA.VMS.Web 의 X-API-Key feature flag (Web PR #10) 와 짝 — VMS 가 Web 머신 endpoint 호출시 헤더 자동 송신.

| PR | 내용 |
|----|------|
| **PR119** | `feat(security): Web 머신 endpoint 호출시 X-API-Key 헤더 송신 — BODA.VMS.Web PR #10 짝` |

**구현 위치**:
- `VMS.Core/Services/HeartbeatService.cs`, `ParameterSyncService.cs`, `SensorPollingService.cs` — 생성자에 `clientApiKey` 파라미터 추가, `HttpClient.DefaultRequestHeaders` 자동 적용
- `VMS.AppSetup` Page 2 "Web Server Integration" 카드 — "Web Client API Key" 입력 UI 신규
- `VMS\Models\SystemConfiguration.cs` — `ClientApiKey` 필드 추가, `%LocalAppData%\BODA VISION AI\system_config.json` 의 `clientApiKey` 키로 저장
- 호환 모드 (Web 측 `ClientApiKey:Required=false`) 에서는 헤더 송신해도 무영향 — 운영 전환 시점에 Web 만 `Required=true` 로 토글하면 enforcement 활성화

---

### Phase 15: 외부 검토 대응 (Tiffany P1~P5) (PR122~127)
**목적**: 외부 보안 검토자의 지적 사항 처리 — 신뢰성 / 보안 모드 / 데이터 무결성 보강.

| PR | 내용 |
|----|------|
| **PR122** | `fix(reliability): ONNX 로드 실패 OnnxLoadException wrap` — SequenceEngine 안전화 (P1-#4) |
| **PR123** | `fix(security): SecurityOptions 폴백 정책 명시화` (P1-#3) |
| **PR124** | `fix(data): SQLite PRAGMA WAL 명시 — InitializeDatabase` (P2) |
| **PR125** | `feat(reliability): Startup health check 에 WebServer 도달성 항목 추가` (P3a) |
| **PR126** | `test(traceability): 보존 정책 프리셋 ↔ 매뉴얼 상수 자동 회귀` (P4) |
| **PR127** | `feat(security): RELEASE = RequireExplicit enforcement 활성 + FallbackOnError 시 Warn` (P5) |

> **사실관계 다름으로 skip 된 지적** (3건): #2 Audit BCrypt 정합 (VMS=JSONL append-only, Web=SQLite — 동일 저장소 아님) / #5 CSV BOM (.NET `Encoding.UTF8` 이미 BOM emit) / #1·#3b DB 공유 전제 (두 시스템 다른 DB 파일).

---

### Phase 16: SSO Migration — 단일 사용자 인증 흐름 (PR128~132)
**목적**: ISO/IEC 25051 보안성 — VMS↔Web 사용자 인증 일원화 (선택 활성), 비상 로컬 폴백 유지.

| PR | 내용 |
|----|------|
| **PR128** | `feat(sso): SSO 설계 문서 + WebAuthClient 인프라` — `docs/gs/SSO_Migration_Plan.md` + `WebAuthClient` (Success / InvalidCredentials / WebUnreachable / ServerError 4 분류) |
| **PR129** | `feat(sso): UserService.AuthenticateViaWebAsync + WebSsoConfig` — `system_config.json:webSso` 로 활성 |
| **PR130** | `feat(sso): 비상 local-admin 폴백 계정 + 권한 제한` — `User.IsLocalFallback` + `UserPermission.RestartWebService` + 제한 권한 + AuditLog 강제 |
| **PR131** | `feat(sso): LoginViewModel SSO 라우팅` — 일반→Web / local-admin→로컬 + `SetupConfiguration.WebSso` |
| **PR132** | `feat(sso): AppSetup Web SSO 카드 + 운영 가이드 §10` |

**활성화**: AppSetup → "Web SSO 활성" 체크 → 저장 → VMS 재시작. 미활성시 기존 로컬 사번-PIN 인증 그대로.
**책임 분리**: Admin/Manager → Web(master) / 키오스크 운영자 → VMS 로컬(오프라인 필수) / 비상 → local-admin(제한). 비밀번호 동기 금지.

---

### Phase 17: Option C(디폴트 시드 제거) + 매뉴얼 매트릭스 (PR133~137)
**목적**: ISO/IEC 25051 보안성 — 출고 디폴트 자격증명 제거 (GS critical) + 매뉴얼-코드 일관성 회귀.

| PR | 내용 |
|----|------|
| **PR133 (C1)** | `feat(security): VMS admin 디폴트 시드 제거 + SeedInitialAdmin 헬퍼` — 하드코딩 `admin/admin123` 삭제. local-admin 자동 시드만 유지. 기존 install 호환 (admin 존재시 skip) |
| **PR134 (C3)** | `feat(security): AppSetup wizard PasswordBox + 운영 가이드 §11` — Page 2 "Initial Admin Passwords" 카드(PasswordBox 2종) + `VMS.AppSetup/Services/InitialAdminSeeder` (SQLite + BCrypt 직접) |
| **PR135** | `docs(manual): User Manual §3.7 — 로컬 UserGrade 권한 매트릭스 추가` |
| **PR136** | `fix(visionsetup): Tool 전환 / 폴더 네비 시 이전 결과·ROI 잔존 제거` (운영 UX) |
| **PR137** | `test(docs): User Manual §3.7 ↔ UserGrade/Permission 매트릭스 회귀 44 테스트` |

> **Option C 보안 의의 (GS critical)**: 출고 디폴트 자격증명을 완전히 제거. 신규 install 은 운영자가 강한 비밀번호(최소 8자, 12자+ 권장)를 명시 입력하지 않으면 admin 계정이 생성되지 않는다. 짝 작업 Web #35(C2) 는 `Initial:AdminPassword` 미설정시 부팅 차단. VMS local-admin·Web admin 비밀번호 별도 관리 (동기 금지).

---

## 3. PR 인덱스 (시간순 요약)

| PR | 영역 | 한 줄 요약 |
|----|------|-----------|
| PR1 | 보안 | 통합 HttpClient 정책 + SecurityOptions Dev/Prod 이중 모드 |
| PR2 | 보안 | 자격증명 검증 + SecureString + DB ACL |
| PR3 | 보안 | 입력 검증 표준화 (Path/Extension/PLC offset/HTTP size) |
| PR4 | 보안 | 감사 로깅 인프라 + 로그인/권한/Config 이벤트 |
| PR5 | 보안 | 감사 로깅 확장 — Recipe/Inspection NG |
| PR6 | UI | Audit Log Viewer |
| PR7 | 테스트 | VMS.Core.Tests + 보안 헬퍼 67 테스트 |
| PR8 | 테스트 | 보안 정책 + PLC 주소 29 테스트 |
| PR9 | 보안 | 감사 로깅 — 권한 거부 / WO / Configuration 보강 |
| PR10 | UX | Sequence 노드 + Common Settings in-app 도움말 |
| PR11 | 문서 | Sequence 5 개 Parameters 도움말 보강 |
| PR12 | 테스트 | AuditLogger 통합 15 (임시 디렉토리 격리) |
| PR13 | 테스트 | VMS.Tests + RecipeService 17 |
| PR14 | 테스트 | ConfigurationService 15 |
| PR15 | 테스트 | UserService 28 |
| PR16 | CI | MSI artifact 업로드 + Build Summary |
| PR17 | 문서 | 운영 환경 회귀 테스트 가이드 v1.2 |
| PR18 | 테스트 | InspectionService 15 |
| PR19 | 보안 | 입력 검증 3b — 외부 JSON DTO sanitization |
| PR20 | 보안 | 입력 검증 3c — 외부 JSON DTO sanitization 확장 |
| **PR21** | **문서** | **gs_compliance_overview_v1.0.md (385 줄) — ISO/IEC 25051 매핑** |
| PR22 | 보안 | 감사 로그 보존 정책 자동화 (초기) |
| PR23 | 테스트 | SequenceEngine 17 |
| **PR24** | **문서** | **gs_msi_code_signing_guide.md** |
| **PR25** | **문서** | **gs_audit_siem_integration_guide.md** |
| **PR26** | **문서** | **gs_distribution_policy.md + LICENSE + NOTICE** |
| PR27 | 보안 | 시작 헬스 체크 — 환경 자기 진단 |
| PR28 | UI | 시작 헬스 체크 UI 윈도우 |
| PR29 | 보안 | 백업 / 복원 서비스 (SHA-256 무결성) |
| PR30 | UI | 백업 / 복원 UI 윈도우 |
| PR31 | 보안 | 자동 백업 스케줄러 |
| PR32 | UI | 자동 백업 설정 UI 윈도우 |
| PR33 | UI | 독립 윈도우 4 종 디자인 통일 (VisionSetup) |
| PR34 | 보안 | 지원 패키지 export 서비스 |
| PR35 | UI | 지원 패키지 export UI 윈도우 |
| PR36 | UI | Admin 윈도우 chromeless + 헤더 단일 드롭다운 |
| PR37 (M) | 보안 | upload_queue 보존 정책 |
| PR38 | UI | 보존 정책 통합 설정 UI |
| PR39 (S) | 보안 | 감사 로그 카테고리별 차등 보존 |
| PR40 (V) | UI | 보존 정책 — 카테고리별 차등 편집 |
| PR41 (W) | UI | 보존 정책 빠른 프리셋 (Conservative/Standard/Minimal) |
| PR42 (X) | 보안 | 보존 정책 dry-run 미리보기 |
| PR43 (Y) | 보안 | 보존 정책 dry-run CSV export |
| **PR44** | **문서** | **MSI 빌드 가이드 v1.0** |
| **PR119** | **보안** | **X-API-Key 헤더 송신 — BODA.VMS.Web PR #10 짝** |
| PR122 | 신뢰성 | ONNX 로드 실패 OnnxLoadException wrap (외부검토 P1-#4) |
| PR123 | 보안 | SecurityOptions 폴백 정책 명시화 (외부검토 P1-#3) |
| PR124 | 데이터 | SQLite PRAGMA WAL 명시 (외부검토 P2) |
| PR125 | 신뢰성 | Startup health check — WebServer 도달성 항목 (외부검토 P3a) |
| PR126 | 테스트 | 보존 정책 프리셋 ↔ 매뉴얼 상수 회귀 (외부검토 P4) |
| PR127 | 보안 | RELEASE RequireExplicit enforcement 활성 (외부검토 P5) |
| **PR128** | **인증** | **SSO 설계 문서 + WebAuthClient 인프라** |
| PR129 | 인증 | UserService.AuthenticateViaWebAsync + WebSsoConfig |
| PR130 | 인증 | 비상 local-admin 폴백 + 권한 제한 |
| PR131 | 인증 | LoginViewModel SSO 라우팅 + SetupConfiguration.WebSso |
| PR132 | 인증 | AppSetup Web SSO 카드 + 가이드 §10 |
| **PR133** | **보안 (Option C)** | **VMS admin 디폴트 시드 제거 + SeedInitialAdmin 헬퍼** |
| **PR134** | **보안 (Option C)** | **AppSetup PasswordBox + InitialAdminSeeder + 가이드 §11** |
| PR135 | 문서 | User Manual §3.7 로컬 UserGrade 권한 매트릭스 |
| PR136 | UX | VisionSetup Tool 전환/폴더 네비 시 결과·ROI 잔존 제거 |
| PR137 | 테스트 | User Manual §3.7 ↔ UserGrade/Permission 회귀 44 |

---

## 4. Web 솔루션과의 짝 작업 매핑

VMS 데스크탑은 BODA.VMS.Web 과 **별도 사용자 DB** 를 가지며 5 머신 endpoint + (선택) SSO 로 통신한다. 두 시스템은 같은 이름(`BodaVision.db`)이지만 다른 파일을 사용한다 — VMS=`%LocalAppData%\BODA VISION AI\`, Web=`C:\ProgramData\BODA\VMS\`. Web 측 GS 작업과 짝지어진 VMS 측 변경 사항:

| Web PR | VMS PR | 짝 내용 |
|--------|--------|---------|
| Web #10 (X-API-Key feature flag) | **VMS PR119** | VMS 가 heartbeat / register / disconnect / inspection result / sensor 호출시 `X-API-Key` 헤더 자동 송신. `system_config.json` 의 `clientApiKey` 필드 |
| Web #30 (익명 GET endpoint X-API-Key) | (VMS 변경 불필요) | VMS GET 호출에도 같은 `clientApiKey` 사용 — VMS 측 코드는 이미 `HttpClient.DefaultRequestHeaders` 사용해 자동 적용 |
| Web #18~#19 (SignalR Hub Authorize) | (VMS 변경 불필요) | VMS 가 호출하는 `/hubs/vms-public` 은 익명 유지 — `/hubs/vms` 는 JWT 사용자만 |
| Web 인증 endpoint (JWT) | **VMS PR128~132 (SSO)** | SSO 활성시 VMS 일반 사용자 인증을 `WebAuthClient` 로 Web 에 위임, 비상시 `local-admin` 로컬 폴백 |
| **Web #35 (Option C2 — admin 시드 제거)** | **VMS PR133~134 (Option C1/C3)** | 양 시스템 모두 디폴트 admin 자동 시드 제거. Web=`Initial:AdminPassword` 미설정시 부팅 차단 / VMS=AppSetup PasswordBox 명시 입력 |

**완전한 Web 측 작업 인덱스**: `D:\Project\BODA.VMS.Web\docs\GS_Certification_Baseline.md` (v1.1 — Critical 4 + High 4 + 잔여 7 + Option C, 자동 테스트 424) 참조.

---

## 5. 인증 시점 운영 상태 (스냅샷)

| 항목 | 값 |
|------|-----|
| 솔루션 빌드 버전 | v1.2.0 (master @ 2026-06-10, PR1~137) |
| 보안 모드 | env `BODA_VMS_SECURITY_MODE` (최우선) → `system_config.json:securityMode` → 폴백/throw. RELEASE = `RequireExplicit` (PR127), DEBUG = `WarnOnFallback`, QA 우회 = `BODA_VMS_RELAX_SECURITY=1` |
| 감사 로그 | `%LocalAppData%\BODA VISION AI\audit\YYYY-MM-DD.jsonl` (JSONL append-only — 일별 회전. SQLite/BCrypt 아님; append-only 가 변조 방지) |
| 백업 경로 | 사용자 설정 (기본 `%LocalAppData%\BODA VISION AI\backups\`) — 자동 백업 스케줄러 활성시 주기 백업 |
| 보존 정책 | 9 카테고리 차등 (Security/UserManagement/Configuration 1095d · Authentication/Authorization/RecipeChange 730d · SequenceControl/Inspection 365d · System 90d 기본). `auditCategoryRetentionDays` 로 override |
| 자동 테스트 | **677** (Core 339 + VMS 203 + VisionSetup 79 + PLC 48 + AppSetup 8, Diagnostic 제외) — CI windows-2025 |
| Web 통신 보안 | X-API-Key 호환 모드 (Required=false 기본) — Web 서버에서 Required=true 토글시 강제 |
| 사용자 인증 | 로컬 사번-PIN (기본) + (선택) Web SSO 위임 + 비상 local-admin 폴백 (PR128~132). 디폴트 admin 시드 제거 — 신규 install 명시 비밀번호 필수 (Option C, PR133~134) |

---

## 6. 잔여 / 후속 발전 영역

GS 인증 신청에는 영향 없음. 운영 개선 / 차기 인증 갱신을 위한 후속 항목:

1. **MSI 자동 서명 — CI 파이프라인 통합** — 현재 PR44 가이드는 수동 절차. EV 인증서 / HSM 도입 검토.
2. **백업 — 별도 디스크/네트워크 드라이브 자동 전송** — 현재는 같은 머신 보관 (단일 실패점). (`DatabaseBackup:Destination` 별도 디스크 권장은 Web 측 적용)
3. **감사 로그 SIEM 실시간 전송** — 가이드(PR25)는 작성, 실제 forwarder 통합은 운영 사이트별 적용 필요.
4. **VMS.DeepLearning 보안 모드 정합성** — DL 추론 입력 (사용자 업로드 이미지) sanitization 표준 적용 확인.
5. **OpenTelemetry 관측성 / 백업 복구 자동 검증** — CI/CD 성숙 후 도입 후보.
6. **`LICENSE` 법무 검토** — 현재 DRAFT (`LICENSE:5` "REQUIRES LEGAL REVIEW BEFORE EXTERNAL DISTRIBUTION"). 외부 배포/제출 전 VASIM 법무 승인 필요 — `gs_distribution_policy §1` 에서 상태 관리.

> **완료된 후속 항목** (v1.0 에서 후보였으나 처리됨): ~~VMS↔Web JWT 사용자 SSO~~ → Phase 16 (PR128~132) 구현 완료. ~~시작 헬스 체크 추가 진단~~ → PR125 WebServer 도달성 항목 추가. ~~매뉴얼-상수 일관성 확장~~ → PR126/137 회귀 테스트.

---

## 7. 참고 자료

### 본 솔루션 GS 문서
- `docs/gs/gs_compliance_overview_v1.0.md` (PR21, v1.1 개정) — ISO/IEC 25051 항목별 매핑 (§3.1 후속 PR / §6 인증 아키텍처)
- `docs/gs/gs_msi_code_signing_guide.md` (PR24)
- `docs/gs/gs_audit_siem_integration_guide.md` (PR25)
- `docs/gs/gs_distribution_policy.md` (PR26)
- `docs/gs/SSO_Migration_Plan.md` (PR128) — VMS↔Web SSO 설계
- `LICENSE`(DRAFT — 법무 검토 대기), `NOTICE` (PR26)
- 운영 절차: `docs/msi_build_guide.md` v1.3 — §9 보안 모드 env / §10 Web SSO 활성 / §11 초기 비밀번호 입력

### 짝 솔루션 (BODA.VMS.Web)
- `D:\Project\BODA.VMS.Web\docs\GS_Certification_Baseline.md` (v1.1) — Web 서버 GS 작업 (Critical 4 + High 4 + 잔여 7 + Option C, 자동 테스트 424)

### 외부 기준
- ISO/IEC 25010:2011 — Systems and software Quality Requirements and Evaluation (SQuaRE) — System and software quality models
- ISO/IEC 25051:2014 — Requirements for quality of Ready to Use Software Product (RUSP)
- TTA GS 인증 평가 기준 (한국정보통신기술협회)

---

## 8. 변경 이력
| 버전 | 날짜 | 변경 |
|------|------|------|
| 1.0 | 2026-06-04 | 초안 — Phase 1~14 (PR1~44 + PR119), 45 PR |
| 1.1 | 2026-06-10 | Phase 15~17 추가 (외부검토 PR122~127 / SSO PR128~132 / Option C PR133~134 / 매뉴얼 매트릭스 PR135~137), 58 PR. §5 스냅샷 정정 (감사 로그 JSONL 경로·9 카테고리 보존·테스트 677). §4 "DB 공유" → "별도 DB" 정정 + Web #35 짝 추가. §6 후속 항목에서 완료된 SSO 제거 + LICENSE 법무 검토 명시 |

---

**문서 끝.**
