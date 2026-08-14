# VMS ↔ Web SSO 통합 마이그레이션 계획

**문서 버전**: v1.0
**작성일**: 2026-06-04
**대상**: VMS 데스크탑 + BODA.VMS.Web — Admin/Manager 사용자 단일화

---

## 1. 배경

### 1.1 문제
현재 두 솔루션이 **별도 사용자 DB** 사용:

| 시스템 | DB 파일 | 인증 |
|--------|---------|------|
| VMS 데스크탑 | `%LocalAppData%\BODA VISION AI\BodaVision.db` Users 테이블 (BCrypt) | username/password 또는 사번/PIN (키오스크) |
| BODA.VMS.Web | `C:\ProgramData\BODA\VMS\BodaVision.db` Users 테이블 (BCrypt) | username/password (JWT) |

결과:
- 같은 관리자가 **두 계정 관리** — 비밀번호 동기화 부담
- 감사 추적 단절 — VMS admin 행위와 Web admin 행위가 별개 user_id 로 기록 (IATF 추적성 약화)
- 사용자 추가 / 삭제 / 권한 변경을 양쪽 동시 수행 필요
- 한쪽이 약한 비밀번호 사용 시 공격 표면 증가

### 1.2 GS 인증 관점
**결함 아님** — 각 시스템이 자체 인증 / 감사를 갖춤. 다만 운영 인계 시 명백한 마찰점 + 통합 가치 큼.

---

## 2. 목표 아키텍처

### 2.1 책임 분리
| 책임 | 위치 |
|------|------|
| **Admin / Manager 인증 (master)** | Web `/api/auth/login` → JWT 발급 |
| 키오스크 작업자 사번/PIN | VMS UserService (현행 유지) — 키오스크는 오프라인 작동 필수 |
| 비상 로컬 admin (폴백) | VMS UserService 의 `local-admin` 단일 계정 (제한 권한) |

### 2.2 인증 흐름

```
[VMS 사용자 로그인 (username/password)]
    ↓
[WebAuthClient.LoginAsync]
    ├─ Web /api/auth/login 호출 (5초 timeout)
    │   ├─ 200 → JWT + DisplayName + Role 반환 → VMS 메모리 저장
    │   ├─ 401 → 잘못된 자격 → 사용자에게 표시
    │   └─ Network / 5xx → 비상 폴백 시도
    │
    └─ 비상 폴백 (Web 도달 불가 시 한정)
        ├─ 입력 username == "local-admin" 인 경우만 로컬 BCrypt 검증
        ├─ 다른 username 은 거부 (운영 계정은 Web 의존)
        └─ 통과 시 AuditCategory.Authentication / Failure /
           details: "Web unreachable, local fallback used"
```

### 2.3 권한 제한 — 비상 로컬 admin
- VMS 키오스크 / 검사 흐름 진입 (현장 작업 지속)
- 시스템 진단 / Web Service 재시작 명령
- **운영 데이터 변경 권한 없음** (레시피 수정 / 사용자 추가 / 보안 정책 변경 등)
- 사용 시 매번 AuditLog 강제 기록

---

## 3. 단일 PC 운영 모델

같은 PC 에서 VMS 데스크탑 + BODA.VMS.Web Windows Service 동시 운영 가정:

```
[Windows 부팅]
    ↓
[Windows Service: BODA.VMS.Web 자동 시작]  ← Recovery 설정 (auto-restart)
    ↓
[사용자 로그온]
    ↓
[VMS.exe 실행]
    ↓
[StartupHealthCheck (PR #125) — Web /health 도달성 확인]
    ├─ Pass → SSO 흐름 정상
    └─ Fail (Service 부팅 지연) → 재시도 또는 비상 폴백 안내
```

