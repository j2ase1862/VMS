# GS 인증 관련 문서

VMS 솔루션 (BODA Vision AI) 의 한국 TTA GS(Good Software) / ISO/IEC 25051 인증 대응 자료. 심사관 / SI 인계 / 운영 인수에 사용.

## 폴더 구조

```
docs/gs/
├── README.md            ← 이 파일 (색인)
├── VMS_*.docx           ← GS 제출물 5종 (아래 생성기로 재생성)
├── screenshots/         ← 매뉴얼·제품설명서에 삽입되는 선별 캡처
├── guides/              ← 인증·운영 가이드 문서 (md)
└── pipeline/            ← 제출물 docx 생성 스크립트 + 중간 산출물
```

## 제출물 (docx)

| 파일 | 생성기 |
|------|--------|
| `VMS_사용자매뉴얼_v1.1.docx` (+ 동명 `.pdf` 내보내기본) | `pipeline/parse_manual.py` → `pipeline/gen_user_manual.js` |
| `VMS_제품설명서_v1.0.docx` | `pipeline/gen_product_description.js` |
| `VMS_OSS_라이선스_확인서_v1.0.docx` `VMS_GS_신청서_템플릿_v1.0.docx` `VMS_GS_신청_체크리스트_v1.0.docx` | `pipeline/gen_gs_supporting_docs.js` |

**docx 를 직접 편집하지 말 것** — 재생성 시 유실된다. 매뉴얼 본문은
`docs/manuals/BODA-VMS-User-Manual.html`, 그림·표는 생성기 스크립트가 진실이다.

재생성:

```powershell
cd docs/gs/pipeline
python parse_manual.py    # 매뉴얼 HTML → _manual_blocks.json
node gen_user_manual.js   # → ../VMS_사용자매뉴얼_v1.1.docx
```

## 매뉴얼 반영 대기 — SW 업데이트 로그

사용자에게 보이는 기능이 master 에 머지되면 **그때마다 아래 표에 한 줄 기재**한다.
매뉴얼을 한꺼번에 갱신할 때 코드를 다시 파헤치지 않기 위한 장부다.

- 기재 시점: 기능 PR 머지 직후 (릴리즈 발행 시 몰아서 적어도 됨)
- 기재 내용: PR 번호 · 매뉴얼에 들어갈 요지(사용자 관점 한 줄) · 들어갈 절(§)
- 매뉴얼 갱신 시: HTML 본문(`docs/manuals/BODA-VMS-User-Manual.html`)에 반영하고
  §8.4 변경 이력에 요약 추가 → docx 재생성 → 아래 표 비우기 (반영 완료 기준선 갱신)

**반영 완료 기준선: SW v1.13.0 (v1.5.13~v1.13.0 누적분 전체 + Web v1.5.0) — 2026-08-24 본문 + 스크린샷 + docx 재생성 완료**
(스크린샷: MSI 마법사 70_msi_welcome 신규, Lot 콤보 hdr_ctx_chip, Image Save 창, Web 생산 이력·작업지시(데모 시드 데이터), 01/05 메인·사이드 패널, Roller 섹션. 데모 캡처 절차는 세션 스크립트 web_capture_partial.js/seed_demo.js 참고 — 운영 DB 무접촉)

