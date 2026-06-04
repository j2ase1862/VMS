# VMS TensorRT GPU 가속 설정 가이드

**버전**: 1.0
**작성일**: 2026-04-20
**대상**: VMS VisionSetup 운영자 / 배포 담당

---

## 목차

1. [개요](#1-개요)
2. [현재 패키지 상태](#2-현재-패키지-상태)
3. [환경 설치 (필수)](#3-환경-설치-필수)
4. [VMS 설정 파일 작성](#4-vms-설정-파일-작성)
5. [동작 검증](#5-동작-검증)
6. [성능 비교 기준](#6-성능-비교-기준)
7. [문제 해결](#7-문제-해결)

---

## 1. 개요

VMS의 ONNX 추론(Detection / Anomaly / Classify / SAM)을 NVIDIA GPU + TensorRT로 가속하는 절차입니다. 모든 코드 변경은 완료되어 있고, **환경 설치 + 설정 파일 한 줄**만 추가하면 작동합니다.

### 가속 단계

| 단계 | 첫 추론 | 캐시 후 | 비고 |
|---|---|---|---|
| **CPU** (기준) | 1× | 1× | 폴백 |
| **CUDA EP** | 2~10× | 동일 | NVIDIA GPU 즉시 활용 |
| **TensorRT FP32** | 빌드 후 +30~50% | 동일 | 첫 빌드 1~5분 |
| **TensorRT FP16** | 빌드 후 +100~200% | 동일 | RTX 클래스 권장 |

---

## 2. 현재 패키지 상태

✅ **이미 적용 완료**:
- `VMS.VisionSetup.csproj`: `Microsoft.ML.OnnxRuntime.Gpu` v1.21.0
- `VMS.DeepLearning.csproj`: `Microsoft.ML.OnnxRuntime.Gpu` v1.21.0

빌드 산출물 (`VMS.VisionSetup/bin/Debug/net8.0-windows7.0/`)에 다음 DLL이 자동 배포됩니다:
- `onnxruntime_providers_cuda.dll`
- `onnxruntime_providers_tensorrt.dll`
- `onnxruntime_providers_shared.dll`

**CUDA/TensorRT가 미설치된 머신**에서도 앱은 정상 실행됩니다 (자동 CPU 폴백). 단, GPU/TRT 가속은 환경 설치 후에만 작동합니다.

---

## 3. 환경 설치 (필수)

### 3.1. 호환 매트릭스 (ORT 1.21.0 기준)

| 컴포넌트 | 권장 버전 | 비고 |
|---|---|---|
| **NVIDIA Driver** | 525 이상 | RTX 30/40 시리즈는 535+ 권장 |
| **CUDA Toolkit** | 12.x (12.4 권장) | 11.x는 ORT 1.18 이하만 |
| **cuDNN** | 9.x | CUDA 12.x용 |
| **TensorRT** | 10.x (10.4 권장) | CUDA 12.x용 |
| **GPU 아키텍처** | Compute Capability 6.1+ | Pascal 이상 (GTX 1060+) |

### 3.2. 설치 순서

#### Step 1: NVIDIA 드라이버
```
https://www.nvidia.com/drivers
→ GeForce / RTX / Quadro 모델 선택 후 최신 Game Ready or Studio Driver
```

설치 후 PowerShell 확인:
```powershell
nvidia-smi
```
출력에서 `CUDA Version: 12.x` 표시 확인.

#### Step 2: CUDA Toolkit 12.x
```
https://developer.nvidia.com/cuda-12-4-0-download-archive
→ Windows / x86_64 / 11 / exe (local)
```

설치 후 환경변수 자동 설정 확인:
```powershell
echo $env:CUDA_PATH
# → C:\Program Files\NVIDIA GPU Computing Toolkit\CUDA\v12.4
```

검증:
```powershell
nvcc --version
```

#### Step 3: cuDNN 9.x
```
https://developer.nvidia.com/cudnn-downloads
→ Windows / x86_64 / Tarball
```

압축 해제 후 다음 폴더에 복사:
```
[zip]/bin/*.dll       → C:\Program Files\NVIDIA GPU Computing Toolkit\CUDA\v12.4\bin\
[zip]/include/*.h     → C:\Program Files\NVIDIA GPU Computing Toolkit\CUDA\v12.4\include\
[zip]/lib/x64/*.lib   → C:\Program Files\NVIDIA GPU Computing Toolkit\CUDA\v12.4\lib\x64\
```

> NVIDIA 계정 로그인 필요. 무료 가입 후 다운로드.

#### Step 4: TensorRT 10.x
```
https://developer.nvidia.com/tensorrt/download
→ TensorRT 10 / Windows zip / CUDA 12.x
```

압축 해제 위치 권장: `C:\TensorRT-10.4.0`

> **주의**: TensorRT 10.x부터 런타임 DLL(`nvinfer_10.dll` 등)은 `bin/` 폴더에 있고, `lib/` 폴더에는 링크용 `.lib` 파일만 있습니다. PATH에는 반드시 `bin`을 등록해야 합니다.

환경변수 추가 (시스템 환경변수 → Path):
```
C:\TensorRT-10.4.0\bin
```

또는 PowerShell 영구 적용 (관리자 권한 PowerShell 필요):
```powershell
[Environment]::SetEnvironmentVariable("Path",
  [Environment]::GetEnvironmentVariable("Path","Machine") + ";C:\TensorRT-10.4.0\bin",
  [EnvironmentVariableTarget]::Machine)
```

설치 검증:
```powershell
ls "C:\TensorRT-10.4.0\bin\nvinfer*.dll"
```

#### Step 5: 시스템 재부팅
PATH 갱신 적용 위해 **반드시 재부팅**합니다.

### 3.3. 설치 검증 (한 번에)

```powershell
nvidia-smi                                    # 드라이버 + GPU 인식
nvcc --version                                # CUDA 12.x
ls $env:CUDA_PATH\bin\cudnn64*.dll            # cuDNN
ls "C:\TensorRT-10.4.0\bin\nvinfer*.dll"      # TensorRT
```

위 4개 모두 출력이 나와야 정상.

---

## 4. VMS 설정 파일 작성

VMS는 시작 시 다음 경로의 JSON에서 EP 설정을 읽습니다:

```
%LocalAppData%\BODA VISION AI\system_config.json
```

PowerShell로 경로 확인:
```powershell
echo "$env:LOCALAPPDATA\BODA VISION AI\system_config.json"
```

### 4.1. 신규 파일 생성

해당 폴더가 없으면 생성:
```powershell
$dir = "$env:LOCALAPPDATA\BODA VISION AI"
if (!(Test-Path $dir)) { New-Item -ItemType Directory -Path $dir }
```

`system_config.json` 작성 (TensorRT FP16 활성화):

```json
{
  "onnxExecutionProvider": "TensorRT",
  "tensorRTCachePath": "C:\\VMS\\trt_cache",
  "tensorRTFp16": true
}
```

### 4.2. 기존 파일에 추가

`system_config.json`이 이미 있으면 (RobotIp, WebServerUrl 등이 들어 있을 것) 다음 3개 키만 **추가**합니다 (다른 키는 손대지 말 것):

```json
{
  "robotIpAddress": "192.168.0.200",
  "robotPort": 30003,
  ...

  "onnxExecutionProvider": "TensorRT",
  "tensorRTCachePath": "C:\\VMS\\trt_cache",
  "tensorRTFp16": true
}
```

### 4.3. EP 옵션 종류

`onnxExecutionProvider` 가능 값:

| 값 | 동작 |
|---|---|
| `"Auto"` | CUDA → DirectML → CPU 순서로 가능한 첫 번째 EP 사용 |
| `"Cpu"` | CPU 강제 (디버깅용) |
| `"Cuda"` | CUDA만 사용 (TensorRT 빌드 시간 절약) |
| `"DirectML"` | DirectX 12 GPU (NVIDIA가 아닌 GPU도 지원) |
| `"TensorRT"` | TensorRT 우선, 실패 시 CUDA → CPU 폴백 ← **권장** |

### 4.4. TensorRT 캐시 폴더 준비

```powershell
New-Item -ItemType Directory -Path "C:\VMS\trt_cache" -Force
```

이 폴더에 모델별 `.engine` 파일이 캐시됩니다. **첫 추론은 1~5분 빌드 시간**이 걸리지만, 두 번째부터는 즉시 로드됩니다.

> 모델을 변경하거나 InputSize/Tile Size를 바꾸면 새 엔진이 빌드됩니다 (자동 감지).

---

## 5. 동작 검증

### 5.1. VMS 실행 후 확인 포인트

1. VMS 실행 → 레시피 로드 → DetectionTool 실행
2. 결과 메시지에서 다음 형태 확인:
   ```
   3개 객체 검출 (SAHI 640px/20%, EP: TensorRT)
   ```
3. `EP: TensorRT` 가 표시되면 성공

### 5.2. 첫 실행 (엔진 빌드)

- 검출 결과가 나오기까지 30초~5분 정도 걸립니다 (모델 크기에 따름)
- 진행 중 디스크 활동이 있고 `C:\VMS\trt_cache\`에 파일이 생성됩니다
- Visual Studio Output 또는 Debug.WriteLine 콘솔에 다음과 같은 ORT 로그가 보일 수 있음:
  ```
  [TRT] Building engine...
  [TRT] Engine built successfully, cached to ...
  ```

### 5.3. 두 번째 실행

- 즉시 추론 시작 (ms 단위)
- `EP: TensorRT` 유지

### 5.4. 폴백 시나리오

`EP: CUDA` 표시 → TensorRT 초기화 실패, CUDA로 폴백
`EP: CPU` 표시 → GPU 인식 실패, CPU로 폴백

이 경우 [7. 문제 해결](#7-문제-해결) 참조.

---

## 6. 성능 비교 기준

다음 워크로드로 Before/After 측정 권장:

### 측정 시나리오

```
DetectionTool:
  - 모델: YOLOv8n (3.2M params, 12MB ONNX)
  - InputSize: 640
  - 입력 영상: 1920×1200 (산업용 카메라 표준)
  - 100회 반복 평균
```

### 기대 성능 (RTX 3060 기준)

| EP | FPS | Latency | 비고 |
|---|---|---|---|
| CPU (i7-12700) | 8 FPS | 125 ms | 기준선 |
| CUDA | 80 FPS | 12 ms | 10× |
| TensorRT FP32 | 110 FPS | 9 ms | 13.7× |
| TensorRT FP16 | 180 FPS | 5.5 ms | 22.5× |

### 측정 코드 예시 (PowerShell)

```powershell
# 검출 실행 100회 후 결과 메시지의 처리 시간 평균 계산
# (VMS 시퀀스 에디터의 Pipeline ExecutionTime 활용)
```

자세한 벤치마크는 [VMS_DeepLearning_Detection_Improvements.md](VMS_DeepLearning_Detection_Improvements.md) 6.2 참조.

---

## 7. 문제 해결

### 7.1. `EP: CPU`로 표시됨 (GPU 인식 실패)

| 원인 | 해결 |
|---|---|
| NVIDIA 드라이버 미설치 또는 구버전 | `nvidia-smi` 실행 후 재설치 |
| CUDA 버전 불일치 (예: CUDA 11 + ORT GPU 1.21) | CUDA 12.x 재설치 |
| `onnxruntime_providers_cuda.dll` 미배포 | `dotnet build` 다시 실행 |
| 32-bit 프로세스 | `csproj`의 `<PlatformTarget>x64</PlatformTarget>` 확인 |

### 7.2. `EP: CUDA`로 표시됨 (TensorRT 초기화 실패)

| 원인 | 해결 |
|---|---|
| TensorRT 미설치 | [3.2 Step 4](#step-4-tensorrt-10x) 진행 |
| `nvinfer_10.dll` PATH 누락 | 시스템 PATH에 `C:\TensorRT-10.4.0\bin` 추가 후 재부팅 |
| TensorRT 버전 불일치 (TRT 8.x ↔ ORT 1.21) | TensorRT 10.x 재설치 |

### 7.3. 첫 추론에서 `OutOfMemoryError`

| 원인 | 해결 |
|---|---|
| GPU VRAM 부족 (FP16 빌드 메모리 일시적으로 큼) | `tensorRTFp16: false` 로 변경하여 FP32 빌드 |
| 다른 프로세스가 VRAM 점유 | 작업관리자에서 GPU 메모리 사용량 확인 |

### 7.4. 매번 `Building engine...` 메시지 (캐시 미적용)

| 원인 | 해결 |
|---|---|
| `tensorRTCachePath` 폴더 권한 부족 | 폴더 권한 확인, 관리자 권한 실행 |
| 경로에 한글/특수문자 | 영문 경로로 변경 (`C:\VMS\trt_cache` 권장) |
| 모델/설정 변경 | 정상 (모델·InputSize 변경 시 자동 재빌드) |

### 7.5. 검출 결과가 CPU와 다름

TensorRT FP16은 미세한 수치 오차가 있어 결과가 약간 달라질 수 있습니다.
- 검출 박스 좌표 ±1 픽셀 차이: 정상
- Confidence 값 ±0.01 차이: 정상
- 검출 개수가 크게 달라짐: FP32로 변경 (`tensorRTFp16: false`)하여 비교

### 7.6. 모델 변경 후 첫 실행이 또 느림

`.engine` 캐시는 모델 해시 + InputSize 조합 단위로 생성됩니다. 새 모델을 처음 추론할 때마다 빌드 시간이 발생하는 것은 정상입니다. 다음 실행부터는 즉시 로드됩니다.

---

## 부록 A: 빠른 시작 체크리스트

```
□ NVIDIA 드라이버 535+ 설치
□ CUDA Toolkit 12.4 설치, 환경변수 CUDA_PATH 확인
□ cuDNN 9.x DLL/lib/include 복사
□ TensorRT 10.4 압축 해제, PATH 등록
□ 재부팅
□ nvidia-smi / nvcc 검증
□ %LocalAppData%\BODA VISION AI\system_config.json 작성
□ trt_cache 폴더 생성
□ VMS 실행 → DetectionTool 결과에 (EP: TensorRT) 확인
□ 첫 빌드 후 두 번째 실행이 즉시 시작되는지 확인
```

---

## 부록 B: 환경 다운그레이드 (CPU로 복구)

GPU 환경에서 문제가 생기면 즉시 CPU로 복귀할 수 있습니다.

방법 1: 설정 파일에서 EP만 변경 (가장 빠름)
```json
"onnxExecutionProvider": "Cpu"
```

방법 2: NuGet 패키지를 다시 CPU 버전으로 교체
```xml
<!-- VMS.VisionSetup.csproj, VMS.DeepLearning.csproj -->
<PackageReference Include="Microsoft.ML.OnnxRuntime" Version="1.21.0" />
```

방법 1만으로 충분합니다 (GPU 패키지는 CUDA 미설치 머신에서도 자동 CPU 폴백되므로).

---

**관련 문서**:
- [VMS_DeepLearning_Detection_Improvements.md](VMS_DeepLearning_Detection_Improvements.md) — 전체 개선 사항
- [VMS_Labeling_Training_Manual.md](VMS_Labeling_Training_Manual.md) — 학습 매뉴얼
