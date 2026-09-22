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

## 9. Detection 학습 기본 백본 D-FINE(Apache 2.0) 전환 — **반영 완료 (2026-09-10, 별책 §2·§3 은 2026-09-08 선반영 + 본편 §5.10 Detection 자동 판별 한 줄)**

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

## 11. AI 학습 도구 ↔ MLOps 레지스트리 연동 + RF-DETR-seg 도구 신설 — **반영 완료 (2026-09-10: 별책 §1 소절 2개·§2.1·§3·§4 v1.1, 본편 §5.10 신설(51_rfdetr_seg_panel.png)·§3 고급 설정·§11.4 + 등록/내려받기 창 그림 3장 `deeplearning_dlg/60~62`(VMS.DeepLearning `--capture-dialogs`, 2026-09-10))**

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

## 12. 불량 이미지 MLOps 학습 데이터 수집 (라인 NG 송신부) — **반영 완료 (2026-09-10: 본편 §4.3 소절·21_dlg_imagesave.png 재촬영·§3 고급 설정·§11.4, 별책 §1 상호 참조)**

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

---

## 13. 인스톨러 "AI 학습 도구 포함" 기본 해제 — **반영 완료 (2026-09-10, 문구 + 71_msi_weboption.png 재캡처)**

| 항목 | 내용 |
|------|------|
| PR | #452 (`chore/installer-aitools-default-off`) |
| 날짜 | 2026-09-10 |
| 앱 · 화면 | 설치 마법사 "설치 구성 선택" 화면 — "AI 학습 도구 포함 (VMS.DeepLearning: 라벨링·모델 학습, 기본 해제)" 체크박스 |
| 변경 종류 | 기본값 변경 (체크 → 해제) + 업그레이드 시 이전 선택 유지 |

### 무엇이 바뀌었나
- 새로 설치할 때 AI 학습 도구 체크가 **해제** 상태로 시작한다. 라벨링·학습이 MLOps 플랫폼(웹 라벨링·학습 워커)으로 옮겨 가서, MLOps 서버 없이 이 PC 에서 직접 학습해야 하는 현장만 체크한다.
- 이미 AI 학습 도구를 설치한 PC 를 업그레이드(인앱 업데이트 포함)하면 이전 선택이 유지된다 — 체크가 켜진 채로 나오며, 무인 업그레이드도 도구를 지우지 않는다. 업그레이드 화면에서 체크를 해제하면 그때 제거되고 다음부터는 기억하지 않는다. 마커가 없던 v1.33 이하에서 올라오는 첫 업그레이드는 기본 설치 폴더의 `VMS.DeepLearning.exe` 존재로 판정(설치 폴더를 바꾼 PC 는 첫 업그레이드를 MSI 수동 실행으로).
- 무인 설치는 `INSTALLAITOOLS=1` 로 포함(이전에는 `=0` 으로 제외). GS 인증 빌드(-cert.msi)는 종전대로 컨트롤 자체가 없다.

### 매뉴얼 반영 포인트
- 본편 §6 안내 문단·§11.4 한 줄, 별책 "이 문서의 위치" 콜아웃 — 반영 완료.
- `71_msi_weboption.png` 재캡처 완료 — `tools\capture-msi-weboption.ps1`(msiexec UI → 준비 대기 → 다음(N) BM_CLICK 2회 → PrintWindow → UI 종료, 설치 없음).

---

## 14. MLOps 학습 워커 설치 패키지 (Phase 3 마무리) — **반영 완료 (2026-09-14, 별책 §2 '웹에서 학습하기' + 그림 3장 `mlops/worker_install_wizard.png`·`mlops/worker_register_dialog.png`·`mlops/worker_card.png`)** — 스크린샷 잔여 없음. 설치 마법사 그림은 dev PC 에서 제거→마법사 캡처→재설치로 얻었다(worker.json·venv 는 %ProgramData% 에 남아 그대로 복구, 같은 workerId 로 재등록 확인)

| 항목 | 내용 |
|------|------|
| PR | BODA.VMS.MLOps `feat/trainworker-msi` (VMS 리포 변경 없음) |
| 날짜 | 2026-09-10 |
| 앱 · 화면 | 학습 워커 설치 마법사(설치 폴더 → 워커 연결 설정 → 설치) · Windows 서비스 `BodaVmsTrainWorker` · CLI `configure`/`diag` |
| 변경 종류 | 신규 설치 패키지 + 오프라인 wheel 번들 + 워커 설치 가이드 |

### 무엇이 바뀌었나
- GPU PC 에 학습 워커를 MSI 로 설치한다(`BODA-VMS-TrainWorker-<버전>.msi`, .NET 동봉 약 30MB). 마법사에서 관리 서버 주소·워커 토큰을 넣으면 서비스가 바로 시작되어 파이썬 환경(venv)을 스스로 준비한다(온라인 약 6GB, 10~40분). 폐쇄망은 `-offline.msi`.
- 라인 PC 는 그대로 CPU 추론만 한다. 학습은 공장당 GPU PC 1대(올인원 또는 분리형)가 맡는다.
- 절차 원본: `D:\Repo\BODA.VMS.MLOps\docs\워커 설치 가이드.md`.

### 매뉴얼 반영 포인트
- 별책(AI 학습 도구) §2 를 "웹에서 학습" 절로 확장: 워커 설치(가이드 §1~§4 요약) · 관리 화면 ▸ 워커 ▸ 토큰 발급 · 학습 작업 제출 시 사전학습 자산 선택 필수(없으면 워커가 실패).
- 본편 §3.2 설정 마법사 고급 설정 표 옆에 "학습 PC 구성" 한 줄: MLOps 서버 주소는 라인 PC(수집·모델 받기)와 워커 PC(학습)가 같은 값을 쓴다.
- 배포 정책 §2.6: 인스톨러 "AI 학습 도구" 체크는 MLOps 서버가 없는 사이트 전용, 서버가 있으면 워커 MSI 로 대체.
- 캡처: 워커 설치 마법사 "워커 연결 설정" 화면 1장, 관리 화면 워커 카드 1장.

---

## 15. train_dfine.py 학습 곡선·에폭별 지표 아티팩트 — **반영 완료 (2026-09-14, 별책 §2 '학습이 끝나면 생기는 파일' + 본편 §11.4)**

| 항목 | 내용 |
|------|------|
| PR | #454 (`feat/dfine-train-curves`) + MLOps #3(사본·화면) |
| 날짜 | 2026-09-10 |
| 앱 · 화면 | 학습 스크립트(WPF 도구·CLI·MLOps 워커 공통) · MLOps 관리 화면 학습 작업 상세 "학습 곡선" 칸 |
| 변경 종류 | 출력 파일 추가 (`metrics.json`·`curves.png`), 사용자 조작 없음 |

### 무엇이 바뀌었나
- D-FINE 학습이 에폭마다 `metrics.json`(요약 숫자 + 에폭별 history)과 `curves.png`(손실·mAP50 곡선, best 에폭 점선)를 출력 폴더에 남긴다. MLOps 워커가 올리면 작업 상세에 곡선이 보이고 모델 버전 지표에 epochs·best_epoch·train_loss·val_loss·map50 요약이 들어간다.
- WPF 학습 도구로 돌려도 같은 파일이 출력 폴더에 생긴다.

### 매뉴얼 반영 포인트
- 별책(AI 학습 도구) 학습 결과 절: 출력 폴더 파일 목록에 두 파일 한 줄 + "웹에서 학습" 절의 작업 상세 그림에 곡선 칸.

---

## 16. MLOps 연동 비활성 사유 표시 (상태바 칩 · 이미지 저장 설정 힌트) — **반영 완료 (2026-09-14, 본편 §4.2.10 신설 + §4.3 카드 + 마법사 표 + 그림 `vms_ctl/status_mlops_issue_chip.png`)** · ⚠ 칩 위치는 **헤더가 아니라 하단 상태바** — 캡처하면서 확인해 문구 정정

| 항목 | 내용 |
|------|------|
| PR | #455 (`feat/mlops-disabled-reason`) |
| 날짜 | 2026-09-10 |
| 앱 · 화면 | VMS 메인 헤더 상태 칩 "MLOps 비활성" · 이미지 저장 설정 창 "MLOps 학습 데이터 수집" 카드 힌트 |
| 변경 종류 | 상태 표시 추가 (동작 변경 없음) |

### 무엇이 바뀌었나
- 실증에서 라인 PC 가 Production 보안 모드일 때 `http://` MLOps 서버 주소가 보안 정책에 거부되어 NG 수집·모델 받기가 **아무 표시 없이** 꺼져 있던 문제. 이제 설정은 있는데 시작 시 연동 클라이언트가 만들어지지 못하면 헤더에 빨간 "MLOps 비활성" 칩(툴팁에 사유)이 뜨고, 이미지 저장 설정 창 힌트가 빨간색으로 사유와 조치("MLOps 서버를 https 로 열거나, 사내 실증이면 AppSetup 보안 모드를 Development 로 바꾼 뒤 VMS 재시작")를 보여 준다.
- MLOps 서버 주소·라인 토큰이 없는(연동 안 하는) PC 에는 아무것도 뜨지 않는다.

### 매뉴얼 반영 포인트
- 본편 §4.3 "MLOps 학습 데이터 수집" 카드 설명에 "전송이 비활성입니다" 힌트와 조치 한 줄.
- 본편 §3.2 보안 모드 설명에 "http:// MLOps 서버는 Production 모드에서 거부됨(https 필요)" 한 줄.
- 헤더 칩 그림 1장(비활성 상태 재현 필요: Production 모드 + http 주소).

---

## 17. MLOps 수집 사진은 오버레이 전 원본으로 전송 — **반영 완료 (2026-09-14, 본편 §4.3 — 로컬·Web 사진과의 차이 명시)**

| 항목 | 내용 |
|------|------|
| PR | #456 (`feat/mlops-original-image`), v1.35.1 |
| 날짜 | 2026-09-10 |
| 앱 · 화면 | VMS 메인 검사 흐름(화면 변화 없음) |
| 변경 종류 | 동작 수정 — MLOps 로 보내는 사진이 결과 그래픽 없는 원본으로 바뀜 |

