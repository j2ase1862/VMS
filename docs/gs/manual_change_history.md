# 매뉴얼 반영 대기 — 변경 히스토리 (상세)

`README.md` 의 "매뉴얼 반영 대기" 표는 **한 줄 요지**만 적는 장부이고, 이 문서는 매뉴얼 검토 기간
(2026-09-02~) 동안 코드 쪽에서 바꾼 내용을 **매뉴얼 편집자가 코드를 다시 열지 않아도 되도록**
화면 · 문구 · 스크린샷 · 대상 절 단위로 풀어 쓴 상세 기록이다.

- 기재 시점: 사용자에게 보이는 변경 PR 을 만들 때 (머지 전이라도 먼저 적고, 머지 후 PR 번호를 채운다)
- 매뉴얼 갱신 완료 시: 항목을 "반영 완료" 로 표시하고 기준선을 올린다 (README 표와 동시에)
- 기준선: **SW v1.29.0 / Web v1.8.0 / 매뉴얼 v2.0 (2026-09-04 §1~§8 전부 반영 — 대기 항목 없음)**

---

## 1. AppSetup 3단계(카메라 구성) — Area Scan 노출/게인 입력란 제거 — **반영 완료 (2026-09-04): HTML §3.1 콜아웃·§5.3 상호 참조·§11.4 한 줄, 생성기 3단계 표 3행 삭제 + 안내 카드 행, 10_appsetup_step3.png 재캡처 + P3_camtypes_* 전부 재캡처·재대조, docx 재생성**

| 항목 | 내용 |
|------|------|
| PR | #416 (`fix/appsetup-remove-exposure-gain`) |
| 날짜 | 2026-09-02 |
| 앱 · 화면 | VMS.AppSetup 설정 마법사 3단계 "Camera Configuration", 카메라 카드의 〈Area Scan〉 패널 |
| 변경 종류 | UI 제거 + 안내 문구 대체 (동작 변경 없음) |

### 무엇이 바뀌었나
- 카메라 타입이 AreaScan2D / AreaScan3D 일 때 카드 아래에 있던 **Exposure (μs)** 와 **Gain** 입력란 2개를 제거했다.
- 같은 자리에 안내 문구 한 줄을 표시한다 (§2 의 3D 항목 제거와 합쳐 카메라 종류와 무관하게 항상 표시):
  > 노출(Exposure)·게인(Gain)과 3D 취득 옵션(점군 후처리 필터, 깊이 Z 범위)은 여기서 설정하지 않습니다. VMS 메인 화면의 카메라 스텝 설정 또는 VisionSetup 레시피 스텝에서 조정하며, 기본은 카메라에 저장된 현재 값을 그대로 사용합니다.
- Line Scan(Trigger / Line Rate / Scan Length / Encoder Res), 3D(Capture Mode / Filter / Z-range), Frame Grabber(MIL) 패널은 그대로다.

### 왜 바꿨나 (매뉴얼에 넣을 설명의 근거)
- 마법사가 저장하던 카메라 단위 노출/게인 값은 VMS · VisionSetup 어느 쪽에서도 읽지 않았다. 사용자가 값을 바꿔도 촬영 결과가 달라지지 않는 죽은 설정이었다.
- 실제 런타임은 **스텝 단위** 값을 쓴다.
  - VMS 메인: 카메라별 스텝의 "카메라 설정 유지"(기본 ON) 체크 → OFF 일 때만 스텝의 노출/게인을 카메라에 적용.
  - VisionSetup: 레시피 스텝의 "카메라 설정 유지"(기본 ON) 와 노출/게인.
- 기존 `system_config.json` 에 남아 있는 `cameras[].exposure` / `gain` 키는 그대로 읽고 보존되므로 업그레이드 시 오류 없음 (값은 사용되지 않음).

### 매뉴얼 반영 지점
| 위치 | 해야 할 일 |
|------|-----------|
| §3.2 AppSetup 3단계 입력 항목 표 (`gen_user_manual.js` `wizardTable` 의 `10_appsetup_step3.png` 블록) | 행 **"〈Area Scan〉 Exposure(μs) / Gain — 노출 시간 / 게인 — 5000 / 1.0"** 삭제. 캡션 문자열 "노출/게인/캡처모드" → "캡처모드" 로 수정 |
| §3.2 3단계 본문 | "노출·게인은 3단계에서 설정하지 않으며 VMS 스텝 설정(§4.x) 또는 VisionSetup 레시피 스텝(§5.3) 에서 조정" 한 문장 추가 |
| §4 VMS 운전 · 카메라 스텝 설정 / §5.3 스텝 관리와 카메라 설정 | 노출/게인 조정 위치가 여기임을 명시 (이미 서술돼 있으면 상호 참조만) |
| §11.4 변경 이력 | "AppSetup 3단계 노출/게인 입력란 제거 (런타임 미사용 설정 정리)" 한 줄 |

### 스크린샷
| 파일 | 조치 |
|------|------|
| `docs/gs/screenshots/10_appsetup_step3.png` (3단계 풀페이지) | AreaScan 카메라 카드가 보이는 상태로 **재캡처** (`VMS.AppSetup.exe --capture-fullpage`) |
| `appsetup_ctl/P3_camtypes_10_TextBox_5000.png`, `P3_camtypes_12_TextBox_1.png` | 더 이상 생성되지 않음 — 표에서 참조 제거. `--capture-controls` 재실행 시 P3_camtypes_* 번호가 당겨지므로 **3단계 컨트롤 캡처 파일명 전부 재대조** 필요 |

### 코드 참조 (검증용)
- `VMS.AppSetup/MainWindow.xaml` — Row 2 Area Scan Border
- `VMS.AppSetup/Models/SetupConfiguration.cs` — `Exposure` / `Gain` 필드는 호환용으로 유지
- 런타임 소비처: `VMS/ViewModels/CameraViewModel.cs` (`Use2DCameraDefault` 분기), `VMS.VisionSetup/ViewModels/MainViewModel.cs` `ApplyStepCameraSettingsAsync`

