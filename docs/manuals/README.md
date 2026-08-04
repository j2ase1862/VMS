# VMS 매뉴얼 / 운영 가이드

VMS 솔루션 (BODA Vision AI) 의 사용자 매뉴얼 및 도구별 운영 가이드. 사용자/현장 작업자/SI 인계용.

## 사용자 매뉴얼 (HTML)

| 파일 | 대상 |
|------|------|
| [`BODA-VMS-User-Manual.html`](BODA-VMS-User-Manual.html) | 전체 사용자 매뉴얼 (GS 제출물의 원본) |
| [`BODA-VMS-Admin-Manual.html`](BODA-VMS-Admin-Manual.html) | **관리자판 (내부 전용)** — 사용자 매뉴얼 §1~8 복사 + §9 WeldTeach(GS 범위 외 PoC). 사용자 매뉴얼 갱신 시 §1~8 재복사 필요 |
| [`VMS-VisionSetup-Manual.html`](VMS-VisionSetup-Manual.html) | VisionSetup 워크스페이스 매뉴얼 |
| [`VMS-DeepLearning-Manual.html`](VMS-DeepLearning-Manual.html) | DeepLearning 도구 매뉴얼 |
| [`VMS_VisionTools_Operating_Manual.html`](VMS_VisionTools_Operating_Manual.html) | 비전 도구 운용 가이드 |
| [`VMS_OCR_SynthData_Manual.html`](VMS_OCR_SynthData_Manual.html) | OCR 합성 데이터 매뉴얼 |

## 워크플로 / 기술 가이드 (Markdown)

| 파일 | 내용 |
|------|------|
| [`VMS_DeepLearning_Detection_Improvements.md`](VMS_DeepLearning_Detection_Improvements.md) | DL Detection 개선 사항 |
| [`VMS_HandEye_Calibration_Manual.md`](VMS_HandEye_Calibration_Manual.md) | Hand-Eye 캘리브레이션 절차 |
| [`VMS_Labeling_Training_Manual.md`](VMS_Labeling_Training_Manual.md) | 라벨링 / 학습 매뉴얼 |
| [`VMS_SAM_Segmentation_Manual.md`](VMS_SAM_Segmentation_Manual.md) | SAM Segmentation 매뉴얼 |
| [`VMS_TensorRT_Setup_Guide.md`](VMS_TensorRT_Setup_Guide.md) | TensorRT 설정 가이드 |
| [`VMS_VisionTools_Validation_Guide.md`](VMS_VisionTools_Validation_Guide.md) | 비전 도구 검증 가이드 |

## docx 원본 (편집 가능)

| 파일 | 비고 |
|------|------|
| [`VMS_DeepLearning_Manual.docx`](VMS_DeepLearning_Manual.docx) | DL 매뉴얼 원본 |
| [`VMS_Labeling_Training_Manual.docx`](VMS_Labeling_Training_Manual.docx) | 라벨링/학습 매뉴얼 원본 |

## 생성 스크립트

| 파일 | 용도 |
|------|------|
| [`generate_dl_manual.js`](generate_dl_manual.js) | DL 매뉴얼 HTML 생성 |
| [`generate_manual.py`](generate_manual.py) | 일반 매뉴얼 HTML 생성 |

## 관련 문서

- **운영 환경 회귀 가이드**: 루트 `docs/manual_regression_v1.2.md` (체크리스트 형식)
- **GS 인증 관련**: 루트 `docs/gs/` 폴더
- **MSI 빌드/배포**: 루트 `docs/msi_build_guide.md`, `docs/release_and_update_guide_v1.0.md`
