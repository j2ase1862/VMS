# GS 인증 보안 정책 종합 (BODA Vision AI v1.2)

문서 버전: v1.1
대상 빌드: master @ 2026-06-10 (PR1~137 — baseline PR1~44 + Web 짝 PR119 + 외부검토 PR122~127 + SSO PR128~132 + Option C PR133~134 + 매뉴얼 매트릭스 PR135~137)
시스템: BODA Vision AI — .NET 8.0 WPF 산업용 머신비전 검사 플랫폼

> 본 문서는 GS (Good Software) 인증 심사관이 처음 접하는 보안/품질 정책 종합 문서입니다.
> ISO/IEC 25051 (소프트웨어 품질 요구사항) 항목별로 변경 내용을 매핑하고,
> 검증 가능한 코드 경로와 검증 절차를 함께 제시합니다.
> §2 핵심 매핑 본문은 baseline PR1~20 을 기준으로 작성됐으며, 이후 추가된 보안 작업
> (Web 짝 PR119, 외부검토 PR122~127, **SSO PR128~132**, **Option C 디폴트 시드 제거 PR133~134**,
> 매뉴얼 일관성 회귀 PR135~137) 은 §3.1 후속 PR 매트릭스 및 §6 사용자 인증 아키텍처에서 다룹니다.

---

## 1. 시스템 개요

### 1.1 구성
| 프로젝트 | 역할 |
|---|---|
| `VMS/` | 메인 런처 + 운영 UI (Auto Process / Recipe / WO 진행) |
| `VMS.AppSetup/` | 시스템 구성 마법사 (PLC / 카메라 / IO 보드) |
| `VMS.VisionSetup/` | 비전 도구 워크스페이스 + Sequence Editor |
| `VMS.DeepLearning/` | DL 라벨링 / 학습 / Inference |
| `VMS.Core/` | 보안 헬퍼, 외부 API 클라이언트, 감사 로깅 |
| `VMS.PLC/` | PLC 프로토콜 (Modbus / Mitsubishi / Siemens / LS / Omron) |
| `VMS.Camera/` | 카메라 추상화 (Hikrobot / Matrox / GigE) |
| `VMS.MasterSetup/` | WiX MSI 인스톨러 |

### 1.2 보안 모드 (이중 모드 정책)
- **Development**: HTTP 허용, 사설망 self-signed 인증서 허용 (개발 편의)
- **Production**: HTTPS 강제, 엄격한 인증서 검증
- 전환: `%LocalAppData%\BODA VISION AI\system_config.json` 의 `securityMode` 키
- 코드: `VMS.Core/Security/SecurityOptions.cs`

### 1.3 신뢰 경계
| 경계 | 내부 (신뢰) | 외부 (검증 필수) |
|---|---|---|
| 파일 시스템 | LocalAppData 자체 작성 파일 | 사용자가 import 한 레시피 / 이미지 |
| 네트워크 | Web 백엔드 (TLS) | HTTP 응답 JSON DTO 필드 값 |
| PLC | 운영자 PLC 주소 입력 | 외부 PLC 응답 바이트열 |
| 사용자 | DB ACL 보호된 사용자 DB | 로그인 입력 (사번/PIN) |

---

## 2. ISO/IEC 25051 항목별 매핑

### 2.1 보안성 (8.2 Security)

#### 2.1.1 인증 (Authentication)
| 항목 | 구현 | 코드 경로 | 관련 PR |
|---|---|---|---|
| 비밀번호 해시 | BCrypt (cost 11, work factor 검증) | `VMS/Services/UserService.cs:Authenticate` | (기존) |
| 자격증명 검증 | 길이 4~128, 제어문자 차단 | `VMS.Core/Security/CredentialGuard.cs` | PR2 |
| 메모리 처리 | `ClearSecretField` 호출 후 GC | `VMS.Core/Security/CredentialGuard.cs:ClearSecretField` | PR2 |
| DB 파일 보호 | Windows ACL, 현재 사용자만 R/W | `VMS/Services/UserService.cs:ApplyDatabaseFileAcl` | PR2 |
| 로그인 시도 감사 | Success / Denied / Failure 기록 | `AuditCategory.Authentication` | PR4 |
| **디폴트 시드 제거** | 하드코딩 `admin/admin123` 제거 — 신규 install 은 운영자가 강한 비밀번호 명시 입력. `SeedInitialAdmin(password, displayName)` 헬퍼만 제공 (자동 시드 없음) | `VMS/Services/UserService.cs:SeedInitialAdmin`, `VMS.AppSetup/Services/InitialAdminSeeder.cs` | **PR133~134 (Option C)** |
| **Web 위임 인증 (SSO, 선택)** | AppSetup 토글시 일반 사용자 인증을 Web(master)에 위임, 비상시 `local-admin` 로컬 폴백 | `VMS.Core/Services/WebAuthClient.cs`, `VMS/Services/UserService.cs:AuthenticateViaWebAsync` | **PR128~132** |

