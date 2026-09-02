# 매뉴얼 반영 대기 — 변경 히스토리 (상세)

`README.md` 의 "매뉴얼 반영 대기" 표는 **한 줄 요지**만 적는 장부이고, 이 문서는 매뉴얼 검토 기간
(2026-09-02~) 동안 코드 쪽에서 바꾼 내용을 **매뉴얼 편집자가 코드를 다시 열지 않아도 되도록**
화면 · 문구 · 스크린샷 · 대상 절 단위로 풀어 쓴 상세 기록이다.

- 기재 시점: 사용자에게 보이는 변경 PR 을 만들 때 (머지 전이라도 먼저 적고, 머지 후 PR 번호를 채운다)
- 매뉴얼 갱신 완료 시: 항목을 "반영 완료" 로 표시하고 기준선을 올린다 (README 표와 동시에)
- 기준선: **SW v1.27.1 / 매뉴얼 v2.0 (2026-09-02 §3.2 AppSetup 2단계 반영분까지)**

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
