# 사용자 매뉴얼 작업 현황 / 남은 일 (2026-07-07 갱신)

## 현재 상태

| 항목 | 상태 |
|------|------|
| PR #158 — AppSetup §2.5 풀캡처 복원 + 6단계 컨트롤 표 | ✅ 머지됨 |
| PR #159 — VisionSetup §3.3 전체화면 복원 + 영역별 컨트롤 표 + 다이얼로그 설명 | ✅ 머지됨 |
| PR #160 — VMS 메인 §3.1~3.3 헤더 칩/사이드 패널 컨트롤 이미지 (+ `--capture-controls` VMS 이식) | ✅ 머지됨 |
| **PR #161 — §1~3 쉬운말 리라이트 + 파이프라인 수정(콜아웃 복구·번호 목록)** | ⏳ **리뷰/머지 대기** |
| **PR #162 — §4~7 쉬운말 리라이트 (#161 위에 스택, base=docs/manual-plain-language-1-3)** | ⏳ **리뷰/머지 대기** |

## 다음 할 일

1. **PR #161 확인 후 머지** — https://github.com/j2ase1862/VMS/pull/161
2. **PR #162 확인 후 머지** — https://github.com/j2ase1862/VMS/pull/162
   (#161 머지 후 base 를 master 로 바꿔 머지. 쉬운말 리라이트는 이걸로 §1~7 전체 완료)
3. (선택) 매뉴얼 v1.1 버전업 — 표지/헤더의 "v1.0" 갱신 여부 결정

## 문서 생성 파이프라인 (중요 — 잊기 쉬움)

- **본문 소스** = `docs/manuals/BODA-VMS-User-Manual.html` ← 여기를 고쳐야 유지됨
- 재생성: `cd docs/gs/pipeline && python parse_manual.py && node gen_user_manual.js` (스크립트는 pipeline/ 하위로 이동됨)
- **docx 직접 편집(python-docx 등)은 재생성 시 유실됨** — 그림/표는 `gen_user_manual.js`의 POST 훅·wizardSection 에, 본문은 HTML 에
- 헤딩 이름을 바꾸면 `gen_user_manual.js` 의 POST 키도 같이 바꿔야 그림이 삽입됨
  (검증: blocks 헤딩 vs POST 키 매칭 — PR #161 본문의 검증 스크립트 참조)
- 검증 렌더: Word COM (docx→XPS, `ExportAsFixedFormat fmt=18`) → XPS fpage 는 UTF-16 디코드로
  텍스트 검색 → WPF RenderTargetBitmap 으로 PNG (스크립트: `docs/control-capture/_work/render_xps.ps1`)

## 컨트롤 캡처 도구 (재캡처 필요 시)

| 앱 | 명령 | 산출 |
|----|------|------|
| VMS.AppSetup | `VMS.AppSetup.exe --capture-controls [폴더]` / `--capture-fullpage` | 마법사 6페이지 컨트롤 / 2·5단계 풀페이지 |
| VMS.VisionSetup | `--capture-controls` / `--capture-fullpage` / `--capture-toolpanels` / `--capture-dialogs` | MainView 컨트롤 / 전체화면 / 툴 패널 34종 / 다이얼로그 |
| VMS | `VMS.exe --capture-controls [폴더]` / `--capture-dialogs` | 헤더 칩·사이드 패널 (장면 3개) / 관리자 다이얼로그 |

- 전부 `#if DEBUG` 전용 (Release 무영향). Debug 빌드 후 실행.
- 매뉴얼에 쓰는 선별본은 `docs/gs/screenshots/{appsetup_ctl, visionsetup_ctl, vms_ctl}/` 에 커밋됨.
- UI 를 바꾸면 해당 앱 캡처 재실행 → 선별본 교체 → docx 재생성.

## 매뉴얼 외 열려 있는 것 (참고)

- PR #148 — 전시회 데모 모드 + 홍보영상 파이프라인 (feat/photometric-stereo, 오픈 상태)
- 포토메트릭 스테레오 B방식 — 하드웨어 대기