#### 2.1.2 권한 (Authorization)
| 항목 | 구현 | 코드 경로 | 관련 PR |
|---|---|---|---|
| 역할 모델 | Admin / Engineer / Operator 3등급 | `VMS/Models/UserGrade.cs` | (기존) |
| 권한 결정 | `HasPermission(UserPermission)` 단일 진입 | `VMS/Services/UserService.cs:HasPermission` | (기존) |
| 권한 거부 감사 | Denied outcome 기록 | `AuditCategory.Authorization` | PR9 |
| 외부 Role 검증 | OperatorRoles 화이트리스트, 미인식은 "Operator" fallback | `VMS.Core/Models/ParameterSync/OperatorDto.cs:Sanitize` | PR19 |
| **비상 폴백 권한 제한** | `local-admin` 은 제한 권한 (`UserPermission.RestartWebService` 등 최소) + 모든 사용시 AuditLog 강제 | `VMS/Models/User.cs:IsLocalFallback` | **PR130** |

#### 2.1.3 통신 보안 (Communication)
| 항목 | 구현 | 코드 경로 | 관련 PR |
|---|---|---|---|
| TLS 강제 | TLS 1.2 / 1.3 만 허용 | `VMS.Core/Security/HttpClientPolicy.cs` | PR1 |
| 인증서 검증 | Production: strict / Development: 사설망 self-signed 허용 | `HttpClientPolicy.Build` | PR1 |
| HTTP URL 거부 | InsecureUrlGuard.Check — Production 모드에서 throw | `VMS.Core/Security/InsecureUrlGuard.cs` | PR1, PR4 |
| 응답 크기 상한 | 10 MB 기본 / 호출자 지정 | `HttpClientPolicy.Build:maxResponseBytes` | PR1, PR3 |
| 응답 압축 | gzip / deflate | `HttpClientPolicy.Build` | PR1 |

#### 2.1.4 데이터 무결성 — 입력 검증
| 항목 | 구현 | 코드 경로 | 관련 PR |
|---|---|---|---|
| Path Traversal 방어 | `IsPathWithinDirectory` 정규화 비교 | `VMS.Core/Security/InputValidator.cs` | PR3 |
| 확장자 화이트리스트 | `HasAllowedExtension` | `InputValidator.cs` | PR3 |
| 문자열 길이 / 범위 | `IsStringLengthValid` / `IsInRange<T>` | `InputValidator.cs` | PR3 |
| PLC 주소 검증 | Vendor별 Offset 상한 (Modbus 0xFFFF, Mitsubishi 0xFFFFFF 등) | `VMS.PLC/Models/PlcAddress.cs:Parse` | PR3 |
| 외부 JSON DTO sanitization | `DtoValidator` — clamp / truncate / whitelist | `VMS.Core/Security/DtoValidator.cs` | PR19, PR20 |

**Sanitize 적용 DTO (8 종)**:
| DTO | 검증 정책 | 적용 사이트 |
|---|---|---|
| `WorkOrderProgressDto` | 수량 [0, 1M] clamp, OrderNo (100) / Status (50) | ParameterSyncService:312, VmsHubClient:148 |
| `OperatorSessionDto` | Role 화이트리스트, 이름 / 사번 truncate | OperatorAuthService:67, :149 |
| `PredictionCurrentDto` | NgRate [0, 1] clamp, NaN/Inf 차단 | PredictionPollingService:82 |
| `WorkOrderDto` | 7 문자열 truncate, 수량 clamp | WorkOrderClient:43 |
| `LotDto` | 4 문자열 truncate, 수량 clamp | LotClient:43 |
| `RecipeSummaryDto` | Name (200) / Description (2000) truncate | ParameterSyncService:105 |
| `RecipeParameterDto` | ParamValue [-1e9, 1e9] clamp, NaN/Inf 차단 | ParameterSyncService:144 |

> 정책 — 운영 폴링 흐름을 유지하면서 손상값만 정상화. 모든 위반은 `AuditCategory.Security` · `Denied` 로 기록되어 사후 추적 가능.

#### 2.1.5 감사 로깅 (Audit)
| 항목 | 구현 | 코드 경로 |
|---|---|---|
| 저장소 | `%LocalAppData%\BODA VISION AI\audit\YYYY-MM-DD.jsonl` — 일별 회전 | `VMS.Core/Security/AuditLogger.cs` |
| 형식 | JSONL append-only — SIEM / grep / jq 호환 |  |
| 동시성 | `_writeLock` 으로 직렬화 — 손실 < 지연 |  |
| 예외 안전 | 쓰기 실패 시 호출자 흐름 깨뜨리지 않음 (best-effort) |  |

