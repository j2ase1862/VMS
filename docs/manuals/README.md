# VMS 매뉴얼 / 운영 가이드

VMS 솔루션 (BODA Vision AI) 의 사용자 매뉴얼 및 도구별 운영 가이드. 사용자/현장 작업자/SI 인계용.

## 사용자 매뉴얼 (HTML)

| 파일 | 대상 |
|------|------|
| [`BODA-VMS-User-Manual.html`](BODA-VMS-User-Manual.html) | 전체 사용자 매뉴얼 v3.0 (GS 제출물의 원본) — **생성물: 직접 편집 금지.** 본문은 `src/NN_*.html` 장별 조각, `python build_manual.py` 로 합성(6장은 `gen_tools_chapter.py` + `tools_prose.json`, 13장 부록은 `gen_appendix.py`). docx/PDF 재생성은 `docs/gs/README.md` 참조 |
| [`BODA-VMS-AI-Tools-Manual.html`](BODA-VMS-AI-Tools-Manual.html) | AI 학습 도구 매뉴얼(별책) — 라벨링 · 학습 · 레지스트리. 제출용 배포본은 `build_ai_tools_manual.py` 로 `docs/gs/` 에 만든다 |
| [`VMS_OCR_SynthData_Manual.html`](VMS_OCR_SynthData_Manual.html) | OCR 합성 데이터 매뉴얼 |

## 워크플로 / 기술 가이드 (Markdown)

| 파일 | 내용 |
|------|------|
| [`VMS_HandEye_Calibration_Manual.md`](VMS_HandEye_Calibration_Manual.md) | Hand-Eye 캘리브레이션 절차 |
| [`VMS_Labeling_Training_Manual.md`](VMS_Labeling_Training_Manual.md) | 라벨링 / 학습 매뉴얼 |
| [`VMS_SAM_Segmentation_Manual.md`](VMS_SAM_Segmentation_Manual.md) | SAM Segmentation 매뉴얼 |
| [`VMS_TensorRT_Setup_Guide.md`](VMS_TensorRT_Setup_Guide.md) | TensorRT 설정 가이드 |

## 관련 문서

- **현장 설치·검증**: 루트 `docs/field_install_checklist.md` (구 `manual_regression_v1.2.md` 는 사문서 — 이력 참고용)
- **GS 인증 관련**: 루트 `docs/gs/` 폴더
- **MSI 빌드/배포**: 루트 `docs/msi_build_guide.md`, `docs/release_manual_procedure.md`
- **업데이트 알림/업그레이드 정책**: 루트 `docs/update_notifier_and_upgrade_policy.md`
