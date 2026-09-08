# VMS.Core.Contracts — MLOps 플랫폼용 규약 패키지

문서 버전: v1.0 (2026-09-08)
관련: `D:\MLOps 개발 문서.md` §3.2 원칙 2·3, Phase 1 §4(업로드 검증), Phase 3 §5(워커 실행)

## 왜 별도 패키지인가

`VMS.Core` 는 `net8.0-windows` + WPF + OpenCvSharp + HelixToolkit + VMS.Camera 에 의존한다. 웹 서버(BODA.VMS.Web, `net8.0`)나
학습 워커가 이것을 통째로 참조하면 안 되므로, 두 쪽이 공유해야 하는 **순수 규약 코드만** `VMS.Core.Contracts`(`net8.0`,
의존성 CommunityToolkit.Mvvm 뿐) 로 분리했다. `VMS.Core` 는 이 프로젝트를 ProjectReference 로 참조하므로 VMS 쪽 코드와
네임스페이스는 바뀌지 않는다.

| 네임스페이스 | 타입 | 용도 |
|---|---|---|
| `VMS.Core.DeepLearning` | `OnnxMetadataReader` | InferenceSession 없이 ONNX metadata_props·그래프 입출력 이름 읽기 |
| `VMS.Core.DeepLearning` | `DetectionModelFormat`, `DetectionModelFormatProbe` | 검출 모델 규약(yolo / dfine) 판별 — 레지스트리 업로드 검증 |
| `VMS.Core.Services` | `TrainingOutputParser`, `TrainingOutputKind` | train_*.py stdout 프로토콜 해석 — 워커 진행률 보고 |
| `VMS.Core.Services` | `TrainingArgumentBuilder` | `TrainingConfig` → 스크립트 인자(화이트리스트) — 워커 실행 |
| `VMS.Core.Models.Annotation` | `TrainingConfig`, `TrainingTarget`, `TrainingStatus`, `TrainingState` | 학습 설정·상태 모델 |

## 패키지 만들기 (VMS 리포, dev PC)

```powershell
.\tools\pack-contracts.ps1                 # → D:\Repo\nuget-local\VMS.Core.Contracts.<버전>.nupkg (+ .snupkg)
.\tools\pack-contracts.ps1 -Suffix dev     # 개발 중 반복 pack: 1.31.0-dev.<타임스탬프>
```

버전은 `Directory.Build.props` 의 `<Version>` 을 그대로 쓴다(릴리즈와 동기화). 규약이 바뀌는 PR 은 VMS 릴리즈(MINOR)와
함께 pack 하고, MLOps 쪽 `PackageReference` 버전을 올린다.

## 소비 측 설정 (BODA.VMS.MLOps · BODA.VMS.Web)

솔루션 루트 `nuget.config`:

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <add key="vms-local" value="D:\Repo\nuget-local" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
```

프로젝트:

```xml
<PackageReference Include="VMS.Core.Contracts" Version="1.31.*" />
```

사용 예:

```csharp
using VMS.Core.DeepLearning;
using VMS.Core.Services;

var format = DetectionModelFormatProbe.Probe(onnxPath);            // 업로드 검증
var meta   = OnnxMetadataReader.Read(onnxPath);                     // names / imgsz / model_format
var kind   = TrainingOutputParser.Apply(line, status);              // 워커 stdout 한 줄 → 상태 갱신
var args   = TrainingArgumentBuilder.Build(config);                 // 워커 프로세스 인자
```

## 규칙

- 규약 변경(프로토콜 태그·인자·ONNX 규약 판별)은 **이 패키지 + 스크립트 5종 + VMS 소비자**를 한 PR 에서 함께 바꾼다.
- 여기에는 Windows·WPF·OpenCV·ONNX Runtime·HTTP 클라이언트를 넣지 않는다. 추론 엔진(`IDetectionEngine`, `DFineOnnxEngine`)은 VisionSetup 에 남는다.
- 사내 NuGet 서버를 두게 되면 `pack-contracts.ps1 -Feed` 만 바꾸면 된다. CI 는 pack 하지 않는다(dev PC 에서 릴리즈와 함께).