| PR | 매뉴얼에 들어갈 내용 | 대상 절 |
|----|--------------------|--------|
| (잔여) | 3D 점군 뷰어 조작 스크린샷 갱신 (#275 밀도 버튼·툴바) — **실점군 로드 상태에서 수동 캡처 필요** (자동 캡처 도구는 점군 화면 미포함, 시뮬레이션 점군은 부적합 판단). 본문 서술은 반영 완료 | §3.9 (점군 화면 조작) |
| (대기) | **불량률 예측 헤더 칩 "다음 1시간: …"** — 헤더에 다음 1시간 예상 NG율/모델 상태 칩 상시 표시(모델 미등록 시 "모델 미등록"). 매뉴얼 미기술 — §3.2 헤더 요소 + Web 불량률 예측 화면과의 관계 설명 필요 | §3.2(헤더), §4.16(Reports) |
| #358 #359 #360 (+Web #87) | **SW 라이선스 (1~3단계)** — 3단계: 서버-다수 클라이언트 구성에서 클라이언트는 Web 서버 좌석 임대로 유효(서버만 라이선스 설치, 72h 유예). 사용자 가시 변화는 상태 메시지뿐 — AppSetup 7페이지가 "Security & License"로 바뀌고 SW 라이선스 카드 추가: 이 PC 지문 코드 표시·복사, 라이선스 파일(license.lic) 가져오기(USB 반입 → 설치, 권한 부족 시 UAC), 상태 표시(정상/미설치/만료 등). 현 단계는 라이선스 없어도 경고만(호환 모드). 캡처는 --capture-fullpage 의 Window_P7 | 설치·초기 구성 절 (Security Mode 절 확장 + 라이선스 활성화 신설) |

## guides/ — 가이드 문서

| 파일 | 역할 | 작성 시점 |
|------|------|----------|
| [`GS_History.md`](guides/GS_History.md) | **시간순 히스토리** — 45 PR 의 14 phase 분류, Web 짝 매핑, 인증 시점 운영 스냅샷 | v1.0 (2026-06-04) |
| [`gs_compliance_overview_v1.0.md`](guides/gs_compliance_overview_v1.0.md) | **ISO/IEC 25051 항목별 매핑** — 구현 위치 / 검증 절차 / 코드 경로. §5.10 프리셋 표는 `ManualPresetConsistencyTests` 가 자동 대조 | PR21 |
| [`gs_msi_code_signing_guide.md`](guides/gs_msi_code_signing_guide.md) | MSI 코드 서명 (Authenticode) 운영 절차 | PR24 |
| [`gs_audit_siem_integration_guide.md`](guides/gs_audit_siem_integration_guide.md) | 감사 로그 SIEM 외부 전송 통합 가이드 | PR25 |
| [`gs_distribution_policy.md`](guides/gs_distribution_policy.md) | 라이선스 / NOTICE / 배포 정책 (루트의 `LICENSE`, `NOTICE` 와 짝) | PR26 |
| [`SSO_Migration_Plan.md`](guides/SSO_Migration_Plan.md) | VMS ↔ Web SSO 통합 마이그레이션 설계 (구현 완료 — 이력 참고) | PR1~5 |

## pipeline/ — 생성 스크립트

| 파일 | 역할 |
|------|------|
| `parse_manual.py` | 매뉴얼 HTML → `_manual_blocks.json` |
| `gen_user_manual.js` | 사용자매뉴얼 docx 생성 (그림 앵커 POST · 마법사/관리자 다이얼로그 섹션 포함) |
| `gen_product_description.js` | 제품설명서 docx 생성 |
| `gen_gs_supporting_docs.js` | OSS 확인서 / 신청서 템플릿 / 체크리스트 docx 생성 |
| `extract_tool_params.py` | VisionSetup ToolSettings XAML → `_tool_params.json` (매뉴얼 도구 파라미터 표) |
| `web_capture.js` | BODA.VMS.Web 화면 캡처 (Chrome DevTools Protocol) → `../screenshots/` |
| `_gen_vms_gs_schedule.py` | GS 인증 세부일정 보고서 xlsx 생성 (2026-07-08 확정본) |
| `_gen_verification_checklist.py` | GS 자체검증 체크리스트 xlsx 생성 |
| `_manual_blocks.json` `_tool_params.json` | 중간 산출물 (재생성 가능하지만 diff 추적을 위해 커밋) |

## 컨트롤 캡처 도구 (UI 변경 시 재캡처)

| 앱 | 명령 | 산출 |
|----|------|------|
| VMS.AppSetup | `VMS.AppSetup.exe --capture-controls [폴더]` / `--capture-fullpage` | 마법사 7페이지 컨트롤 (7페이지 = Security Mode) / 2·5단계 풀페이지 |
| VMS.VisionSetup | `--capture-controls` / `--capture-fullpage` / `--capture-toolpanels` / `--capture-dialogs` | MainView 컨트롤 / 전체화면 / 툴 패널 34종 / 다이얼로그 |
| VMS | `VMS.exe --capture-controls [폴더]` / `--capture-dialogs` | 헤더 칩·사이드 패널 (장면 3개) / 관리자 다이얼로그 |
| VMS.DeepLearning | `VMS.DeepLearning.exe --capture-controls [폴더]` | 라벨링/학습 화면 (Detection·Segmentation·Anomaly 장면 3개 + 섹션별 컴포지트) |

- 전부 `#if DEBUG` 전용 (Release 무영향). Debug 빌드 후 실행. 원본 출력은 `docs/control-capture/`.
- 매뉴얼에 쓰는 선별본은 `screenshots/{appsetup_ctl, visionsetup_ctl, vms_ctl, deeplearning_ctl}/` 에 커밋됨.
- UI 를 바꾸면 해당 앱 캡처 재실행 → 선별본 교체 → docx 재생성.

## 검증 렌더 (docx 육안 확인)

Word COM 으로 docx→XPS 변환(`ExportAsFixedFormat` 포맷 18) → XPS fpage 는 UTF-16 으로 읽고
`UnicodeString` 속성을 이어붙여 텍스트 검색(글리프 런으로 분절되어 있음) → 대상 페이지만
`docs/control-capture/_work/render_xps.ps1` 로 PNG 렌더 후 육안 확인.

## 짝 솔루션 (Web 서버)

BODA.VMS.Web 측 GS 작업 → `D:\Project\BODA.VMS.Web\docs\GS_Certification_Baseline.md` (v1.1, 609 줄). VMS PR119 ↔ Web PR #10 (X-API-Key) 짝.

## 어느 문서부터 봐야 할까

- **처음 접하는 심사관/SI**: `guides/GS_History.md` (개요 + 흐름) → `guides/gs_compliance_overview_v1.0.md` (구체적 항목)
- **MSI 배포 운영자**: `guides/gs_msi_code_signing_guide.md` + 루트 `docs/msi_build_guide.md`
- **SIEM 통합 담당**: `guides/gs_audit_siem_integration_guide.md`
- **법무/계약**: `guides/gs_distribution_policy.md` + 루트 `LICENSE` / `NOTICE`