**9 카테고리** (`AuditCategory`):
| 카테고리 | 활성 PR | 대표 이벤트 |
|---|---|---|
| Authentication | PR4 | OperatorLogin, AdminLogin |
| Authorization | PR9 | HasPermission Denied |
| UserManagement | PR4 | CreateUser, DeleteUser, ChangePassword |
| RecipeChange | PR5 | RecipeLoad, RecipeSave, RecipeDelete, RecipeExport |
| SequenceControl | PR4 | SequenceStart, SequenceStop, SequenceReset |
| Inspection | PR5 | InspectionNG, InspectionException |
| Configuration | PR9 | SaveSystem / Layout / PlcSignal Configuration |
| Security | PR1, PR4, PR19, PR20 | InsecureUrlAttempt, DtoFieldClamped/Truncated/Rejected |
| System | (기본) | 미분류 시스템 이벤트 |

**Viewer** (`VMS/Views/AuditLogViewerWindow.xaml`, PR6):
- Admin 권한 전용 — `Tools` 메뉴에서 진입
- 날짜 범위 / 카테고리 / Outcome / 텍스트 검색
- CSV export (RFC 4180 준수)

### 2.2 신뢰성 (8.4 Reliability)

#### 2.2.1 예외 처리 정책
| 위치 | 정책 |
|---|---|
| 감사 로그 쓰기 | best-effort — 실패해도 호출자 흐름 유지 |
| 외부 API 응답 | try/catch + Debug.WriteLine + 기본값 반환 |
| 파일 I/O | 실패 시 빈 객체 반환, Configuration.Failure 감사 |
| 검사 실행 | InspectionService outer try/catch → `Inspection error:` 결과 + Inspection.Failure 감사 |

#### 2.2.2 재시도 / 장애 격리
| 영역 | 메커니즘 | 코드 경로 |
|---|---|---|
| 결과 업로드 실패 | 디스크 큐 (timestamp+guid JSON) → 자동 재시도 | `ParameterSyncService.EnqueueFailed` (C6) |
| SignalR 끊김 | 자동 재연결 | `VmsHubClient` (C5) |
| Web 백엔드 다운 | VMS 자체 검사 히스토리 (Recent Inspections) 로 작업자 지속 확인 | `RecentInspectionsService` (D8) |

#### 2.2.3 테스트 커버리지
| 프로젝트 | 테스트 수 |
|---|---|
| VMS.Core.Tests | 339 (보안 헬퍼 + DTO sanitization + 감사 로거 + WebAuthClient / WebSsoConfig) |
| VMS.Tests | 203 (RecipeService / UserService(+SeedInitialAdmin / LocalFallback) / ConfigurationService / InspectionService 통합) |
| VMS.VisionSetup.Tests | 79 (ToolSerializer / VisionTool 회귀) |
| VMS.PLC.Tests | 48 (PlcAddress / GigEVision) |
| VMS.AppSetup.Tests | 8 |
| **합계** | **677 (CI 자동 실행, windows-2025, Diagnostic 제외)** |

진단(Diagnostic) 테스트는 외부 ONNX 가중치 의존이라 CI 필터로 제외 (`--filter "FullyQualifiedName!~Diagnostic"`).
> 수치는 `master @ 2026-06-10` 기준 `dotnet test --list-tests` 디스커버리 결과 (PR135~137 의 User Manual §3.7 UserGrade/Permission 매트릭스 회귀 44 테스트 포함).

### 2.3 유지보수성 (8.6 Maintainability)

#### 2.3.1 CI/CD 파이프라인
- `.github/workflows/build.yml` — windows-2025, .NET 8
- 전 솔루션 빌드 + 단위/통합 테스트 자동 실행
- WiX MSI 자동 생성 + 30일 보관 (`BODA-VMS-installer-{sha}` artifact, PR16)
- Build Summary 에 MSI 파일 크기 / 빌드 정보 출력

#### 2.3.2 코드 품질
- 빌드 경고 0개 (PR1~20 전 기간 유지)
- nullable 활성화 (`<Nullable>enable</Nullable>`)
- MVVM 강제 (`CLAUDE.md` — 코드비하인드 비즈니스 로직 금지)

### 2.4 추적성 (Traceability)

#### 2.4.1 사용자 활동 추적
- 모든 시퀀스 시작/중지, 레시피 변경, 권한 거부, 검사 NG, 설정 변경 → 감사 로그
- 작업자 식별: `OperatorAuthService.CurrentSession.EmployeeNumber` → `userName` 필드
- 작업지시 추적: Phase 3 4필드 (WorkOrderId / LotId / OperatorId / SerialNumber) → InspectionService

#### 2.4.2 검사 결과 추적
- Web 업로드 결과: `ParameterSyncService.UploadResultsAsync`
- VMS 자체 히스토리: `RecentInspectionsService` (Web 끊겨도 사이드 패널에서 확인)
- 예측 모델 피처 (V1~V3): Brightness / FocusScore / DLConfidence 등 자동 산출 → 업로드 동봉

---

## 3. PR 추적성 매트릭스 (PR1~20)

