# GS 인증 보안 정책 종합 (BODA Vision AI v1.2)

문서 버전: v1.0
대상 빌드: master @ 2026-05-29 (PR1~20 + xUnit 경고 정리)
시스템: BODA Vision AI — .NET 8.0 WPF 산업용 머신비전 검사 플랫폼

> 본 문서는 GS (Good Software) 인증 심사관이 처음 접하는 보안/품질 정책 종합 문서입니다.
> ISO/IEC 25051 (소프트웨어 품질 요구사항) 항목별로 PR1~20 의 변경 내용을 매핑하고,
> 검증 가능한 코드 경로와 검증 절차를 함께 제시합니다.

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

#### 2.1.2 권한 (Authorization)
| 항목 | 구현 | 코드 경로 | 관련 PR |
|---|---|---|---|
| 역할 모델 | Admin / Engineer / Operator 3등급 | `VMS/Models/UserGrade.cs` | (기존) |
| 권한 결정 | `HasPermission(UserPermission)` 단일 진입 | `VMS/Services/UserService.cs:HasPermission` | (기존) |
| 권한 거부 감사 | Denied outcome 기록 | `AuditCategory.Authorization` | PR9 |
| 외부 Role 검증 | OperatorRoles 화이트리스트, 미인식은 "Operator" fallback | `VMS.Core/Models/ParameterSync/OperatorDto.cs:Sanitize` | PR19 |

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
| VMS.Core.Tests | 153 (보안 헬퍼 + DTO sanitization + 감사 로거) |
| VMS.Tests | 75 (RecipeService / UserService / ConfigurationService / InspectionService 통합) |
| VMS.VisionSetup.Tests | 69 (ToolSerializer / VisionTool 회귀) |
| VMS.PLC.Tests | 46 (PlcAddress / GigEVision) |
| VMS.AppSetup.Tests | 4 |
| **합계** | **347 (CI 자동 실행, windows-2025)** |

진단(Diagnostic) 테스트는 외부 ONNX 가중치 의존이라 CI 필터로 제외 (`--filter "FullyQualifiedName!~Diagnostic"`).

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

---

## 4. 검증 절차

### 4.1 자동화 검증 (CI)
모든 PR 은 master 머지 전 windows-2025 CI 통과 필수.
- 전체 빌드 (8 프로젝트)
- 347 단위/통합 테스트
- MSI 인스톨러 생성
- 결과 artifact 30일 보관

### 4.2 수동 회귀 검증 (운영 환경)
`docs/manual_regression_v1.2.md` — 26 항목 체크리스트
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
| 라이브러리 | 버전 | 용도 |
|---|---|---|
| CommunityToolkit.Mvvm | 8.4.0 | MVVM 인프라 |
| OpenCvSharp4 | 4.11.0 | 이미지 처리 |
| BCrypt.Net-Next | 4.0.3 | 비밀번호 해시 |
| Microsoft.Data.Sqlite | 8.0.0 | 사용자 DB |
| Microsoft.AspNetCore.SignalR.Client | 8.x | 실시간 푸시 |
| xunit | 2.9.0 | 단위 / 통합 테스트 |

### 5.3 후속 강화 후보
| 항목 | 권장 시점 | 비고 |
|---|---|---|
| MSI 코드 서명 (Authenticode) | 외부 배포 시점 | 운영 절차 문서화 완료 — `docs/gs_msi_code_signing_guide.md` (PR24) |
| 감사 로그 SIEM 외부 전송 | 통합 모니터링 도입 시 | 운영 절차 문서화 완료 — `docs/gs_audit_siem_integration_guide.md` (PR25) |
| 침입 탐지 — 비정상 로그인 패턴 알림 | 사이트 규모 확대 시 | SIEM 알람 룰로 대체 가능 (PR25 §6.2) |

### 5.4 감사 로그 보존 정책 (PR22)
| 항목 | 값 / 동작 |
|---|---|
| 기본 보존 기간 | 365일 (GS 권장 1년 이력) |
| 구성 키 | `system_config.json` 의 `auditRetentionDays` (int) |
| 안전 범위 | [7, 3650] 으로 자동 clamp — 실수 / 손상 방지 |
| 오늘 파일 | 보존 기간 무관 항상 유지 (진행 세션 보호) |
| 정리 실행 시점 | VMS 시작 시 1회 (`App.xaml.cs:OnStartup`) |
| 정리 행위 감사 | `AuditCategory.System` · `AuditLogRetention` 이벤트 — Deleted / OldestRemaining 기록 |
| 패턴 외 파일 | YYYY-MM-DD.jsonl 외 파일은 절대 삭제하지 않음 |
| 코드 경로 | `VMS.Core/Security/AuditLogRetention.cs` |

---

## 변경 이력
| 버전 | 날짜 | 변경 |
|---|---|---|
| v1.0 | 2026-05-29 | 초안 작성 — PR1~20 반영 |
