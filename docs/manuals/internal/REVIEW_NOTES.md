# 사내 매뉴얼 초안 — 검토 메모 (2026-11-11)

## 0. 11-16 반영 (GS 동결 목록 D-01 ~ D-30, 코드 PR #615 ~ #622 · Web #166 · MLOps #67 의 수정 뒤 동작 기준)

- 아래 A 항목 처리: 1 현행 유지 · §1.6 · §13.7 에 명시(VO-01) / 2 바인딩은 기록, 라인 반영은 VisionSetup 모델 지정 — §9.10 · §7.14(ML-01 · VM-13) /
  3 ROBOT N/A 뜻 툴팁 · §4.7(VM-12) / 4 사내판은 이미 맞음 / 5 업그레이드 때 구성 유지(#618 VO-02) — §2.7 / 6 msi_build_guide 고침(#618 브랜치) /
  7 PCI-7396 은 지원 모델 아님 — §1.3 · §3.7(VO-03) / 8 관리자 매뉴얼 옵션 표 5개(#622 브랜치) / 9 · 10 그림 재캡처 대기 / 11 웨이포인트 번호(VS-03) — §5.13
- 매뉴얼 원문이 코드 PR 머지 뒤 동작을 쓰므로, PR 이 머지되기 전 빌드와는 다를 수 있음.
- 부록 13.2 파라미터 표는 동결 자료(`docs/gs/pipeline/_tool_params.json`)에서 만들어 Threshold Type · Caliper Edge Pair Width 행이 없음 — GS 이후 자료 갱신 때.
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
  - 13.7 LicGen 창 — 발급 대장에 실제 고객 이름 · 지문, 서명 키 이름이 보임. 빈 대장(다른 USERPROFILE)으로 찍을 수는 있으나 LicGen 에 화면 밖 캡처 모드가 없어 실제 창을 띄워야 함 → 담당자 판단 후.
  - 7.11 ~ 7.14 예제 — 5.13 · 9장 그림으로 갈음. 실촬영(3D · 로봇 · PLC) 장면은 영상 계획의 H 편과 함께.

## C. 내용 출처

- 확장 옵션 · 로봇 · 멀티뷰 · 핸드아이 · 레지스트리 · 3D 스캔: 코드 조사(VMS.Core LicenseOptions, VisionSetup MainView · MultiViewSetupWindow · HandEyeCalibrationWindow · ModelRegistryPickerWindow).
- 설치 복구 · 제거 · Updater · 명령줄: VMS.MasterSetup · VMS.Updater · UpdateInstallService · 각 App.xaml.cs.
- LicGen: tools/LicGen.App · tools/licgen · docs/licgen_admin_manual.md.
- 머신비전 기초 · TensorRT: 일반 지식 + OnnxSettingsDialog(실행 백엔드 Auto/Cpu/Cuda/DirectML/TensorRT, ORT 1.21).