| PR | 카테고리 | 변경 요약 |
|---|---|---|
| PR1 | 통신 보안 | HttpClientPolicy + SecurityOptions (Dev/Prod) |
| PR2 | 인증 | CredentialGuard + DB ACL + 메모리 0-fill |
| PR3 | 입력 검증 | InputValidator (Path/Extension/Range/Length) + PLC 주소 |
| PR4 | 감사 | AuditLogger 인프라 + Authentication / Sequence / Recipe |
| PR5 | 감사 확장 | Recipe 변경 + Inspection NG |
| PR6 | 감사 UI | Audit Log Viewer + CSV export |
| PR7 | 테스트 | VMS.Core.Tests 신설 + 보안 헬퍼 67건 |
| PR8 | 테스트 | 보안 정책 분기 + PLC 주소 +29건 |
| PR9 | 감사 보완 | Authorization Denied + WO / Configuration |
| PR10 | 사용성 | Sequence 노드 + Common Settings in-app 도움말 |
| PR11 | 사용성 | Sequence 노드 나머지 5개 Parameters |
| PR12 | 테스트 | AuditLogger 통합 +15건 (임시 디렉토리 격리) |
| PR13 | 테스트 | VMS.Tests 신설 + RecipeService 통합 +17건 |
| PR14 | 테스트 | ConfigurationService 통합 +15건 |
| PR15 | 테스트 | UserService 통합 +28건 (BCrypt 해시 직접 검증) |
| PR16 | CI/CD | MSI 인스톨러 artifact 자동 업로드 |
| PR17 | 문서 | 운영 환경 회귀 테스트 가이드 v1.2 (26 항목) |
| PR18 | 테스트 | InspectionService 통합 +15건 |
| PR19 | 입력 검증 | DtoValidator + WorkOrderProgressDto / OperatorSessionDto / PredictionCurrentDto |
| PR20 | 입력 검증 | WorkOrderDto / LotDto / RecipeSummaryDto / RecipeParameterDto |

### 3.1 후속 PR 매트릭스 (PR119~137)

baseline PR1~44 이후 추가된 보안/품질 작업. 시간순 상세는 `GS_History.md` Phase 14~17 참조.

