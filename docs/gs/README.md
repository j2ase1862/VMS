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

**반영 완료 기준선: SW v1.5.11 + v1.5.12 예정분(#293~#300) — 2026-08-11 docx 재생성 완료**

| PR | 매뉴얼에 들어갈 내용 | 대상 절 |
|----|--------------------|--------|
| (잔여) | 3D 점군 뷰어 조작 스크린샷 갱신 (#275 밀도 버튼·툴바) — **실점군 로드 상태에서 수동 캡처 필요** (자동 캡처 도구는 점군 화면 미포함). 본문 서술은 반영 완료 | §3.9 (점군 화면 조작) |
| #314 | **AUTO RUN 활성 조건 정정** — 시스템 사용자 로그인 없이도 검사 시작 가능(매뉴얼 §3.5 "미로그인 시 기본 허용" 이 실제 동작과 어긋나 있던 것을 코드에 반영). 로그인한 경우에만 등급 권한 적용, 권한 없으면 사유 툴팁. Grab·Live 동일 기준. **매뉴얼 §3.5 본문은 이미 이 동작을 기술 — 수정 불필요**, §3.7 등급표 각주만 점검 | §3.5(활성 조건), §3.7(UserGrade) |
| #311 | **IO 보드 연결 수정** — ADLink 드라이버 파일명·카드 종류·출력 포트 교정(현장 PCI-7432 실증 대조). 연결 실패 시 로그(출처 `IoBoard`)에 **시도한 드라이버 파일명·프로세스 비트수·오류 번호·모델/보드 번호**가 남음. **VMS 는 64비트라 x64 DASK 드라이버 필요** — 트러블슈팅에 항목 추가 | §5(시퀀스 운전), §7 트러블슈팅 |
| #317 | **IO 보드 연결 재수정(#311 값 교정)** — 카드 종류·포트를 ADLink 공식 헤더 기준으로 정정(2026-08-18 현장 -13 오류 해소). 사용자 관점 변화 없음 — #311 로 기재한 트러블슈팅 내용 그대로 유효, 지원 카드에 PCI-7433/7434 는 64채널(채널 0~63) 명시만 추가 | §5(시퀀스 운전), §7 트러블슈팅 |
| #319 | **AUTO RUN 검사 카운트 정상화** — 트리거 1회에 대시보드 Total/OK/NG 가 2씩 오르던 문제 수정 (판정 이미지 저장·Web 이미지 업로드도 1회로). 사용자 절차 변화 없음 | §3.4(대시보드) |
| #321 | **Work Order 수량 자동 집계 (1사이클 = 1개)** — AUTO RUN 에서 시퀀스 1사이클(트리거→검사→반복)마다 선택된 작업지시 수량이 1 증가, 계획 수량 도달 시 자동 완료. Web 대시보드 금일 성과·불량률에도 반영. **Web 연동 레시피 전제** — 매뉴얼 §5 에 "작업지시 진행이 안 될 때 = Web 연동 레시피인지 확인" 항목 추가 | §5(시퀀스 운전), §3.8(작업지시), §7 트러블슈팅 |
| #322 | **판정 결과 엄격화** — 검출 0개/판정 불능이 합격으로 처리되던 4건 수정: Blob 면적 판정(0개=면적 0으로 비교), Blob 판정만 켠 경우(검출 유무로 판정), DL 검출 각도 판정(계산 불가=NG), 코드 판독 품질 등급(등급 산출 불가=NG). **기존에 합격으로 나오던 미검출 검사가 NG 로 바뀔 수 있음** — 툴 설명(§4)의 판정 항목에 명시 필요 | §4(비전 툴: Blob·DL 검출·코드 판독) |
| #324 | **DL 세그먼테이션(YOLO-Seg)도 0건 검출 = 불합격** — DL 검출 툴과 판정 기준 통일 (#322 와 같은 엄격화 계열, 기존 합격 → NG 변경 가능) | §4(비전 툴: DL 세그먼테이션) |
| #325 | **작업지시 완료 기준 선택 (양품/총생산)** — Web 에서 WO 생성 시 완료 기준 선택(기본 **양품 수량 기준** — NG 만큼 자동으로 더 생산해 양품이 계획 수량에 도달하면 완료). 기존 WO 는 총 생산 기준 유지. VMS 헤더 칩은 기준에 맞는 진행률 표시(예: 양품 80/100) | §3.8(작업지시), Web 매뉴얼 WO 절 |
| #307 #308 | **IO 보드(PLC 없는 구성) 운전** — 상단 표시줄에 `IO n/m` 상태 표시(정상 회색/실패 적색) · 보드 연결 실패 사유가 로그(출처 `IoBoard`)에 남음 · 시퀀스 노드 오류도 로그 기록 후 5초 간격 재시도 · **시퀀스 수정이 VMS 재시작 없이 운전 시작 시 반영** · 시퀀스 편집기의 무부하 테스트·모니터가 **PLC 없이 IO 보드만으로 동작**(입력 인덱스=채널 번호, 보드는 ON/OFF 전용) | §3.2(헤더 상태), §5(시퀀스 운전), §7 트러블슈팅 |
| #302 | 카메라 SDK 미탑재 빌드에서 연결 시 **시뮬레이션 모드 경고** — VisionSetup 은 경고 대화상자 + 상태바 표기, VMS 카메라 카드는 SIMULATION 메시지 표기. 트러블슈팅에 "영상이 실제와 다름 → SDK 미탑재 빌드 확인" 항목 추가 후보 | §3.3 (카메라), §7 트러블슈팅 |

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
