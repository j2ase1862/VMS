# 사내 매뉴얼 초안 — 검토 메모 (2026-11-11)

## 0-2. 11-24 그림 재캡처 (master 61d74bd DEBUG, 확장 옵션 켬)

- 11-23 반영 때 남긴 "사내판 자체 그림" 7장을 build 7 화면으로 다시 찍음. 제출본 그림으로 바꾸지 않은 이유: 사내판 그림에는 ◆PLC Output · ◆레지스트리 단추 · ◆3D 탭 · ◆Mech-Mind 카메라가 보여야 함(제출본 그림은 기본 구성).
- 환경: `VMS_LICENSE_OPTIONS=3d-camera,robot,mlops,plc,line-scan`. VMS `--instance mancap --capture-inspect`(1 카메라 설정, GS Measure 레시피 사본, MS02 → MS01 → MS03 → MS04) · AppSetup `--capture-fullpage` · VisionSetup `--capture-dialogs`(3 카메라 설정 — Basler 2 · Mech-Mind 1) · `--capture-toolpanels`.
- 바꾼 그림: `vms/inspect_camera_box.jpg`(권한 안내 줄 · ◆Depth Map · Point Cloud 탭 · [Save 3D]) · `as/FullPage_P2_ApplicationSettings.png`(생산 관리 두 칸 기본 켬, Web SSO 끔) · `vs/dlg_CameraManager.png`(창 높이 · Enabled 줄 온전 · "Mech-Mind" 표시 이름) · `tools/06_Threshold` · `14_Caliper`(+ `split/14_Caliper_split`) · `21_PointCloud_Filter` · `34_Anomaly`(Web 파라미터 연동 라벨 · 공통 버튼 틀).
- 그대로 둔 것: `vs/dlg_InferenceSettings.png` — 새 캡처가 지금 그림과 화소까지 같음. `tools/24_PointCloud_Cluster.png` — 새 캡처가 33 px 길지만 이번 목록 밖이라 두었음.

## 0-1. 11-23 반영 (제출본 v3.2 · BODA VMS v1.48.1 build 7 기준)

- 제출본 v3.2 의 문장 개정(제품 코드 한 문장씩 대조)을 사내판 장 번호 · 확장 옵션 맥락에 맞춰 옮김 — 2 · 3 · 4 · 5 · 7 · 8 · 11 · 12장, 9 · 10장은 확장 별책에 맞춤.
- build 6 · 7 동작: 인앱 업데이트 SHA-256 필수(§2.7), 설정 마법사 제조사 표시 이름 "Mech-Mind"(§3.4), Web 파라미터 연동 라벨(§5.7), 설정 파일 오류 창(§12.7).
- 6장 · 부록: `tools_prose.json` 을 제출본과 맞추고 Expert Mode 전용 칸은 `(Expert)` 표기(제출본과 같은 규칙)로 바꿈. `gen_appendix.py` 는 제출본 스크립트(DESC_TOOL · EXPERT · 팔레트 순서)를 바탕으로 다시 만들고 사내판 장 번호 · ◆로봇 항목만 살림.
  `_tool_params.json` 에 Threshold Type · Caliper Edge Pair Width 행이 들어갔으므로 예전 `EXTRA` 끼워 넣기는 없앰.
- 6장 생성 때 `figs/split/` 의 긴 패널 분할 그림이 다시 만들어짐 — 원본 패널 그림(`docs/gs/screenshots/v3/tools/`)이 제출본 재캡처로 바뀐 결과.
- 결과 · 근거 표: `D:\Temp\gs_manual_audit\doc\M8_사내판.md`.

## 0. 11-16 반영 (GS 동결 목록 D-01 ~ D-30, 코드 PR #615 ~ #622 · Web #166 · MLOps #67 의 수정 뒤 동작 기준)

