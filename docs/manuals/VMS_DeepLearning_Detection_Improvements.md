# VMS 딥러닝 검출력 향상 개선 사항

**버전**: 1.0
**작성일**: 2026-04-20
**브랜치**: `feat/handeye-calibration-and-robot-protocol`
**대상**: VMS VisionSetup / VMS.DeepLearning 개발자 · 오퍼레이터

---

## 목차

1. [개요](#1-개요)
2. [Phase 1 — 즉시 효과 (검출률/속도)](#phase-1--즉시-효과-검출률속도)
   1. [CUDA / DirectML Execution Provider 활성화](#11-cuda--directml-execution-provider-활성화)
   2. [클래스별 Confidence 임계값](#12-클래스별-confidence-임계값)
   3. [CLAHE 전처리](#13-clahe-전처리)
   4. [YOLO 증강 파라미터 노출](#14-yolo-증강-파라미터-노출)
3. [Phase 2 — 구조적 개선](#phase-2--구조적-개선)
   1. [SAHI Tiled Inference](#21-sahi-tiled-inference)
   2. [PatchCore 백본 / Coreset 튜닝](#22-patchcore-백본--coreset-튜닝)
   3. [EnsembleTool (Detection + Anomaly 결합)](#23-ensembletool-detection--anomaly-결합)
4. [Phase 3 — 고급 기능](#phase-3--고급-기능)
   1. [TensorRT 엔진 캐시 + FP16 + 전역 설정](#31-tensorrt-엔진-캐시--fp16--전역-설정)
   2. [Anomaly 자동 임계값 캘리브레이션](#32-anomaly-자동-임계값-캘리브레이션)
   3. [Augmentation 프리셋 (원클릭)](#33-augmentation-프리셋-원클릭)
5. [전체 변경 파일 목록](#5-전체-변경-파일-목록)
6. [실 환경 테스트 체크리스트](#6-실-환경-테스트-체크리스트)
7. [앞으로의 개선 방향](#7-앞으로의-개선-방향)

---

## 1. 개요

`VMS.DeepLearning` + `VMS.VisionSetup`의 검출력을 개선하기 위해 3개 Phase로 나누어 10개 항목을 적용했습니다. 핵심 목표는 아래 4가지입니다.

| 목표 | 기대 효과 | 주요 기여 항목 |
|---|---|---|
| **미검(False Negative) ↓** | 작은 결함·조명 변동에서 놓치는 결함 감소 | SAHI · CLAHE · Coreset↑ · Augmentation |
| **과검(False Positive) ↓** | 불필요한 NG 판정 감소 | Per-Class Threshold · Ensemble(AND) · Auto Calibration |
| **속도 ↑** | 실시간 검사 FPS 확보 | CUDA/TensorRT EP · FP16 · Engine Cache |
| **운영 편의성 ↑** | 튜닝에 드는 사람 시간 감소 | Auto Calibration · Aug 프리셋 · 전역 설정 |

모든 개선은 **기존 레시피와 하위 호환**됩니다. 새 파라미터는 합리적 기본값을 가지며 켜지 않으면 기존 동작과 동일합니다.

---

## Phase 1 — 즉시 효과 (검출률/속도)

### 1.1. CUDA / DirectML Execution Provider 활성화

**변경**: `VMS.VisionSetup/VisionTools/DeepLearning/OnnxModelBase.cs`

`OnnxExecutionProvider` enum을 추가하고 `LoadModel()`에서 리플렉션으로 EP를 안전하게 주입합니다. 런타임 DLL이 없으면 예외 없이 CPU로 폴백합니다.

```csharp
public enum OnnxExecutionProvider { Auto, Cpu, Cuda, DirectML, TensorRT }

public static OnnxExecutionProvider PreferredProvider { get; set; } = OnnxExecutionProvider.Auto;
public string ActiveProvider { get; private set; } = "CPU";
```

**우선순위 (Auto 모드)**: `CUDA → DirectML → CPU`

**현재 패키지 상태** (2026-04-20 업데이트):

| 프로젝트 | 적용 패키지 | 버전 |
|---|---|---|
| `VMS.VisionSetup` | `Microsoft.ML.OnnxRuntime.Gpu` ✅ | 1.21.0 |
| `VMS.DeepLearning` | `Microsoft.ML.OnnxRuntime.Gpu` ✅ | 1.21.0 |

CUDA · TensorRT 런타임 DLL이 빌드 시 자동 배포됩니다 (`onnxruntime_providers_cuda.dll`, `onnxruntime_providers_tensorrt.dll`). CUDA 미설치 환경에서는 자동 CPU 폴백되므로 배포는 안전합니다.

**환경 설치 + 설정 파일 작성**은 별도 가이드 참조:
👉 [VMS_TensorRT_Setup_Guide.md](VMS_TensorRT_Setup_Guide.md)

활성 EP는 검출 결과 메시지에 `(EP: TensorRT)` / `(EP: CUDA)` / `(EP: CPU)` 형태로 표시되며, `VisionResult.Data["ExecutionProvider"]`로도 조회 가능합니다.

---

### 1.2. 클래스별 Confidence 임계값

**변경**: `DetectionTool.cs`, `DetectionToolSettingsViewModel.cs`, `DetectionToolSettings.xaml`

기존 단일 Confidence 임계값은 결함 종류별 특성 차이를 반영하지 못했습니다. 예: 크랙은 임계값↓(민감), 먼지 노이즈는 임계값↑(강건)을 원할 때.

**신규 UI**:

```
[x] Use Per-Class Thresholds
  ┌─────────────────────────────────────────┐
  │ crack     [───────●─────]  0.35        │
  │ scratch   [────●────────]  0.20        │
  │ dust      [──────────●──]  0.75        │
  └─────────────────────────────────────────┘
```

**구현**: `YoloOnnxEngine.Detect()`에 `float[]? perClassConfThresholds` 파라미터를 추가해 NMS 전에 클래스별 필터링. 전역 임계값은 fallback.

---

### 1.3. CLAHE 전처리

**변경**: `OnnxModelBase.ApplyClahe()` 신규, `DetectionTool`에 3개 파라미터 추가

공장 조명 변동 환경에서 결함 경계가 희미해질 때 대비(contrast)를 국소적으로 향상시킵니다. LAB 색공간의 L 채널에만 적용해 **색상 왜곡 없이** 대비만 증폭.

| 파라미터 | 기본값 | 설명 |
|---|---|---|
| `UseClahe` | false | 추론 전 CLAHE 적용 여부 |
| `ClaheClipLimit` | 2.0 | 대비 증폭 한계 (값↑ 대비 강화, 노이즈↑) |
| `ClaheTileGridSize` | 8 | 타일 N×N 분할 크기 |

```csharp
// OpenCvSharp 직접 호출
using var clahe = Cv2.CreateCLAHE(clipLimit, new Size(tile, tile));
clahe.Apply(channels[0], channels[0]);  // L 채널에만
```

---

### 1.4. YOLO 증강 파라미터 노출

**변경**: `VMS.Core/Models/Annotation/TrainingConfig.cs`, `train_yolo.py`, `MainWindow.xaml`

Ultralytics 학습의 증강 파라미터를 UI에서 직접 조정 가능하도록 노출.

| 파라미터 | 기본 | 의미 |
|---|---|---|
| `Mosaic` | 1.0 | 4-way Mosaic 증강 확률 (작은 객체 검출에 효과적) |
| `Mixup` | 0.0 | 두 이미지 혼합 확률 (분류 분기 강건화) |
| `HsvH` | 0.015 | Hue 증강 범위 |
| `HsvS` | 0.7 | Saturation 증강 범위 |
| `HsvV` | 0.4 | Value(Brightness) 증강 범위 ← **조명 변동 대응 핵심** |

`TrainingService.BuildArguments()`는 스크립트 파일명이 `yolo`를 포함할 때만 이 인자들을 추가합니다.

---

## Phase 2 — 구조적 개선

### 2.1. SAHI Tiled Inference

**변경**: `DetectionTool.cs`에 `RunSahiDetection()` + `YoloOnnxEngine.GlobalNMS()` 신규

**문제**: 4K+ 고해상도 영상을 InputSize(예: 640)로 리사이즈하면 작은 결함이 몇 픽셀로 축소돼 검출 불가.

**해결 (SAHI = Slicing Aided Hyper Inference)**:
1. 원본 이미지를 `TileSize × TileSize` 타일로 분할 (겹침 `OverlapRatio`)
2. 각 타일을 독립적으로 YOLO 추론 → 타일별 검출 박스 획득
3. 타일 좌표 → 원본 좌표 변환
4. 전역 NMS로 겹친 박스(타일 경계에 걸친 중복 검출) 병합

```
원본 4000×3000
┌───────┬───────┬───────┐
│  T11  │  T12  │  T13  │   각 타일 = 640×640
├───────┼───────┼───────┤   겹침 = 20%
│  T21  │  T22  │  T23  │
├───────┼───────┼───────┤
│  T31  │  T32  │  T33  │
└───────┴───────┴───────┘
```

| 파라미터 | 기본값 | 설명 |
|---|---|---|
| `UseSahi` | false | 타일링 활성화 |
| `SahiTileSize` | 640 | 타일 한 변 크기 (InputSize와 같거나 크게) |
| `SahiOverlapRatio` | 0.2 | 타일 간 겹침 비율 (0.0 ~ 0.5) |

**자동 폴백**: 이미지가 타일보다 작으면 일반 추론으로 전환.

---

### 2.2. PatchCore 백본 / Coreset 튜닝

**변경**: `TrainingConfig`, `train_anomaly.py`, `MainWindow.xaml`

| 파라미터 | 기본 | 선택 | 영향 |
|---|---|---|---|
| `AnomalyMethod` | patchcore | patchcore / fastflow / efficient_ad | 알고리즘 종류 |
| `AnomalyBackbone` | resnet18 | resnet18 / resnet50 / wide_resnet50_2 | 특징 표현력 ↑ = 미세 결함 검출↑, 메모리↑ |
| `CoresetRatio` | 0.1 | 0.01 ~ 1.0 | 값↑ = 메모리 뱅크 샘플↑, 미검↓, 메모리↑ |

`train_anomaly.py`:
- anomalib API: `Patchcore(backbone=..., coreset_sampling_ratio=...)` 주입 (구버전 호환 try/except 포함)
- `train_simple_patchcore` 폴백 경로에서도 백본 동적 선택 + 무작위 coreset 근사 샘플링

---

### 2.3. EnsembleTool (Detection + Anomaly 결합)

**변경**: 신규 `VisionTools/DeepLearning/EnsembleTool.cs`, VM, XAML, VisionService 배선, Serializer

원본 마크다운 문서의 권고:
> DetectionTool로 알려진 결함을 찾고, AnomalyTool로 정의되지 않은 비정상 상태를 교차 체크하는 구조를 구축하면 **과검과 미검을 동시에 개선**할 수 있습니다.

**판정 모드 4종**:

| 모드 | 로직 | 용도 |
|---|---|---|
| **And** | 모두 정상 → OK | 과검 최소화 (민감 라인) |
| **Or** | 하나만 정상이어도 OK | 미검 최소화 (중요 결함 놓치지 말 것) |
| **Weighted** | `α·det_fail_ratio + β·max_anomaly_score > threshold ⇒ NG` | 가중치 튜닝 가능 |
| **Consensus** | 모든 소스 판정이 일치해야 고신뢰 | 불일치는 **검토 플래그**로 처리 (A/B 테스팅 유용) |

**연결 방식**: DetectionTool / AnomalyTool을 Result 연결로 EnsembleTool에 연결. `VisionService.RunPipeline`이 소스의 전체 `VisionResult`(DetectionCount, AnomalyScore 포함)를 `SourceResults`로 주입.

```
[Camera] ──→ DetectionTool (YOLO)
                ╲
                 ├→ Result ──→ EnsembleTool (Weighted, α=0.6, β=0.4, th=0.5) ──→ Final OK/NG
                 ╱
          ──→ AnomalyTool (PatchCore)
```

---

## Phase 3 — 고급 기능

### 3.1. TensorRT 엔진 캐시 + FP16 + 전역 설정

**변경**: `OnnxModelBase.cs`, `App.xaml.cs`

#### 3.1.1. TensorRT Provider 옵션 주입

```csharp
public static string TensorRTCachePath { get; set; } = string.Empty;
public static bool TensorRTFp16 { get; set; } = true;
```

ORT 1.14+의 `AppendExecutionProvider("Tensorrt", Dictionary<string,string>)` 시그니처를 리플렉션으로 호출해 아래 옵션을 주입:

| TRT Provider Option | 효과 |
|---|---|
| `trt_engine_cache_enable = 1` | 첫 빌드 후 `.engine` 파일 캐시 → 재실행 시 수 분 단축 |
| `trt_engine_cache_path` | 캐시 저장 폴더 |
| `trt_fp16_enable = 1` | FP32 → FP16 최적화 (RTX 클래스에서 2~3× 속도) |

#### 3.1.2. 전역 설정 로드

`App.xaml.cs`의 `LoadAIConfig()`가 앱 시작 시 `%LocalAppData%\BODA VISION AI\system_config.json`의 아래 키를 읽어 `OnnxModelBase` 정적 속성에 반영합니다.

```json
{
  "onnxExecutionProvider": "TensorRT",
  "tensorRTCachePath": "C:\\VMS\\trt_cache",
  "tensorRTFp16": true
}
```

값: `Auto` | `Cpu` | `Cuda` | `DirectML` | `TensorRT`

#### 3.1.3. 런타임 지원 여부 조회

```csharp
if (OnnxModelBase.IsProviderAvailable(OnnxExecutionProvider.Cuda)) { ... }
```

UI에서 사용 가능 EP만 선택지로 노출하는 데 활용 가능.

---

### 3.2. Anomaly 자동 임계값 캘리브레이션

**변경**: `AnomalyTool.cs`, `AnomalyToolSettingsViewModel.cs`, `AnomalyToolSettings.xaml`

**문제**: Anomaly threshold는 수동 튜닝이 어렵고 라인마다 재튜닝 필요.

**해결**: 정상 이미지 폴더를 지정하고 "Calibrate Threshold" 버튼 클릭 → 자동 산출.

#### 공식

```
threshold = mean(AnomalyScore) + σ × std(AnomalyScore)
```

| σ (`CalibrationSigma`) | 정상 커버율 | 오검률 |
|---|---|---|
| 2.0 | 95.0% | 5.0% |
| **3.0 (기본)** | **99.7%** | **0.27%** |
| 4.0 | 99.994% | 0.006% |
| 5.0 | 99.99994% | ~0.0001% |

#### UI 흐름

```
Normal Images Folder: [C:\data\normal_samples]  [...]
Sigma (k): [────●────] 3.0
[Calibrate Threshold]
─────────────────────────────────────────────
샘플 47장 · mean=0.124 · std=0.056 · suggested=0.292 (→ 적용: 0.292)
```

비동기 실행이므로 UI 블록 없이 계산. 중복 실행 방지는 `IsCalibrating` 상태로 처리.

---

### 3.3. Augmentation 프리셋 (원클릭)

**변경**: `LabelingMainViewModel.cs`, `MainWindow.xaml`

매번 5개 HSV/Mosaic/Mixup 값을 맞추는 대신 검증된 4개 프리셋을 버튼 하나로 적용.

| 프리셋 | Mosaic | Mixup | HSV-H | HSV-S | HSV-V | 상황 |
|---|---|---|---|---|---|---|
| **Default** | 1.0 | 0.0 | 0.015 | 0.7 | 0.4 | Ultralytics 기본값, 일반 용도 |
| **Factory Lighting** | 1.0 | 0.1 | 0.02 | 0.8 | **0.7** | 공장 조명 변동이 심한 라인 |
| **Small Defects** | 1.0 | **0.15** | 0.015 | 0.7 | 0.5 | 작은 결함 검출 (Mosaic + Mixup 최대) |
| **Minimal** | 0.5 | 0.0 | 0.005 | 0.3 | 0.2 | 대규모 데이터셋 / 과증강이 악영향일 때 |

---

## 5. 전체 변경 파일 목록

### C# (VMS.VisionSetup)

| 파일 | 변경 내용 |
|---|---|
| `VisionTools/DeepLearning/OnnxModelBase.cs` | EP enum · CUDA/DML/TRT 리플렉션 주입 · CLAHE · TensorRT 캐시/FP16 · 지원 여부 조회 |
| `VisionTools/DeepLearning/DetectionTool.cs` | Per-class thresholds · CLAHE · SAHI tiling · GlobalNMS |
| `VisionTools/DeepLearning/AnomalyTool.cs` | 자동 임계값 캘리브레이션 |
| `VisionTools/DeepLearning/EnsembleTool.cs` | **신규** — 4개 판정 모드 앙상블 도구 |
| `Services/VisionService.cs` | EnsembleTool 팩토리/카테고리/소스 주입 배선 |
| `Services/ToolSerializer.cs` | Detection/Anomaly/Ensemble 신규 파라미터 직렬화 |
| `ViewModels/MainViewModel.cs` | Ensemble VM 스위치 등록 |
| `ViewModels/ToolSettings/DetectionToolSettingsViewModel.cs` | 신규 속성 바인딩 |
| `ViewModels/ToolSettings/AnomalyToolSettingsViewModel.cs` | 캘리브레이션 커맨드 + 상태 |
| `ViewModels/ToolSettings/EnsembleToolSettingsViewModel.cs` | **신규** |
| `Views/ToolSettings/Tools/DetectionToolSettings.xaml` | SAHI/CLAHE/Per-class UI |
| `Views/ToolSettings/Tools/AnomalyToolSettings.xaml` | 자동 캘리브레이션 UI |
| `Views/ToolSettings/Tools/EnsembleToolSettings.xaml(.cs)` | **신규** |
| `Views/ToolSettings/ToolSettingsView.xaml` | Ensemble 템플릿 등록 |
| `Views/ToolSettings/ToolSettingsTemplateSelector.cs` | Ensemble 선택 분기 |
| `App.xaml.cs` | `LoadAIConfig()` — 전역 EP 설정 로드 |

### C# (VMS.Core)

| 파일 | 변경 내용 |
|---|---|
| `Models/Annotation/TrainingConfig.cs` | Mosaic/Mixup/HSV · AnomalyMethod/Backbone/CoresetRatio |
| `Services/TrainingService.cs` | BuildArguments: 스크립트별 분기(YOLO/Anomaly) |

### Python (VMS.DeepLearning/scripts)

| 파일 | 변경 내용 |
|---|---|
| `train_yolo.py` | `--mosaic/--mixup/--hsv_h/--hsv_s/--hsv_v` CLI + kwargs 패스스루 |
| `train_anomaly.py` | `--backbone/--coreset_ratio` CLI · anomalib 파라미터 주입 · simple fallback 백본 선택 |

### XAML (VMS.DeepLearning)

| 파일 | 변경 내용 |
|---|---|
| `Views/MainWindow.xaml` | Anomaly/Augmentation Expander · 프리셋 버튼 |
| `ViewModels/LabelingMainViewModel.cs` | 4개 프리셋 `[RelayCommand]` |

### Docs

| 파일 | 변경 내용 |
|---|---|
| `docs/VMS_DeepLearning_Detection_Improvements.md` | **신규** (이 문서) |

---

## 6. 실 환경 테스트 체크리스트

실제 현장에서 검출력 향상을 수치화하려면 다음을 순서대로 검증하세요.

### 6.1. GPU 가속

1. `VMS.VisionSetup.csproj`의 `Microsoft.ML.OnnxRuntime` 패키지를 `.Gpu` 또는 `.DirectML`로 교체
2. `%LocalAppData%\BODA VISION AI\system_config.json`에 추가:
   ```json
   "onnxExecutionProvider": "Cuda"
   ```
3. 앱 재시작 → 첫 DetectionTool 실행 시 메시지에 `(EP: CUDA)` 확인
4. CPU 대비 FPS 2~10배 향상 확인 (모델 크기에 따름)

### 6.2. TensorRT

1. NVIDIA TensorRT 설치 + CUDA toolchain 확인
2. `system_config.json`:
   ```json
   {
     "onnxExecutionProvider": "TensorRT",
     "tensorRTCachePath": "C:\\VMS\\trt_cache",
     "tensorRTFp16": true
   }
   ```
3. 첫 실행: 엔진 빌드로 수 분 소요
4. 두 번째 실행부터: 캐시된 엔진 로드 (초 단위)
5. FPS가 CUDA 대비 추가로 1.5~3배 향상되는지 확인

### 6.3. SAHI (고해상도 영상)

- Before: 원본 4000×3000 영상에서 10px 이하 작은 결함을 놓침
- After: Use SAHI 체크 + TileSize 640, Overlap 0.2 → 동일 영상에서 검출 확인
- 속도는 타일 수만큼 느려지므로 SAHI는 필요한 ROI/모델에만 선택 적용

### 6.4. CLAHE

- 조명 불균일한 샘플(반사광, 음영) 선정
- Use CLAHE 토글 Before/After 박스 수 및 Confidence 비교

### 6.5. Per-Class Threshold

- 모델 출력 클래스 중 과검이 많은 하나 식별
- 해당 클래스 슬라이더만 0.5 → 0.8 올리고 다른 클래스는 0.25 유지
- Overall recall은 유지되면서 특정 클래스 precision 향상 확인

### 6.6. Ensemble

- 정상/이상 샘플 각 30장 준비
- DetectionTool 단독 / AnomalyTool 단독 / Ensemble(And) / Ensemble(Weighted) 4가지 모드로 각각 평가
- 혼동행렬(TP/FP/FN/TN) 비교 — Ensemble(Weighted)이 최소 한 모드 이상에서 Best 나오는지 확인

### 6.7. Anomaly Auto Calibration

- 정상 이미지 30~50장 폴더 지정
- 수동 threshold 값 기록 → Calibrate Threshold 클릭 → 자동값 기록
- 실 테스트 데이터로 오검률/미검률 비교 — 자동값이 수동 대비 오검 5% 이내 범위에서 일관적인지 확인

### 6.8. Augmentation Preset

- 동일 데이터셋 + 동일 에폭으로 각 프리셋 4개 학습
- 테스트셋 mAP 비교
- 조명 변동이 큰 라인: "Factory Lighting"이 Default보다 +3~10% mAP 향상 기대

---

## 7. 앞으로의 개선 방향

현재 개선 사항으로 커버되지 않는 영역 중 향후 고려할 만한 항목:

| 항목 | 우선순위 | 난이도 | 기대 효과 |
|---|---|---|---|
| **Active Learning 루프** (모호한 샘플 자동 선별 → 재라벨링) | 중 | 중 | 라벨링 비용 50%↓ |
| **실시간 임계값 드리프트 감지** (온라인 점수 분포 모니터링) | 중 | 중 | 자동 재캘리브레이션 트리거 |
| **모델 A/B 테스팅 툴** | 낮 | 낮 | 모델 업데이트 시 성능 검증 자동화 |
| **Knowledge Distillation** (큰 모델 → 작은 모델) | 낮 | 높 | 작은 모델이 큰 모델 성능의 95%↑ 달성 |
| **Self-supervised pretraining** (라벨 없는 현장 데이터 활용) | 낮 | 높 | 소량 라벨로 고성능 달성 |
| **EfficientAD UI 확장** (현재 백본 선택 제한적) | 낮 | 낮 | 빠른 이상 탐지 모델 지원 강화 |
| **Segmentation 파이프라인** (픽셀 단위 결함 마스크) | 중 | 중 | 결함 면적 계측 가능 |
| **OpenVINO EP** (Intel NPU 활용) | 낮 | 중 | 저전력 엣지 추론 |

---

**문의 / 이슈**: 브랜치 `feat/handeye-calibration-and-robot-protocol` — 커밋 히스토리 참조.
