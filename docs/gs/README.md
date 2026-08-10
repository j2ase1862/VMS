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
| `VMS_사용자매뉴얼_v1.1.docx` | `pipeline/parse_manual.py` → `pipeline/gen_user_manual.js` |
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

**반영 완료 기준선: SW v1.5.3 (PR #262) — 2026-07-28 docx 재생성 완료**

| PR | 매뉴얼에 들어갈 내용 | 대상 절 |
|----|--------------------|--------|
| #285 | AppSetup Web 서버 카드(구성 완료 상태)에 [서비스 시작 (관리자 권한)] 버튼 추가 — 서비스가 Stopped 일 때만 표시, 클릭 시 UAC 승인 → 자동시작 전환 + 시작 + 응답 확인. Stopped 상태 문구도 버튼 안내로 변경 | AppSetup Page 2 절 (§2.x) |
| #286 | 카메라 Save Parameter 가 결과 메시지를 표시(저장 완료/실패/카메라 미등록 시 AppSetup 실행 안내). 부수: Save Parameter·AppSetup 재저장이 서로의 설정(보안 모드·Web SSO ↔ 노출/게인)을 초기화하던 문제 해소 — 트러블슈팅 서술이 있다면 갱신 | 카메라 설정 절 (Save Parameter 언급 위치) |
| #287 | VMS Recipe 패널에 새로고침 버튼 추가(Load/New 옆 아이콘) — Web 에서 만든 레시피를 60초 자동 동기화 전에 즉시 가져옴. VisionSetup Recipe Manager 는 창이 열려 있는 동안에도 Web 레시피 자동 반영 | VMS Recipe 절 + VisionSetup Recipe Manager 절 |
| #288 | 새로고침 시 상태바에 동기화 요약 표시("Web 레시피 N개 (ClientIndex i) — 새 항목 M, 이름 연결 K") — 0개면 라인 확인 힌트. Production 에서 Web URL 이 비-loopback HTTP 면 시작 시 "Web 연동 비활성" 경고 대화상자 (VMS·VisionSetup). Web 쪽 짝: 레시피 생성 다이얼로그 대상 라인 경고 (BODA.VMS.Web #47) | VMS Recipe 절 + 트러블슈팅 (Web 연동) |
| #290 | 대시보드 TACT TIME 은 AUTO RUN 중 검사 간 간격만 표시(수동 Inspect 는 미갱신 — 대기 시간 오인 방지), 카드에 "PROC N ms"(마지막 검사 처리 시간) 병기 | 대시보드/KPI 절 (Tact Time 설명 위치) |
| #291 | VisionSetup 에서 레시피 저장 시 실행 중인 VMS 가 자동으로 다시 불러옴 (상태바 "외부 변경 반영 완료" 표시, AUTO RUN 중이면 정지 시 적용 안내) — "저장 후 VMS 재로드" 수동 절차 서술이 있다면 갱신 | VMS Recipe 절 + VisionSetup 연동 절 |
| #264 | DeepLearning 체크박스·슬라이더가 다크 테마로 바뀜(파란 채움 체크, 파란 트랙 슬라이더) — 본문 서술 변경 없음, §3.8 스크린샷·컨트롤 이미지가 구식이 됨. `--capture-controls` 재캡처 필요 | §3.8 |
| #264 | VisionSetup 라디오버튼 5곳 다크 테마 적용(이미지 폴더 탐색 모드 Navigate/Run All/Run Selected, Geometry3D Point Source) — 해당 컨트롤이 보이는 스크린샷 구식화. `--capture-controls` 재캡처 필요 | §3.9 (Geometry3D), §3.1~3.2 (탐색 모드 노출 시) |
| #264 | VMS.WeldTeach 신규 앱 — **GS 인증·사용자 매뉴얼 범위에서 제외 확정** (2026-08-04). 관리자 매뉴얼(`docs/manuals/BODA-VMS-Admin-Manual.html`) §9 에 수록 완료 — 사용자 매뉴얼에는 반영할 것 없음 | (완료 — 관리자 매뉴얼 §9) |
| #275 | 점군 뷰어 개선 3종 — ① **표시 밀도**: Mech-Mind 풀해상도(310만 점)가 기본 전체 표시(기존 1/4 간축), 뷰 툴바에 **밀도 버튼**(자동→전체→1/2→1/4) 신설 ② **회전**: 점군 중심 턴테이블 방식(수평 유지·뒤집힘 방지) ③ **휠 줌**: Point Cloud 2%/노치·Depth Map ×1.02, **Ctrl+휠 = 정밀 줌**(1%/×1.01). Point Cloud 탭 스크린샷·조작 설명 갱신 필요 | §3.4~3.5 (3D 뷰어 조작), VMS 본체 3D 화면 절 |
| #272 | 스텝 별칭·로봇 노드 번호 — Camera Settings 패널에 **"스텝 정보"** 그룹 신설: **별칭**(자유 라벨, 예: "Node 3" — 그리드/트리에 "1-3 — Node 3" 병기)과 **로봇 노드 번호**(로봇이 보내는 노드 index 와 스텝 매칭, 비우면 순번 매칭) 입력란. Recipe Manager 의 Step Name 입력란은 **Step Alias**(별칭 편집)로 변경 — 파생 이름은 편집 불가 | §3.2 (스텝 관리), 로봇 연동 절 있으면 해당 절 |
| (신규) | MSI 에 Web 서버 동봉 — 설치 옵션에 "BODA VMS Web 서버" Feature 추가(기본 포함), AppSetup Page 2 에 **"Web 서버 초기 구성 (이 PC)"** 카드 신설: admin 비밀번호 입력만으로 Web 서버 구성+시작 (수동 sc.exe 절차 대체). Page 2 스크린샷 구식화 — `--capture-fullpage` 재캡처 필요 | §2 (설치), §5.5 부근 (Web 연동 설정) |
| #272 | 스텝 이름 파생화 — 스텝 이름("1-1")은 저장값이 아니라 **현재 PC 카메라 등록 순서 기준으로 로드 시마다 재계산**. 이 PC에 등록 안 된 카메라의 스텝은 **"?-n"**으로 표시(다른 PC에서 만든 레시피의 스텝과 새 스텝의 이름 중복 문제 해소). 미등록 카메라를 참조하는 레시피 로드 시 **경고 대화상자** 표시 | §3.2 (스텝 관리), §5 (레시피 이동/호환 안내 있으면 해당 절) |
| #272 | VisionSetup Steps 그리드 의미 변경 — 항상 **"선택된 카메라의 스텝"만** 표시(기존: 카메라 미선택 시 전체 스텝 표시 → 선택 시 사라져 보이는 혼동). ① Recipe 로드 시 레시피가 참조하는 카메라 자동 선택 ② 그리드 빈 상태 안내 문구(카메라 미선택 / 해당 카메라 스텝 없음) ③ Steps 헤더에 현재 카메라 이름 표시("Steps — Cam1") | §3.2 (스텝 관리) |
| #270 | 엣지 간 거리 측정 판정 체인 완성: ① 스텝 그리드의 **Resolution(mm/px)이 이제 실제로 동작** — 캘리브레이션 없을 때 측정 도구 mm 폴백 (기존엔 죽은 속성) ② Geometry 도구에 **Judgment**(기준값 ± 공차, mm/px 단위, OK/NG 오버레이) ③ 템플릿에 Result 판정 연결 추가 — 엣지 간 거리·**Blob 검출·컬러 객체 검출** ④ BlobTool Max Area/Perimeter/AspectRatio 기본값 E+308 → 읽을 수 있는 수(1e8/1e6/1000) ⑤ Run Results 그리드 높이 300·스크롤 통일. 도구 파라미터 표(extract_tool_params) 재생성 필요 | §3.9 (템플릿·Geometry/Blob 파라미터) |
| #282 | VisionSetup 툴바에 **Camera Live 시작/정지 버튼** 신설(카메라 아이콘 + ▶/■ 배지) — Camera 메뉴 진입 없이 Grab/Live/정지 가능. 앱 시작 시 **첫 카메라 자동 선택**(카메라 패널 조작 없이 툴바 바로 동작). 툴바 스크린샷 구식화 — 재캡처 필요 | §3.1~3.2 (VisionSetup 화면 구성·카메라 조작) |
| #282 | VMS 카메라 카드 컨트롤 박스 — Grab/Live 버튼이 비활성일 때 **툴팁으로 사유 표시**(Start/Stop 권한 없음 / Operator 로그인 필요 / 카메라 미연결). Grab 도 Live 와 동일한 권한 게이트 적용 | VMS 운영 절 (카메라 조작), 트러블슈팅 절 있으면 해당 절 |
| #282 | Basler 카메라 — 스텝 **노출/게인 설정이 실제 적용**되기 시작(기존 미지원), 연결 시 자동 노출/게인 Off (Grab 밝기 요동 해소). pylon Viewer 튜닝값을 쓰려면 "카메라 설정 유지" 체크(기본) 안내 | §3.2 (카메라 설정), Basler 연동 안내 절 있으면 해당 절 |
| #293 | BlobTool Web 파라미터 연동 위치 변경 — Judgment 의 **Expected Area / Area Tolerance (+) / (-)** 에 Web 파라미터 연동 콤보 신설, Area Filter 의 Min/Max Area 연동 콤보는 **제거**(필터는 레시피 로컬 설정). 기존 레시피에 걸려 있던 Min/Max Area 연동은 로드 시 자동 해제 — 연동 설명·스크린샷이 있다면 갱신 | §3.9 (Blob 파라미터), Web 파라미터 연동 절 |

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
| `_manual_blocks.json` `_tool_params.json` | 중간 산출물 (재생성 가능하지만 diff 추적을 위해 커밋) |

## 컨트롤 캡처 도구 (UI 변경 시 재캡처)

| 앱 | 명령 | 산출 |
|----|------|------|
| VMS.AppSetup | `VMS.AppSetup.exe --capture-controls [폴더]` / `--capture-fullpage` | 마법사 6페이지 컨트롤 / 2·5단계 풀페이지 |
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