- 아래 A 항목 처리: 1 현행 유지 · §1.6 · §13.7 에 명시(VO-01) / 2 바인딩은 기록, 라인 반영은 VisionSetup 모델 지정 — §9.10 · §7.14(ML-01 · VM-13) /
  3 ROBOT N/A 뜻 툴팁 · §4.7(VM-12) / 4 사내판은 이미 맞음 / 5 업그레이드 때 구성 유지(#618 VO-02) — §2.7 / 6 msi_build_guide 고침(#618 브랜치) /
  7 PCI-7396 은 지원 모델 아님 — §1.3 · §3.7(VO-03) / 8 관리자 매뉴얼 옵션 표 5개(#622 브랜치) / 9 · 10 그림 재캡처 대기 / 11 웨이포인트 번호(VS-03) — §5.13
- 매뉴얼 원문이 코드 PR 머지 뒤 동작을 쓰므로, PR 이 머지되기 전 빌드와는 다를 수 있음.
- 부록 13.2 파라미터 표는 동결 자료(`docs/gs/pipeline/_tool_params.json`)에서 만든다. 그 뒤 생긴 Threshold Type · Caliper Edge Pair Width(Expected Width · Width Tolerance) 행은
  `gen_appendix.py` 의 `EXTRA` 로 끼워 넣음(11-19, 동결 파일은 그대로).
- 재캡처 필요 그림 목록은 `D:\Temp\video\postgs\D_결과.md`.

## A. 제품 · 문서 쪽에서 확인 · 결정이 필요한 것

1. **라이선스가 없으면 확장 옵션이 전부 켜짐.** 라이선스 파일이 없거나 `options` 항목이 없는 예전 라이선스, 서버 좌석으로 붙는 라인 PC 는 `AllEnabled` (LicenseOptions.cs). 기본 구성만 보여야 하는 PC 는 옵션이 빈 라이선스가 반드시 있어야 함 — 매뉴얼 §1.6 에 주의로 적음.
2. **MLOps [레시피 바인딩] 화면의 기록을 VMS 가 읽지 않음.** VMS 는 레시피의 `model://` 참조로 `/resolve` 만 부르고 바인딩 payload API 는 쓰지 않음. MLOps 매뉴얼 REVIEW_NOTES_v2.0 A-1 과 같은 건.
3. 상태바 **ROBOT 칩은 "ROBOT N/A" 고정 표시**(연결 상태와 무관).
4. 설치 문서: 제출본 §2.7 의 **"시작 메뉴의 제거 바로가기"** 는 없음(Package.wxs). 사내판은 고침. 제출본은 동결이라 그대로.
5. 설치 문서: "설치 구성 선택(Web 포함)은 업그레이드 때 유지" — 코드가 INSTALLWEB 를 따로 저장하지 않음. WiX 기본 동작에 기댄 것이라 실제 업그레이드로 확인 필요.
6. `docs/msi_build_guide.md` 의 조용한 제거 명령이 `/x` 에 **UpgradeCode** 를 넘김 — `/x` 는 ProductCode 가 필요.
7. IO 보드 **PCI-7396** 이 문서에는 있으나 코드에 해당 카드 형식이 없음(PCI-7432 로 취급). PCI-7433 · 7434 는 실장비 미확인.
8. LicGen 관리자 매뉴얼의 옵션 표는 7 개, 화면 체크박스는 5 개.
9. 유지 관리 화면의 [변경] / [복구] / [제거] 글자는 WiX 기본 한국어 문자열 — 화면 캡처로 확인 필요(리포에 캡처 없음).
10. 3.5 PLC 단계 그림이 "Step 4 of 6"(로봇 옵션 없이 찍힌 것). 모든 옵션 기준(7 단계)으로 다시 찍으면 좋음.
11. **웨이포인트 스텝 번호가 기존 스텝과 겹침** — Generate Robot Waypoint Steps 로 만든 스텝 이름이 "3-1 Top · 3-2 Side-1 …" 로, 같은 카메라의 기존 스텝 "3-1 — … · 3-2 — …" 와 번호가 같음(그림 vs_waypoint_steps 참고).

## B. 그림

- **5.13 확장 옵션 화면 6장은 찍음(11-11)** — VisionSetup DEBUG `--capture-extension`(새로 더한 캡처 모드, Release 빌드에는 없음)으로
  `mancap` 인스턴스 · GS 3D 레시피 사본(`recipe_GS_3D.json`)에서 화면 밖 렌더. 레지스트리 창은 MLOps 서버에 닿지 않게 가짜 목록(시연용 이름)으로 채움.
  다시 찍기: `set VMS_CAPTURE_RECIPE=<3D 레시피 사본>` · `VMS_CAPTURE_STEP=0` → `VMS.VisionSetup.exe --instance mancap --capture-extension <폴더>` → 크롭은 `figs/ext/` 참고.
- **찍지 않은 것 (이유)**
  - 2.7 MSI 유지 관리(복구 · 제거) 화면 — 기존 방법(`tools/capture-msi-weboption.ps1`)은 실제 MSI 를 띄워 UI Automation 으로 버튼을 누름. 이 PC 는 BODA VMS(운영 Web 포함)가 설치돼 있어 유지 관리 화면에서 잘못 누르면 복구 · 제거가 실행됨 → 설치되지 않은 PC(또는 VM)에서 찍어야 함.
  - 13.7 LicGen 창 — 발급 대장에 실제 고객 이름 · 지문, 서명 키 이름이 보임. 빈 대장(다른 USERPROFILE)으로 찍을 수는 있으나 LicGen 에 화면 밖 캡처 모드가 없어 실제 창을 띄워야 함 → 11-19 DEBUG 캡처 모드(임시 폴더 · 예시 대장)로 찍어 넣음.
  - 7.11 ~ 7.14 예제 — 5.13 · 9장 그림으로 갈음. 실촬영(3D · 로봇 · PLC) 장면은 영상 계획의 H 편과 함께.
- **1.48.1 이후 수정 재캡처(11-18, v1.48.1 build 3 코드)** — 바뀐 화면을 `figs/recap/` 에 두고 본문 경로를 그쪽으로 바꿈(동결된 `docs/gs/screenshots` 는 그대로).
  - VMS(`mancap`): 검사 운전 3장(GS Measure 레시피 사본 · MS02 → MS01 → MS03 → MS04, 수동 [Inspect] 라 집계 · NG History 비어 있음), 사이드 패널(타일 축소),
    생산 이력 3장 · 사용자 관리 · 감사 로그 · 상태 점검(License 까지 — 캡처러가 ItemsControl 스크롤도 늘리게 고침). 캡처 머리글에는 인스턴스 이름을 붙이지 않음.
  - AppSetup: 2단계 전체 · Web SSO 체크, 4단계 PLC 창 · 컨트롤 8장(Step 4 of 7 — 파일 번호가 바뀌어 새 이름으로). `--instance mancap` 이면 멈춰서 기본 인스턴스로 찍음(캡처 모드는 저장하지 않음).
  - VisionSetup: Camera Manager(첫 카메라 고른 장면 `CameraManager_Selected` 추가) · Inference Settings · Sequence, 도구 패널 5장(Threshold · Caliper · Anomaly · PC Filter · PC Cluster —
    `gen_tools_chapter.py` 가 `figs/recap/tools/` 를 먼저 씀).
  - 1.1 구성도: `figs/recap/diagrams/system_overview.html`(카메라 칸 "Basler · ◆Mech-Mind 등")을 headless Chrome 2배로 렌더.
  - 11-19 이어서: Inspection Parameters · 작업 지시 창(임시 Web 의 A동 1호 라인 — `clientIndex` 11), LicGen 두 탭(DEBUG `LicGenApp --capture <폴더>` — 임시 폴더의 버리는 키 · 예시 대장 3 건, 실제 키 · 대장은 읽지 않음),
    13.2 부록 표 3 행. 단독 모드로 뜨던 원인은 캡처용 설정 파일의 카메라 제조사 철자(`MechMind` → `Mech_Mind`) — 설정 파일을 못 읽으면 기본 설정으로 조용히 뜨던 것을 경고 창 · 로그로 알리게 고침.
    작업 지시 캡처의 문서용 대표 1건은 조회가 끝나고도 비어 있을 때만 넣음(조회 중에 넣어 실제 목록과 섞이던 것). Status 배지 열 폭을 넓힘(InProgress 잘림).
  - **남은 것**: 2.7 설치 구성 · 유지 관리 화면(설치 안 된 PC 에서 MSI 화면을 넘겨야 함 — 화면 입력 자동화 금지라 사람이 찍음), 레지스트리 [선택] 내려받기 상태(선택).

## C. 내용 출처

- 확장 옵션 · 로봇 · 멀티뷰 · 핸드아이 · 레지스트리 · 3D 스캔: 코드 조사(VMS.Core LicenseOptions, VisionSetup MainView · MultiViewSetupWindow · HandEyeCalibrationWindow · ModelRegistryPickerWindow).
- 설치 복구 · 제거 · Updater · 명령줄: VMS.MasterSetup · VMS.Updater · UpdateInstallService · 각 App.xaml.cs.
- LicGen: tools/LicGen.App · tools/licgen · docs/licgen_admin_manual.md.
- 머신비전 기초 · TensorRT: 일반 지식 + OnnxSettingsDialog(실행 백엔드 Auto/Cpu/Cuda/DirectML/TensorRT, ORT 1.21).