| PR | 카테고리 | 변경 요약 |
|---|---|---|
| PR119 | 통신 보안 | Web 머신 endpoint 호출시 X-API-Key 헤더 자동 송신 (BODA.VMS.Web PR #10 짝) |
| PR122 | 신뢰성 | ONNX 로드 실패 `OnnxLoadException` wrap — SequenceEngine 안전 (외부검토 P1-#4) |
| PR123 | 보안 모드 | SecurityOptions 폴백 정책 명시화 (외부검토 P1-#3) |
| PR124 | 데이터 무결성 | SQLite PRAGMA WAL 명시 — `InitializeDatabase` (외부검토 P2) |
| PR125 | 신뢰성 | Startup health check 에 WebServer 도달성 항목 추가 (외부검토 P3a) |
| PR126 | 추적성 | 보존 정책 프리셋 ↔ 매뉴얼 상수 자동 회귀 (외부검토 P4) |
| PR127 | 보안 모드 | RELEASE = `RequireExplicit` enforcement 활성 + FallbackOnError 시 Warn (외부검토 P5) |
| **PR128~132** | **인증** | **SSO Migration** — `WebAuthClient` (Success/InvalidCredentials/WebUnreachable/ServerError 4 분류) + `UserService.AuthenticateViaWebAsync` + `WebSsoConfig` + `User.IsLocalFallback` / `local-admin` 폴백 + LoginViewModel SSO 라우팅 + AppSetup "Web SSO" 카드 |
| **PR133~134** | **인증 (Option C)** | **디폴트 admin 시드 제거** — 하드코딩 `admin/admin123` 삭제, `SeedInitialAdmin(password, displayName)` 헬퍼 + AppSetup Page 2 "Initial Admin Passwords" PasswordBox 2종 + `InitialAdminSeeder` (SQLite + BCrypt). 기존 install 호환 (admin 존재시 skip) |
| PR135~137 | 추적성 | User Manual §3.6 Web Role / §3.7 로컬 UserGrade·Permission 매트릭스 + `UserGrade`/`HasPermission` ↔ 매뉴얼 자동 회귀 44 테스트 |

> **Option C 보안 의의 (GS critical)**: 출고 디폴트 자격증명 (`admin/admin123`) 을 완전히 제거하여,
> 운영자가 첫 가동시 강한 비밀번호(최소 8자, 12자+ 권장)를 명시 입력하지 않으면 admin 계정이 생성되지 않습니다.
> VMS local-admin(비상 제한 권한) 과 Web admin(일상 master) 비밀번호는 **별도 관리(동기 금지)** — 침해 격리.
> 운영 절차: `docs/msi_build_guide.md §11` (VMS) / `BODA.VMS.Web` `Initial:AdminPassword` env (Web).

---

## 4. 검증 절차

### 4.1 자동화 검증 (CI)
모든 PR 은 master 머지 전 windows-2025 CI 통과 필수.
- 전체 빌드 (8 프로젝트)
- 677 단위/통합 테스트 (Diagnostic 제외)
- MSI 인스톨러 생성
- 결과 artifact 30일 보관

### 4.2 수동 회귀 검증 (운영 환경)
`docs/manual_regression_v1.2.md` — 26 항목 체크리스트 (v1.2 시점 기록 — 현행 절차는 `docs/field_install_checklist.md`)
- 3.1 IO Device Integration (5)
- 3.2 Expert Mode (4)
- 3.3 Security Mode (3, GS critical)
- 3.4 Credentials + DB ACL (3)
- 3.5 Input Validation (3, 1 optional)
- 3.6 Audit Logging (4, GS critical)
- 3.7 in-app Help (3)
- 3.8 Integration Test Areas (3)

합격 기준: 26 항목 중 25 이상 PASS (Optional 1 항목 제외 가능).

### 4.3 감사 로그 자체 검증
1. 운영 중 `%LocalAppData%\BODA VISION AI\audit\YYYY-MM-DD.jsonl` 생성 확인
2. `Tools → Audit Log Viewer` (Admin 권한) — UI 조회 + CSV export
3. 카테고리별 이벤트 분포 확인 — Authentication / Inspection 이 정상 운영 중 최다 발생

---

## 5. 부록

### 5.1 보안 정책 파일 위치 (운영 환경 기준)
| 파일 | 경로 |
|---|---|
| 시스템 구성 | `%LocalAppData%\BODA VISION AI\system_config.json` |
| 사용자 DB (ACL 보호) | `%LocalAppData%\BODA VISION AI\BodaVision.db` |
| 감사 로그 | `%LocalAppData%\BODA VISION AI\audit\YYYY-MM-DD.jsonl` |
| 결과 업로드 큐 | `%LocalAppData%\BODA VISION AI\upload_queue\` |
| 레시피 | `%LocalAppData%\BODA VISION AI\recipes\` |

### 5.2 외부 의존성
| 라이브러리 | 버전 | 용도 | 라이선스 |
|---|---|---|---|
| CommunityToolkit.Mvvm | 8.4.0 | MVVM 인프라 | MIT |
| OpenCvSharp4 | 4.11.0 | 이미지 처리 | Apache 2.0 |
| BCrypt.Net-Next | 4.0.3 | 비밀번호 해시 | BSD-2-Clause |
| Microsoft.Data.Sqlite | 8.0.0 | 사용자 DB | MIT |
| Microsoft.AspNetCore.SignalR.Client | 8.x | 실시간 푸시 | MIT |
| xunit | 2.9.0 | 단위 / 통합 테스트 | MIT |

전체 의존성 / 옵션 벤더 SDK 매트릭스 + 배포 정책 → `docs/gs/gs_distribution_policy.md` (PR26).
NOTICE 파일 (Apache 2.0 §4 표기 의무) → 루트 `NOTICE`.
VMS 자체 라이선스 → 루트 `LICENSE` (DRAFT, 법무 검토 필요).

### 5.3 후속 강화 후보
| 항목 | 권장 시점 | 비고 |
|---|---|---|
| MSI 코드 서명 (Authenticode) | 외부 배포 시점 | 운영 절차 문서화 완료 — `docs/gs/gs_msi_code_signing_guide.md` (PR24) |
| 감사 로그 SIEM 외부 전송 | 통합 모니터링 도입 시 | 운영 절차 문서화 완료 — `docs/gs/gs_audit_siem_integration_guide.md` (PR25) |
| 침입 탐지 — 비정상 로그인 패턴 알림 | 사이트 규모 확대 시 | SIEM 알람 룰로 대체 가능 (PR25 §6.2) |

### 5.10 보존 정책 통합 UI (PR38 + PR40 카테고리별 차등 + PR41 프리셋 + PR42 dry-run 미리보기)
| 항목 | 값 / 동작 |
|---|---|
| 진입 | 메인 헤더 Admin Tools 드롭다운 → Retention Settings... — Admin 전용 |
| 전역 편집 키 | `auditRetentionDays` (§5.4) / `autoBackup.retentionDays` (§5.7) / `uploadQueueRetentionDays` (§5.9) |
| 카테고리별 편집 (PR40) | `auditCategoryRetentionDays` 의 9 카테고리 키 — ObservableCollection 일괄 편집 |
| 빠른 프리셋 (PR41) | Conservative / Standard / Minimal — 3 버튼 클릭 시 12 키 동시 채움, Save 전엔 디스크 미반영 |
| Dry-run 미리보기 (PR42) | Preview 버튼 → `VMS.Core.Retention.RetentionPreviewService` 호출, 4 정책별 영향 (삭제 예정 파일 수 / MB / 가장 오래된 남는 날짜 / 카테고리별 라인 제거 수) 표시. **파일을 절대 수정하지 않음** (read-only) |
| Dry-run CSV export (PR43) | Export Preview... 버튼 → SaveFileDialog → RFC 4180 CSV. 헤더 `Section,Policy,Metric,Value` + Meta (GeneratedUtc) + Settings 스냅샷 (전역 3 + 카테고리 9) + GlobalSummary + CategoryFilter. `Configuration · RetentionPreviewExported` 감사. GS 심사 / 문서화 / 외부 검토 자료용 |
| UI 구조 | ScrollViewer + 4 카드 (프리셋 / 전역 / 미리보기 (옵션) / 카테고리별) |
| 정책 | JsonNode 격리 편집 — 다른 키 / autoBackup / auditCategoryRetentionDays 객체 보존 |
| Clamp | 저장 직전 각 키별 강제 (UI 라벨에 범위 표시) — 카테고리별은 [1, 3650] |
| 감사 기록 | `Configuration` · `RetentionConfigSaved` (전역 + CategoryKeys 카운트 details) |
| 적용 시점 | VMS 재시작 — 모든 보존 정책이 OnStartup 에서 실행됨 |
| 코드 경로 | `VMS/Views/RetentionSettingsWindow.xaml`, `VMS/ViewModels/RetentionSettingsViewModel.cs`, `VMS/ViewModels/CategoryRetentionItem.cs` |

**프리셋 값 (PR41)**:
| Preset | Audit | AutoBackup | UploadQueue | Sec/User/Cfg | Auth/Authz/Recipe | Seq/Insp | System |
|---|---|---|---|---|---|---|---|
| Conservative (규제) | 730 | 90 | 60 | 1825 | 1095 | 730 | 180 |
| Standard (GS 권장) | 365 | 30 | 30 | 1095 | 730 | 365 | 90 |
| Minimal (소형) | 90 | 7 | 14 | 365 | 180 | 90 | 30 |

### 5.9 검사 결과 — upload_queue 보존 (PR37)
| 항목 | 값 / 동작 |
|---|---|
| 대상 | `%LocalAppData%\BODA VISION AI\upload_queue\*.json` — Web 업로드 실패 시 재시도용 JSON |
| 기본 보존 | 30일 (네트워크 / Web 백엔드 장애 복구 충분 기간) |
| 구성 키 | `system_config.json` 의 `uploadQueueRetentionDays` (int) |
| 안전 범위 | [1, 365] 자동 clamp |
| 파일명 패턴 | `yyyyMMddHHmmssfff_{guid:N}.json` (ParameterSyncService.EnqueueFailed) |
| 패턴 외 파일 | timestamp prefix 미일치 / 길이 다른 / parse 실패 시 절대 미터치 — 사용자 임의 파일 보호 |
| 정리 실행 시점 | VMS 시작 시 1회 (`App.xaml.cs:OnStartup`, StartupHealthCheck 직후) |
| 정리 행위 감사 | `AuditCategory.System` · `UploadQueueRetention` — Deleted / OldestRemaining 기록 |
| 운영 차단 | 없음 (best-effort) |
| 코드 경로 | `VMS.Core/Retention/UploadQueueRetention.cs` |
| 다른 검사 데이터 | RecentInspectionsService 는 in-memory 200건 circular — 디스크 영구 저장 없음. InspectionService 도 NG 이미지 미저장. trt_cache / BatchTest 결과는 사용자 관리. |

### 5.8 지원 패키지 export (PR34 / PR35 UI)
| 항목 | 값 / 동작 |
|---|---|
| 포함 | `config/system_config.json`, `config/layout_config.json`, `config/plc_signals.json`, `audit/*.jsonl` (최근 N일), `environment.txt`, `backups_index.txt`, `health_snapshot.json`, `support_manifest.json` |
| 의도적 제외 | `BodaVision.db` (BCrypt 해시 PII), `recipes/` (IP) |
| Audit 일수 | 기본 7일, clamp [1, 90] |
| MachineName | 기본 제외 (사이트 식별 PII), `IncludeMachineName=true` 시 포함 |
| Environment / BackupIndex | 기본 포함, 옵션으로 제외 가능 |
| Manifest | schemaVersion / createdAtUtc / productVersion / machineName / auditDaysIncluded / includesEnvironment / includesBackupIndex / fileCount |
| 감사 기록 | `System` · `SupportPackageCreated` (Success / Failure) |
| 수동 실행 (UI) | 메인 헤더의 Support Package 버튼 — Admin 전용. 옵션 (감사 일수 / Env / BackupIndex / MachineName) + SaveFileDialog, 결과 상태 색 표시 (PR35) |
| 코드 경로 | `VMS.Core/SupportPackage/SupportPackageService.cs`, `VMS/Views/SupportPackageWindow.xaml`, `VMS/ViewModels/SupportPackageViewModel.cs` |
| 용도 | 원격 지원 / 엔지니어링 분석용 — 백업 (복원용) 과 분리 |

### 5.7 자동 백업 스케줄러 (PR31 / PR32 UI)
| 항목 | 값 / 동작 |
|---|---|
| 활성 조건 | `system_config.json` 의 `autoBackup.enabled = true` (기본 false) |
| 주기 | `autoBackup.intervalHours` (기본 24h, clamp [1, 720]) |
| 백업 위치 | `autoBackup.directory` (기본 `%LocalAppData%\BODA VISION AI\backups`) |
| 보존 | `autoBackup.retentionDays` (기본 30일, clamp [1, 365]) |
| Audit 포함 | `autoBackup.includeAudit` (기본 false) |
| 파일명 패턴 | `auto-backup-YYYYMMDD-HHmmss.zip` |
| 마지막 실행 시각 | `last_backup_at.txt` (ISO 8601 UTC) — 재시작 후 잔여 시간 계산 |
| 보존 정리 | 매 tick 종료 시 패턴 일치 + RetentionDays 초과분만 삭제, 사용자 임의 ZIP 보호 |
| 감사 기록 | `System` · `AutoBackupStarted` (시작) + `Configuration` · `AutoBackup` (매 실행 Success / Failure) |
| 설정 UI (PR32) | 메인 헤더의 Auto Backup Settings 버튼 — Admin 전용. JsonNode 격리 편집으로 system_config.json 의 다른 키 보존, 저장 시 `Configuration · AutoBackupConfigSaved` 감사. 변경은 VMS 재시작 후 적용 |
| 운영 차단 | 없음 (best-effort) — tick 실패는 다음 주기에 재시도 |
| 코드 경로 | `VMS.Core/Backup/AutoBackupOptions.cs`, `VMS.Core/Backup/AutoBackupScheduler.cs`, `VMS/Views/AutoBackupSettingsWindow.xaml`, `VMS/ViewModels/AutoBackupSettingsViewModel.cs` |

### 5.6 백업 / 복원 정책 (PR29 / PR30 UI)
| 항목 | 값 / 동작 |
|---|---|
| 백업 대상 | `system_config.json` / `layout_config.json` / `plc_signals.json` / `BodaVision.db` / `recipes/` (audit/ 는 옵션) |
| 포맷 | 표준 ZIP + 루트 `backup_manifest.json` (schemaVersion, createdAtUtc, productVersion, includesAudit, includesUsersDb, fileCount, sourceDir) |
| 압축 | Optimal |
| 복원 보안 | ZIP entry 경로가 target 디렉토리 밖으로 escape 시 거부 + `Security` · `BackupEntryRejected` 감사 기록 |
| 충돌 정책 | Overwrite 옵션 (기본 true), RestoreAudit 옵션 (기본 true) |
| 감사 기록 | `Configuration` · `BackupCreated` / `BackupRestored` (Success / Failure) |
| 수동 실행 (UI) | 메인 헤더의 Backup / Restore 버튼 — Admin 전용. SaveFileDialog / OpenFileDialog + 옵션 체크박스, 결과 / manifest 표시 (PR30) |
| 코드 경로 | `VMS.Core/Backup/BackupRestoreService.cs`, `VMS/Views/BackupRestoreWindow.xaml`, `VMS/ViewModels/BackupRestoreViewModel.cs` |

### 5.5 시작 헬스 체크 (PR27 / PR28 UI)
| 항목 | 값 / 동작 |
|---|---|
| 검사 항목 | AppDataDir 쓰기 가능 / AuditDir 쓰기 가능 / SystemConfig 존재 / SecurityOptions 로드 / DiskFreeSpace ≥ 1 GB |
| 자동 실행 | VMS 시작 시 1회 (`App.xaml.cs:OnStartup`, AuditLogRetention 직후) |
| 수동 실행 (UI) | 메인 헤더의 Health Check 버튼 — Admin 권한 전용. Refresh / Clipboard Copy 지원 (PR28) |
| 결과 기록 | `AuditCategory.System` · `StartupHealthCheck` 단일 이벤트 — 종합 + 항목별 상태 details |
| Outcome 매핑 | Overall=Pass/Warn → `Success`, Fail → `Failure` |
| 운영 차단 | 없음 (best-effort) — 감사 흔적만 남기고 앱 진행 |
| 코드 경로 | `VMS.Core/Health/StartupHealthCheck.cs`, `VMS/Views/HealthCheckWindow.xaml`, `VMS/ViewModels/HealthCheckViewModel.cs` |

### 5.4 감사 로그 보존 정책 (PR22 + PR39 카테고리별 차등)
**PR22 — 전역 파일 단위 정리**
- 기본 365일 (clamp [7, 3650]), `system_config.json` 의 `auditRetentionDays`
- 패턴 외 파일 미터치, 오늘 파일 무조건 유지
- `AuditCategory.System` · `AuditLogRetention` 감사
- 코드: `VMS.Core/Security/AuditLogRetention.cs`

**PR39 — 카테고리별 차등 라인 필터** (Whole-file 정리 후 남은 파일에 적용)
| 카테고리 | 기본 보존 일수 | 의도 |
|---|---|---|
| Security / UserManagement / Configuration | 1095 (3년) | 침해 / CRUD / 설정 변경 (GS critical) |
| Authentication / Authorization / RecipeChange | 730 (2년) | 권한 / 레시피 흐름 |
| SequenceControl / Inspection | 365 (1년) | 운영 이벤트 |
| System | 90 (3개월) | 내부 운영 (보존 정리, 헬스 체크 등) |

- `system_config.json` 의 `auditCategoryRetentionDays` 객체로 카테고리별 override (각 키 [1, 3650] clamp)
- 라인별 timestamp + category 파싱 → 정책 적용
- timestamp / category parse 실패 라인은 안전하게 유지 (silent drop 방지)
- 모든 라인 제거 → 파일 삭제, 일부 남음 → rewrite
- `AuditCategory.System` · `AuditCategoryRetention` 감사 (FilesScanned / Rewritten / Deleted / LinesRemoved)
- 코드: `VMS.Core/Security/AuditCategoryRetention.cs`

**실행 시점**: VMS 시작 시 1회 (`App.xaml.cs:OnStartup`) — AuditLogRetention 직후 카테고리별 필터 호출.

---

## 6. 사용자 인증 아키텍처 (SSO + Option C 통합)

baseline 이후 추가된 인증 강화 작업의 종합. 두 시스템(VMS 데스크탑 ↔ BODA.VMS.Web)이 **별도 사용자 DB** 를 가지므로 책임을 분리한다.

### 6.1 책임 분리
| 사용자 유형 | 인증 주체 | 비고 |
|---|---|---|
| Admin / Manager | Web (master) | SSO 활성시 Web 에 위임 |
| 키오스크 운영자 (사번-PIN) | VMS `UserService` 로컬 | 오프라인 필수 — 라인 운영 중 Web 단절 대비 |
| 비상 관리자 | VMS `local-admin` (로컬 폴백) | 제한 권한 + 모든 사용 AuditLog 강제 |

> **비밀번호 동기 금지**: VMS local-admin(비상 제한) 과 Web admin(일상 master) 은 역할·사용 빈도가 다르므로 별도 관리하여 침해를 격리한다.

### 6.2 SSO Migration (PR128~132, 선택 활성)
| 단계 | 핵심 | 코드 경로 |
|---|---|---|
| PR128 | `WebAuthClient` — Success/InvalidCredentials/WebUnreachable/ServerError 4 분류 | `VMS.Core/Services/WebAuthClient.cs` |
| PR129 | `UserService.AuthenticateViaWebAsync` + `WebSsoConfig` (`system_config.json:webSso`) | `VMS.Core/Security/WebSsoConfig.cs` |
| PR130 | `User.IsLocalFallback` + `UserPermission.RestartWebService` + `local-admin` 자동 시드(제한 권한) | `VMS/Models/User.cs` |
| PR131 | LoginViewModel SSO 라우팅 (일반→Web / local-admin→로컬) | `VMS/ViewModels/LoginViewModel.cs` |
| PR132 | AppSetup "Web SSO" 카드 + `SetupConfiguration.WebSso` | `VMS.AppSetup/...` |

**활성화**: AppSetup → "Web SSO 활성" 체크 → 저장 → VMS 재시작. 미활성시 기존 로컬 인증 그대로.

### 6.3 Option C — 디폴트 시드 제거 (PR133~134)
출고 디폴트 자격증명을 완전히 제거. 신규 install 시 운영자가 강한 비밀번호를 명시 입력해야 admin 계정이 생성된다.

| 단계 | 솔루션 | 핵심 |
|---|---|---|
| PR133 (C1) | VMS | `InitializeDatabase` 의 `admin/admin123` 제거 + `SeedInitialAdmin(password, displayName)` 헬퍼. local-admin 자동 시드만 유지 |
| PR134 (C3) | VMS | AppSetup Page 2 "Initial Admin Passwords" 카드(PasswordBox 2종) + `VMS.AppSetup/Services/InitialAdminSeeder` (SQLite + BCrypt 직접) + `msi_build_guide §11` |
| (짝) Web #35 (C2) | Web | `Program.cs` admin/admin 하드코딩 제거, `Initial:AdminPassword` 미설정 + admin 부재시 부팅 차단 |

**비밀번호 정책 (§11.3)**: 최소 8자(12자+ 권장) / VMS·Web 별도(동기 금지).
**호환성**: 기존 install (admin 이미 DB 존재) → 시드 skip → 영향 zero. 신규 install 만 명시 비밀번호 필수.

### 6.4 실제 DB 분리 (중요)
| 시스템 | DB 파일 | 책임 |
|---|---|---|
| VMS UserService | `%LocalAppData%\BODA VISION AI\BodaVision.db` | 사용자 인증 |
| Web | `C:\ProgramData\BODA\VMS\BodaVision.db` | 운영 데이터 (검사/WO/Operator/감사/알람) |

이름은 같으나 다른 파일 — cross-system lock / 백업 충돌 시나리오 없음.

---

## 변경 이력
| 버전 | 날짜 | 변경 |
|---|---|---|
| v1.0 | 2026-05-29 | 초안 작성 — PR1~20 반영 |
| v1.1 | 2026-06-10 | §3.1 후속 PR 매트릭스(PR119~137) + §6 사용자 인증 아키텍처(SSO PR128~132 / Option C PR133~134) 추가. 테스트 수 347→677 갱신. §2.1 인증/권한에 디폴트 시드 제거·SSO·local-admin 반영 |
