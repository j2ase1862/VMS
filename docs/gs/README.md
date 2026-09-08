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
| `VMS_사용자매뉴얼_v2.0.docx` (+ 동명 `.pdf` 내보내기본) | `pipeline/parse_manual.py` → `pipeline/gen_user_manual.js` |
| `VMS_제품설명서_v1.0.docx` | `pipeline/gen_product_description.js` |
| `VMS_OSS_라이선스_확인서_v1.0.docx` `VMS_GS_신청서_템플릿_v1.0.docx` `VMS_GS_신청_체크리스트_v1.0.docx` | `pipeline/gen_gs_supporting_docs.js` |

**docx 를 직접 편집하지 말 것** — 재생성 시 유실된다. 매뉴얼 본문은
`docs/manuals/BODA-VMS-User-Manual.html`, 그림·표는 생성기 스크립트가 진실이다.

재생성:

```powershell
cd docs/gs/pipeline
python parse_manual.py    # 매뉴얼 HTML → _manual_blocks.json
node gen_user_manual.js   # → ../VMS_사용자매뉴얼_v2.0.docx
```

## 매뉴얼 반영 대기 — SW 업데이트 로그

사용자에게 보이는 기능이 master 에 머지되면 **그때마다 아래 표에 한 줄 기재**한다.
매뉴얼을 한꺼번에 갱신할 때 코드를 다시 파헤치지 않기 위한 장부다.

- 기재 시점: 기능 PR 머지 직후 (릴리즈 발행 시 몰아서 적어도 됨)
- 기재 내용: PR 번호 · 매뉴얼에 들어갈 요지(사용자 관점 한 줄) · 들어갈 절(§)
- 매뉴얼 갱신 시: HTML 본문(`docs/manuals/BODA-VMS-User-Manual.html`)에 반영하고
  §8.4 변경 이력에 요약 추가 → docx 재생성 → 아래 표 비우기 (반영 완료 기준선 갱신)
- **상세 기록은 [`manual_change_history.md`](manual_change_history.md)** — 매뉴얼 검토 기간(2026-09-02~) 동안의
  변경을 화면·문구·스크린샷·대상 절 단위로 풀어 쓴 장부. 아래 표는 요지, 상세는 그 문서 (한꺼번에 반영용)

