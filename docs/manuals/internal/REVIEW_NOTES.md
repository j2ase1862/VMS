# 사내 매뉴얼 초안 — 검토 메모 (2026-11-11)

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

## B. 아직 없는 그림

- 5.13 의 MultiView Scan Setup · Hand-Eye Calibration · 레지스트리 모델 고르기 창, 3D 스캔 패널.
  VisionSetup DEBUG `--capture-dialogs` 에 이 창들을 더해야 찍을 수 있음(제품 코드 변경 → 별도 브랜치에서 할 일).
- 2.7 유지 관리(복구 · 제거) 화면, 13.7 LicGen 창.
- 7.11 ~ 7.14 확장 옵션 예제 화면(실장비 필요한 것 포함 — 영상 계획의 H 편).

## C. 내용 출처

- 확장 옵션 · 로봇 · 멀티뷰 · 핸드아이 · 레지스트리 · 3D 스캔: 코드 조사(VMS.Core LicenseOptions, VisionSetup MainView · MultiViewSetupWindow · HandEyeCalibrationWindow · ModelRegistryPickerWindow).
- 설치 복구 · 제거 · Updater · 명령줄: VMS.MasterSetup · VMS.Updater · UpdateInstallService · 각 App.xaml.cs.
- LicGen: tools/LicGen.App · tools/licgen · docs/licgen_admin_manual.md.
- 머신비전 기초 · TensorRT: 일반 지식 + OnnxSettingsDialog(실행 백엔드 Auto/Cpu/Cuda/DirectML/TensorRT, ORT 1.21).
