# 운영 환경 회귀 테스트 가이드 — v1.2

> ⚠️ **사문서 (2026-08-14 판정)** — v1.2 마일스톤(2026-05-29) 시점 기록으로 보존하며,
> 현행 절차로 사용하지 말 것. 주요 불일치: §1.1 CI artifact MSI 는 이제 **현장 배포 금지**
> (Mech-Eye SDK·Web 미포함, [release_manual_procedure.md](release_manual_procedure.md) 사고 규칙)
> · MSI 파일명 `VMS-<버전>.msi` · 설치 경로 `C:\Program Files\VASIM\...` · securityMode 누락 시
> Development 폴백이 아니라 **부팅 차단** · 로그인은 username/password + Web SSO · 감사 9 카테고리.
> **현행 설치·검증 절차: [field_install_checklist.md](field_install_checklist.md) +
> [msi_build_guide.md](msi_build_guide.md) §9~§13.**

본 가이드는 v1.1 → v1.2 마일스톤 (보안 PR1~3 / 감사 PR4~6,9 / 도움말 PR10~11 / 단위 테스트 PR7~8 / 통합 테스트 PR12~15 / MSI 자동화 PR16) 의 변경을 운영 환경에서 직접 검증하는 절차입니다.

CI 가 영역별 207 → 282 테스트로 회귀를 자동 차단하지만, **UI / 외부 시스템 / Production 보안 모드 / 실제 PLC·IO 보드 연동**은 코드만으로 검증 불가능합니다. 본 문서의 항목은 모두 그러한 수동 검증 대상입니다.

대상: QA / 운영 엔지니어 / GS 인증 심사 시연 담당.

---

## 1. 인스톨러 다운로드 및 설치

### 1.1 GitHub Actions Artifacts 에서 다운로드 (PR #44 도입)

CI 가 통과한 모든 커밋의 MSI 가 30일간 자동 보관됩니다.

1. https://github.com/j2ase1862/VMS/actions 접속
2. 최신 `Build & Test` 실행 → `success` 항목 클릭
3. 페이지 하단 **Artifacts** 섹션에서 `BODA-VMS-installer-{git-sha}` 다운로드 (≈ 540MB ZIP)
4. ZIP 해제 → `VMS.MasterSetup.msi` 추출

### 1.2 설치

