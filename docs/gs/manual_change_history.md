# 매뉴얼 반영 대기 — 변경 히스토리 (상세)

`README.md` 의 "매뉴얼 반영 대기" 표는 **한 줄 요지**만 적는 장부이고, 이 문서는 매뉴얼 검토 기간
(2026-09-02~) 동안 코드 쪽에서 바꾼 내용을 **매뉴얼 편집자가 코드를 다시 열지 않아도 되도록**
화면 · 문구 · 스크린샷 · 대상 절 단위로 풀어 쓴 상세 기록이다.

- 기재 시점: 사용자에게 보이는 변경 PR 을 만들 때 (머지 전이라도 먼저 적고, 머지 후 PR 번호를 채운다)
- 매뉴얼 갱신 완료 시: 항목을 "반영 완료" 로 표시하고 기준선을 올린다 (README 표와 동시에)
- 기준선: **SW v1.28.1 / 매뉴얼 v2.0 (2026-09-04 §3·§4·§5 단독 모드 로컬 생산이력 반영분까지 — §1·§2 AppSetup 3단계 입력란 정리는 미반영)**

---

## 1. AppSetup 3단계(카메라 구성) — Area Scan 노출/게인 입력란 제거

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

## 2. AppSetup 3단계(카메라 구성) — 3D 패널(Capture Mode / Filter Strength / Z Min / Z Max) + Encoder Res 제거

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
| 생산 이력 조회 창 (이력 목록 탭, NG 행 선택 + 이미지) | 신규 캡처 — `VMS.exe --capture-dialogs` 에 "InspectionHistory" 항목 추가됨(이력 없으면 문서용 대표 6행 표시) |
| 일별 집계 탭 · NG 파레토 탭 | 수동 캡처 (탭 전환) |
| Recent Inspections 패널 헤더 ([전체 이력] 버튼) | `--capture-controls` 사이드 패널 장면 재캡처 |

### 코드 참조 (검증용)
- `VMS/Views/InspectionHistoryWindow.xaml(.cs)`, `VMS/ViewModels/InspectionHistoryViewModel.cs`
- `VMS/Services/LocalHistory/InspectionHistoryCsvExporter.cs`, 저장소 `GetRecipeNames`
- `VMS/ViewModels/MainViewModel.cs` `OpenInspectionHistory` / `IsLocalHistoryAvailable`, `VMS/Views/MainWindow.xaml` Recent Inspections 헤더
- 테스트: `VMS.Tests/ViewModels/InspectionHistoryViewModelTests.cs` 7건