---

## 2. AppSetup 3단계(카메라 구성) — 3D 패널(Capture Mode / Filter Strength / Z Min / Z Max) + Encoder Res 제거 — **반영 완료 (2026-09-04): HTML §3.1 콜아웃·§5.3 상호 참조·§11.4 한 줄, 생성기 3단계 표 3행 삭제 + 안내 카드 행, 10_appsetup_step3.png 재캡처 + P3_camtypes_* 전부 재캡처·재대조, docx 재생성**

| 항목 | 내용 |
|------|------|
| PR | #416 (§1 과 같은 PR) |
| 날짜 | 2026-09-02 |
| 앱 · 화면 | VMS.AppSetup 설정 마법사 3단계, 카메라 카드의 〈3D〉 패널 전체 + 〈Line Scan·Encoder〉 Encoder Res(P/mm) |
| 변경 종류 | UI 제거 (동작 변경 없음) |

### 무엇이 바뀌었나
- 3D 카메라(AreaScan3D / LineScan3D) 카드에 있던 **Capture Mode / Filter Strength / Z Min (mm) / Z Max (mm)** 4개 입력란을 제거했다. (이 패널은 원래 2D 카메라에는 표시되지 않았다.)
- Line Scan + Trigger=Encoder 일 때 나타나던 **Encoder Res (P/mm)** 입력란을 제거했다.
- §1 의 안내 카드 문구에 3D 취득 옵션을 합쳐 카메라 종류와 무관하게 카드 1장을 항상 표시한다.
- 남는 패널: 〈Line Scan〉 Trigger / Line Rate / Scan Length, 〈Frame Grabber〉 Board Type / Board# / Digitizer# / DCF.

### 왜 바꿨나
- `cameras[].captureMode / filterStrength / zRangeMin / zRangeMax / encoderResolution` 은 VMS · VisionSetup · 카메라 드라이버 어디에서도 읽지 않는다.
- 3D 취득 옵션의 실제 소유자는 **VisionSetup 레시피 스텝**(점군 후처리 프리셋 `PointCloudPostProcess`, 깊이 범위 `UseDepthRange` / `DepthRangeMinMm` / `DepthRangeMaxMm`) 이며 Grab/Live 직전에 `Apply3DSettingsAsync` 로 카메라에 적용된다.
- Line Scan 의 Trigger / Line Rate / Scan Length 는 VMS 가 `CameraInfo` 로 넘기긴 하지만 **현재 드라이버(Basler·Mech-Mind·Matrox) 중 읽는 곳이 없다** — 라인 스캔 드라이버 자체가 미구현(시뮬레이션만). 배관이 있어 이번엔 남겨 두었고, 라인 스캔 지원 여부 결정 시 함께 정리.

### 매뉴얼 반영 지점
| 위치 | 해야 할 일 |
|------|-----------|
| §3.2 3단계 입력 항목 표 (`gen_user_manual.js`) | 행 **"〈Line Scan·Encoder〉 Encoder Res(P/mm)"** 와 **"〈3D〉 Capture Mode / Filter / Z Min·Max(mm)"** 삭제. 캡션 "노출/게인/캡처모드" → 항목 목록에서 캡처모드도 제거 |
| §3.2 3단계 본문 | "3D 취득 옵션(점군 후처리·깊이 범위)은 VisionSetup 레시피 스텝(§5.3)에서 조정" 문장 (§1 문장과 합쳐 한 문장으로) |
| §5.3 스텝 관리와 카메라 설정 | 3D 설정(후처리 프리셋·깊이 범위)이 스텝 소유임을 명시 (이미 서술돼 있으면 상호 참조만) |
| §11.4 변경 이력 | §1 과 한 줄로 합쳐 "AppSetup 3단계 노출/게인·3D 옵션·Encoder Res 입력란 제거 (런타임 미사용 설정 정리)" |

### 스크린샷
| 파일 | 조치 |
|------|------|
| `10_appsetup_step3.png` | §1 과 동일 — 재캡처 1회로 충분 (3D 카메라 카드 기준: Name/IP/Type/Manufacturer + 안내 카드) |
| `appsetup_ctl/P3_camtypes_28_TextBox_10.png`, `P3_camtypes_42_ComboBox_Both.png`, `P3_camtypes_44_TextBox_3.png` | 더 이상 생성되지 않음 — 표에서 참조 제거 |

### 코드 참조 (검증용)
- `VMS.AppSetup/MainWindow.xaml` — Row 2 안내 카드(항상 표시), Row 3 Line Scan 그리드 5열
- `VMS.AppSetup/Models/SetupConfiguration.cs` — `CaptureMode`/`FilterStrength`/`ZRangeMin`/`ZRangeMax`/`EncoderResolution` 필드 호환용 유지, `ShowEncoderResolution` 제거
- 런타임 3D 소유자: `VMS.VisionSetup/Models/InspectionStep.cs`, `VMS.VisionSetup/ViewModels/MainViewModel.cs` `ApplyStepCameraSettingsAsync`

---

## 3. VMS 메인 — 단독 모드에서도 Recent Inspections(최근 검사) 패널에 기록 — **반영 완료 (2026-09-04, v1.28.1 핫픽스 PR 동승: HTML §4.3·§4.5·§7.6·§9.4·§11.3·§11.4 + docx 재생성 + 캡처 36_dlg_inspection_history / 28_dlg_retention 교체)**

| 항목 | 내용 |
|------|------|
| PR | #417 (`fix/standalone-recent-inspections`) |
| 날짜 | 2026-09-04 |
| 앱 · 화면 | VMS 메인 화면 우측 슬라이딩 패널 "Recent Inspections" (Total/Pass/NG 셀 + 최근 200건 목록) |
| 변경 종류 | 동작 수정 (단독 모드 공백) + 도움말 문구 수정 |