**반영 완료 기준선: SW v1.29.0 / Web v1.8.0 — 2026-09-04 Match Align 원점·각도(#424) + Web 로그인 유지(Web #96) 반영** (직전 기준선 v1.28.1 / 2026-09-04 단독 모드 로컬 생산이력(#417) 반영) (직전 기준선 v1.24.0 / 2026-09-01 매뉴얼 v2.0 전면 개편 (앱별 11장 구성: 개요→설치→AppSetup→VMS 운전→VisionSetup→DeepLearning→Web→워크플로→관리자→트러블슈팅(5절 확장)→부록. 산출물 `VMS_사용자매뉴얼_v2.0.docx`. 생성기: 캡처는 '첫 문단 뒤' 삽입 규칙 + 삽입/누락 검증 로그 + 마법사 §3.2·관리자 다이얼로그 §4.9 앵커 이동 — ⚠ 앵커 = 4장/5장 h2 제목 문자열, 제목 변경 시 gen_user_manual.js 동기화 필수) (§3.2.9 예측 칩·§3.9 Feature Match 학습/다중 인스턴스·복사 3종·얼라인 템플릿·재연결 창·§4.9 Web 복사·§8.4 이력 반영. ⚠ gen_user_manual.js 의 WIZARD_BEFORE 앵커가 HTML §2.6 제목 문자열 — 제목 변경 시 앵커도 함께)
(직전 기준선 v1.13.0 의 스크린샷 안내는 이력에서 유지 — 데모 캡처 절차 web_capture_partial.js/seed_demo.js, 운영 DB 무접촉)

| PR | 매뉴얼에 들어갈 내용 | 대상 절 |
|----|--------------------|--------|
| #429 | Detection 학습 기본 백본을 D-FINE(Apache 2.0) 으로 전환 — DeepLearning 앱에서 Detection 데이터셋을 열면 자동 스크립트가 `train_dfine.py`(이전 `train_yolo.py`), 사전 준비 pip 목록이 `torch torchvision transformers onnx` 로 바뀜 (ultralytics 불필요). 증강 패널 안내 문구 변경 (mosaic/mixup 은 YOLO 전용). VisionSetup Detection 도구는 D-FINE / YOLO ONNX 를 자동 판별 — 사용자 조작 동일. 상세: manual_change_history.md §9 | §6 (DeepLearning 학습·사전 준비) §5 (Detection 도구) §11.4 |
| #427 | (버그 수정·본문 변경 없음) 회전 ROI(RectangleAffine)로 학습한 Feature Match 가 레시피 재로드 후 [Show ROI] 에서 축 정렬 Rect 로 표시되던 결함 수정 — 이제 각도 그대로 복원. §11.4 변경 이력 한 줄만 | §11.4 |
| (반영 완료 2026-09-04) | #424 Feature Match 재학습 원점 + Match Align 기준 각도 + 재열기 원점 복원 + 재학습 알림 — HTML §5.1 콜아웃 · §5.7 소절 신설 · §11.4 + 신규 그림 `50_fm_retrain_origin.png`(툴 패널 캡처 크롭) + docx 재생성. 상세: manual_change_history.md §8 | §5.1 §5.7 §11.4 |
| (반영 완료 2026-09-04) | Web #96 로그인 유지 체크박스 — HTML §7.1 3번 항목 교체 + §11.4 + `40_web_login.png` 재캡처(v1.8.0 라이브) + docx 재생성. 상세: manual_change_history.md §7 | §7.1 §11.4 |
| (잔여·스크린샷) | 3D 점군 뷰어 조작 스크린샷 갱신 (#275 밀도 버튼·툴바) — **실점군 로드 상태에서 수동 캡처 필요** (자동 캡처 도구는 점군 화면 미포함, 시뮬레이션 점군은 부적합 판단). 본문 서술은 반영 완료 | §3.9 (점군 화면 조작) |
| (잔여·스크린샷) | 안내 창 다크 디자인(#364 #366) — 매뉴얼 내 안내 창 스크린샷 재캡처 (동작 동일, 본문 변경 없음) | 전반 |
| (잔여·스크린샷) | Feature Match 설정 화면 재캡처 — Basic 신규 파라미터(Max Instances·Min Coverage)·[기준 이미지 불러오기] 버튼·마스크 편집기 신 UI(#375)·다중 인스턴스 오버레이(#번호) | §3.9 (Feature Match) |
| (잔여·스크린샷) | Recipe Manager [Duplicate]/[Rename]·Steps 패널 복제 버튼·도구 우클릭 메뉴(#400), VisionSetup 툴바 [Acquire]·연결 토글(#397), 갤러리 얼라인 탭(#395) | §3.9 |
| (잔여·스크린샷) | Web 검사항목 툴바 [레시피 복사]/[이름 변경] (Web #94) | §4.9 |
| (반영 완료 2026-09-04) | #417 단독 모드 로컬 생산이력 1~3단계 — HTML §4.3(생산 이력 조회 창 소절 신설)·§4.5·§7.6·§9.4·§11.3·§11.4 + docx 재생성 + 캡처 `36_dlg_inspection_history.png`(신규)·`28_dlg_retention.png`(교체)·`37_dlg_inspection_history_summary.png`·`38_dlg_inspection_history_pareto.png`(탭 장면, `--capture-dialogs` 자동)·`vms_ctl/sec_recent.png`([전체 이력] 버튼, 재캡처) — 스크린샷 잔여 없음 | §4.3 §4.5 §4.9 §7.6 §9.4 §11.3 §11.4 |
| (반영 완료 2026-09-02) | AppSetup 2단계 재캡처 — 단독 모드 체크박스(#407) + "고급 설정 — Web Client API Key" 접이식 전환 + 설정 마법사 전용 아이콘. 본문·§3.2 표·§11.4 이력·docx 재생성 완료 (capture 도구에 ToggleButton 추가, HelpIcon ? 토글은 제외) | §3.2 |
| (반영 완료 2026-09-04) | 사이드 패널 버튼 아이콘 세로 정렬 수정(시각만) — `vms_ctl/sec_*.png` 전부 재캡처·docx 재생성. 상세: manual_change_history.md §6 | §4.3 (스크린샷) |
| (반영 완료 2026-09-04) | #416 AppSetup 3단계 카메라 카드 입력란 제거 — §3.1 콜아웃 + 생성기 §3.2 표 3행 삭제·안내 카드 행 + §5.3 상호 참조 + §11.4 한 줄, `10_appsetup_step3.png` 재캡처 + `appsetup_ctl/P3_*` 전부 재캡처·재대조(번호 이동), docx 재생성. 상세: manual_change_history.md §1·§2 | §3.1 §3.2 §5.3 §11.4 |
| #369 #370 | **라이선스 발급 GUI LicGen.App + 백업 자동화** — ⚠ 사내 전용(MSI 비동봉·고객 비노출)이라 **사용자 매뉴얼 대상 아님, Admin 매뉴얼만**: 발급 담당자 절차를 CLI 예시에서 LicGen.App 화면 기준으로 교체(발급 탭·발급 대장 탭 비고·백업 배너/[백업 실행] 스크린샷), `licgen backup` USB 2부 규칙·issue 백업 경고 소개. 상세 절차 원본은 docs/license_operations.md (반영 완료) | Admin 매뉴얼 라이선스 절 (§2.6 발급 측 상대편) |

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
