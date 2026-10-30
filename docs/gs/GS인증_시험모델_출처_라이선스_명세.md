# 시험용 딥러닝 모델 출처 · 라이선스 명세

> BODA VMS v1.48.0 GS 시험인증 — 시험자료(`GS_TestData_v1.48.0.zip`)에 들어 있는 ONNX 모델 명세
> 작성일 2026-10-23 · 개정 2026-10-30(시험 대상 BODA VMS v1.48.0) · 작성 VASIM(BODA)

## 1. 요약

- 시험자료에 들어 있는 학습된 모델은 **5 개**이며, 모두 공개 사전학습 가중치를 출발점으로 **당사가 직접 촬영한 당사 부품 이미지로 미세조정(fine-tuning)** 한 것입니다.
- 출발점이 된 사전학습 가중치와 학습에 쓴 라이브러리는 모두 **Apache License 2.0 또는 BSD 3-Clause** 로 공개되어 있어, 미세조정한 모델을 시험기관에 제공하고 고객에게 납품하는 데 제약이 없습니다.
- 소스 공개 의무가 따르는 라이선스(AGPL-3.0 · GPL 등)의 가중치나 학습 코드에서 나온 모델은 **없습니다**. (당사는 AGPL-3.0 인 Ultralytics YOLO 로 학습한 모델을 제공 · 납품하지 않습니다.)
- 이 밖에 결함 허용성 시험용 파일 3 개는 당사가 만든 **고의로 손상된 파일**이고, 문자 인식(OCR) 도구가 쓰는 인식 모델은 시험자료가 아니라 **제품 설치 파일에 들어 있습니다**(3절).

## 2. 시험자료 모델 명세

### 2-1. 한눈에 보기