### 운영 가이드 (`docs/msi_build_guide.md §10` — 작성 완료)
| 항목 | 설명 |
|------|------|
| Service 계정 | `LocalSystem` 또는 전용 service 계정 — `C:\ProgramData\BODA\VMS\` (DB) + Jwt__Key 접근 가능 |
| Recovery | "Restart on failure" 활성 (1차 / 2차 / 후속) |
| 부팅 순서 | Service 가 사용자 로그온 전 시작 — VMS 시작 시 보통 ready |
| 자원 | RAM 8GB+ (Web + VMS + 비전 동시) |
| 비상 admin 비밀번호 | AppSetup wizard 에서 별도 설정 — Web admin 과 무관 |

---

## 4. 마이그레이션 PR 시퀀스

| PR | 범위 | 호환성 |
|----|------|--------|
| **PR1 (본 PR)** | 설계 문서 + `WebAuthClient` 인프라 + 단위 테스트. **호출자 변경 없음** — 인프라만. | 무영향 |
| **PR2** | `UserService` 에 `AuthenticateViaWebAsync(username, password)` 추가. 기존 로컬 인증 흐름 그대로. AppSetup `WebSsoEnabled` 옵션 (기본 false). | feature flag — 활성 안 함 |
| **PR3** | "비상 로컬 admin" 분리. `local-admin` 계정만 권한 제한 + AuditLog 강제. 기존 admin 마이그레이션 절차 (1회) | 마이그레이션 PR |
| **PR4** | AppSetup wizard 에 "Web SSO" 옵션 + 비상 로컬 admin 비밀번호 별도 입력 UI | 운영 UI |
| **PR5** | 운영 가이드 (msi_build_guide §10 SSO 운영) + memory 갱신 + AppSetup 자동 검증 | 문서 |

각 PR 은 독립 머지 가능. 운영 활성화는 PR5 이후 AppSetup 에서 `WebSsoEnabled=true` 설정 시점.

---

## 5. 호환성 / 롤백 전략

### 5.1 호환성 표
| 단계 | VMS 로컬 Admin | Web Admin | SSO |
|------|---------------|-----------|-----|
| PR1~4 머지 후, SSO=false | 사용 가능 (현행) | 사용 가능 | 미적용 |
| SSO=true (PR4 옵션 활성) | local-admin 만 (제한 권한) | master 계정 | 활성 — VMS Admin/Manager 가 Web 자격으로 로그인 |
| Web 도달 불가 시 | local-admin 폴백 가능 (AuditLog) | — | 자동 폴백 |

### 5.2 롤백
운영 중 SSO 문제 발생 시:
1. AppSetup wizard 에서 `WebSsoEnabled=false` 토글 → 즉시 로컬 인증 복원
2. 기존 로컬 Users 테이블은 마이그레이션 시점 그대로 유지 (PR3 가 삭제 안 함, deactivate 만)
3. 재활성화 (re-enable) 시 비밀번호 변경 없이 즉시 사용 가능

---

## 6. AuditLog 설계

| 이벤트 | Category | Outcome | details |
|--------|----------|---------|---------|
| VMS SSO 로그인 성공 | Authentication | Success | `"Web JWT, user={username}, traceId={...}"` |
| VMS SSO 로그인 실패 (Web 401) | Authentication | Denied | `"Web rejected credentials, user={username}"` |
| VMS SSO 도달 실패 → 폴백 | Authentication | Failure | `"Web unreachable ({error}), local fallback attempted"` |
| 비상 local-admin 사용 | Authentication | Success | `"Local fallback used (Web unreachable), elevated activity not permitted"` |
| 비상 local-admin 운영 변경 시도 | Authorization | Denied | `"Local fallback cannot modify operational state"` |

Web 측 AuditInterceptor 가 동일 traceId 로 기록 → 양 솔루션 로그 병합 시 일관 추적 가능 (운영 가이드에 jq 예시 추가 예정).

---

## 7. 테스트 계획

### 단위 테스트 (PR1~3)
- `WebAuthClient`: 200 / 401 / 5xx / timeout / network error 각각 mock HttpMessageHandler
- `UserService.AuthenticateViaWebAsync`: SSO 성공 / 실패 / 폴백 시도 / 폴백 권한 제한

### 통합 테스트 (PR3~4)
- 실제 `IntegrationTestFactory` 의 Web `/api/auth/login` 호출 (in-memory) — JWT 발급 흐름 정합
- AuditLog 양쪽 (VMS + Web) 모두 기록 검증

### 수동 검증 (PR5)
- 운영 PC 시뮬레이션: Web Service stop → VMS local-admin 로그인 → 운영 변경 시도 → 거부 확인
- Web Service restart → 일반 admin 로그인 → 정상

---

## 8. 일정 / 우선순위

본 마이그레이션은 **인증 신청 후 운영 안정화 단계** 작업 — 신청 자체에는 영향 없음.

권장 진행:
- **즉시**: PR1 (인프라만, 위험 zero)
- **인증 신청 직후**: PR2~3 (활성화 전 코드 준비)
- **첫 운영 사이트 인계 시점**: PR4~5 (실제 사이트 운영 정책 결정 후 활성화)

---

## 9. 참고 자료

- `docs/gs/gs_compliance_overview_v1.0.md` §2.1.1 인증 (현행 BCrypt 정책)
- `docs/gs/GS_History.md` 47~51 PR 시간순
- BODA.VMS.Web `docs/GS_Certification_Baseline.md` v1.1 §3.1 JWT 외부화
- 본 PR 시퀀스: PR1 (본 PR), PR2~5 (별도 추적)

---

**문서 끝.**