### 무엇이 바뀌었나
- 실증에서 MLOps 에 올라온 라인 사진에 검사 결과 그래픽(박스·문자)이 그려져 있었다. 검사 완료 이벤트가 화면용 오버레이 이미지를 넘겨 저장·업로드 모두 그 이미지를 쓰고 있었기 때문. 이제 검사에 넣은 원본을 함께 넘기고 **MLOps 학습 데이터 수집만 원본**을 보낸다.
- 로컬 OK/NG 저장과 Web NG 이미지는 사람이 보는 용도라 종전대로 결과 그래픽이 있는 이미지를 유지한다.

### 매뉴얼 반영 포인트
- 본편 §4.3 "MLOps 학습 데이터 수집" 카드 설명에 "결과 그래픽 없는 원본 해상도 사진이 올라간다" 한 줄. (로컬 저장·Web 업로드는 결과 그래픽 포함 — 차이 명시)

---

## 18. Web 연동 계약 결함 3건 (API 키·결과 폐기·이미지 평문) — **반영 완료 (2026-09-14, 본편 §8.3·§10.1 + 마법사 표 API Key 행)**

| 항목 | 내용 |
|------|------|
| PR | #459 (`fix/web-audit-vms-contract`), v1.36.0 |
| 날짜 | 2026-09-14 |
| 앱 · 화면 | VMS 메인(작업자 로그인·작업지시·Lot·예측 위젯), 이미지 업로드 |
| 변경 종류 | 동작 수정 — 화면 구성 변화 없음, 오류 메시지 1건 변경 |

### 무엇이 바뀌었나

1. **Web API 키를 보내지 않던 통신 4종** — 작업자 로그인, 작업지시 목록, Lot 자동 채움, 예측 위젯이
   `X-API-Key` 를 아예 보내지 않았다. 운영 Web 을 키 강제 모드(`ClientApiKey:Required=true`)로 바꾸면
   이 넷이 전부 인증 실패가 되는데, 화면에는 "데이터가 없다" 로만 보였다. 이제 다른 통신과 같은 방식으로
   키를 보낸다. **운영 전환 전제: 이 빌드가 현장 전 라인에 배포돼 있어야 한다.**
2. **작업자 로그인 실패 메시지** — 기존에는 원인과 무관하게 "사번 또는 PIN이 올바르지 않습니다" 였다.
   이제 서버가 키를 거부한 경우 **"Web 서버가 이 PC 의 API 키를 거부했습니다. 시스템 설정의 [Web API 키]를
   확인하세요."** 로 구분되고, 이 PC 에 키가 설정돼 있지 않으면 그 사실을 함께 알려 준다.
3. **검사 결과 폐기** — Web 이 "이 라인을 모른다"(404)고 답하면 그 결과를 다시 보내지 않고 치웠다.
   Web DB 복원·라인 재등록 중이면 수십 초 뒤 정상화되는데 그 사이 생산분의 검사 이력·작업지시 수량이
   경고 없이 사라졌다. 이제 재시도 큐에 남겨 30분까지 다시 시도하고, 그래도 안 되면 큐를 막지 않도록
   격리하면서 감사 로그에 남긴다.
4. **이미지 업로드 보안 정책** — 이미지 업로드만 TLS/HTTP 차단 검사를 타지 않아 Production 보안 모드에서도
   원격 `http://` 로 이미지와 API 키가 평문 전송됐다. 이제 다른 통신과 같은 정책을 적용하며, 그 때문에
   업로드가 꺼지면 로그에 `검사 이미지 업로드 비활성화` 와 사유가 남는다.

### 매뉴얼 반영 포인트

- 본편 §4.9(또는 Web 연동 설정 절): "Web API 키" 항목에 **모든 라인에 같은 키를 넣어야 한다**는 한 줄과,
  서버를 키 강제 모드로 바꿀 때의 순서(전 라인 배포 → 서버 전환) 안내.
- 본편 §9(트러블슈팅): "작업지시 목록이 비어 보인다 / 작업자 로그인이 계속 실패한다" → 서버 키 강제 모드와
  PC 키 설정 확인. 새 오류 문구를 그대로 인용.
- 본편 §9: "NG 이미지가 Web 에 올라가지 않는다" → 로그의 `검사 이미지 업로드 비활성화` 확인,
  원격 `http://` 는 Production 모드에서 차단되므로 `https://` 로 바꾸거나 보안 모드 조정.
- 스크린샷 변경 없음.

---

## 19. Web 연동 정합성 마무리 (검사 시각·종료 통지·이미지 큐·지표) — **반영 완료 (2026-09-14, 본편 §7.6·§8.3·§8.5·§9.4·§10.1·§11.4 + 그림 `vms_ctl/sec_system_log_wo_warning.png`)**

| 항목 | 내용 |
|------|------|
| PR | #462 · #464 · #465 · #466 · #467 · #468, v1.37.0 (동봉 Web v1.10.0) |
| 날짜 | 2026-09-14 |
| 앱 · 화면 | VMS 메인(운전·작업자 세션·이미지 업로드), 설정 마법사 2단계, Web 전반 |
| 변경 종류 | 동작 수정 + 경고 표시 1건 신설 — 화면 구성 변화 없음 |

### 무엇이 바뀌었나