### 무엇이 바뀌었나
- **단독 모드(Web 서버 미구성)** 에서 AUTO RUN·수동 검사를 해도 Recent Inspections 패널이 항상 0건이던 문제를 고쳤다. 이제 Web 연동 여부와 무관하게 사이클(또는 수동 검사) 1건마다 기록된다.
- 목록의 **NG 코드 자리**: Web 연동 레시피면 종전대로 Web 파라미터 코드, 단독 모드·파라미터 미연결 레시피면 **실패한 도구 이름**(예: `Blob 1,Edge 2`)이 표시된다.
- 목록의 **레시피 이름**: 단독 모드에서도 현재 로컬 레시피 이름이 표시된다 (종전엔 "Recipe#0").
- 패널 도움말(?) 문구: "단독 모드나 Web 연결이 끊긴 상태에서도 기록되며, VMS 를 다시 시작하면 비워집니다 (Web 연동 시 전체 이력은 Web 의 Production History)".
- Web 연동 모드 부수 효과: 수동 검사에서 파라미터는 전부 OK 인데 최종 판정이 NG 인 경우 종전엔 PASS 로 기록되던 것이 NG(실패 도구 이름 포함)로 바로잡힘. 파라미터 미연결 레시피의 수동 검사도 이제 목록에 남는다.