1. 관리자 권한 PowerShell / 명령 프롬프트에서 `VMS.MasterSetup.msi` 실행
2. 설치 경로: `C:\Program Files\BODA Vision AI\BODA Vision System\`
3. 바탕화면 / 시작메뉴 바로가기 자동 생성 확인
4. 첫 실행 시 `%LocalAppData%\BODA VISION AI\` 디렉토리 자동 생성 확인

---

## 2. 사전 준비

### 2.1 system_config.json 작성 (AppSetup 마법사)

`%LocalAppData%\BODA VISION AI\system_config.json` 이 없으면 AppSetup 마법사로 작성:

```bash
# 시작 메뉴 → BODA AppSetup 실행
```

마법사로 설정해야 할 항목:
- **Application Settings** — Application Name, System IP
- **Camera Mode** — Live (실 카메라) / Virtual (테스트)
- **PLC Settings** — Vendor (Modbus/Mitsubishi/Siemens/Omron/LS) + 통신 파라미터
- **IO Boards** — ADLink PCI-743x / Advantech PCI-17xx 등 (선택)
- **Robot Settings** — UR / Doosan / Jaka 등 (선택)
- **Web Server** — WebServerUrl + ClientIndex (운영지원 시스템 연동 시)

### 2.2 보안 모드 결정

본 가이드의 **항목 5 (보안)** 검증을 위해 `system_config.json` 에 다음 키 직접 편집:

```json
{
  "securityMode": "Production"
}
```

- `Production`: HTTPS 강제, cert 엄격 검증 (운영 권장)
- `Development`: HTTP 허용, 사내망 self-signed 허용 (개발/사내 테스트)
- 키 누락 시 Development 기본값

변경 후 앱 재시작 필요.

---

## 3. 회귀 테스트 체크리스트

각 항목은 **목적 / 절차 / 통과 기준** 으로 구성. 통과 시 ✅ 체크.

### 3.1 IO 디바이스 통합 (사이클 외 + PR ↓)

#### [ ] 3.1.1 AppSetup IO 보드 추가 → Sequence Editor 콤보 노출
- **목적**: PR #41 의 IO 디바이스 추상화가 Sequence Editor 콤보에 정상 노출되는지
- **절차**:
  1. AppSetup → Page 6 (IO 보드) 에서 ADLink 또는 Advantech 보드 1 개 추가, 저장
  2. VisionSetup 실행 → Sequence Editor → InputCheck 노드 추가
  3. 디바이스 콤보 클릭
- **통과 기준**: 콤보에 `MainPLC [PLC]` + 추가한 보드 (`ADLink_1 [ADLink]` 등) 2 개 이상 표시

#### [ ] 3.1.2 디바이스 선택 → 주소 입력 힌트 분기
- **절차**:
  1. 위 콤보에서 PLC 선택 → 주소 라벨이 `PLC 주소 (예: X0, M100, D200, %MX0.0)` 인지
  2. IO 보드 선택 → 주소 라벨이 `채널 번호 (0 ~ N-1, 예: 0, 5, 15)` 로 자동 변경되는지
- **통과 기준**: 디바이스 종류에 따라 라벨 자동 변경

#### [ ] 3.1.3 OutputAction 의 Bit-only 제한
- **절차**: OutputAction 노드 추가 → IO 보드 선택 → 데이터 타입 콤보 확인
- **통과 기준**: PLC 선택 시 Bit/Word/Int/Float 모두 노출, IO 보드 선택 시 Bit 만 노출 (Word/Float 자동 숨김)

#### [ ] 3.1.4 Tool 매핑 → IO 보드 채널 출력 라우팅
- **절차**:
  1. 비전 도구 1 개 선택 → ParameterControl 의 "PLC Output" 섹션
  2. "+ Add PLC Mapping" → Device 콤보에서 IO 보드 선택
  3. Channel 입력란에 채널 번호 입력 (예: `5`)
  4. 시퀀스 실행
- **통과 기준**: 도구 결과가 IO 보드 채널로 정상 출력 (해당 출력 LED / 멀티미터로 확인)

#### [ ] 3.1.5 IO 보드 모니터 패널 분리
- **절차**: Sequence Editor → PLC Monitor 열기 → 하단의 "IO Board Monitor" 섹션 확인
- **통과 기준**: PLC 모니터와 별도 섹션으로 IO 보드 채널 표시 (구성된 채널만)

### 3.2 Expert Mode (사이클 외)

#### [ ] 3.2.1 Tool Settings 헤더 Toggle Switch
- **절차**: VisionSetup → 도구 추가 → Tool Settings 패널 헤더 우측
- **통과 기준**: 둥근 pill 토글 스위치 표시 (체크박스 아님), 클릭 시 0.12 초 슬라이딩 애니메이션

#### [ ] 3.2.2 도구 타입별 영구 저장
- **절차**:
  1. BlobTool → 토글 ON
  2. 앱 종료 후 재실행 → BlobTool 다시 선택 → 토글 상태 확인
- **통과 기준**: ON 상태 유지 (`%LocalAppData%\BODA VISION AI\expert_mode.json` 에 저장 확인)

#### [ ] 3.2.3 도구 타입별 독립
- **절차**: BlobTool 토글 ON 상태에서 YoloSegTool 선택
- **통과 기준**: YoloSegTool 의 토글은 OFF 로 표시 (도구 타입별 독립)

#### [ ] 3.2.4 Expert 분류 파라미터 가시성
- **절차**: CaliperTool 선택 → Expert OFF / ON 비교
- **통과 기준**:
  - OFF: Polarity / Edge Threshold / Filter Half Width / Mode 만 표시
  - ON: 추가로 Projection Width / Search Axis / Max Edges + Signal Processing 섹션 표시

### 3.3 보안 모드 (PR1)

#### [ ] 3.3.1 Development 모드 — HTTP 허용
- **절차**:
  1. `system_config.json` 에 `"securityMode": "Development"` + `"webServerUrl": "http://localhost:5292"`
  2. 앱 실행 → 정상 동작
- **통과 기준**: 앱이 실행되고 Web 연동 시도 (성공 여부는 서버 상태에 따라)

#### [ ] 3.3.2 Production 모드 — HTTP 거부
- **절차**:
  1. `"securityMode": "Production"` 으로 변경 + `"webServerUrl": "http://localhost:5292"` 유지
  2. 앱 실행
- **통과 기준**: 시작 시 보안 정책 위반 예외 발생 (스플래시 또는 에러 다이얼로그), 사용자에게 "Production 모드에서 HTTPS 가 필수" 메시지 표시

#### [ ] 3.3.3 Production 모드 — HTTPS 허용 + cert 검증
- **절차**:
  1. `"webServerUrl": "https://정식CA-도메인"` 으로 변경
  2. 앱 실행 → 정상 시작
- **통과 기준**: 정상 시작 (cert 검증 통과). self-signed 도메인이면 거부되는지도 추가 확인 권장.

### 3.4 자격증명 + DB ACL (PR2)

#### [ ] 3.4.1 비밀번호 입력 검증
- **절차**: 로그인 화면에서 사용자명에 개행/탭 포함 (`admin\n`), PIN 빈값 시도
- **통과 기준**: 둘 다 즉시 "사번 또는 PIN이 올바르지 않습니다" 거부 (DB 호출 없이)

#### [ ] 3.4.2 BodaVision.db 파일 ACL
- **절차**:
  1. `%LocalAppData%\BODA VISION AI\BodaVision.db` 마우스 우클릭 → 속성 → 보안 탭
  2. 다른 사용자 계정으로 로그인 시도 (가능한 환경에서)
- **통과 기준**: 현재 사용자만 FullControl, 다른 사용자/그룹 권한 없음 (상속 차단)

#### [ ] 3.4.3 PIN 로그 노출 없음
- **절차**: 로그인 후 시스템 로그 (앱 내 또는 디버그 로그) 검토
- **통과 기준**: 어떤 로그에도 PIN 평문이 노출되지 않음 (사용자명 / 결과만 기록)

### 3.5 입력 검증 (PR3)

#### [ ] 3.5.1 PLC 주소 형식 거부
- **절차**: AppSetup 또는 시퀀스 노드에서 잘못된 PLC 주소 입력:
  - 64 자 초과 문자열
  - 음수 / 비현실적 큰 값 (Modbus `4x99999`)
- **통과 기준**: 저장/실행 시점에 에러 메시지로 거부 (UI 또는 로그)

#### [ ] 3.5.2 잘못된 ONNX 모델 경로 거부
- **절차**: OCR Tool → CustomDetModelPath 에 `.exe` 또는 `..\..\windows\system32\foo.exe` 입력
- **통과 기준**: 도구 실행 시 ArgumentException → 도구가 NG 로 처리, 시스템 로그에 입력 검증 위반 기록

#### [ ] 3.5.3 HTTP 응답 크기 상한
- **절차**: (선택, 어려운 항목) Web 서버에서 의도적으로 큰 응답 (예: 50MB JSON) 보내기
- **통과 기준**: HttpClient 가 거부 (10MB 기본 상한), 응답 처리 실패로 로그 기록

### 3.6 감사 로깅 (PR4~6, PR9)

#### [ ] 3.6.1 audit/YYYY-MM-DD.jsonl 자동 생성
- **절차**: 로그인 1회, 레시피 1개 저장 → `%LocalAppData%\BODA VISION AI\audit\` 디렉토리 확인
- **통과 기준**: 오늘 일자 파일 (`2026-MM-DD.jsonl`) 생성, 1줄 = 1 JSON entry

#### [ ] 3.6.2 8 카테고리 활성 확인
- **절차**: 다음 작업 실행 후 `2026-MM-DD.jsonl` 에 해당 entry 존재 확인:
  1. 로그인/로그아웃 (`Authentication`)
  2. 권한 없는 메뉴 클릭 (`Authorization` / `PermissionDenied`)
  3. 새 사용자 생성 (`UserManagement`)
  4. 레시피 저장 (`RecipeChange`)
  5. 시퀀스 시작/중지 (`SequenceControl`)
  6. NG 검사 발생 (`Inspection`)
  7. AppSetup 저장 (`Configuration`)
  8. Production 모드에서 HTTP URL 사용 시도 (`Security` / `InsecureHttpBlocked`)
- **통과 기준**: 모든 항목이 JSONL 의 `category` 필드와 정확히 매치

#### [ ] 3.6.3 Audit Log Viewer 동작
- **절차**:
  1. Admin 계정 로그인 → MainWindow 우측 헤더에 감사 로그 아이콘 버튼 표시
  2. 클릭 → AuditLogViewerWindow 열림
  3. 일자 범위 / 카테고리 / Outcome 필터 동작 확인
  4. 검색어 입력 → 결과 즉시 필터링
  5. "📥 CSV 내보내기" 클릭 → `audit_YYYYMMDD_YYYYMMDD.csv` 저장
- **통과 기준**: 모든 필터 정상 동작, CSV 파일이 Excel 에서 열림 + 데이터 무결성 유지

#### [ ] 3.6.4 Operator 권한으로는 Audit Viewer 진입 불가
- **절차**: Operator 등급 사용자로 로그인 → 감사 로그 아이콘 버튼 가시성
- **통과 기준**: 버튼이 숨김 처리 (Admin 전용)

### 3.7 in-app 도움말 (PR10~11)

#### [ ] 3.7.1 도구 파라미터 도움말
- **절차**: 비전 도구 (예: BlobTool) 선택 → 파라미터 옆 `?` 아이콘 호버
- **통과 기준**: 0.3 초 후 도움말 팝업 (Cognex 동등 도구 + 설명 + 사용 예시) 표시

#### [ ] 3.7.2 Sequence 노드 헤더 도움말
- **절차**: Sequence Editor → InputCheck 노드 선택 → 우측 속성 패널 헤더 `?` 아이콘 호버
- **통과 기준**: "Input Check (입력 신호 검사)" 도움말 팝업

#### [ ] 3.7.3 Common Settings 도움말
- **절차**: 임의 도구 선택 → "Enabled" 체크박스 옆 `?` 아이콘 호버
- **통과 기준**: "도구 활성화 여부..." 도움말 팝업

### 3.8 통합 테스트 영역 (PR12~15)

CI 가 자동 회귀를 차단하지만 운영 환경 확인 항목:

#### [ ] 3.8.1 레시피 저장/로드 (PR13)
- **절차**: 레시피 신규 작성 → 저장 → 종료 → 재실행 → 로드 → 변경된 도구 / 파라미터 / 시퀀스가 그대로 복원
- **통과 기준**: 100% 라운드트립 (PLC 주소 / Tool 매핑 / Expert Mode 상태 / Sequence Editor 노드까지)

#### [ ] 3.8.2 설정 저장 (PR14)
- **절차**: AppSetup 에서 PLC IP 변경 → 저장 → 재실행 → 변경된 IP 가 그대로 로드
- **통과 기준**: 라운드트립

#### [ ] 3.8.3 사용자 관리 (PR15)
- **절차**: Admin 으로 로그인 → User Management 열기 → 신규 Engineer 사용자 추가 → 로그아웃 → 신규 사용자로 로그인
- **통과 기준**: 로그인 성공, EditRecipe / SystemConfiguration 메뉴 접근 가능, ManageUsers 메뉴는 숨김/거부

---

## 4. 이슈 발견 시 보고 절차

1. **재현 가능성 확인**: 같은 절차로 2~3 회 반복 시도
2. **로그 수집**:
   - `%LocalAppData%\BODA VISION AI\audit\2026-MM-DD.jsonl` (감사 로그)
   - 앱 내 시스템 로그 (헤더의 알림 아이콘)
   - Windows Event Viewer → 응용 프로그램 로그 (앱 충돌 시)
3. **GitHub Issue 작성**: https://github.com/j2ase1862/VMS/issues/new
   - 제목 형식: `[Regression v1.2] 영역 — 한 줄 요약`
   - 본문에 포함:
     - 이 가이드의 항목 번호 (예: `3.5.2`)
     - 재현 절차 (단계별)
     - 기대 동작 / 실제 동작
     - 환경 (Windows 버전, MSI git SHA, securityMode)
     - 첨부: JSONL / 스크린샷
4. **긴급 (보안 / 데이터 손실)**: GitHub Issue + 운영 책임자 직접 통보

---

## 5. 합격 기준

| 영역 | 통과 기준 |
|------|----------|
| IO 디바이스 통합 (3.1) | 5/5 |
| Expert Mode (3.2) | 4/4 |
| 보안 모드 (3.3) | 3/3 — Production HTTP 거부는 GS 인증 필수 |
| 자격증명 (3.4) | 3/3 |
| 입력 검증 (3.5) | 2/3 — 3.5.3 은 선택 |
| 감사 로깅 (3.6) | 4/4 — GS 인증 핵심 |
| in-app 도움말 (3.7) | 3/3 |
| 통합 테스트 영역 (3.8) | 3/3 |

**총 26 항목 중 25 항목 이상 통과 시 v1.2 릴리스 합격**.

---

## 6. 변경 이력

| 버전 | 일자 | 작성자 | 비고 |
|------|------|--------|------|
| v1.2 | 2026-05-29 | Claude Code | PR1~16 통합 회귀 가이드 신규 |

`master` 의 `HEAD` 가 본 가이드와 동기화되는 시점:
- PR #42 (보안 + 도움말) 머지 완료 (2026-05-29)
- PR #43 (통합 테스트) 머지 완료 (2026-05-29)
- PR #44 (MSI 자동화) 머지 완료 (2026-05-29)