1. **다른 라인에 배정된 작업지시로 올라간 검사를 알린다** (#462) — Web 에서 WO 를 다른 라인으로
   재배정했는데 이 PC 가 그대로 운전 중이면 검사 이력은 남지만 **수량이 집계되지 않는다.** 예전에는
   아무 표시가 없어 "생산했는데 수량이 안 는다" 로만 보였다. 이제 그 사실을 현장에 경고로 알린다
   (다른 WO 소속 Lot 도 동일).
2. **검사 시각이 '도착 시각' 으로 찍히던 문제** (#467) — 네트워크가 끊기면 결과는 재전송 큐에 쌓였다가
   복구 후 한꺼번에 올라간다. 판정 전용 업로드(측정값 없이 OK/NG 만 보내는 사이클)는 올라간 시각으로
   기록돼 몇 시간치가 전부 '지금' 이 됐다 — 교대 배정·일별 집계·불량률 예측이 어긋난다. 이제 **검사한
   시각**을 함께 보낸다.
3. **정상 종료했는데 작업자가 로그아웃되지 않던 문제** (#465) — 종료 통지에만 API 키가 빠져 있어,
   서버를 키 강제 모드로 바꾸면 이 통지만 거부된다. 그러면 대시보드에 라인이 '접속 중' 으로 남고
   **작업자 세션이 열린 채 남아 다음 검사가 이미 퇴근한 작업자에게 귀속**된다.
4. **NG 이미지가 큐에서 밀려나던 문제** (#465 · #466) — 서버가 받을 수 없다고 답해도(400/413 등)
   영원히 재시도해 큐를 채웠고, 상한에 걸리면 정상 NG 이미지부터 버려졌다. 이제 다시 보내도 소용없는
   응답은 보관함으로 옮기고, 재시도도 6시간을 넘기면 치운다(보관함은 최근 200건·200MB 제한).
   전송 크기가 서버 상한(30MB)을 넘으면 **JPEG → 썸네일 순으로 낮춰 보낸다** — 다MP 카메라의 무압축
   BMP 는 예전에는 영원히 올라가지 못했다.
5. **사이클 검사 시간이 작게 기록되던 문제** (#465) — AUTO RUN 사이클 업로드의 검사 시간이 마지막
   스텝 값만 담겨 로컬 이력(택트)과 Web 이력의 숫자가 달랐다. 이제 사이클 합계를 보낸다.
6. **16bit·실수 이미지에서 품질 지표가 전부 0** (#464) — 밝기·대비·초점 등 5종이 소리 없이 0 으로
   기록됐다. 8bit 기준으로 변환한 뒤 계산한다. 설정 마법사의 **라인 번호 입력 범위(0~99)** 도 서버와
   맞췄다 — 벗어나면 하트비트는 통과하는데 등록만 영구 실패해 라인 미등록 + 결과 폐기로 이어졌다.
7. **작업자 로그아웃·실시간 푸시 계약** (#468) — 로그아웃 요청에 '내 세션' 임을 밝히는 값을 함께 보내고,
   실시간 푸시는 자기 라인 그룹으로 받는다. 사용자 조작은 동일.

### 동봉 Web v1.10.0 (운영자 관점)

- 검사 이미지가 **로그인해야 열린다**(예전에는 URL 만 알면 열렸다), 운영 Swagger 기본 비공개
- 비밀번호 초기화·등급 강등이 **즉시** 반영(예전엔 최대 8시간 뒤)
- 검사 0건인 라인의 Quality·OEE 가 100% 가 아니라 **빈 값**
- DB 백업이 재시작해도 주기를 이어받고, `/health` 에서 마지막 백업 시각을 볼 수 있다
- 배포가 운영 로그·이미지·백업·화면에서 바꾼 설정을 지우지 않는다

### 매뉴얼 반영 포인트

- 본편 §4(운전 화면): WO 재배정 경고 문구 소개 — "수량이 오르지 않는" 상황의 원인 표시.
- 본편 §9(트러블슈팅): ① "지난 시간 생산분이 한꺼번에 올라올 때 시각" — 이제 검사 시각으로 기록됨
  ② "NG 이미지가 안 올라간다" → 큐/보관함 폴더 확인 절차와 자동 축소 전송 설명
  ③ "작업자가 로그아웃되지 않는다 / 퇴근한 작업자가 검사에 붙는다" → 종료 통지와 키 설정.
- 본편 §3.2(설정 마법사): 라인 번호 입력 범위 0~99 명시.
- 본편 §7(Web): 검사 이미지 열람에 로그인 필요, 지표가 빈 값으로 보이는 경우의 의미.
- 본편 §11.4: 위 변경 요약 + 서버 스위치 3종(키 강제·로그아웃 신원·푸시 범위)은 **전 라인 배포 후** 전환.
- 스크린샷: WO 재배정 경고 표시 1장(현장 재현 필요) 외 변경 없음.

---

## 20. 인앱 업데이트가 Web 서버를 반쪽으로 남기던 문제 (v1.37.2, VMS #478) — **반영 완료 (2026-09-15: 본편 §10.1 항목 신설 + §11.4)**

**현장에서 무슨 일이 있었나 (2026-09-15 실증 PC).** v1.35.1 → v1.37.1 인앱 업데이트 직후부터 VMS 에서
Web 접속이 되지 않았고, 설정 마법사의 [Web 서버 서비스 시작] 을 눌러도 시작되지 않았다. 같은 설치
파일을 한 번 더 실행하니 정상으로 돌아왔다.

**원인.** 업데이트가 Web 서버를 **끄지 않은 채** 파일을 갈아 끼우려 했다. 사용 중인 파일은 지우지도
덮어쓰지도 못하므로 Web 폴더가 반쪽만 바뀌었고(구성 파일 하나가 빠짐), Web 서버가 켜지자마자 스스로
죽었다. 업데이트는 VMS 본체가 닫히는 것은 기다렸지만 Web 서버가 닫히는 것은 기다리지 않았다.

**고친 것.**

1. 업데이트 전에 Web 서버를 먼저 확실히 내린다(프로그램이 실제로 끝날 때까지 최대 30초 대기, 그래도
   남으면 강제 종료). 대상은 그 서비스의 PID 로 특정한다 — 같은 PC 에서 따로 배포해 돌리는 Web 서버를
   잘못 건드리지 않기 위해서다.
2. 그래도 Web 이 뜨지 않으면 설치 파일로 한 번 더 파일을 채워 넣고 다시 켠다(자동 복구).
3. 끝내 못 살리면 **설치 파일을 지우지 않고 남긴다** — 예전에는 지워 버려서 복구 수단이 재다운로드뿐이었다.
4. 시작 후 **실제 응답을 확인**한다(켜진 것과 서비스 중인 것은 다르다). 시작 실패는 최대 90초 재시도.
5. 실패하면 **이유를 남긴다** — 예전 기록에는 "서비스를 시작할 수 없습니다" 문장만 남아 원인을 짚을
   단서가 없었고, 이번에도 Windows 이벤트 뷰어를 따로 확인해야 원인을 알 수 있었다.
6. 관리자가 일부러 꺼 둔(사용 안 함) Web 서비스는 업데이트가 되살리지 않는다.

**⚠ 배포 순서.** 이 수정은 **업데이트를 진행하는 쪽**이 새 버전이어야 효과가 난다.

- 아직 v1.37.1 을 배포하지 않은 라인 → v1.37.1 을 건너뛰고 **v1.37.2 를 배포**
- 이미 v1.37.1 이 깔린 라인 → 이번 한 번은 예전 방식으로 진행되므로 **업데이트 후 Web 접속 확인**.
  안 되면 설정 마법사 [Web 서버 서비스 시작] → 그래도 안 되면 **설치 파일(MSI)을 한 번 더 실행**

### 매뉴얼 반영 포인트

- 본편 §10.1(트러블슈팅): **"업데이트 후 Web 접속이 안 된다"** 항목 신설 — 확인 순서(설정 마법사 카드
  상태 → `C:\ProgramData\BODA\VMS\update-bootstrap.log` → MSI 재실행)와 정상 로그 4줄
  (`stopping web service before install` / `web process exited - files are free` /
  `web service started` / `web health check OK`).
- 본편 §11.4: v1.37.2 한 줄 + 위 배포 순서 주의.
- 스크린샷: 변경 없음.

---

## 21. 안돈 보드 새 탭이 로그인을 잃던 문제 + 상시 표시 PC 운용 정책 (Web #154, Web v1.10.2) — **반영 완료 (2026-09-15: 본편 §7.1 콜아웃·§7.4 '여는 방법'·§10.3 항목 신설 + §11.4)**

**현장에서 무슨 일이 있었나 (2026-09-15 실증 PC).** Web 로그인 후 현장 모니터링 → 안돈 보드로 들어가면
새 탭이 열리는데, ① "로그인 유지" 를 끈 세션에서는 그 탭이 미인증으로 열려 다시 로그인해야 했고
② 로그인해도 안돈 보드가 아니라 대시보드가 떠서 **끝내 안돈 보드에 도달하지 못했다.**

**원인 두 가지.**

1. "로그인 유지" 를 끄면 로그인 정보는 **그 탭에만** 보관된다(2026-09-04 정책). 그런데 브라우저는
   새 탭으로 여는 링크에 보안 차단을 자동 적용해 **새 탭이 원래 탭의 보관 내용을 물려받지 못한다.**
   (Playwright 로 실측 확인: 링크 새 탭 = 값 없음, `window.open` 새 탭 = 물려받음)
2. 로그인 화면이 성공 후 **무조건 대시보드로** 갔다. 원래 가려던 주소로 돌아가는 기능이 아예 없었다.

**고친 것.** ① 안돈 보드는 브라우저 차단을 받지 않는 방식으로 새 탭을 열어 **로그인이 그대로 따라간다**
(팝업이 막히면 같은 탭에서 연다). 진입 경로가 둘이라 메뉴와 대시보드의 "전체 보기" 링크를 모두 고쳤다.
② 로그인 후 **원래 가려던 화면으로 돌아간다** — 안돈 보드뿐 아니라 북마크·새로고침·주소 직접 입력으로
보호된 화면에 들어오는 경우가 함께 고쳐졌다(같은 사이트 주소만 허용 — 외부로 튕기는 악용 방지).

**운용 정책 (2026-09-15 사용자 결정).** **안돈 보드를 벽걸이 모니터에 상시 표시하는 PC 는
"로그인 유지" 를 켜고 운용한다.** 이번 수정으로 "메뉴에서 새 탭으로 열 때" 는 해결되지만, 브라우저를
아예 닫았다 켜면 로그인 유지가 꺼진 세션은 여전히 재로그인이 필요하기 때문이다. 일반 사무용 PC 는
기본값(끄기) 유지 — 공용 PC 에서 세션이 남지 않게 하려는 것이 원래 취지다.

### 매뉴얼 반영 포인트

- 본편 §7.1(Web 로그인): "로그인 유지" 설명에 **운용 기준 한 줄 추가** — 안돈 보드 상시 표시 PC 는 켜고,
  공용·사무용 PC 는 끈 채로 쓴다.
- 본편 §7(Web) 안돈 보드 절: 메뉴에서 열면 새 탭으로 열리고 **로그인이 그대로 이어진다**는 설명.
  주소를 직접 열었을 때는 로그인 후 안돈 보드로 이어진다는 것도 함께.
- 본편 §10.1(트러블슈팅): "안돈 보드를 열면 로그인 화면이 뜬다" 항목 — 상시 표시 PC 는 로그인 유지 ON
  으로 두라는 안내(v1.10.2 미만 서버에서 발생).
- 본편 §11.4: Web v1.10.2 한 줄.
- 스크린샷: 변경 없음(로그인 화면 `40_web_login.png` 는 v1.8.0 캡처 그대로 유효).

---

## 22. 다중 Web 서버 현장 — 라인 번호 대역 규칙 — **반영 완료 (2026-09-15: 본편 §8.5 소절 신설)**

**배경 (2026-09-15 사용자 제시).** 이미 생산 중인 공장은 네트워크 공사가 어려워 모든 라인 PC 를
한 대의 서버에 연결하지 못한다. 서버 3대에 라인 15대를 5대씩 나누어 붙이거나, 라인 PC 한 대가
자기 안의 Web 을 쓰는 구성이 실제로 발생한다.

**확인한 사실.** 라인 번호(`Clients.Index`)는 **그 서버의 DB 안에서만** 유일하다(유니크 제약).
서버가 다르면 같은 번호를 써도 시스템이 막지 않는다. 그대로 두면 서버마다 "1번 라인" 이 생겨
**나중에 서버를 합치거나 보고서를 모을 때 구분이 불가능**하고, 이력이 섞이면 되돌릴 수 없다.
스키마에 공장·사이트 개념이 없어 서버 하나가 곧 하나의 섬이다.

**결정 (사용자).** 라인 번호 **십의 자리를 서버 번호**로 쓴다 — 서버당 10개씩.

| 대역 | 용도 |
|---|---|
| 0~9 | 시험·예비 (공장 배정 금지) |
| 10~19 | 1번 서버 |
| … | … |
| 90~99 | 9번 서버 |

예: `24` = 2번 서버의 다섯 번째 라인. 서버 9대 × 라인 10대 = 90대. 설정 마법사 입력 범위가
0~99 라 그대로 들어간다. **나중에 한 대로 합쳐도 번호가 유효하다** — 이 규칙의 핵심 이득.
한 서버에 10대를 넘으면 대역을 하나 더 배정하고 배포 대장에 적는다.

**중앙 서버 한 대 구성도 그대로 가능하다** — 제약은 네트워크 공사이지 소프트웨어가 아니다.
한 서버가 라인 100대까지 받는다. 운영 단순성은 중앙 집중이 가장 낫다.

### 매뉴얼 반영 포인트 (완료)

- 본편 §8.5: "Web 서버가 여러 대인 공장 — 라인 번호 대역" 소절 신설. 대역 표 + `24` 예시 +
  서버를 나누면 함께 나뉘는 것(대시보드·작업지시·품질 분석·백업·관리자 계정·기준 정보) 콜아웃.
- §11.4 는 건드리지 않았다 — 소프트웨어 변경이 아니라 운영 규칙이라서.

### 매뉴얼 밖의 잔여 (코드·운영)

- **라이선스 좌석 감사가 서버별로 쪼개진다** — `LicenseService` 는 그 서버가 본 라인만 센다.
  서버 3대에 같은 라이선스를 넣으면 어느 서버도 전체 사용량을 보지 못한다(15좌석 계약인데
  서버마다 15좌석씩 45대까지 올라가도 초과로 안 잡힘). 지금은 전환기 정책이라 항상 허용이지만,
  **좌석 강제 모드를 켜기 전에 셈 방식을 정해야 한다**.
- 전사 통합 화면은 별도 설계 필요(각 서버가 상위로 올려보내는 구조).

---

## 23. 카메라 캘리브레이션 — 도구 도움말 정정 · 창 도움말 신설 · 매뉴얼 절 신설 — **반영 완료 (2026-09-15: 본편 §5.11·§10.2·§11.4)**

**사용자 제기 (2026-09-15).** ① Image Rectify 파라미터 도움말이 부실 ② Camera → Calibration Manager
창에 도움말이 하나도 없음 ③ 카메라 캘리브레이션 사용법이 매뉴얼에 없음.

**확인한 사실 — ①은 부실이 아니라 틀린 것이었다.** `HelpContent["ImageRectifyTool"]` 이 문서화한
파라미터 4개 중 **3개는 도구에 존재하지 않고**(UseCalibrationFile · CalibrationFilePath ·
InterpolationMode), 화면에 실제로 있는 `ApplyHomography` 는 **설명이 비어 있었다**(눌러도 아무것도 안 뜸).
②는 그 창이 도움말을 실어 나르는 래퍼 컨트롤 대신 날 TextBox/CheckBox 를 써서 HelpIcon 이 0개였다.

**조치**
- `HelpContent["ImageRectifyTool"]` 을 실제 2개 항목으로 교체. **어떤 캘리브레이션 방식에서만
  동작하는지**를 명시 — Undistort 는 Checkerboard, ApplyHomography 는 N-Point 에서만 동작하고
  Single Scale 로는 둘 다 안 된다. 이걸 몰라서 "켰는데 아무 일도 안 일어난다" 가 된다.
- `HelpContent["CalibrationManager"]` 신설(11항목) + 창의 컨트롤을 래퍼로 교체해 물음표 배선.
  결과 표시의 `Reprojection Error` 단위 표기도 정정 — **방식마다 단위가 다르다**(Checkerboard=px,
  N-Point=mm, Single Scale=0). 예전에는 항상 "px" 로 찍혀 N-Point 에서 틀린 표기였다.
- 본편 **§5.11 카메라 캘리브레이션** 신설 — 창 여는 법 · 이미지 소스 3종 · 방식 3종 선택 표 ·
  체커보드 절차(안쪽 교차점 수·칸 크기 실측·10~20장 누적) · 결과 읽는 법 · 레시피 적용 ·
  Image Rectify 와의 관계 표 · Resolution 직접 입력(§5.6)과의 비교.
- 본편 **§10.2** 트러블슈팅 3건 신설 — 패턴을 못 찾는 경우 · Image Rectify 가 안 먹는 경우 ·
  mm 값이 실제와 다른 경우.

**⚠ 절 번호 주의.** 기존 절을 밀지 않고 **§5.11 로 덧붙였다** — `gen_user_manual.js` 가
`"5.10 딥러닝 도구 — 모델 파일과 레지스트리"` 제목 문자열을 키로 스크린샷을 꽂기 때문에,
번호를 밀면 그 매핑이 조용히 깨진다.

### 재발 방지 테스트 — 그리고 드러난 같은 결함 3건

`HelpContentMatchesXamlTests` 신설: XAML 의 (ToolType, ParameterName) 쌍과 도구 클래스의 실제 속성을
대조해 ① 물음표가 있는데 설명이 빈 자리 ② 도구에 없는 설정을 설명하는 자리를 잡는다.

이 스캔으로 **같은 유형의 결함이 3개 도구에 더** 있는 것이 드러났다(기준선으로 고정, `KnownStaleHelp`):

| 도구 | 설명은 이렇게 돼 있는데 | 실제 항목은 |
|---|---|---|
| PlaneFitTool | InlierThresholdMm · MaxIterations · MinInlierRatio | RansacIterations · RansacThreshold · SampleStride |
| SegmentationTool | ConfidenceThreshold · TargetClassIndex · DrawOverlay | UseImageNetNormalization · BackgroundClass · ShowOverlay |
| Geometry3DTool | ExpectedValue · Tolerance · EnableJudgment | (해당 속성 없음) |

**셋 다 문서화된 항목이 전부 옛 이름이고, 실제 항목은 설명이 비어 있었다** — Image Rectify 와 같다.
**같은 세션에서 3건 모두 정정 완료** — 실제 항목 이름으로 교체하고 설명을 새로 썼다
(PlaneFit 의 RANSAC 3항목 · Segmentation 의 정규화/배경 클래스/오버레이 · Geometry3D 는 존재하지 않는
판정 3항목 제거 + 이미 쓰여 있던 설명이 화면에 뜨도록 물음표 배선). `KnownStaleHelp` 는 **비었다**.

남은 것은 **물음표는 있는데 설명이 빈 자리 21곳**(DetectionTool 의 SAHI·CLAHE·Dot 계열,
PhotometricStereo, AnomalyTool)이며 `KnownEmptyHelp` 기준선에 올라 있다. 목록은 늘면 테스트가 깨지고,
채우면 목록에서 지우게 되어 자연히 줄어든다.

### 매뉴얼 반영 포인트 (완료)

- §5.11 신설 · §5.6 에서 §5.11 로 연결 · §10.2 트러블슈팅 3건 · §11.4 문서 보강 항목 · 목차 1줄.
- 스크린샷 없음 — 창 캡처를 넣으려면 실제 체커보드 이미지를 띄운 상태가 필요하다(잔여).

---

## 24. 캘리브레이션 적용이 "어디에 반영됐는지" 보이지 않던 문제 — **반영 완료 (2026-09-15: 본편 §5.11 5단계 개정 · §5.6 · §10.2 · §11.4 + 그림 `53_calib_apply.png`)**

| 항목 | 내용 |
|------|------|
| PR | #488 (`fix/circles-grid-blob-size` — 원형 타겟 검출 수정과 합본) · v1.38.1 발행 |
| 날짜 | 2026-09-15 |
| 앱 · 화면 | VMS.VisionSetup — Camera Calibration 창의 **Apply** 구역, 메인 화면 왼쪽 **Steps** 표 |
| 변경 종류 | 동작 추가(스텝 Resolution 갱신) + 화면 추가(적용 절차·대상 목록) + 표시 형식 변경 |

### 무엇이 문제였나 (dev PC 검증)

1. **[Apply to Current Recipe] 가 무엇을 바꾸는지 화면 어디에도 없었다.** 눌러도 상태 줄에
   `Saved to recipe '...'` 한 줄만 나왔고, 그게 측정 도구의 mm 판정에 쓰인다는 사실은 도움말을
   열어야만 알 수 있었다.
2. **왼쪽 Steps 표의 Resolution 에 적용 결과가 안 보였다.** 실제로 Apply 는 `recipe.Calibration`
   만 채우고 **스텝의 Resolution(mm/px) 은 건드리지 않았다** — 표의 값은 기본값 `0.050` 그대로였다.
   게다가 표시 형식이 `F3` 이라 고배율(예: 0.0004 mm/px)은 `0.000` 으로 뭉개져, 값을 손으로 넣어도
   확인할 수 없었다.

### 무엇이 바뀌었나

- **Apply 구역에 "적용 절차" 안내 상자** — 누르면 일어나는 세 가지를 번호로 적는다.
  ① 레시피에 저장(측정 mm 판정 + Image Rectify) ② 고른 스텝의 Resolution 갱신
  ③ 레시피 파일 저장 → 왼쪽 Steps 표에 반영.
- **"지금 레시피에 저장돼 있는 캘리브레이션" 요약 줄** — 방식 · mm/px · 재투영 오차 · 캘리브레이션
  시각. 누르기 전/후를 비교하면 적용 여부를 바로 알 수 있다. 저장된 게 없으면 "없음".
- **"Resolution 을 갱신할 스텝" 목록** (체크 + Step · Camera · `현재값 → 적용값`) 과
  [모두 선택] / [모두 해제].
  - 왜 고르게 하나: 캘리브레이션은 레시피에 **한 벌**만 저장되지만 Resolution 은 **스텝마다** 있다.
    한 레시피에 카메라가 여러 대면 같은 mm/px 를 전부에 밀어 넣으면 다른 카메라 측정이 틀어진다.
  - 기본 체크: **카메라로 직접 촬영**했으면 그 카메라의 스텝만, 파일·VMS 프레임이면 전체(어느
    카메라인지 알 수 없으므로).
- **Apply 가 실제로 스텝 Resolution 을 쓴다.** 체크한 스텝만. 저장 실패 시에는 그 사실을 알린다.
- **Steps 표 Resolution 표시 `F3` → `F5`** + 셀 툴팁("mm/px — 캘리브레이션을 레시피에 적용하면
  이 값이 갱신됩니다").
- **[Clear Calibration from Recipe] 의 안내 정정** — 지워도 **스텝 Resolution 은 남아** 측정 도구가
  계속 mm 로 환산한다. 예전 문구("픽셀 단위로 돌아간다")는 틀린 설명이었다. 완전히 픽셀로 돌리려면
  Steps 표의 Resolution 도 0 으로 바꿔야 한다.
- 곁다리 결함: `CalibrationMetadata.IsValid()` 의 switch 에 **CirclesGrid 가 빠져 있어** 원형 타겟
  결과가 항상 무효로 떨어졌다 (현재 호출자는 없지만 잠복 결함) → Checkerboard 와 같은 조건으로 합침.
- 새 창을 머지 전에 눈으로 볼 수 있도록 `--capture-dialogs` 목록에 Camera Calibration 창을 추가했다.

### 매뉴얼 반영 포인트

- **§5.11 카메라 캘리브레이션** — "레시피 적용" 부분을 다시 쓴다. 누르면 일어나는 세 가지,
  대상 스텝 고르기(다중 카메라 주의), 적용 확인 방법(**왼쪽 Steps 표의 Resolution 이 바뀌는 것으로
  확인**), 지웠을 때 스텝 Resolution 이 남는다는 점.
- **§5.6 스텝 Resolution 직접 입력** — §5.11 로 적용하면 이 값이 자동으로 채워진다는 한 줄.
- **§10.2 트러블슈팅** — "캘리브레이션을 적용했는데 mm 값이 안 바뀐다" 항목: 대상 스텝이 체크됐는지,
  Steps 표 Resolution 이 바뀌었는지 순서로 확인.
- **§11.4** 문서 보강 한 줄.
- 스크린샷: **`53_calib_apply.png` 신규 반영 완료** — 실촬영 원형 타겟(2448×2048)으로 Circles Grid 를
  실행해 20점 검출 오버레이가 그려진 상태 + Apply 구역(절차 안내 · 요약 줄 "없음" · 스텝 목록
  `0.05000 → 0.11213` 5행)이 한 화면에 담긴다. 생성기 `gen_user_manual.js` 의 `POST` 에
  `"5.11 카메라 캘리브레이션 — mm 로 재기 위한 준비"` 키로 등록했다(§5.11 절 제목이 앵커이므로
  **제목을 바꾸면 그림이 조용히 빠진다**).

### ⚠ 캡처 중 실제 레시피가 수정된 사고 (2026-09-15)

이 그림을 찍는 과정에서 `--capture-dialogs` 실행 한 번이 dev PC 의 실제 레시피
`recipe_apple_iphone17_pro.json` 에 **캘리브레이션을 저장하고 스텝 1-1 의 Resolution 을
0.05 → 0.11213 으로 바꿨다**(12:51:47Z 저장). 두 필드를 쓰는 코드 경로는 `ApplyToRecipe` 뿐인데,
**같은 명령을 두 번(격리 인스턴스 · 기본 인스턴스) 다시 돌려도 재현되지 않았다** — 호출자 스택을
기록하도록 계측한 재현 실행에서는 `ApplyToRecipe` 가 아예 불리지 않았다. 원인 미상.

- 조치: 두 필드를 원래 값으로 되돌렸다(`modifiedAt` 은 그 시각으로 남음). 캡처 직후 상태 사본은
  세션 스크래치패드에 보관했다.
- **교훈: 캡처 실행은 `--instance <이름>` 으로 격리된 AppData 에서 돌릴 것.** 기본 인스턴스로
  돌리면 실제 레시피·설정 파일이 사정권에 들어온다. 매뉴얼에 실린 그림은 격리 인스턴스에서
  다시 찍은 것이라 레시피를 건드리지 않았다.

---

## 25. 비전 설정 현장 결함 3건 (카메라 이중 점유 · 공유 프레임 수신 · Fixture 각도 전달) — **매뉴얼 반영 대기**

| 항목 | 내용 |
|------|------|
| PR | #495 (`fix/calib-camera-conflict-and-sharedframe-reader`) · #496 (`fix/fixture-angle-propagation`) · #497 (`fix/fixture-angle-canvas-sync`) · bump #498 — **v1.39.1 발행 (2026-09-16)** |
| 날짜 | 2026-09-16 |
| 앱 · 화면 | VMS.VisionSetup — Camera Calibration 창 · 메인 화면 Grab/ROI · VMS.AppSetup 2단계 「고급 설정」 |
| 변경 종류 | 동작 수정(판정이 달라질 수 있음) + 안내 추가(팝업·상태 문구) + 입력란 활성 조건 변경 |

### 무엇이 문제였나 (v1.39.0 현장 점검에서 막혀서 못 넘어간 칸들)

1. **캘리브레이션 창에서 카메라 촬영 무반응** — 비전 설정 메인 화면이 카메라를 연결해 둔 채로
   `[Capture from Camera]` 를 누르면 이미지가 안 들어왔다. 카메라를 **두 번 여는 것**이 원인
   (산업용 카메라는 배타 점유). 실패 문구가 우측 패널 맨 아래에만 떠서 "무반응" 으로 보였다.
2. **VMS 실행 중 이미지를 한 번에 못 받음(주기적)** — ① 비전 설정이 Grab 을 *요청한 뒤에* 수신
   등록을 해서 그 촬영분이 공유 메모리에 안 실렸고, ② 캘리브레이션 창이 가용성을 확인할 때마다
   여러 창이 공유하는 "수신자 있음" 신호를 꺼 버려 메인 화면 수신까지 끊겼다(창을 여닫을 때마다 재발).
3. **FeatureMatch 가 각도를 전달하지 않음** — Coordinates 연결이 ROI **중심 위치만** 회전 이동시키고
   ROI 자체는 축 정렬로 두어, 부품이 돌면 ROI 는 제자리 자세로 궤도만 돌아 **다른 자리를 봤다.**
4. **(함께 드러남) 운전에서 탐색 영역이 안 따라감** — Shape Match/Color/OCV 의 SearchRegion 시프트가
   비전 설정에만 있고 AUTO RUN 에는 없어, 같은 레시피가 설정과 운전에서 다르게 동작할 수 있었다.
5. **단독 모드가 MLOps 입력란까지 잠금** — MLOps 는 Web 과 별개 서버인데 Web 입력 블록 안에 있었다.
   현장에서는 "재설치해야 다시 넣을 수 있는 줄" 알고 F 구역 검증을 통째로 건너뛰었다.

### 무엇이 바뀌었나

- 캘리브레이션 창이 **카메라를 두 번 열지 않는다** — 메인 화면이 쥐고 있으면 그쪽에 촬영을 부탁해
  이미지만 받아온다(연결을 끊을 필요가 없다). 못 여는 상황은 **팝업으로 사유 + 할 일**을 안내한다.
- Grab 요청 **전에** 수신 등록을 하고, 가용성 확인은 공유 신호를 건드리지 않는다. 새 프레임이
  안 실렸으면 옛 이미지를 새 것인 양 넘기지 않고 `VMS가 새 프레임을 기록하지 않았습니다
  (기대 #n, 수신 #m)` 로 알린다.
- **ROI 가 부품과 같은 각도로 기운다.** 사용자가 기울여 그려 둔 ROI 는 그 기울기 **위에** 변화분이
  더해진다. 각도를 안 내보내는 소스(Blob 등)는 **지금까지와 동일**(이동만).
- 회전 ROI(RectAffine)로 그렸으면 **화면 사각형도 같이 기운다.** 축 정렬 Rectangle 로 그렸으면
  도형이 기울기를 표현 못 해 검사 영역만 기운다(정상).
- 단독 모드에서도 **MLOps 주소·라인 토큰 입력·저장 가능**(API Key 는 Web 전용이라 계속 잠김).

### ⚠ 판정 변화 가능 — 매뉴얼에 경고가 필요하다

FeatureMatch 로 ROI 를 따라가게 해 둔 레시피는 **업데이트 전후 판정이 달라질 수 있다.**
그동안 ROI 가 안 기울어 부품의 다른 자리를 보고 있었다면 **새 결과가 맞다.** 다만 그 상태에 맞춰
기준값을 조정해 두었다면 기준값을 다시 잡아야 할 수 있다.

### 매뉴얼 반영 포인트

- **§5.11 카메라 캘리브레이션** — "카메라로 직접 촬영" 항목에서 **메인 화면 연결을 끊으라는 종전
  안내가 있다면 삭제.** 이제 끊을 필요가 없다. VMS 실행 중에는 `VMS Shared Frame` 을 쓴다는 점은 유지.
- **§5.x 도구 연결(Fixture/Coordinates)** — "FeatureMatch 를 연결하면 ROI 가 부품을 따라간다" 설명에
  **"위치와 기울기가 모두 따라간다"** 를 명시. 각도를 내보내지 않는 소스(Blob 등)는 이동만이라는 점,
  회전 ROI 로 그려야 화면에서도 기우는 것이 보인다는 점을 함께.
- **§3.x 설정 마법사 2단계** — 단독 모드 체크 시 잠기는 항목 목록에서 **MLOps 두 칸을 뺀다.**
  「고급 설정」은 접혀 있어 저장된 값이 없으면 안 보인다는 한 줄과, **설정 마법사를 다시 여는 방법**
  (VMS 메인 [시스템 설정] · 시작 메뉴 "BODA VMS 설정 마법사 (AppSetup)")을 함께 적는다 —
  현장에서 "재설치해야 하는 줄" 알았던 자리다.
- **§10.2 트러블슈팅** — 항목 3개 신설: ① "캘리브레이션 창에서 촬영이 안 된다" ②
  "이미지가 안 바뀐다 / 한 번에 안 들어온다"(위 문구와 숫자를 함께 보내라는 안내) ③
  "업데이트 후 판정이 달라졌다"(FeatureMatch 각도 전달 — 새 결과가 맞고 기준값 재확인).
- **§11.4** 변경 이력 한 줄.
- 스크린샷: 별도 신규 없음. 기존 §5.11 그림(`53_calib_apply.png`)은 그대로 유효.

---

## 26. 캘리브레이션 창이 VMS 에 촬영을 요청 — 버튼 이름 변경 — **매뉴얼 반영 대기**

| 항목 | 내용 |
|------|------|
| PR | #500 (`fix/calib-receive-requests-grab`) — v1.39.1 **이후** 릴리즈에 실림 |
| 날짜 | 2026-09-16 |
| 앱 · 화면 | VMS.VisionSetup — Camera Calibration 창의 **Image Source = VMS Shared Frame** 패널 |
| 변경 종류 | 동작 추가(촬영 요청) + **버튼 이름 변경** + 안내 문구 신설 |

### 무엇이 문제였나 (v1.39.1 현장 점검)

VMS 가 실행 중이면 `[Capture from Camera]` 가 "VMS Shared Frame 으로 바꾸세요" 로 안내하는데,
정작 바꿔서 `[Receive Frame from VMS]` 를 눌러도 **이미지가 안 들어왔다.**

이 창은 공유 메모리를 **읽기만** 했다. VMS 는 받는 쪽이 붙어 있을 때만 사진을 기록하는데
이 창은 버튼을 누르는 순간에야 붙으므로, 그 전에 VMS 가 찍어 둔 사진은 애초에 기록되지 않았다.
→ **창을 열고 처음 누르면 늘 빈손.** VMS 창으로 가서 Grab 을 누르고 돌아와야 했다.

### 무엇이 바뀌었나

- **버튼 이름: `Receive Frame from VMS` → `Grab from VMS`.** 이제 실제로 카메라가 찍히므로
  "받아온다" 는 이름은 사실과 달랐다.
- 버튼 아래 안내 신설: "VMS 가 지금 한 장 찍어 이 창으로 보냅니다. 운전(AUTO RUN)·라이브 중에는
  거절됩니다."
- 거절 사유가 상태 줄에 그대로 표시된다(조용히 실패하지 않는다).
- 받은 사진의 카메라를 알 수 있게 되어 **그 카메라의 스텝만 자동 체크**된다 — 예전에는 VMS
  프레임이면 "어느 카메라인지 모름" 이라 전체가 체크됐다.

### 매뉴얼 반영 포인트

- **§5.11 카메라 캘리브레이션** — 이미지 소스 3종 설명에서 **버튼 이름을 `Grab from VMS` 로 고치고**,
  "VMS 가 그 순간 촬영한다 · 운전/라이브 중에는 거절된다" 를 명시.
  "VMS 창으로 가서 Grab 을 누르고 오라" 는 안내가 있다면 **삭제**(이제 불필요).
- **§5.11 대상 스텝 고르기** — "파일이나 VMS 화면으로 했다면 어느 카메라인지 알 수 없어 전체가
  체크된다" 는 설명에서 **VMS 를 빼고 파일만 남긴다**(VMS 프레임은 이제 카메라를 안다).
- 스크린샷: 캘리브레이션 창 그림에 이 버튼이 보이면 재캡처 필요. 현재 §5.11 그림
  (`53_calib_apply.png`)은 소스가 File 상태라 **재캡처 불필요.**

## 27. 예제 템플릿 '얼라인' 탭 2D 항목 통합 (PR #505) — **매뉴얼 반영 완료 (같은 PR)**

| 항목 | 내용 |
|------|------|
| 날짜 | 2026-09-17 |
| PR | #505 |
| 화면 | VisionSetup → 예제 템플릿 갤러리 → 얼라인 탭 |
| 변경 종류 | 템플릿 1개 제거 + 제목 문구 변경 |

### 무엇이 문제였나

얼라인 탭의 2D 항목 3개('표준 2D 매치 얼라인' · '2점 매치 얼라인' · '2-스텝 얼라인') 가운데
앞의 둘은 툴 팔레트의 **Match Align** 툴을 그대로 감싼 것이었다. '2점' 은 Match Align 의
Mode 를 TwoPoint 로 바꾼 것과 같은 구성이라, 갤러리에서 고른 것과 팔레트의 Match Align 이
같은 물건인지 사용자가 알기 어려웠다. '2-스텝 얼라인' 만 스텝 2개 생성 + 스텝 간 소스
배선이라는 템플릿만의 역할이 있다.

### 무엇이 바뀌었나

- **'2점 매치 얼라인' 템플릿 제거.** Match Align 템플릿 설명문에 "각도 정밀도가 더 필요하면
  Feature Match 를 하나 더 붙여 Result 로 연결하고 Mode 를 TwoPoint 로" 안내를 넣었다.
- 2D 템플릿 제목이 **툴명으로 시작**한다: "Match Align — 2D 매치 얼라인 (ΔX/ΔY/Δθ)",
  "Multi-Step Align — 2-스텝 얼라인 (카메라 이동, 대형 부품)". 3D 3종은 그대로.
- 저장된 레시피는 템플릿 Id 를 갖지 않으므로 기존 레시피 영향 없음.

### 매뉴얼 반영 (완료)

- **§5 예제 템플릿 표** — 얼라인 2행을 새 제목으로 갱신, "한 화면에 들어올 때 / 안 들어올 때"
  구분 조건과 2점 전환 방법 기재.
- **§7.8 얼라인 예제** — 1단계 문구를 "Match Align — 2D 매치 얼라인 생성 → 필요 시 Feature Match
  추가 + Mode=TwoPoint" 로 변경.
- 스크린샷: `vs/dlg_TemplateGallery.png` 은 3D 탭이 열린 그림이라 **재캡처 불필요.**
  다이어그램 `templates/2d-match-align-2pt.png` 삭제(고아).

## 28. 툴 팔레트 Alignment 카테고리 분리 (PR #506) — **매뉴얼 반영 완료 (같은 PR), 스크린샷 1장 재캡처 대기**

| 항목 | 내용 |
|------|------|
| 날짜 | 2026-09-17 |
| PR | #506 (#505 후속) |
| 화면 | VisionSetup → 왼쪽 Tool Palette |
| 변경 종류 | 팔레트 카테고리 신설(툴 2종 이동) |

### 무엇이 문제였나

#505 로 갤러리 얼라인 템플릿을 정리한 뒤에도 Match Align · Multi-Step Align 이 팔레트의
**Pattern Matching** 아래 있어, 갤러리 '얼라인' 탭에서 고른 것과 팔레트의 툴이 같은 물건인지
드러나지 않았다. "템플릿에 있는 툴을 팔레트에서 빼자"는 안은 툴 삭제 후 재추가·기존 레시피에
얼라인 추가·2D+3D 하이브리드 조합이 막혀 채택하지 않았다.

### 무엇이 바뀌었나

- 팔레트에 **Alignment** 카테고리 신설(Pattern Matching 바로 뒤). Match Align · Multi-Step Align 이동.
  팔레트는 15 카테고리 39 도구.
- 레시피 직렬화와 무관(카테고리는 표시 전용) — 기존 레시피 영향 없음.

### 매뉴얼 반영 (완료)

- **§5 화면 구성 표 ③ 왼쪽 패널** — "Tool Palette(15 카테고리 39 도구)".
- **§6 비전 도구** — §6.3 Pattern Matching 은 Feature Match · Shape Match 만, **§6.4 Alignment — 얼라인** 신설
  (Match Align · Multi-Step Align). 이후 절 번호 한 칸 이동(§6.5 Blob Analysis ~ §6.15 Surface Analysis).
  다른 장에서 §6.4 이상을 참조하는 곳은 없음(§6.1·§6.3 참조만 존재).
- 생성기: `gen_tools_chapter.py` ORDER 분리, `extract_tool_params.py` 카테고리 → `_tool_params.json`.

### 스크린샷 — 재캡처 완료 (2026-09-17)

- `vs/full_main_scene.png` (§5 메인 화면): 팔레트 트리에 Alignment 카테고리(Match Align · Multi-Step Align)가
  보이고 제거된 WeldTeach 메뉴가 사라진 상태로 재캡처. `capture_visionsetup_scene.ps1` 장면 1 과 같은
  조건(mancap 인스턴스 · recipe_apple_iphone17_pro 스텝 4 · RefImages 기준 이미지 · Feature Match 선택)으로
  `--capture-fullpage` 실행 후 원본 1/2 축소. **크기 2048×1232**(이전 2560×1392) — 캡처 코드가 주 모니터
  작업 영역을 창 크기로 쓰므로 캡처 PC 의 주 모니터에 따라 달라진다. 매뉴얼은 data-width 620 고정이라 영향 없음.
- 별건: GS 제품설명서 생성기 `gen_product_description.js` 의 "팔레트 12 카테고리" 표는 이미 구식(현재 15).

---

## 29. 세그 도구 설명에서 라이선스 고지 문구 제거 (GS 제출본) — **반영 완료 (docx/PDF 재생성 2026-09-18)**

| 항목 | 내용 |
|------|------|
| 날짜 | 2026-09-18 |
| 앱 · 화면 | VisionSetup Tool Settings — YOLOv8-seg · RF-DETR-seg · Detection 패널과 물음표 도움말 / 사용자 매뉴얼 §6.11 |
| 변경 종류 | 문구만 (동작 · 파라미터 · 직렬화 변경 없음) |

### 무엇이 문제였나

본편 매뉴얼 §6.11 의 YOLOv8-seg 설명에 "Ultralytics 는 AGPL-3.0 이므로 상용 배포 라이선스를 확인하고"
라는 문장이 있었고, RF-DETR-seg 설명은 "Ultralytics(AGPL) 의존이 없어 소스 공개 의무가 없습니다" 로
대비시키고 있었다. GS 제출본에서 이 서술은 두 가지로 불리하다.

- **사실과 다르게 읽힌다.** VisionSetup 의 `YoloSegTool` 은 ONNX Runtime + OpenCvSharp 로 output0/output1 을
  직접 후처리하는 자체 구현이고, 제품 배포물에 Ultralytics 코드는 없다. AGPL 은 학습 패키지와 그 가중치에
  붙으며, 학습 스택(`scripts\train_yolo.py` 등)은 인스톨러 `AiTools` Feature 라 인증 빌드
  (`-p:ExcludeAiTools=true`)에는 파일 자체가 존재하지 않는다.
- **문서 층위가 맞지 않는다.** 제3자 구성요소 라이선스는 `docs/gs/guides/gs_distribution_policy.md` §2.6 이
  정본이고, 사용자 매뉴얼은 절차서다. 같은 내용을 두 곳에 두면 한쪽이 낡는다. 학습 쪽 경고는 AI 학습 도구
  별책(`BODA-VMS-AI-Tools-Manual.html`)에 그대로 남겨 두었다 — AGPL 의무가 실제로 발생하는 지점이 거기다.

### 무엇이 바뀌었나 (문구)

| 위치 | 전 | 후 |
|------|----|----|
| §6.11 제목 | YOLOv8-seg — 인스턴스 분할 (Ultralytics) | YOLOv8-seg — 인스턴스 분할 |
| §6.11 본문 | "Ultralytics 는 AGPL-3.0 이므로 상용 배포 라이선스를 확인하고, 대안으로 RF-DETR-seg 를 권장합니다." | "새로 만드는 검사에는 RF-DETR-seg 를 권장하며, 이 도구는 기존 YOLOv8/v11 규약 ONNX 를 쓰던 레시피 호환용입니다." |
| §6.11 파라미터 표 | Ultralytics 표준 export(output0 + output1 프로토타입) | YOLOv8/v11 표준 export 규약(output0 + output1 프로토타입) |
| §6.11 제목 | RF-DETR-seg — 인스턴스 분할 (Apache 2.0) | RF-DETR-seg — 인스턴스 분할 |
| §6.11 본문 | "Ultralytics(AGPL) 의존이 없어 상용 배포 시 소스 공개 의무가 없습니다." | "새로 만드는 검사에는 이 도구를 권장합니다." |
| §6.11 Detection 본문 | 기본 학습 백본은 D-FINE(Apache 2.0)이며 | 기본 학습 백본은 D-FINE 이며 |
| 물음표 도움말 `YoloSegTool` | "Ultralytics YOLOv8/v11 … 추론", "Ultralytics에서 학습한 .pt를 .onnx로 export(yolo export …)" | 벤더 명칭 없이 규약 기준 서술 + RF-DETR-seg 권장 한 줄 |
| 물음표 도움말 `RfdetrSegTool` | 이름 "RF-DETR-seg (인스턴스 분할, Apache-2.0)" · AGPL 대비 문장 | "RF-DETR-seg (인스턴스 분할)" · 권장 문장 |
| 도구 패널 하단 안내 | "Ultralytics YOLOv8/v11-seg 표준 export." / "RF-DETR(Apache-2.0) 표준 export" | "YOLOv8/v11-seg 표준 export 규약." / "RF-DETR 표준 export" |

정본은 `docs/manuals/tools_prose.json` 이고 `gen_tools_chapter.py` → `build_manual.py` 로 §6.11 을 재생성했다.
팔레트 표시 이름(`YOLOv8-seg` · `RF-DETR-seg`)과 도구 타입 문자열(`YoloSegTool` · `RfdetrSegTool`)은 손대지
않았으므로 레시피 호환성에는 영향이 없다.

### 남은 일

- docx/PDF 재생성 **완료 (2026-09-18)** — 198쪽 · 그림 154장으로 이전과 동일, PDF 본문에서
  `Ultralytics` · `AGPL` · `Apache 2.0` · `TMED` · `Cognex:` 가 모두 사라진 것과 대체 문구가 들어간 것을 확인
  (남은 `Cognex` 는 10쪽 "지원 목록에 없는 제조사" 하드웨어 목록 한 곳뿐 — 사실 기재라 유지)
- 도구 설정 패널 캡처 `tools/31_YOLOv8-seg.png` · `32_RF-DETR-seg.png` **재캡처 완료 (2026-09-18)** —
  `capture_wpf_apps.ps1` 과 같은 조건(`--instance mancap --capture-toolpanels`)으로 39개 패널을 다시 찍고
  이 2장만 반영. 크기는 이전과 동일(606×1382 · 606×1419)이고 픽셀 차이는 안내 문구 줄 22행뿐.
  이미지가 docx 에 박히므로 **docx/PDF 를 한 번 더 재생성**했다(198쪽 · 그림 154장 유지, PDF 109쪽에서
  패널 그림 하단이 "YOLOv8/v11-seg 표준 export 규약." 으로 바뀐 것 확인).

---

## 30. 예제 장에서 타사 제품명(TMED) 비유 제거 (GS 제출본) — **반영 완료 (docx/PDF 재생성 2026-09-18)**

| 항목 | 내용 |
|------|------|
| 날짜 | 2026-09-18 |
| 위치 | 사용자 매뉴얼 §7.3 위치 보정 후 검사 · §7.8 얼라인 (`docs/manuals/src/07_examples.html`) |
| 변경 종류 | 문구만 (코드 · 화면 무관) |

### 무엇이 문제였나

예제 두 곳이 타사 제품(TMED)의 기능 이름에 빗대어 상황을 설명하고 있었다. 집필 때 참고한 자료의
용어가 본문에 남은 것으로, 제출 문서에 남길 이유가 없다 — 타사 제품명·기능명을 제품 매뉴얼 본문에서
설명 기준으로 쓰는 것은 맞지 않고, 그 제품을 모르는 독자에게는 설명도 되지 않는다. 두 문장 모두
비유를 빼도 앞뒤 서술만으로 상황이 온전히 전달된다.

### 무엇이 바뀌었나

| 절 | 전 | 후 |
|----|----|----|
| §7.3 | "… 늘 같은 자리를 검사합니다. **TMED 방식의 "위치 보정(Fixture)" 에 해당합니다.**" | "… 늘 같은 자리를 검사합니다. **이렇게 기준을 잡아 주는 방식을 위치 보정(Fixture) 이라고 부릅니다.**" (용어 소개는 유지) |
| §7.8 | "상황 — **TMED 의 "로봇 얼라인" 처럼,** 부품이 기준 위치에서 얼마나 이동 · 회전했는지 재서 …" | "상황 — 부품이 기준 위치에서 얼마나 이동 · 회전했는지 재서 로봇이나 서보에 보정량을 보냅니다**(로봇 얼라인)**." |

본문 전체를 다시 훑어 타사 제품명이 남은 곳은 없음을 확인했다. 매뉴얼에 남아 있는 타사 명칭은
**하드웨어 호환 목록**(카메라 제조사, PLC 프로토콜 Mitsubishi · Siemens · LS Electric · Omron, NVIDIA GPU)과
**데이터셋 형식 이름**(mvtec 등) 뿐이며, 이는 사실 기재라 그대로 둔다.

### 같이 처리 — 물음표 도움말의 "Cognex 대응" 줄 숨김

VisionSetup 물음표 도움말은 도구마다 **"Cognex: ViDi …"** 한 줄을 화면에 보여 주고 있었다. 매뉴얼 본문은
아니지만 같은 성격의 타사 제품명 노출이고 심사 중 화면에 뜰 수 있어 **표시만 껐다**.

- `HelpIcon.xaml.cs` 도구 전체 도움말 분기에서 `CognexBorder` 를 항상 `Collapsed` 로 둔다
  (XAML 의 기본값도 `Collapsed` 라 어느 경로로도 뜨지 않는다).
- **데이터는 남긴다** — `HelpContent.ToolHelp.CognexEquivalent` 속성과 도구별 값은 사내 참고용으로 그대로 둔다.
  다시 보이게 하려면 그 분기 한 곳만 되돌리면 된다.
- 매뉴얼에 물음표 도움말 팝업을 찍은 그림은 없어 스크린샷 재캡처는 불필요.

---

## 31. 제품설명서 팔레트 카테고리 표 갱신 + 매뉴얼 도구 수 정정 (GS 제출본) — **반영 완료 (docx/PDF 재생성 2026-09-18)**

| 항목 | 내용 |
|------|------|
| 날짜 | 2026-09-18 |
| 대상 | `docs/gs/pipeline/gen_product_description.js` → `VMS_제품설명서_v1.0.docx` · 사용자 매뉴얼 §1 |
| 변경 종류 | 사실 정정 (코드·동작 무관) |

### 제품설명서 — 팔레트가 12 카테고리로 굳어 있었다

2026-06-12 작성 이후 팔레트가 **15 카테고리 39 도구**로 늘었는데(§6.4 Alignment 신설 #506, 3D 가
Processing/Measurement 로 분리, Surface Analysis 추가) 제품설명서는 12 카테고리 목록 그대로였다.
GS 시험 시나리오의 기준 문서라 실제 화면과 어긋나면 안 된다.

| 위치 | 전 → 후 |
|------|---------|
| §1.1 주요 특징 | "12개 카테고리의 비전 도구" → "15개 카테고리 39종의 비전 도구" (ROI 목록에 RectAffine 추가) |
| §6.3.4 | 제목 "(12 카테고리)" → "(15 카테고리 · 39종)", 표 TP-01~TP-12 → **TP-01~TP-15**, 각 행에 실제 도구명·개수 기입 |
| §7.2 그림 | `03_visionsetup.png`(2026-06-12 캡처, 구 팔레트) → **`v3/vs/full_main_scene.png`**(2026-09-17 재캡처, Alignment 카테고리 반영) |
| §7.2 캡션 | "Tool Palette(12 카테고리)" → "(15 카테고리 39종)" |
| §4.2 소프트웨어 요구사항 | "Windows 10 / 11", ".NET 8.0 Desktop Runtime", "Visual C++ 재배포 패키지" → **Windows 11**, 두 런타임은 "MSI에 동봉(self-contained) · 별도 설치 불필요" (#406 런타임 동봉 반영, 사전 체크리스트 기재와 일치) |
| 문서 정보 | 대상 OS "Windows 10 / 11 (x64)" → "Windows 11 (x64)", 제품 버전 자리표시자 `v1.0 ([정식 릴리스 버전 기입])` → **v1.40.1** |

문서 버전·작성일(1.0 / 2026-06-12)은 그대로 두었다 — 개정 표기는 제출 직전에 한 번에 정하는 편이 낫다.

### 사용자 매뉴얼 — 도구 수 40 → 39

§1 두 곳(화면 구성표 VisionSetup 칸, 용어표 "도구")이 **"40 종"** 이라고 적고 있었다. WeldTeach 도구가
빠지면서(#504) 팔레트가 39 종이 됐고 §5·§6 은 이미 "15 카테고리 39 도구"라 문서 안에서 숫자가 어긋났다.
`src/01_overview.html` 2곳을 39 종으로 고치고 재생성(**198쪽 · 그림 154장 유지**).

---

## 32. 제품설명서 제품 구성표에 MLOps 한 줄 추가 (GS 제출본) — **반영 완료 (docx 재생성 2026-09-18)**

사용자 매뉴얼에는 **9장 「AI 학습 플랫폼 (BODA.VMS.MLOps)」** 이 12개 절 · 그림 15장 · PDF 158~170쪽
분량으로 들어 있는데(장 첫머리에 "인증 범위 밖의 선택 구성 요소" 배지 있음), 제품설명서 §3 제품 구성표에는
MLOps 가 아예 없었다. `(선택 구성) VMS.DeepLearning` 은 이미 "인증 범위 외" 로 한 줄 잡혀 있는데
MLOps 만 빠져, 매뉴얼을 본 심사자가 "구성에 없는 프로그램" 으로 읽을 여지가 있었다.

- 추가한 행: **`(선택 구성) BODA.VMS.MLOps`** — "딥러닝 모델 수집·라벨링·학습·배포를 관리하는 별도 웹
  플랫폼(GPU 학습 워커 포함) — 선택 구성 요소, 인증 범위 외 (제품 MSI 미포함, 별도 서버에 설치).
  사용자 매뉴얼 9장 참조."
- 이로써 세 문서가 맞는다 — 매뉴얼(장 배지로 범위 밖 명시) · 제품설명서(구성에는 있되 범위 외) ·
  사전 체크리스트 기능 리스트(행 없음).

### 검토했으나 하지 않은 것

매뉴얼 9장을 10장(AI 학습 도구)처럼 **별책으로 분리하는 안은 보류**. §10~§13 절 번호가 밀리고,
본문 곳곳의 "모델 파일은 MLOps(9장)에서 만듭니다" 식 상호 참조(매뉴얼 40쪽에 걸쳐 88회)를 전부 손봐야 한다.
장 첫머리 범위 배지로 충분하다고 보고, 심사에서 문제 삼으면 그때 분리한다.

---

## 33. AI 학습 도구 별책 — 본편 v3.0 기준 상호 참조 정정 + 그림이 보이는 배포본 — **반영 완료 (2026-09-18)**

| 항목 | 내용 |
|------|------|
| 날짜 | 2026-09-18 |
| 대상 | `docs/manuals/BODA-VMS-AI-Tools-Manual.html` (편집용) · `docs/gs/VMS_AI학습도구매뉴얼.html` (배포용, 신규) · 본편 §10 |
| 변경 종류 | 문구·상호 참조 + 배포본 생성기 추가 |

### 문제 1 — 별책이 v2.0 시절 절 번호를 가리키고 있었다

별책은 2026-09-08 에 본편 v2.0 의 6장을 떼어 만든 문서다. 그 뒤 본편이 v3.0(13장)으로 개편되면서
절 번호가 전부 바뀌었는데 별책은 그대로였다. 표지도 실제 개정(v1.2)과 어긋난 v1.0 이었다.

| 자리 | 전 → 후 |
|------|---------|
| 표지 | `v1.0 (2026-09-08) · 대상 SW v1.31.0 · 본편 v2.0 의 6장을 분리` → **`v1.3 (2026-09-18) · 대상 SW v1.40.1 · 본편 v3.0 10장에 대응`** |
| 딥러닝 검사 도구 | 본편 §5 → **§6.11** (OCR 은 §6.7 로 분리 표기) |
| 3D 객체 검출 예제 | 본편 §5 → **§7.9** |
| Image Save Settings | 본편 §4.3 → **§4.6** (사이드 패널 Settings) |
| 설정 마법사 2단계 | 본편 §3 → **§3.3** |
| `model://` 레시피 바인딩 | 본편 §5.10 → **§9.10** |
| 도구 지정 위치(RF-DETR-seg · Detection) | 본편 §5.10 · §5 → **§6.11** |
| 도구 이름 | `YOLO-seg` → **`YOLOv8-seg`** (RF-DETR-seg 병기) |

### 문제 2 — 파일 하나만 열면 그림이 하나도 안 보인다

별책은 그림 20장을 **자기 폴더 밖**(`../gs/screenshots/…`)에서 상대 경로로 참조한다. 리포 안에서
열면 보이지만, 파일만 복사해 보내거나 상위 폴더 접근이 막힌 뷰어로 열면 **본문만 보이고 그림이 전부 깨진다**.
제출물은 파일 하나로 오가므로 그대로 두면 받는 쪽에서 그림 없는 문서를 보게 된다.

- `docs/manuals/build_ai_tools_manual.py` 신설 — `<img src>` 를 base64 `data:` URI 로 바꿔
  **`docs/gs/VMS_AI학습도구매뉴얼.html`** (1.3MB, 그림 20장 내장)을 만든다. 누락 이미지가 있으면 종료 코드 1.
- 편집은 계속 `docs/manuals/BODA-VMS-AI-Tools-Manual.html` 에서 하고, 고친 뒤 이 스크립트로 배포본을 다시 만든다
  (배포본 표지에 그 안내가 한 줄 들어간다).
- 본편 §10 의 안내 문구도 파일명을 배포본 이름으로 바꿨다 —
  "별책 「BODA VMS AI 학습 도구 매뉴얼」(`VMS_AI학습도구매뉴얼.html` — 본 매뉴얼과 함께 제공되는 별도 파일)".
  본편 docx/PDF 재생성(198쪽 · 그림 154장 유지), PDF 172쪽에서 확인.

### 문제 3 — 별책에 PDF 가 없었다 (해결)

본편은 docx/PDF 로 제출하는데 별책은 HTML 뿐이었다. **`docs/gs/VMS_AI학습도구매뉴얼.pdf`**(13쪽, 1.1MB)를
만들었다.

- 생성기 **`docs/gs/pipeline/html2pdf_chrome.ps1`** 신설 — Chrome 헤드리스 `--print-to-pdf` 로 HTML 을 그대로
  인쇄한다(A4). Word COM(`docx2pdf_render.ps1`)과 달리 브라우저가 CSS 를 해석하므로 화면과 같은 판면이 나온다.
  `-OutDir`·`-Pages` 를 주면 검수용 PNG 도 함께 뽑는다(WinRT 렌더러, docx2pdf_render.ps1 과 같은 방식).
- 별책 `@media print` 보강 — **화면은 다크 테마라 그대로 인쇄하면 흰 종이에 연회색 글씨**가 된다(브라우저는
  배경색을 인쇄하지 않는다). 인쇄 시 흰 바탕·검은 글씨로 토큰을 덮어쓰고, 사이드 목차 숨김 · A4 여백 ·
  그림/표/코드 페이지 분리 금지 · 배포본 안내 줄 숨김을 넣었다.
- 본편 §10 의 안내 파일명도 **`VMS_AI학습도구매뉴얼.pdf`** 로 맞췄다(PDF 독자가 PDF 를 찾도록). 재생성 후
  198쪽 · 그림 154장 유지, PDF 172쪽에서 확인.

> **PowerShell 함정** — Chrome 은 USB/GCM 잡음을 stderr 로 뱉는데 PowerShell 5.1 은 네이티브 stderr 를
> 오류 레코드(NativeCommandError)로 감싸 `$ErrorActionPreference='Stop'` 에서 스크립트가 죽는다.
> 호출 연산자(`&`) 대신 **`Start-Process -Wait`** 로 띄워 해결했다.

### 문제 4 — 제출본에 사내 변경 이력이 실려 있었다 (해결)

별책 §4 "변경 이력"은 개정 기록이라 시험기관·고객에게 줄 문서에 들어갈 이유가 없다
(본편 v3.0 도 개편 때 변경 이력 절을 폐지했다). 다만 기록 자체는 남겨야 하므로 **편집 원본에는 두고
배포본에서만 뺀다**:

- 원본의 해당 `<section>` 과 목차 항목에 **`data-internal`** 표시.
- `build_ai_tools_manual.py` 가 `data-internal` 이 붙은 절·목차 항목을 배포본에서 제거하고
  몇 개를 뺐는지 출력한다. 앞으로 사내 전용 내용이 생기면 같은 표시만 붙이면 된다.
- 결과: 배포본 HTML·PDF 에 "변경 이력" 없음(목차도 3항목), PDF 13쪽 유지.

---

## 34. 기준 좌표(Fixture) 소스가 실패하면 뒤 도구를 실행하지 않는다 (v1.41.0) — **반영 완료 (docx/PDF 재생성 2026-09-22)**

| 항목 | 내용 |
|------|------|
| 날짜 | 2026-09-22 |
| 대상 | 본편 §1.4 용어 · §5.5 도구 연결 · §7.3 위치 보정 예제 · §12.4 트러블슈팅 · §13.4 문서 정보 |
| 변경 종류 | 동작 변경에 따른 설명 추가 (사용자에게 보이는 판정 변화) |

### 무엇이 바뀌었나

Coordinates(Fixture) 로 기준 위치를 주는 앞 도구가 **실패하면, 그 뒤 도구는 실행되지 않고 NG** 가 된다.
종전에는 기준을 못 받아도 뒤 도구가 그대로 돌았는데, 도구 인스턴스가 사이클 간 재사용되므로
**ROI 가 직전 검사 위치에 그대로 남은 채로** 측정했다. 제품이 없는 화면이라도 그 자리에 엣지가 있으면
값이 나오고, 그 값이 공차에 들면 **AUTO RUN 이 OK 를 내보냈다**(2026-09-22 재현: 패턴 없이 막대 두 개만
있는 이미지가 50.077mm · 스텝 OK).

화면에 나오는 사유 문구는 두 가지다.

| 문구 | 언제 |
|------|------|
| `기준 위치를 주는 도구가 실패하여 건너뜀: <도구> ← <소스>` | 앞 도구가 제품을 못 찾았을 때 |
| `기준 좌표를 받지 못해 건너뜀: <도구> ← <소스>` | 앞 도구는 성공했지만 중심 좌표를 안 내보낼 때 |

앞 도구가 아직 실행되지 않았거나 비활성인 경우는 **종전대로** 통과한다(고정 ROI 운용).

### 매뉴얼 반영

| 절 | 내용 |
|----|------|
| §1.4 핵심 용어 | Fixture 설명에 "기준을 주는 도구가 제품을 못 찾으면 뒤 도구는 실행되지 않고 NG" 한 줄 추가 |
| §5.5 도구 배치·연결 | Coordinates(Fixture) 항목에 "기준을 주는 도구가 실패하면 뒤 도구는 실행되지 않고 NG" 한 문장 추가 |
| §7.3 위치 보정 예제 | 팁 콜아웃 신설 — **Feature Match 도 Result 로 판정 도구에 연결**해 두어야 NG 사유가 최종 판정에 남는다 |
| §12.4 레시피 편집·판정 | 트러블슈팅 항목 신설 — 두 사유 문구의 뜻과 확인 순서(부품 유무·조명·Score Threshold), Fixture 기준으로 쓸 수 있는 도구 종류 |

> **제출본 문체** — 매뉴얼에는 **현재 동작만** 적고 "종전에는 이랬다 / 왜 바꿨다" 는 쓰지 않는다.
> 초안에 들어갔던 배경 설명(직전 검사 위치 ROI 재사용 → 제품 없이도 값이 나옴)은 제출본에서 빼고
> 이 장부와 릴리즈노트에만 남겼다 (2026-09-22 사용자 지적). 본편 v3.0 이 변경 이력 절을 폐지한 것과 같은 원칙이다.
| §13.4 문서 정보 | 대상 소프트웨어 `VMS 1.39.2` → **`VMS 1.41.0`** (v1.40.x 때 갱신이 누락돼 있었다) |

> **기존 레시피 주의** — Fixture 소스가 간헐적으로 실패하던 레시피는 종전에 OK 로 지나가던 사이클이
> 이제 NG 로 잡힌다. **새 판정이 맞다**(기준 없이 직전 자리를 측정한 값이었다). 현장 적용 시 NG 율이
> 갑자기 오르면 그 앞 도구의 매칭 안정성을 먼저 보라.
