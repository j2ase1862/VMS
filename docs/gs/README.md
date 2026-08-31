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

**반영 완료 기준선: SW v1.13.0 + 라이선스 1~4단계·좌석 UI (VMS #358~#361, Web #87~#89) — 2026-08-25 본문 + 스크린샷 + docx 재생성 완료** (§2.6·§4.18 신설. ⚠ gen_user_manual.js 의 WIZARD_BEFORE 앵커가 HTML §2.6 제목 문자열 — 제목 변경 시 앵커도 함께)
(스크린샷: MSI 마법사 70_msi_welcome 신규, Lot 콤보 hdr_ctx_chip, Image Save 창, Web 생산 이력·작업지시(데모 시드 데이터), 01/05 메인·사이드 패널, Roller 섹션. 데모 캡처 절차는 세션 스크립트 web_capture_partial.js/seed_demo.js 참고 — 운영 DB 무접촉)

| PR | 매뉴얼에 들어갈 내용 | 대상 절 |
|----|--------------------|--------|
| #364 #366 | **안내 창 다크 디자인 통일 (Phase A+B)** — VMS·VisionSetup·AppSetup·DeepLearning·Updater 의 모든 메시지/확인 창이 다크 디자인으로 변경 (동작 동일, WeldTeach 만 잔여 — MSI 제외). 매뉴얼 내 안내 창 언급 스크린샷이 있다면 재캡처 대상 | 전반 (스크린샷 위주) |
| #363 | **미등록 카메라 재연결 창** — 다른 PC 에서 만든 레시피를 열면 "?-n" 경고 대신 재연결 창이 먼저 뜨고, 이 PC 카메라를 선택해 [적용 및 저장]하면 스텝을 그대로 사용 가능. 기존 §3 "다른 PC 에서 만든 레시피" 문단(등록하라고만 안내)을 재연결 창 절차로 교체 필요 | §3.9 부근 (다른 PC 레시피 문단) |
| (잔여) | 3D 점군 뷰어 조작 스크린샷 갱신 (#275 밀도 버튼·툴바) — **실점군 로드 상태에서 수동 캡처 필요** (자동 캡처 도구는 점군 화면 미포함, 시뮬레이션 점군은 부적합 판단). 본문 서술은 반영 완료 | §3.9 (점군 화면 조작) |
| (대기) | **불량률 예측 헤더 칩 "다음 1시간: …"** — 헤더에 다음 1시간 예상 NG율/모델 상태 칩 상시 표시(모델 미등록 시 "모델 미등록"). 매뉴얼 미기술 — §3.2 헤더 요소 + Web 불량률 예측 화면과의 관계 설명 필요 | §3.2(헤더), §4.16(Reports) |
| #371 | **카메라 Live 장시간 구동 안정화** — VisionSetup Live/Live Receive 가 몇 컷 뒤 느려지다 멈추던 문제 해결 (메모리 누적 제거). 사용자 절차 변화 없음 — Live 관련 서술에 "장시간 구동 가능" 문구 검토만 | VisionSetup 매뉴얼 Live 절 |
| #372 #373 | **Feature Match 학습 마스크(Train Mask) + 360° 각도 표기 정규화** — [Edit Mask...] 편집기(브러시/사각형/지우개)로 그림자·가변 각인·반사 영역을 학습에서 제외, 특징 이미지의 빨간 영역으로 확인. 360° 검색은 Angle Start=-180 + Extent=360 안내 필요. 도구 파라미터 표(§ Feature Match)·설정 화면 스크린샷 갱신 | VisionSetup 매뉴얼 Feature Match 절 |
| #375 | **학습 마스크 편집기 개편 (v1.18.0)** — 도구 바가 도구(브러시/사각형/다각형)×동작(마스크/해제 UnMask) 구성으로 변경 ("지우개" 명칭 삭제 — 브러시+해제로 대체). 다각형: 좌클릭 꼭지점 추가→우클릭/더블클릭 완성, Esc 취소. 줌 1~8배: Ctrl+휠 또는 [+]/[−]. 위 #373 행과 같은 절에 함께 반영 — #373 기준 편집기 스크린샷은 이 UI 로 재캡처 | VisionSetup 매뉴얼 Feature Match 절 |
| #393 | **회전 ROI(RectAffine) 실행 반영** — Blob: 회전 ROI가 그린 영역 그대로 분석되도록 수정(기존엔 각도가 거울 반전된 영역 분석). Feature Match: 학습(Train) 시 회전 ROI 각도를 존중해 회전 정렬된 패턴으로 학습(SearchRegion 은 여전히 축 정렬 — 추후). 매뉴얼의 ROI 종류 설명에 "RectAffine 은 Blob·Feature Match 학습에 적용" 명시 검토 | VisionSetup 매뉴얼 ROI·Blob·Feature Match 절 |
| #394 | **RectAffine ROI 크기 조절 방식 변경** — 꼭짓점을 끌면 잡은 꼭짓점만 이동하고 대각 반대편은 고정(기존: 중심 대칭으로 양쪽이 같이 늘어남). ROI 편집 조작 설명·스크린샷에 언급이 있으면 갱신 | VisionSetup 매뉴얼 ROI 절 |
| #395 | **예제 템플릿 갤러리 "얼라인" 탭에 3D 얼라인 3종 추가** — ① 표준 3D 얼라인(6DOF, 기준 형상 정합: 기준 .vpc/.stl 대비 ΔX/ΔY/ΔZ·RX/RY/RZ) ② 평면 틸트 얼라인(기준면/대상면 Plane Fit 2개 → 법선 사이각으로 기울기 보정) ③ 2D+3D 하이브리드 얼라인(XYθ는 Match Align, 기울기·높이는 Plane Fit). 전부 "3D 카메라 필요" 배지. 매뉴얼 예제 템플릿 절의 템플릿 목록·갤러리 스크린샷 갱신 | VisionSetup 매뉴얼 예제 템플릿 절 |
| #397 | **VisionSetup에서 VMS 카메라 Grab 요청 + 연결 토글 버튼** — ① VMS 실행 중 VisionSetup 툴바의 [Acquire]가 **VMS에 Grab을 요청해 이미지를 바로 받아온다** (기존: VMS 창으로 가서 Grab → VisionSetup으로 돌아와 수신하는 왕복). VMS가 운전(AUTO RUN)·라이브 중이면 사유와 함께 거절되고 상태바에 표시. 요청한 카메라와 다른 카메라의 프레임이면 경고. ② 툴바에 **카메라 연결 토글 버튼** 신설 — 연결 시 초록, 미연결 시 무색, VMS 실행 중이면 비활성(툴팁이 사유 안내). 기존엔 Camera Info 팝업을 열어야 연결 상태를 알 수 있었다. VisionSetup 매뉴얼의 카메라 연결·VMS 연동 절 + 툴바 스크린샷 갱신 | VisionSetup 매뉴얼 카메라·VMS 연동 절 |
| #399 | **Feature Match 기준(원본) 이미지 자동 저장 + [기준 이미지 불러오기]** — [Train] 성공 시 학습에 쓴 전체 장면이 RefImages/ 폴더에 PNG로 자동 저장되고, 설정 화면의 [기준 이미지 불러오기] 버튼으로 그 장면을 메인 화면에 되불러와 ROI 조정·재학습 가능 (기존: 레시피에 ROI 크롭 템플릿만 남아 원래 장면 확인 불가). 재학습하면 같은 파일을 덮어씀. Feature Match 절 버튼 설명 + 설정 화면 스크린샷 갱신 | VisionSetup 매뉴얼 Feature Match 절 |
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