| No | 파일 (시험자료 안 위치) | 쓰는 도구 | 모델 구조 | 사전학습 가중치 (출처) | 라이선스 |
|---|---|---|---|---|---|
| 1 | `DeepLearning\Detection\models\GS_DL_dfine_TY.onnx` (`Ensemble\models\` 에 같은 파일) | Detection · Ensemble | D-FINE Small (백본 HGNetV2) | `ustc-community/dfine-small-obj2coco` — Hugging Face | Apache-2.0 |
| 2 | `DeepLearning\InstanceSeg\models\GS_DL_rfdetrseg_TY.onnx` | RF-DETR-seg | RF-DETR Seg Medium (백본 DINOv2 계열) | `rf-detr-seg-medium` — Roboflow RF-DETR | Apache-2.0 |
| 3 | `DeepLearning\SemanticSeg\models\GS_DL_deeplabv3_TY.onnx` | Segmentation | DeepLabV3 (백본 MobileNetV3-Large) | torchvision `DeepLabV3_MobileNet_V3_Large_Weights.COCO_WITH_VOC_LABELS_V1` | BSD-3-Clause |
| 4 | `DeepLearning\Classification\models\GS_DL_resnet18_cls.onnx` | Classify | ResNet-18 | torchvision `ResNet18_Weights.IMAGENET1K_V1` | BSD-3-Clause |
| 5 | `DeepLearning\Anomaly\models\GS_DL_patchcore.onnx` (`Ensemble\models\` 에 같은 파일) | Anomaly · Ensemble | PatchCore (특징 추출 백본 ResNet-18) | timm `resnet18.a1_in1k` — Hugging Face | Apache-2.0 |

### 2-2. 모델별 상세

**1. 객체 검출 — `GS_DL_dfine_TY.onnx`**

| 항목 | 내용 |
|---|---|
| SHA-256 | `451c5d80586426f26b85c38000f0396b8b97693c3a5ac6fbc06e05b2169a9b65` (41,355,144 B) |
| 모델 구조 | D-FINE Small — 원 논문 *D-FINE: Redefine Regression Task in DETRs as Fine-grained Distribution Refinement* (2024) |
| 사전학습 가중치 | `ustc-community/dfine-small-obj2coco` (Objects365 로 사전학습 후 COCO 로 미세조정한 공개 가중치), 모델 카드 라이선스 **Apache-2.0** |
| 학습 라이브러리 | Hugging Face transformers 5.20.0 (Apache-2.0), PyTorch 2.6.0 (BSD-3-Clause) |
| 당사 미세조정 | 당사 부품 이미지 — 학습 50 · 검증 9 · 시험 12 장, 2 클래스(`T` · `Y`), 입력 640 × 640 |
| ONNX 입출력 | 입력 `images`[N,3,640,640] · `orig_target_sizes`[N,2] → 출력 `labels` · `boxes` · `scores` (opset 17) |

**2. 인스턴스 분할 — `GS_DL_rfdetrseg_TY.onnx`**

| 항목 | 내용 |
|---|---|
| SHA-256 | `db18239a7d7f22c71b48d7a9a15a9ecaa0f9e25bcf3245585b3a96256dbc944b` (138,211,736 B) |
| 모델 구조 | RF-DETR Seg Medium (Roboflow) |
| 사전학습 가중치 | `rf-detr-seg-medium` (COCO 사전학습 공개 가중치). RF-DETR 저장소는 분할(Seg) 모델 전 크기를 **Apache-2.0** 로 공개 — 별도 상용 라이선스(PML 1.0)가 붙는 것은 검출용 일부 크기(Atto · Femto · Pico · XL · 2XL)와 `rfdetr_plus` 확장뿐이며, 이 모델에는 해당하지 않음 |
| 학습 라이브러리 | rfdetr 1.11.2 (Apache-2.0), PyTorch 2.6.0 (BSD-3-Clause) |
| 당사 미세조정 | 당사 부품 이미지 — 학습 50 · 검증 9 · 시험 12 장, 2 클래스(`T` · `Y`), 입력 552 × 552 |
| ONNX 입출력 | 입력 `input`[1,3,552,552] → 출력 `dets` · `labels` · `masks` (opset 17) |

**3. 시맨틱 분할 — `GS_DL_deeplabv3_TY.onnx`**

| 항목 | 내용 |
|---|---|
| SHA-256 | `b6a4cd08b01440392827ddf851fada9b4048bf2d730f60f2f0265d41ffe08a53` (44,073,696 B) |
| 모델 구조 | DeepLabV3, 백본 MobileNetV3-Large (torchvision 구현) |
| 사전학습 가중치 | torchvision `COCO_WITH_VOC_LABELS_V1` (파일 `deeplabv3_mobilenet_v3_large-fc3c493d.pth`), torchvision 라이선스 **BSD-3-Clause** |
| 학습 라이브러리 | torchvision 0.21.0 · PyTorch 2.6.0 (BSD-3-Clause) |
| 당사 미세조정 | 당사 부품 이미지 — 학습 50 · 검증 9 · 시험 12 장, 배경 + 2 클래스(`T` · `Y`), 입력 512 × 512. 분류 헤드만 새로 만들어 학습 |
| ONNX 입출력 | 입력 `input`[1,3,512,512] → 출력 `logits`[1,3,512,512] (opset 17) |

**4. 이미지 분류 — `GS_DL_resnet18_cls.onnx`**

| 항목 | 내용 |
|---|---|
| SHA-256 | `6277b290efc992966efbeed91e2b31644e2982bddb3f9240006ef7a931ea782d` (44,704,812 B) |
| 모델 구조 | ResNet-18 (torchvision 구현) |
| 사전학습 가중치 | torchvision `IMAGENET1K_V1` (파일 `resnet18-f37072fd.pth`, ImageNet-1k 사전학습), torchvision 라이선스 **BSD-3-Clause** |
| 학습 라이브러리 | torchvision 0.21.0 · PyTorch 2.6.0 (BSD-3-Clause) |
| 당사 미세조정 | 당사 부품 이미지 — 4 클래스(양품 `OK` · 불량 `cap` · `pen` · `tape`), 입력 448 × 448. 마지막 분류 층을 4 클래스로 바꿔 학습 |
| ONNX 입출력 | 입력 `input`[batch,3,448,448] → 출력 `output`[batch,4] (opset 13) |

**5. 이상 탐지 — `GS_DL_patchcore.onnx`**

| 항목 | 내용 |
|---|---|
| SHA-256 | `7b86832b5394b066d01db856a8e3b219b8d0676b9c2e8ef0e6e44eedb1b8ea81` (20,553,226 B) |
| 모델 구조 | PatchCore — 원 논문 *Towards Total Recall in Industrial Anomaly Detection* (2022). 특징 추출 백본 ResNet-18 |
| 사전학습 가중치 | timm `resnet18.a1_in1k` (ImageNet-1k 사전학습), 모델 카드 라이선스 **Apache-2.0**. 백본 가중치는 학습하지 않고 그대로 씀 |
| 학습 라이브러리 | anomalib 1.2.0 (Apache-2.0), timm 1.0.30 (Apache-2.0), PyTorch 2.6.0 (BSD-3-Clause) |
| 당사 학습 | 당사 부품 **정상 이미지만으로** 정상 특징 기억 장치(메모리 뱅크, 코어셋 10 %)를 만듦, 입력 224 × 224 |
| ONNX 입출력 | 입력 `input`[1,3,224,224] → 출력 `anomaly_map` · `pred_score` (opset 14) |

### 2-3. 학습 데이터

- 미세조정에 쓴 이미지는 **모두 당사가 당사 장비로 직접 촬영한 당사 생산 부품 이미지**이며, 사람 얼굴 · 개인정보는 들어 있지 않습니다.
- 사전학습 가중치가 학습된 공개 데이터셋(ImageNet-1k · COCO · Objects365)의 이미지는 시험자료와 모델에 **들어 있지 않습니다**. 당사는 가중치 공개자가 배포한 가중치만 내려받아 썼습니다.

### 2-4. 결함 허용성 시험용 파일 (학습 모델 아님)

| 파일 (`DeepLearning\FaultTolerance\models\`) | 내용 |
|---|---|
| `FT01_corrupt.onnx` (65,536 B) | 내용을 고의로 깨뜨린 파일 |
| `FT02_wrong_contract.onnx` (429 B) | 정상 ONNX 형식이지만 입출력 규약이 맞지 않게 당사가 만든 파일 |
| `FT03_empty.onnx` (0 B) | 빈 파일 |

세 파일 모두 당사가 직접 만들었으며 제3자 가중치를 포함하지 않습니다.

## 3. 참고 — 제품 설치 파일에 들어 있는 인식 모델

시험자료와 별도로, **문자 인식(OCR) 도구가 쓰는 인식 모델은 제품 설치 파일(`VMS-1.48.0.msi`)에 들어 있어** 따로 제공하지 않습니다. 빌드할 때 고정된 판을 받아 SHA-256 을 검증한 뒤 설치 파일에 넣습니다.

| 설치 위치 | 내용 | 라이선스 |
|---|---|---|
| `models\ppocr\` | PP-OCRv4 문자 영역 검출 · 문자 인식 ONNX + 문자 사전 (PaddleOCR 원본, RapidOCR ONNX 변환본) | Apache-2.0 |
| `tessdata\` | Tesseract 언어 데이터 tessdata_best (영어 · 한국어 · 일본어 · 중국어 간체) | Apache-2.0 |

## 4. 무결성 확인

- 위 SHA-256 값은 시험자료 압축 파일 안의 `GS_TestData\SHA256SUMS.txt` 에 적힌 값과 같습니다.
- 같은 모델이 두 폴더에 있는 경우(1번 · 5번)는 바이트까지 같은 파일입니다.
- 추론은 제품 내장 추론 엔진(ONNX Runtime 기반)으로 하며, 같은 이미지 · 같은 설정이면 CPU 만으로도 항상 같은 결과가 나옵니다.

## 5. 출처 링크

| 구성 | 공개 위치 |
|---|---|
| D-FINE Small 가중치 | https://huggingface.co/ustc-community/dfine-small-obj2coco |
| RF-DETR (Seg 포함) | https://github.com/roboflow/rf-detr |
| torchvision 사전학습 가중치 (DeepLabV3 · ResNet-18) | https://github.com/pytorch/vision |
| timm ResNet-18 가중치 | https://huggingface.co/timm/resnet18.a1_in1k |
| anomalib (PatchCore 구현) | https://github.com/open-edge-platform/anomalib |
| PaddleOCR · RapidOCR | https://github.com/PaddlePaddle/PaddleOCR · https://github.com/RapidAI/RapidOCR |
| Tesseract tessdata_best | https://github.com/tesseract-ocr/tessdata_best |
