# 다음 진행 사항 (Next Steps)

작성일: 2026-06-01
대상 브랜치: `feat/dl-tool-enhancements` (master 와 동기 — PR1~44 머지 완료)

---

## 0. 현재 상태 요약

| 항목 | 값 |
|---|---|
| 마지막 master 커밋 | `6d557c6` (PR44 MSI 가이드 + race fix) |
| 누적 테스트 수 | 486 (CI 기준) |
| 빌드 경고 | 0 |
| MSI artifact | `BODA-VMS-installer-{git-sha}` (30일 보관) |
| GS 인증 자산 | 보안 8종 / Health / Backup / SupportPackage / 보존 4종 + UI / 9 감사 카테고리 / Admin UI 6 (chromeless) / 문서 7종 + LICENSE + NOTICE |

---

## 1. 우선 확인 (사용자 액션)

### A. 다른 PC 에서 MSI 설치 / 동작 검증
- [ ] `BODA-VMS-installer-{sha}.msi` 다운로드 (GitHub Actions Artifacts)
- [ ] 정상 설치 / 데스크탑 단축키 / 시작 메뉴 단축키 확인
- [ ] 첫 실행 — Admin 로그인 (admin / admin123)
- [ ] 헤더 More(⋮) 드롭다운 → 6 Admin 항목 확인:
  - Audit Log Viewer
  - Health Check
  - Backup / Restore
  - Auto Backup Settings
  - Retention Settings (전역 / 카테고리 / 프리셋 / Preview / Export)
  - Support Package...
- [ ] 각 윈도우 chromeless 타이틀바 + 닫기 / 최대화 동작 확인
- [ ] 다크 DatePicker (Audit Viewer 의 From/To) 확인
- [ ] 운영 회귀 가이드 v1.2 (`docs/manual_regression_v1.2.md`) 통과
- [ ] 제거 (msiexec /x) 후 잔여 파일 / 단축키 정리 확인

### B. 발견 시 보고
- `LICENSE` / `gs_distribution_policy.md` §3 EULA 템플릿 — 법무 검토 (외부 첫 배포 전 필수)

---

## 2. 다음 작업 후보 (재개 시 진행)

### T. RecentInspections 영속화 옵션
- 현재: `RecentInspectionsService` 는 in-memory 200 건 circular — 프로세스 재시작 시 손실
- 목표: 옵션으로 SQLite 또는 JSONL 영속화 + 검색 / export
- 범위: Service + 옵션 + 테스트 (~3-4h)
- 위치: `VMS/Services/RecentInspectionsService.cs`

### U. Audit Log Viewer Outcome chip 스타일
- 현재: Outcome 컬럼이 text + FontWeight 만 다른 색상
- 목표: 작은 둥근 chip (Success / Failure / Denied) — 가독성↑
- 범위: `AuditLogViewerWindow.xaml` 의 DataGridTextColumn → DataGridTemplateColumn 으로 교체 (~1-2h)

### Z. 보존 정책 변경 이력 시간 흐름 추적
- 현재: `RetentionConfigSaved` 감사 이벤트 단발
- 목표: 보존 설정 변경 히스토리 별도 timeline 뷰 (언제 누가 어떤 값을 어디로 바꿨는지)
- 데이터 원천: 기존 audit JSONL 의 `Configuration · RetentionConfigSaved` 필터
- 범위: 새 윈도우 또는 RetentionSettings 안 expander (~3-4h)

### AA. 보존 정책 dry-run 차등 비교
- 현재: Preview 는 단일 시점 결과
- 목표: 현재 적용된 정책과 변경 적용 후 정책 차이 (delta) 표시
- 예: "현재 설정으로 5 파일 / 100 라인 영향 → 새 설정으로 12 파일 / 250 라인 영향 (delta +7 / +150)"
- 범위: 2 회 PreviewService 호출 + delta 계산 + UI (~2h)

### BB. 버전 자동 증가 자동화
- 현재: `VMS.MasterSetup/Package.wxs` 의 `Version="1.0.0.0"` 수동 변경
- 목표: CI 또는 빌드 스크립트가 git tag / SemVer 기반 자동 업데이트
- 범위: PowerShell 스크립트 + workflow 조정 (~2h)

### CC. AppSetup 마법사에 보안 / 보존 / 자동백업 설정 통합
- 현재: 운영자는 Tools 메뉴 5 다이얼로그 흩어진 설정 (보안 모드 / 보존 / 자동백업)
- 목표: VMS.AppSetup 마법사에 1 step 추가 — 사이트 첫 설치 시 일괄 입력
- 범위: AppSetup MainWindow.xaml 새 페이지 + JsonNode 저장 (~4h)

---

## 3. 후속 강화 후보 (장기)

| 항목 | 의도 | 비고 |
|---|---|---|
| MSI 코드 서명 적용 | 외부 첫 배포 시 SmartScreen 우회 | 인증서 발급 후 진행 — `gs_msi_code_signing_guide.md` (PR24) 절차 적용 |
| SIEM 외부 전송 활성 | 다중 사이트 통합 모니터링 | `gs_audit_siem_integration_guide.md` (PR25) 절차 적용 |
| MSI 코드 서명 자동화 (CI) | PR 머지 후 자동 서명된 MSI 산출 | EV 인증서 + Azure Key Vault 필요 |
| 실 운영 회귀 (manual_regression_v1.2) | GS 심사 직전 사이트 전체 검증 | 26 항목 체크리스트 |

---

## 4. 알려진 제약 / 후속 검토

- **LICENSE / EULA DRAFT** — 외부 배포 전 법무 검토 필수 (한국 약관규제법 / 수출국 적합성)
- **YOLOv8 모델 가중치** — AGPL-3.0, 상업적 사용 시 통합사가 별도 라이선스 확인
- **벤더 SDK 옵션 빌드** — Mech-Mind / Basler / Matrox / ADLink / Advantech 활성화 시 사이트마다 별도 EULA 동의 필요
- **로컬 환경 의존 테스트** — `AutoBackupOptions.LoadFromAppData_ReturnsDisabledByDefault` 가 로컬 system_config.json 상태에 따라 실패 가능. CI runner 는 clean → 통과. 추후 임시 디렉토리 격리 fix 가능

---

## 5. 재개 시 권장 순서

1. **A. MSI 검증 결과 보고** — 발견된 시각 / 동작 이슈 hot fix
2. **U. Outcome chip** — 빠른 UI 개선 (~1-2h)
3. **T. RecentInspections 영속화** — 의미 있는 기능 추가
4. **AA. dry-run delta** — 운영자 만족도 직접 영향
5. **Z. 보존 변경 이력** — GS 추적성 강화

---

## 변경 이력
| 버전 | 날짜 | 변경 |
|---|---|---|
| v1.0 | 2026-06-01 | 초안 — MSI 검증 대기 중 후속 작업 후보 정리 |