### 왜 바꿨나
- 로컬 이력 push 코드가 "Web 동기화 서비스 + Web 레시피 ID" 가드 안쪽에 있어 단독 모드에서는 실행되지 않았다. 단독 모드(#407) 도입 후 드러난 공백.
- 영구 로컬 생산이력(SQLite 저장소 + 조회 창)은 후속 PR 로 별도 진행 — 본 건은 그 1단계.

### 매뉴얼 반영 지점
| 위치 | 해야 할 일 |
|------|-----------|
| §3.5 단독 모드 절차 | "검사 결과는 화면 우측 Recent Inspections 패널에서 최근 200건까지 확인(재시작 시 비워짐)" 한 문장 추가. NG 코드 자리에 실패 도구 이름이 표시됨을 명시 |
| §4 VMS 운전 · Recent Inspections 설명 | 도움말 문구 갱신에 맞춰 "Web 연동 시 전체 이력은 Web" 으로 서술 조정 |
| §11.4 변경 이력 | "단독 모드에서도 최근 검사 패널에 기록 (실패 도구 이름 표시)" 한 줄 |

### 스크린샷
| 파일 | 조치 |
|------|------|
| Recent Inspections 패널 (단독 모드, NG 행에 도구 이름 표시 상태) | 신규 캡처 권장 — 기존 스크린샷이 Web 연동 기준이면 교체 |

### 코드 참조 (검증용)
- `VMS/Services/InspectionService.cs` — `RecordLocalInspection` / `RecordInspectionOutcome` / `FlushCycleResultAsync` (로컬 기록이 Web 가드 바깥으로 이동), `CurrentRecipeNameProvider`
- `VMS/App.xaml.cs` — `CurrentRecipeNameProvider` 배선 (단독 모드 분기 직후)
- `VMS.Core/Models/InspectionRecord.cs` — `DisplayLabel` 의 "Recipe#0" 대체
- 테스트: `VMS.Tests/Services/StandaloneRecentInspectionsTests.cs`

---

## 4. VMS 메인 — 로컬 검사 이력 영구 저장 (inspection_history.db) + 보존 설정 — **반영 완료 (2026-09-04, v1.28.1 핫픽스 PR 동승: HTML §4.3·§4.5·§7.6·§9.4·§11.3·§11.4 + docx 재생성 + 캡처 36_dlg_inspection_history / 28_dlg_retention 교체)**

| 항목 | 내용 |
|------|------|
| PR | #417 (§3 과 같은 PR — 2단계) |
| 날짜 | 2026-09-04 |
| 앱 · 화면 | VMS 메인 (백그라운드 기록, 화면 없음) + 관리자 〈Retention Settings〉 창 전역 카드 4번째 행 |
| 변경 종류 | 기능 추가 (조회 화면은 후속 3단계) |

### 무엇이 바뀌었나
- 검사 결과가 **PC 로컬 SQLite 파일**(`%LocalAppData%\BODA VISION AI\inspection_history.db`)에 사이클 1건(AUTO RUN) 또는 수동 검사 1건당 1행으로 영구 저장된다. 단독 모드에서 VMS 를 다시 켜도 이력이 남는 첫 기능이며, Web 연동 모드에서도 항상 기록되어 오프라인 백업이 된다.
- 저장 내용: 검사 시각, 판정, 레시피 이름, NG 코드(단독 모드는 실패 도구 이름), 도구별 판정/소요 시간, 저장된 이미지 파일 경로(이미지 저장 옵션이 켜져 있을 때, NG 이미지 우선), 작업지시/Lot/시리얼(연동 시), 사이클 시간.
- 〈Retention Settings〉(관리자 → Admin Tools) 전역 카드에 **"검사 이력 (inspection_history.db)"** 행 추가: 보존일 입력(기본 90일, [1, 3650]) + **"검사 결과를 로컬 이력 DB 에 저장"** 체크박스(기본 켜짐). 미리보기(Preview)에 "검사 이력 DB — 삭제 예정 N 건" 행 추가, CSV 내보내기에도 포함. 프리셋: Conservative 365 / Standard 90 / Minimal 30.
- 오래된 행은 VMS 시작 시 자동 삭제(보존일 기준). 백업/복원(Backup & Restore)에 이 파일이 포함된다. 지원 패키지에는 포함되지 않는다.

### 왜 바꿨나
- 단독 모드(Web 서버 없음)에서는 생산 이력이 어디에도 남지 않았다 (Recent Inspections 는 재시작 시 소멸, 감사 로그는 NG 만).

### 매뉴얼 반영 지점
| 위치 | 해야 할 일 |
|------|-----------|
| §3.5 단독 모드 | "검사 이력은 PC 에 자동 저장되며(기본 90일) 관리자 보존 설정에서 기간/저장 여부를 바꿀 수 있다" 문단. 조회 화면은 3단계 반영 시 함께 |
| §4.9 관리자 · Retention Settings | 전역 행 4개로 갱신(검사 이력 행 + 체크박스), 미리보기 행, 프리셋 표에 InspHistory 열 |
| §4.x 백업/복원 | 백업 대상 파일 목록에 `inspection_history.db` 추가 |
| §11.4 변경 이력 | "검사 결과 로컬 영구 저장(inspection_history.db) + 보존 설정" 한 줄 |

### 스크린샷
| 파일 | 조치 |
|------|------|
| Retention Settings 창 (전역 카드) | 재캡처 — `VMS.exe --capture-dialogs` |

### 코드 참조 (검증용)
- `VMS/Services/LocalHistory/LocalInspectionHistoryStore.cs` (저장소·큐·보존), `LocalInspectionEntry.cs`, `ILocalInspectionHistoryStore.cs`
- `VMS.Core/Retention/InspectionHistoryOptions.cs` — `inspectionHistory.{enabled, retentionDays}`
- `VMS/Services/InspectionService.cs` `RecordLocalInspection` — 기록 지점, `VMS/Services/InspectionImageSaver.cs` `ImageSaved` — 이미지 경로 연결
- `VMS/ViewModels/RetentionSettingsViewModel.cs` + `Views/RetentionSettingsWindow.xaml`, `VMS.Core/Retention/RetentionPresets.cs`
- `VMS.Core/Backup/BackupRestoreService.cs` 화이트리스트, GS 개요 `docs/gs/guides/gs_compliance_overview_v1.0.md` §5.11
- 테스트: `VMS.Tests/Services/LocalInspectionHistoryStoreTests.cs`, `InspectionServiceLocalHistoryTests.cs`

---

## 5. VMS 메인 — 생산 이력 조회 창 (Inspection History) 신설 — **반영 완료 (2026-09-04, v1.28.1 핫픽스 PR 동승: HTML §4.3·§4.5·§7.6·§9.4·§11.3·§11.4 + docx 재생성 + 캡처 36_dlg_inspection_history / 28_dlg_retention 교체)**

| 항목 | 내용 |
|------|------|
| PR | #417 (§3·§4 와 같은 PR — 3단계) |
| 날짜 | 2026-09-04 |
| 앱 · 화면 | VMS 메인 우측 〈Recent Inspections〉 패널 헤더의 **[전체 이력]** 버튼 → 새 창 "생산 이력 조회 — Inspection History" |
| 변경 종류 | 화면 신설 (모든 사용자 등급 진입 가능) |

### 무엇이 바뀌었나
- 〈Recent Inspections〉 헤더에 **[전체 이력]** 버튼 추가(기존 [Clear] 왼쪽). 로컬 이력 저장이 꺼져 있으면 비활성.
- 새 창 구성:
  - **필터 바**: From/To 날짜, 판정(전체/PASS/NG), 레시피(기간 내 목록 콤보), NG 코드/실패 도구 포함 검색, [조회]·[필터 초기화]·[CSV 내보내기]. Web 연동 모드에서는 "전체 이력은 Web 의 Production History" 안내 문구.
  - **탭 ① 이력 목록**: 검사 시각·판정(색)·레시피·NG 코드/실패 도구·구분(사이클/수동)·ms·추적(Serial/WO/Lot)·IMG 체크. 행 선택 시 오른쪽 **상세** — 도구별 결과 표(도구·종류·결과·ms·메시지) + 저장된 검사 이미지(NG 우선) + [폴더 열기]. 이미지가 없으면 안내 문구.
  - **탭 ② 일별 집계**: 총 검사/PASS/NG/수율 카드 + 날짜별 표.
  - **탭 ③ NG 파레토**: NG 코드(단독 모드는 도구 이름)별 건수·비율·누적 비율 상위 30.
  - **하단**: DB 파일 위치·크기, 페이지 이동(200건 단위).
- CSV 는 현재 필터의 **전체** 결과(페이지 무관)를 UTF-8 BOM 으로 저장 — Excel 에서 바로 열림. 열: InspectedAt, Verdict, Recipe, NgCodes, Mode, CycleTimeMs, SerialNumber, WorkOrderId, LotId, ImagePath, CorrelationKey, ToolResults.

### 매뉴얼 반영 지점
| 위치 | 해야 할 일 |
|------|-----------|
| §3.5 단독 모드 | "생산 이력 조회" 소절 신설: [전체 이력] 버튼 → 필터·탭 3개·CSV 절차. 기본 조회 기간 최근 7일 |
| §4 VMS 운전 · Recent Inspections | [전체 이력] 버튼 설명 한 줄 + 새 창 상호 참조 |
| §7 Web(Production History) | "단독 모드에서는 VMS 의 생산 이력 조회 창을 사용" 상호 참조 |
| §11.4 변경 이력 | "생산 이력 조회 창(로컬 DB) 신설 — 목록/상세 이미지/일별 집계/NG 파레토/CSV" |

### 스크린샷
| 파일 | 조치 |
|------|------|
| 생산 이력 조회 창 (이력 목록 탭, NG 행 선택 + 이미지) | ✅ `36_dlg_inspection_history.png` — `VMS.exe --capture-dialogs` "InspectionHistory"(이력 없으면 문서용 대표 6행) |
| 일별 집계 탭 · NG 파레토 탭 | ✅ `37_dlg_inspection_history_summary.png` · `38_dlg_inspection_history_pareto.png` — `--capture-dialogs` "InspectionHistory_Summary/_Pareto" 장면(HistoryTabs.SelectedIndex, 대표 집계 데이터 VM 주입) |
| Recent Inspections 패널 헤더 ([전체 이력] 버튼) | ✅ `vms_ctl/sec_recent.png` 재캡처 (`--capture-controls`) |

### 코드 참조 (검증용)
- `VMS/Views/InspectionHistoryWindow.xaml(.cs)`, `VMS/ViewModels/InspectionHistoryViewModel.cs`
- `VMS/Services/LocalHistory/InspectionHistoryCsvExporter.cs`, 저장소 `GetRecipeNames`
- `VMS/ViewModels/MainViewModel.cs` `OpenInspectionHistory` / `IsLocalHistoryAvailable`, `VMS/Views/MainWindow.xaml` Recent Inspections 헤더
- 테스트: `VMS.Tests/ViewModels/InspectionHistoryViewModelTests.cs` 7건

---

## 6. VMS 메인 — Settings 사이드 패널 버튼 아이콘 세로 정렬 — **반영 완료 (2026-09-04, 캡처 교체 + docx 재생성)**

| 항목 | 내용 |
|------|------|
| PR | #421 (`fix/sidepanel-icon-valign`) |
| 날짜 | 2026-09-04 |
| 앱 · 화면 | VMS 메인 우측 Settings 사이드 패널의 아이콘+텍스트 버튼 전부 (Recipe Load/New · External Tools · Updates · Web Parameters · Image Saving · Roller) |
| 변경 종류 | 시각 수정 (동작 변경 없음) |

### 무엇이 바뀌었나
- 버튼 안 아이콘(Segoe MDL2 글리프)이 텍스트보다 위로 떠 있던 것을 텍스트와 세로 중앙에 맞췼다. 원인은 가로 StackPanel 안의 TextBlock 두 개가 세로 정렬 없이 늘어나 위에서부터 그려지던 것 — 22개 TextBlock 에 VerticalAlignment=Center.

### 매뉴얼 반영 지점
| 위치 | 해야 할 일 |
|------|-----------|
| 본문 | 변경 없음 |
| 스크린샷 | `vms_ctl/sec_*.png` 사이드 패널 섹션 캡처 전부 교체(`VMS.exe --capture-controls`) — 완료 |

---

## 7. Web — 로그인 화면 "로그인 유지" 체크박스 + 세션 절대 만료 30일 — **반영 완료 (2026-09-04): HTML §7.1 3번 항목 교체 · §11.4 한 줄, `40_web_login.png` 재캡처(v1.8.0 라이브 서버, 1578×902), 생성기 캡션 갱신, docx 재생성**

| 항목 | 내용 |
|------|------|
| PR | Web #96 (`feat/login-remember-me-absolute-expiry`) |
| 날짜 | 2026-09-04 |
| 앱 · 화면 | BODA.VMS.Web 로그인 화면 (아이디·비밀번호 아래, [로그인] 버튼 위) |
| 변경 종류 | UI 추가 + 세션 유지 정책 변경 |

### 무엇이 바뀌었나
- 로그인 화면에 **"로그인 유지"** 체크박스가 생겼다 (기본 해제). 아래 힌트 문구가 함께 표시된다:
  > 체크하지 않으면 브라우저를 닫거나 8시간이 지나면 다시 로그인합니다. 공용 PC에서는 체크하지 마세요.
- **체크 안 함(기본)**: 브라우저(탭)를 닫거나 로그인 후 8시간이 지나면 다시 로그인해야 한다.
- **체크함**: 브라우저를 닫아도 로그인이 유지되고 8시간마다 자동으로 연장된다. 단 **처음 로그인한 날로부터 30일**이 지나면 무조건 다시 로그인한다.
- [로그아웃]은 종전과 같다 (즉시 끊김).

### 왜 바꿨나 (매뉴얼에 넣을 설명의 근거)
- 종전에는 로그인 상태가 자동으로 계속 연장되어 공장 공용 PC 에 관리자 계정이 사실상 무기한 남아 있었다 (실증 PC 에서 발견). GS 심사의 세션 관리 항목 대비.
- 관리자가 서버 설정으로 기간을 바꿀 수 있다: 8시간 = `Jwt:ExpireMinutes`, 30일 = `RefreshToken:AbsoluteExpireDays` (0 이면 상한 없음), 자동 연장 단위 7일 = `RefreshToken:ExpireDays`.

### 매뉴얼 반영 지점
| 위치 | 해야 할 일 |
|------|-----------|
| §7.1 로그인 / 회원가입 3번 항목 ("한 번 로그인하면 브라우저가 로그인 상태를 기억하므로…") | 문장 교체: "로그인 유지를 체크하면 브라우저를 닫아도 로그인이 유지됩니다(최대 30일). 체크하지 않으면 브라우저를 닫거나 8시간이 지나면 다시 로그인합니다. 여러 사람이 쓰는 PC 에서는 체크하지 마세요." |
| §7.1 그림 4-0 `40_web_login.png` | 체크박스가 보이도록 재캡처 (Web 배포 후 데모 캡처 절차, 운영 DB 무접촉) |
| §9 관리자 매뉴얼 (Web 서버 설정) | 세 설정 키와 기본값 한 줄 (선택) |
| §11.4 변경 이력 | Web v1.8.0 한 줄 |

### 코드 참조 (검증용)
- Web `BODA.VMS.Web.Client/Pages/Login.razor`, `Services/AuthStateProvider.cs`(localStorage ↔ sessionStorage)
- Web `Services/AuthService.cs` `IssueTokensAsync`, `Services/RefreshTokenOptions.cs`, `Data/Entities/RefreshToken.cs` `AbsoluteExpiresAt`
- 테스트: `AuthServiceRefreshTests`(+6), `AuthRefreshEndpointTests`(+1)

---

## 8. VisionSetup — Feature Match 재학습 원점 선택 + 회전 ROI 얼라인 기준 각도 + 재학습 알림 — **반영 완료 (2026-09-04): HTML §5.1 얼라인 콜아웃 · §5.7 학습 목록 보강 + "재학습 원점" 소절 신설 · §11.4 6줄, 신규 그림 `50_fm_retrain_origin.png`(`--capture-toolpanels` 09_Feature_Match 상단 크롭) + 생성기 POST 앵커, docx 재생성**

| 항목 | 내용 |
|------|------|
| PR | #424 (`fix/matchalign-trained-angle-retrain-origin`) |
| 날짜 | 2026-09-04 |
| 앱 · 화면 | VMS.VisionSetup — Feature Match 도구 설정(Models 영역), Match Align 도구, 학습(Train) 후 상태 표시줄/경고 창 |
| 변경 종류 | 결함 수정 2 + UI 옵션 추가 1 + 알림 추가 1 |

### 무엇이 바뀌었나
1. **회전 ROI로 학습한 뒤 Match Align Δθ 가 ROI 각도만큼 나오던 결함 수정.** 학습 기준 각도가 항상 0° 였는데, 이제 학습 당시 ROI 각도를 기준 각도로 씁니다. 같은 장면을 다시 Run 하면 Δθ ≈ 0.
2. **레시피를 다시 열면 학습 원점이 ROI 위치가 아닌 템플릿 중심으로 복원되던 결함 수정.** 이제 학습 원점(중심 X/Y·각도)이 레시피에 저장되고 그대로 복원됩니다. 이전 레시피는 열 때 ROI 기준으로 자동 재계산합니다.
3. Feature Match 설정 Models 영역에 **"재학습 원점"** 선택이 생겼습니다.
   - **UseCurrentImage(기본)**: [Train Selected] 로 다시 학습하면 원점을 현재 ROI 의 위치·각도로 옮기고 기준 이미지도 현재 장면으로 바꿉니다. (종전 동작)
   - **KeepReference**: 원점·기준 각도·기준 이미지는 그대로 두고 템플릿 특징만 새로 학습합니다. Match Align 마스터 포즈를 유지한 채 다른 장면으로 특징을 보강할 때 씁니다.
   - [Add Model] 로 만드는 새 모델은 항상 현재 이미지 기준입니다.
4. **재학습 후 알림.** Feature Match 에 Match Align 이 Result 로 연결돼 있으면 학습 완료 상태줄에 "기준 원점이 현재 이미지 학습 위치로 갱신됨 / 기준 이미지 그대로 유지됨"이 붙습니다. Match Align 이 **수동 기준**(학습 기준 사용 해제)을 쓰고 있으면 경고 창이 뜹니다:
   > 연결된 Match Align '…' 은(는) 수동 기준(Ref X/Y/θ)을 사용 중입니다. … 기준 부품을 놓고 Run 한 뒤 Match Align 설정의 [현재 매칭을 기준으로 등록]으로 원점을 다시 등록하세요.

### 왜 바꿨나 (매뉴얼에 넣을 설명의 근거)
- 실증 PC(2026-09-04): 표준 2D 매치 얼라인 템플릿에서 부품과 Train ROI(회전)를 옮겨 재학습했더니 같은 장면인데 Δ 가 0 이 아니었다. 회전 ROI 는 정렬 워프 후 학습되므로 매칭 각도가 ROI 각도로 나오는데 기준 각도가 0° 로 고정돼 있었다.
- 재학습이 얼라인 기준을 조용히 바꾸는 구조라서, 의도(기준 유지 vs 새 기준)를 사용자가 고를 수 있어야 하고 연결된 얼라인에 영향을 알려야 한다.

### 매뉴얼 반영 지점
| 위치 | 해야 할 일 |
|------|-----------|
| §5 VisionSetup — Feature Match 도구 설정 (Models 영역) | "재학습 원점" 항목 추가: 두 값의 뜻과 기본값, 새 모델은 항상 현재 이미지 기준. 스크린샷 재캡처 (`--capture-controls` FeatureMatch 설정) |
| §5 VisionSetup — Match Align 도구 | "학습 기준 사용" 설명에서 기준 각도 = 학습 ROI 각도(축 정렬이면 0°) 로 문구 수정. 재학습 후 상태줄/경고 창 안내 한 단락 |
| §5 얼라인 워크플로 (템플릿 갤러리 얼라인 3종) | "부품을 옮기고 재학습하면 기준이 새 위치로 바뀐다(기본). 마스터 포즈를 유지하려면 재학습 원점을 KeepReference 로" 주의 문장 |
| §11.4 변경 이력 | 한 줄 |

### 코드 참조 (검증용)
- `VMS.VisionSetup/VisionTools/PatternMatching/FeatureMatchTool.cs` `RetrainOriginMode`, `TrainPattern`(TrainedAngle), `FixLegacyTrainedOrigins`
- `VMS.VisionSetup/VisionTools/PatternMatching/MatchAlignTool.cs` 학습 기준 refTheta
- `VMS.VisionSetup/Services/ToolSerializer.cs` 모델 TrainedCenterX/Y/TrainedAngle 저장·복원, `VMS.VisionSetup/Services/MatchAlignRetrainAdvisor.cs`
- `VMS.VisionSetup/ViewModels/MainViewModel.cs` `TrainPattern`
- 테스트: `FeatureMatchRetrainOriginTests`(8), `MatchAlignToolTests`(+2), `MatchAlignRetrainAdvisorTests`(3)


---

## 9. Detection 학습 기본 백본 D-FINE(Apache 2.0) 전환 — **반영 대기**

| 항목 | 내용 |
|------|------|
| PR | #429 (`feat/dfine-detection-backbone`, 2026-09-08 머지) |
| 날짜 | 2026-09-08 |
| 앱 · 화면 | VMS.DeepLearning 학습 패널(Training) · VMS.VisionSetup Detection 도구 설정 |
| 변경 종류 | 학습 백본 교체(라이선스 대응) + 안내 문구 · 사전 준비 절차 변경 (도구 조작 동일) |

### 무엇이 바뀌었나
- Detection 데이터셋을 열면 자동으로 잡히는 학습 스크립트가 `train_yolo.py` → **`train_dfine.py`** 로 바뀐다. Ultralytics YOLO(AGPL-3.0) 대신 D-FINE(Apache 2.0, HuggingFace transformers 구현) 으로 학습한다.
- 사전 준비(pip) 안내: `pip install ultralytics onnx` → **`pip install torch torchvision "transformers>=4.52" onnx onnxruntime pyyaml`** (선택 `torchmetrics` 설치 시 학습 패널에 mAP50 표시).
- 사전학습 모델 입력란을 비우면 `ustc-community/dfine-small-obj2coco` 를 자동 내려받는다 (첫 실행 인터넷 필요). 로컬 폴더 경로도 가능.
- 학습률 기본 권장값이 0.001(YOLO) → **0.00025**(D-FINE, AdamW). 큰 값이면 로그에 경고.
- 증강 패널 안내 문구: hsv_h/s/v 는 D-FINE·YOLO 공통, **mosaic/mixup 은 YOLO 학습에만 적용** (D-FINE 에서는 무시된다는 경고 로그).
- 라벨링 형식(YOLO txt + data.yaml)은 그대로. 세그멘테이션 폴리곤 라벨은 외접 박스로 자동 변환.
- 출력은 동일하게 `best.onnx`. VisionSetup Detection 도구는 파일을 열 때 D-FINE / YOLO 규약을 자동 판별하므로 **도구 조작·파라미터는 이전과 동일** (Model Path, Input Size 640, Confidence, IoU, SAHI, Dot 분석 전부 그대로).
- `train_yolo.py` 는 남아 있으며 학습 스크립트 경로를 수동으로 바꾸면 계속 쓸 수 있다 — Ultralytics Enterprise License 보유 사이트 전용이라는 주의 문구를 매뉴얼에 넣는다.

### 매뉴얼 반영 포인트
- §6 DeepLearning: "사전 준비" pip 목록, "학습 시작" 절의 스크립트 이름·학습률 권장값, 증강 절의 mosaic/mixup 주석, 스크린샷(학습 패널 안내 문구 변경).
- §5 Detection 도구: 도움말 문구 "D-FINE 또는 YOLO ONNX 자동 판별" 한 줄.
- §11.4 변경 이력 한 줄 + 부록 라이선스 표(`gs_distribution_policy.md` §2.6 과 동일하게 D-FINE Apache 2.0 / YOLO 옵션·AGPL).

---

## 10. AI 학습 도구 GS 인증 범위 제외 — 매뉴얼 6장 별책 분리 · 인스톨러 선택 구성 — **반영 완료 (2026-09-08)**

| 항목 | 내용 |
|------|------|
| PR | #431 (`feat/aitools-installer-feature`, 코드) + #432 (`docs/gs-scope-ai-tools`, 문서) — 2026-09-08 머지, v1.31.0 |
| 날짜 | 2026-09-08 |
| 앱 · 화면 | 설치 마법사 "설치 구성 선택" 화면 · VisionSetup Tools 메뉴 · OCR Synth Data 창 |
| 변경 종류 | 설치 옵션 추가 + 매뉴얼 구성 변경 (인증 범위 정의: `guides/gs_scope_ai_tools.md`) |

### 무엇이 바뀌었나
- 설치 구성 선택 화면에 **"AI 학습 도구 포함 (VMS.DeepLearning: 라벨링·모델 학습)"** 체크박스가 Web 서버 체크박스 아래에 추가됐다(기본 켬). 해제하면 VMS.DeepLearning 과 `scripts\*.py` 가 설치되지 않는다. 무인 설치는 `INSTALLAITOOLS=0`.
- 미설치 PC 에서는 VisionSetup Tools 메뉴의 **Deep Learning 항목이 보이지 않는다**. OCR Synth Data 창은 학습 카드와 [Generate & Train] 이 숨겨지고 합성 데이터 생성만 남는다.
- GS 인증 제출 빌드(`VMS-x.y.z-cert.msi`)에는 이 구성 요소가 아예 없다.
- 매뉴얼: 본편 6장을 **별책 `BODA-VMS-AI-Tools-Manual.html`** 로 옮기고, 본편 6장은 "AI 학습 도구 (별책 · 선택 구성 요소)" 안내 절로 축약. 별책 §2 에 #429(D-FINE) 사전 준비·학습률·증강 안내, §3 에 라이선스 안내 수록.

### 매뉴얼 반영 포인트
- §2.1 설치: 설치 구성 선택 화면 캡처 `71_msi_weboption.png` 재캡처 필요 (체크박스 2개).
- §6·§11.4·목차: 반영 완료 (HTML). docx 재생성 필요.
- 제품설명서 생성기(`gen_product_description.js`) 적용 범위 문장·구성표 갱신 → docx 재생성 필요.

---

## 11. AI 학습 도구 ↔ MLOps 레지스트리 연동 + RF-DETR-seg 도구 신설 — **반영 대기**

| 항목 | 내용 |
|------|------|
| PR | #439 (`feature/wpf-registry-upload`) · #440 (`feature/wpf-dataset-download`) · #441 (`feature/rfdetr-segmentation`) · #442 (`feature/rfdetr-seg-wiring`) — 2026-09-09 머지, 릴리즈 미발행 |
| 날짜 | 2026-09-09 |
| 앱 · 화면 | VMS.DeepLearning 학습 패널(Training)·Export 구역 · VMS.VisionSetup Deep Learning 도구 팔레트·RF-DETR-seg 도구 설정 |
| 변경 종류 | 기능 추가 (버튼 2개 + 창 2개, 도구 1종) + 결함 수정 1건 |

### 무엇이 바뀌었나
- **[레지스트리에 등록]** (학습 패널, #439): 학습이 끝난 ONNX 를 MLOps 모델 레지스트리에 새 버전으로 올린다. 창에서 웹 계정으로 로그인 → 새 모델 계열 또는 기존 계열 선택 → 라이선스(YOLO 계열은 AGPL-3.0 필수)·메모 → [올리기]. 올린 버전은 **Candidate** 라 라인에 바로 나가지 않고, 레지스트리에서 승격해야 라인 PC 가 `model://` 참조로 받아 간다. 등록 권한은 현재 **Web Admin 계정만** 통과한다(MLOps Engineer 정책, Web 역할에 Engineer 없음 — 운영 정책 확정 시 문구 재검토).
- **[웹 데이터셋 내려받기]** (Export 구역, #440): 웹에서 라벨링한 데이터셋의 **판(스냅샷)** 을 골라 받으면 학습 스크립트가 읽는 내보내기 폴더로 풀리고 학습 데이터셋 경로가 그 폴더로 설정된다. 이미 뜬 판을 고르거나 [새 판 뜨기] 로 지금 상태를 굳힌다. 옵션: 미라벨 포함(검출 배경 샘플용)·검토 완료만. 폴더 이름 `{판 이름}-{해시 8자리}`, 다시 받으면 폴더를 비우고 새로 푼다.
- 두 창 모두 `system_config.json` 의 MLOps 서버 주소·웹 서버 주소가 없으면 열리지 않고 무엇이 빠졌는지 알려 준다 (설정 마법사 2단계 고급 설정).
- **RF-DETR-seg 도구** (#441 #442): Deep Learning 팔레트에 YOLOv8-seg 옆 [RF-DETR-seg]. 하는 일은 같고 Ultralytics(AGPL-3.0) 의존이 없다. 파라미터: Model Path(.onnx, [레지스트리…] 가능) · Input Size(**모델을 열면 ONNX 가 정하는 값으로 덮이고 칸이 잠김** — RF-DETR 은 모델마다 배수가 달라 손으로 맞추면 안 됨) · Confidence Threshold · Max Instances(점수 순 상한) · Show Mask Overlay·Overlay Opacity·Draw Boxes · Output Mask Image. **IoU·마스크 임계 없음**(집합 예측이라 NMS 미사용). 결과 키 `Inst{i}_Class/ClassName/Score/X/Y/Width/Height/MaskPixels`, 0건 검출 = NG(자매 도구와 동일).
- 세그멘테이션 데이터셋을 열면 학습 스크립트가 `train_rfdetr_seg.py` 로 자동 매칭된다. 사전 준비: `pip install "rfdetr[train]>=1.10,<2" onnx onnxruntime` (COCO 형식 내보내기 사용).
- 결함 수정: YOLOv8-seg 도구의 Output Mask Image 가 레시피를 다시 열면 꺼져 있던 문제 — 이제 저장한 대로 복원.

### 매뉴얼 반영 포인트
- 별책 `BODA-VMS-AI-Tools-Manual.html`: 학습 패널 절에 [레지스트리에 등록] 소절(창 캡처 4상태 중 로그인·등록 대상 2장), Export 절에 [웹 데이터셋 내려받기] 소절(창 캡처 1장), §2 학습 환경 준비에 세그멘테이션 스크립트·pip 한 줄, 라이선스 표에 RF-DETR Apache 2.0 한 줄.
- 본편 §5 Deep Learning 도구 표에 RF-DETR-seg 행 + 소절(툴 패널 캡처 `--capture-toolpanels` → `32_RF-DETR-seg.png`, 임시 캡처 `D:/Temp/toolpanels/32_RF-DETR-seg.png` 참고), YOLOv8-seg 소절에 "라이선스 회피 대안 RF-DETR-seg" 상호 참조 한 줄.
- 본편 §3.2 설정 마법사 2단계 고급 설정 표: MLOps 서버 주소·웹 서버 주소가 학습 도구의 등록·내려받기에도 쓰인다는 한 줄.
- §11.4 변경 이력 3줄 (등록·내려받기·RF-DETR-seg) + 부록 라이선스 표(RF-DETR Apache 2.0).
- 6장 별책은 GS 인증 범위 외이므로 인증 제출본에는 본편 §5 도구 행만 들어간다.

---

## 12. 불량 이미지 MLOps 학습 데이터 수집 (라인 NG 송신부) — **반영 대기**

| 항목 | 내용 |
|------|------|
| PR | #446 (`feat/mlops-line-ng-sender`) — 2026-09-09 머지, 릴리즈 미발행 |
| 날짜 | 2026-09-09 |
| 앱 · 화면 | VMS 메인 → 설정 → 이미지 저장 설정 창 → 새 카드 "MLOps 학습 데이터 수집" (Web 연동 카드 아래, 파일명 규칙 위) |
| 변경 종류 | 기능 추가 (체크박스 1 + 숫자 칸 1 + 안내 문구) |

### 무엇이 바뀌었나
- **[NG 이미지 MLOps 전송]** 체크: 불량으로 판정된 사진을 원본 해상도 그대로 MLOps 데이터 풀에 올린다. 웹의 데이터 관리 화면에 출처 "라인 NG" 로 쌓이고, 라벨링해 다음 모델 학습에 쓴다. Web 생산 이력으로 가는 "NG 이미지 Web 전송"(썸네일, 사람이 보는 용도)과는 별개의 스위치다.
- **양품 샘플 비율**: 양품 N 장에 1 장을 함께 보낸다(기본 200). 0 이면 양품은 보내지 않는다. 정상 사진이 조금은 있어야 모델이 "정상" 을 배운다는 뜻을 안내 문구로.
- 카드 아래 안내: MLOps 서버 주소·라인 토큰이 설정돼 있으면 "설정되어 있습니다", 없으면 "설정 마법사(AppSetup) 2단계 고급 설정에서 입력해야 전송됩니다" — 토글을 켜도 나가지 않는 상황을 화면이 먼저 말한다.
- 동작 특성(사용자에게 보이는 것만): 검사 속도에 영향 없음, 서버가 꺼져 있으면 큐에 쌓였다가 다시 켜지면 나감(재시작해도 유지), 서버가 파일을 거절하면 다시 보내지 않고 `mlops_line_ng_queue\rejected` 에 사유 파일과 함께 남음.

### 매뉴얼 반영 포인트
- 본편 §4 이미지 저장 설정 절: 카드 소절 1개 + 설정 창 캡처 재촬영(카드가 추가돼 창이 길어짐, `--capture-dialogs` 의 `ImageSave.png`).
- 본편 §3.2 설정 마법사 2단계 고급 설정 표: MLOps 서버 주소·라인 토큰이 "불량 이미지 수집" 에도 쓰인다는 한 줄(§11 의 한 줄과 합쳐도 됨).
- 별책(AI 학습 도구) 데이터 관리 절: "라인에서 올라온 NG 사진" 이 어디서 켜지는지 본편 §4 상호 참조 한 줄.
- §11.4 변경 이력 1줄.
