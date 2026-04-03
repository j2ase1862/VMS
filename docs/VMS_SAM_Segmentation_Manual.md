# VMS SAM Auto-Segmentation 오퍼레이터 매뉴얼

**버전**: 1.0
**작성일**: 2026-04-03
**대상**: VMS DeepLearning 오퍼레이터

---

## 목차

1. [개요](#1-개요)
2. [사전 준비](#2-사전-준비)
3. [화면 구성](#3-화면-구성)
4. [데이터셋 생성](#4-데이터셋-생성)
5. [SAM 모델 로드](#5-sam-모델-로드)
6. [세그멘테이션 라벨링](#6-세그멘테이션-라벨링)
7. [데이터 내보내기 (Export)](#7-데이터-내보내기-export)
8. [모델 학습 (Training)](#8-모델-학습-training)
9. [전체 워크플로우 요약](#10-전체-워크플로우-요약)
10. [문제 해결 (FAQ)](#11-문제-해결-faq)

---

## 1. 개요

SAM(Segment Anything Model) Auto-Segmentation은 **이미지 위를 클릭하는 것만으로** 객체의 정밀한 폴리곤 마스크를 자동 생성하는 기능입니다.

MobileSAM ONNX 모델을 사용하여 오프라인에서 동작하며, 생성된 폴리곤 라벨은 YOLO-Seg 형식으로 내보내 인스턴스 세그멘테이션 모델을 학습할 수 있습니다.

### 전체 흐름

```
SAM ONNX 준비 → 데이터셋 생성 (Segmentation) → 모델 로드 → 클릭 라벨링 → 내보내기 → 학습
```

| 단계 | 설명 |
|------|------|
| 모델 준비 | MobileSAM Encoder/Decoder ONNX 파일 준비 |
| 라벨링 | 객체 위 클릭 → 자동 폴리곤 생성 → Enter로 확정 |
| 내보내기 | YOLO-Seg 학습 포맷으로 데이터 변환 |
| 학습 | Python 스크립트로 YOLO 세그멘테이션 모델 학습 |

---

## 2. 사전 준비

### 2.1 MobileSAM ONNX 모델 준비

SAM 기능을 사용하려면 **2개의 ONNX 파일**이 필요합니다:

| 파일 | 설명 | 크기 (참고) |
|------|------|------------|
| `mobile_sam_encoder.onnx` | 이미지 인코더 — 이미지를 임베딩 벡터로 변환 | ~20MB |
| `mobile_sam_decoder.onnx` | 마스크 디코더 — 클릭 포인트로 마스크 생성 | ~16MB |

#### ONNX 모델 생성 방법

Python 환경에서 MobileSAM 공식 모델을 ONNX로 변환합니다:

```bash
# 필수 패키지 설치
pip install mobile-sam onnx onnxruntime torch

# 변환 스크립트 실행
python export_mobile_sam_onnx.py
```

변환 스크립트 예시 (`export_mobile_sam_onnx.py`):

```python
import torch
from mobile_sam import sam_model_registry, SamOnnxModel

# 1. 모델 로드 (mobile_sam.pt 체크포인트 필요)
sam = sam_model_registry["vit_t"](checkpoint="mobile_sam.pt")

# 2. Encoder 변환
dummy_input = torch.randn(1, 3, 1024, 1024)
torch.onnx.export(
    sam.image_encoder,
    dummy_input,
    "mobile_sam_encoder.onnx",
    input_names=["input_image"],
    output_names=["image_embeddings"],
    opset_version=17
)

# 3. Decoder 변환
onnx_model = SamOnnxModel(sam, return_single_mask=True)
embed_dim = sam.prompt_encoder.embed_dim
embed_size = sam.prompt_encoder.image_embedding_size

dummy_inputs = {
    "image_embeddings": torch.randn(1, embed_dim, *embed_size),
    "point_coords": torch.randint(0, 1024, (1, 2, 2), dtype=torch.float),
    "point_labels": torch.randint(0, 2, (1, 2), dtype=torch.float),
    "mask_input": torch.randn(1, 1, 256, 256),
    "has_mask_input": torch.tensor([0.0]),
    "orig_im_size": torch.tensor([512.0, 512.0]),
}

torch.onnx.export(
    onnx_model,
    tuple(dummy_inputs.values()),
    "mobile_sam_decoder.onnx",
    input_names=list(dummy_inputs.keys()),
    output_names=["masks", "iou_predictions", "low_res_masks"],
    opset_version=17
)

print("Export 완료: mobile_sam_encoder.onnx, mobile_sam_decoder.onnx")
```

> **참고**: `mobile_sam.pt` 체크포인트는 [MobileSAM 공식 리포지토리](https://github.com/ChaoningZhang/MobileSAM)에서 다운로드할 수 있습니다.

### 2.2 Python 환경 (학습 시에만 필요)

라벨링만 수행할 경우 Python은 불필요합니다. 학습까지 진행하려면:

```bash
# Python 3.8 ~ 3.10 권장
pip install ultralytics torch torchvision
```

> GPU 학습 시 CUDA 지원 PyTorch를 설치하세요.

---

## 3. 화면 구성

Segmentation 모드를 선택하면 기존 Detection/OCR 모드와 다른 화면 구성이 표시됩니다.

```
┌─────────────┬──────────────────────────┬──────────────┐
│  좌측 패널   │       중앙 캔버스         │  우측 패널    │
│             │ ┌──────────────────────┐ │              │
│ [Dataset]   │ │ SAM Segment          │ │ [Classes]    │
│  - 목록     │ │ 좌클릭=전경 우클릭=배경 │ │  - 클래스 목록│
│  - 생성/삭제 │ │ [Confirm] [Clear]    │ │  - 추가      │
│             │ └──────────────────────┘ │              │
│ [Images]    │                          │ [Labels]     │
│  - 이미지   │   이미지 + 폴리곤 오버레이  │  - 폴리곤 목록│
│    목록     │   + 클릭 포인트 표시       │              │
│  - 추가/삭제 │                          │ [SAM Model]  │
│  - ◀ ▶ 탐색 │                          │  - Encoder   │
│             │                          │  - Decoder   │
│             │                          │  - Load 버튼  │
│             │                          │              │
│             │                          │ [Export &    │
│             │                          │  Training]   │
└─────────────┴──────────────────────────┴──────────────┘
```

### Segmentation 모드 전용 UI 요소

| 요소 | 위치 | 설명 |
|------|------|------|
| **SAM Segment 툴바** | 캔버스 좌상단 | 클릭 안내, Confirm/Clear 버튼, 상태 메시지 |
| **SAM Model 패널** | 우측 패널 | Encoder/Decoder 경로 지정 및 모델 로드 |
| **폴리곤 프리뷰** | 캔버스 위 | 녹색 점선으로 SAM 예측 폴리곤 표시 |
| **클릭 포인트** | 캔버스 위 | 전경=녹색 점, 배경=빨간 점 |
| **확정된 폴리곤** | 캔버스 위 | 클래스 색상의 실선 폴리곤 |

---

## 4. 데이터셋 생성

1. 좌측 패널 > **Dataset** 섹션에서 데이터셋 이름을 입력합니다.
   - 예: `Bottle_Seg`, `PCB_Components`
2. Task Type 콤보박스에서 **"Segmentation — 인스턴스 세그멘테이션"** 을 선택합니다.
3. **"+"** 버튼을 클릭하여 데이터셋을 생성합니다.

> 콤보박스 아래에 설명이 표시됩니다:
> ```
> SAM 모델로 객체를 클릭하면 자동 폴리곤 마스크를 생성합니다. YOLO-Seg 형식으로 Export합니다.
> ▶ SAM 모델 로드 → 클릭 세그멘테이션 → Export YOLO-Seg → 학습
> ```

### 이미지 추가

1. **"+ Add"** 버튼 클릭 → 이미지 파일 선택
   - 지원 포맷: BMP, JPG, JPEG, PNG, TIF, TIFF
2. 실제 검사 환경 이미지를 충분히 수집합니다 (최소 50장 이상 권장)

---

## 5. SAM 모델 로드

이미지를 추가한 후 SAM 모델을 로드해야 세그멘테이션이 가능합니다.

1. 우측 패널의 **SAM Model** Expander를 펼칩니다.
2. **Encoder** 항목 옆의 **"..."** 버튼을 클릭하여 `mobile_sam_encoder.onnx` 파일을 선택합니다.
3. **Decoder** 항목 옆의 **"..."** 버튼을 클릭하여 `mobile_sam_decoder.onnx` 파일을 선택합니다.
4. **"Load SAM Model"** 버튼을 클릭합니다.
5. 성공 시 하단에 **"SAM 모델 로드됨 — 이미지 위를 클릭하세요."** (녹색) 메시지가 표시됩니다.

> **주의**: Encoder와 Decoder는 **동일한 SAM 모델에서 변환한 쌍**이어야 합니다. 서로 다른 버전을 섞으면 동작하지 않습니다.

---

## 6. 세그멘테이션 라벨링

### 6.1 클래스 등록

라벨링 전에 분류할 객체 클래스를 등록합니다.

1. 우측 패널 > **Classes** 섹션에서 클래스 이름을 입력합니다.
   - 예: `bottle`, `cap`, `defect_area`
2. **"+"** 버튼으로 추가합니다.
3. 현재 사용할 클래스를 목록에서 **선택(클릭)** 합니다.

### 6.2 기본 조작 — 클릭으로 세그멘테이션

| 조작 | 키/마우스 | 설명 |
|------|-----------|------|
| **전경 포인트** | 좌클릭 | 분할하려는 객체 위를 클릭 |
| **배경 포인트** | 우클릭 | 제외할 영역을 클릭 (보정용) |
| **라벨 확정** | Enter | 현재 프리뷰 폴리곤을 라벨로 저장 |
| **초기화** | Escape | 현재 프리뷰와 클릭 포인트 모두 초기화 |

### 6.3 라벨링 절차 (Step by Step)

```
Step 1. 클래스 선택
  └─ 우측 Classes에서 라벨링할 클래스 선택

Step 2. 첫 번째 좌클릭
  └─ 객체 중앙을 좌클릭
  └─ 첫 클릭 시 이미지 임베딩이 자동 생성됨 (~1초 대기)
  └─ 녹색 점선 폴리곤이 즉시 표시됨

Step 3. 보정 (필요 시)
  └─ 폴리곤이 부족한 영역: 해당 부분에 추가 좌클릭 (전경)
  └─ 폴리곤이 초과한 영역: 제외할 부분에 우클릭 (배경)
  └─ 클릭할 때마다 폴리곤이 실시간 업데이트됨

Step 4. 확정
  └─ Enter 키 또는 툴바의 "Confirm (Enter)" 버튼 클릭
  └─ 폴리곤이 라벨로 저장되고 좌측 Labels 목록에 추가됨

Step 5. 다음 객체
  └─ 같은 이미지의 다른 객체를 계속 클릭하여 라벨링
  └─ 클래스를 변경하려면 우측 Classes에서 다른 클래스 선택

Step 6. 다음 이미지
  └─ ◀/▶ 버튼으로 이동 (이전 라벨은 자동 저장됨)
  └─ 새 이미지에서 다시 Step 2부터 시작
```

### 6.4 화면 표시 요소

라벨링 중 캔버스에 다음과 같은 시각적 요소가 표시됩니다:

| 요소 | 모양 | 설명 |
|------|------|------|
| **녹색 점 (●)** | 작은 원, 흰색 테두리 | 전경 클릭 포인트 |
| **빨간 점 (●)** | 작은 원, 흰색 테두리 | 배경 클릭 포인트 |
| **녹색 점선 폴리곤** | 점선, 반투명 녹색 채우기 | SAM 예측 프리뷰 (아직 확정되지 않음) |
| **클래스 색상 폴리곤** | 실선, 반투명 채우기 | 확정된 라벨 (클래스별 고유 색상) |
| **클래스명 배지** | 폴리곤 상단 텍스트 | 확정된 라벨의 클래스 이름 |

### 6.5 라벨링 팁

| 상황 | 권장 조작 |
|------|----------|
| 단순한 객체 (병, 상자 등) | 객체 중앙에 한 번 좌클릭이면 충분 |
| 복잡한 형태 | 객체의 여러 부분에 2~3회 좌클릭 |
| 인접 객체가 포함됨 | 제외할 인접 객체에 우클릭 (배경 포인트) |
| 결과가 불만족 | Escape로 초기화 후 다시 시도 |
| 작은 객체 | 객체 정중앙을 정확히 클릭 |

### 6.6 라벨 관리

확정된 라벨은 우측 **Labels** 섹션에서 관리합니다:

- 라벨 목록에서 **클릭** → 해당 폴리곤 선택
- **Class** 드롭다운 → 클래스 변경 가능
- **Delete** 버튼 → 선택된 라벨 삭제

---

## 7. 데이터 내보내기 (Export)

라벨링이 완료되면 YOLO-Seg 형식으로 내보냅니다.

### 7.1 자동 분할

1. 우측 패널 하단 > **Export** Expander를 펼칩니다.
2. **"Auto Split (Train/Val)"** 버튼을 클릭합니다.
3. 라벨링된 이미지가 80:20 비율로 Train/Validation에 자동 분배됩니다.

### 7.2 YOLO-Seg 형식 내보내기

1. **"Export for Training"** 버튼을 클릭합니다.
2. 저장할 폴더를 지정합니다.
3. 다음 구조의 파일들이 생성됩니다:

```
출력폴더/
├── data.yaml               # 데이터셋 설정 (경로, 클래스)
├── images/
│   ├── train/              # 학습 이미지
│   │   ├── img_001.jpg
│   │   └── ...
│   └── val/                # 검증 이미지
│       ├── img_050.jpg
│       └── ...
└── labels/
    ├── train/              # 학습 라벨 (정규화 폴리곤 좌표)
    │   ├── img_001.txt
    │   └── ...
    └── val/                # 검증 라벨
        ├── img_050.txt
        └── ...
```

### 라벨 파일 형식

각 `.txt` 파일의 한 줄은 하나의 폴리곤 라벨을 나타냅니다:

```
<class_id> <x1> <y1> <x2> <y2> ... <xn> <yn>
```

- 좌표는 0~1로 정규화된 값 (이미지 너비/높이로 나눈 값)
- 예시:
  ```
  0 0.123456 0.234567 0.345678 0.234567 0.345678 0.456789 0.123456 0.456789
  ```

### `data.yaml` 내용 예시

```yaml
train: images/train
val: images/val
nc: 3
names: ['bottle', 'cap', 'defect_area']
```

---

## 8. 모델 학습 (Training)

### 8.1 학습 설정

우측 패널의 **Training** Expander를 펼칩니다.

| 항목 | 설명 | 기본값 |
|------|------|--------|
| **Python** | Python 실행 경로 | `python` |
| **Script** | 학습 스크립트 경로 (자동 매칭: `train_yolo_seg.py`) | 자동 설정 |
| **Epochs** | 학습 반복 횟수 | `100` |
| **Batch Size** | 배치 크기 (GPU 메모리에 맞게 조절) | `8` |

> Segmentation 데이터셋을 선택하면 Script가 `train_yolo_seg.py`로 자동 매칭됩니다.

### 8.2 학습 실행

1. Export를 **먼저 완료**합니다 (학습 데이터셋 경로가 자동 설정됨).
2. 학습 설정을 확인합니다.
3. **"Start Training"** 버튼을 클릭합니다.

학습 중 표시되는 정보:

| 항목 | 설명 |
|------|------|
| **Progress Bar** | 전체 학습 진행률 |
| **Loss** | 손실값 — 낮을수록 좋음 |
| **Acc** | 정확도 — 높을수록 좋음 |
| **Log** | 실시간 학습 로그 |

### 8.3 학습 파라미터 조정

| 상황 | 조치 |
|------|------|
| Loss가 줄지 않음 | Epochs 늘리기 (200~300), 데이터 더 수집 |
| GPU 메모리 부족 | Batch Size 줄이기 (8 → 4 → 2) |
| 과적합 | 데이터 더 수집, Epochs 줄이기 |
| 학습이 너무 느림 | Batch Size 줄이기, 이미지 해상도 낮추기 |

---

## 9. 전체 워크플로우 요약

```
┌──────────────────────────────────────────────────────────────┐
│            VMS SAM 세그멘테이션 워크플로우                       │
├──────────────────────────────────────────────────────────────┤
│                                                              │
│  Step 1. ONNX 모델 준비                                      │
│    └─ mobile_sam_encoder.onnx + mobile_sam_decoder.onnx      │
│                                                              │
│  Step 2. 데이터셋 생성                                        │
│    └─ Task Type → "Segmentation" 선택 → "+" 클릭             │
│                                                              │
│  Step 3. 이미지 추가                                          │
│    └─ "+ Add" → 검사 환경 이미지 선택 (50장 이상 권장)           │
│                                                              │
│  Step 4. SAM 모델 로드                                        │
│    └─ 우측 SAM Model > Encoder/Decoder 경로 지정 > Load       │
│                                                              │
│  Step 5. 클래스 등록                                          │
│    └─ Classes → 객체 종류별 클래스명 입력 → "+" 클릭            │
│                                                              │
│  Step 6. 세그멘테이션 라벨링                                    │
│    └─ 각 이미지에서:                                           │
│       1) 클래스 선택                                           │
│       2) 객체 위 좌클릭 → 폴리곤 자동 생성                      │
│       3) 필요 시 추가 좌클릭/우클릭으로 보정                     │
│       4) Enter로 확정                                         │
│       5) ◀/▶ 로 다음 이미지                                   │
│                                                              │
│  Step 7. 내보내기                                             │
│    └─ "Auto Split" → "Export for Training" (YOLO-Seg)        │
│                                                              │
│  Step 8. 학습                                                 │
│    └─ Epochs/Batch 설정 → "Start Training"                   │
│                                                              │
│  Step 9. 검증                                                 │
│    └─ 학습된 ONNX 모델을 VisionSetup에서 적용 후 검사 실행      │
│                                                              │
└──────────────────────────────────────────────────────────────┘
```

---

## 10. 문제 해결 (FAQ)

### Q: SAM Model 패널이 보이지 않습니다.
**A**: 데이터셋의 Task Type이 **Segmentation**인지 확인하세요. Detection이나 다른 타입으로 생성된 데이터셋에서는 SAM Model 패널이 표시되지 않습니다.

### Q: "Load SAM Model" 클릭 시 오류가 발생합니다.
**A**: 다음을 확인하세요:
- Encoder와 Decoder 경로가 모두 지정되었는지
- ONNX 파일이 실제로 존재하는지
- ONNX 파일이 손상되지 않았는지 (다시 변환 시도)
- Encoder와 Decoder가 동일한 SAM 모델에서 변환된 쌍인지

### Q: 첫 클릭 후 반응이 느립니다.
**A**: 첫 클릭 시 이미지 임베딩을 생성하는 과정(~1초)이 필요합니다. 두 번째 클릭부터는 즉시 반응합니다. 이미지를 전환하면 임베딩이 초기화되어 다시 생성됩니다.

### Q: 클릭했는데 폴리곤이 생성되지 않습니다.
**A**: 다음을 확인하세요:
- SAM 모델이 로드되었는지 (하단 녹색 메시지 확인)
- 이미지가 표시된 영역 안을 클릭했는지
- 상태 메시지에 오류가 표시되지 않았는지

### Q: 폴리곤이 원하는 객체를 정확히 감싸지 않습니다.
**A**: 추가 클릭으로 보정하세요:
- 폴리곤에 포함되어야 하는데 빠진 영역: 해당 부분에 **좌클릭** (전경)
- 폴리곤에 포함되면 안 되는 영역: 해당 부분에 **우클릭** (배경)
- 2~3회 보정으로 대부분 정확한 결과를 얻을 수 있습니다

### Q: Enter를 눌렀는데 라벨이 저장되지 않습니다.
**A**: 녹색 점선 프리뷰 폴리곤이 표시된 상태에서만 Enter가 동작합니다. 프리뷰가 없으면 먼저 객체를 클릭하여 폴리곤을 생성하세요.

### Q: 이전에 확정한 폴리곤을 수정하고 싶습니다.
**A**: 현재 버전에서는 확정된 폴리곤의 형태 직접 수정은 지원되지 않습니다. 우측 Labels에서 해당 라벨을 **삭제** 후 다시 클릭하여 라벨링하세요.

### Q: Export 시 라벨 파일에 데이터가 없습니다.
**A**: 다음을 확인하세요:
- 라벨이 확정(Enter)되었는지 — 프리뷰 상태만으로는 저장되지 않음
- Auto Split이 실행되었는지 — 분할되지 않은 이미지는 Export되지 않음
- 이미지에 라벨이 있는지 — 이미지 목록에서 녹색 원(●) 확인

### Q: GPU가 없어도 사용할 수 있나요?
**A**: 네. MobileSAM은 CPU에서도 동작합니다. 다만 첫 클릭 시 임베딩 생성이 CPU에서는 2~5초 정도 소요될 수 있습니다.

### Q: 데이터셋은 어디에 저장되나요?
**A**: `%APPDATA%/VMS/Datasets/` 폴더에 데이터셋별 하위 폴더로 저장됩니다. 폴리곤 좌표는 `dataset.json` 파일 안의 각 이미지 라벨 `Points` 필드에 기록됩니다.

---

## 부록: 키보드 단축키

| 키 | 동작 |
|----|------|
| **Enter** | SAM 프리뷰 폴리곤을 라벨로 확정 |
| **Escape** | SAM 프리뷰 및 클릭 포인트 초기화 |

## 부록: SAM Decoder 입력/출력 텐서 사양

기술 참고용 — 커스텀 SAM 모델을 직접 변환할 때 참고하세요.

### 입력 텐서

| 이름 | Shape | 설명 |
|------|-------|------|
| `image_embeddings` | Encoder 출력 | 이미지 임베딩 (캐시됨) |
| `point_coords` | (1, N, 2) | 클릭 좌표 (1024x1024 공간으로 스케일링) |
| `point_labels` | (1, N) | 1=전경, 0=배경 |
| `mask_input` | (1, 1, 256, 256) | 이전 마스크 (첫 예측: 전부 0) |
| `has_mask_input` | (1,) | 이전 마스크 존재 여부 (첫 예측: 0.0) |
| `orig_im_size` | (2,) | 원본 이미지 [height, width] |

### 출력 텐서

| 이름 | Shape | 설명 |
|------|-------|------|
| `masks` | (1, N_masks, H, W) | 예측 마스크 (>0이면 전경) |
| `iou_predictions` | (1, N_masks) | 각 마스크의 IoU 예측 점수 |
| `low_res_masks` | (1, N_masks, 256, 256) | 저해상도 마스크 |

---

*VMS DeepLearning — SAM Auto-Segmentation Module*
