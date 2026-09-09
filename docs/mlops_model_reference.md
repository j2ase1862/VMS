# 모델 참조 (`model://`) — MLOps 레지스트리의 모델을 라인에서 쓰기

문서 버전: v0.1 (2026-09-09) · 관련 리포: BODA.VMS.MLOps

## 무엇이 달라지는가

지금까지 검사 도구는 모델 파일의 절대 경로를 레시피에 담았습니다.

```json
"ModelPath": "D:\\models\\scratch-v7.onnx"
```

모델을 바꾸려면 사람이 파일을 라인 PC 마다 복사하고 경로를 다시 입력해야 했고,
어느 PC 가 어느 버전으로 검사하는지도 알 수 없었습니다.

이제 참조로 담을 수 있습니다.

```json
"ModelPath": "model://3f2c1a9e-0000-4000-8000-000000000001@production"
```

레시피는 "이 모델의 운영 버전" 이라고만 말하고, 실제 파일은 라인 PC 가 레지스트리에서 받아
로컬 캐시에 둡니다. 승격·롤백은 MLOps 쪽에서 끝나고, 라인 PC 는 다음 레시피 로드 때 따라옵니다.

**기존 절대 경로는 그대로 동작합니다.** 참조인지 아닌지로만 갈라집니다.

## 참조 형식

| 형태 | 뜻 | 언제 쓰나 |
|---|---|---|
| `model://{modelId}@production` | 지금 운영 단계인 버전 | 보통 이것 |
| `model://{modelId}@staging` | 테스트 라인 | 검증 중인 라인 |
| `model://{modelId}@7` | 7번 버전 고정 | 검증이 끝나 못 박을 때 |

단계 참조는 가리키는 파일이 바뀝니다. 버전 참조는 언제 열어도 같은 파일입니다.

## 설정

`%LocalAppData%\BODA VISION AI\system_config.json` 에 두 값을 넣습니다.

```json
{
  "MlopsServerUrl": "https://mlops.example.local:5310",
  "MlopsLineToken": "ln_...",
  "ModelCacheMaxGB": 20
}
```

토큰은 MLOps 관리자가 라인마다 발급합니다 (`POST /api/line-clients`).
발급 응답에 한 번만 나오고 서버에는 해시만 남으니, 잃어버리면 재발급받아야 합니다.

이 토큰으로 할 수 있는 일은 셋뿐입니다 — 모델 목록·참조 해석, 아티팩트 내려받기, NG 사진 올리기.
학습 큐나 모델 등록·승격은 열리지 않습니다.

설정이 비어 있으면 참조를 풀지 못하고, 이미 캐시에 있는 모델만 씁니다.

## 고르는 방법

VisionSetup 의 도구 설정에서 모델 경로 칸 옆 **[레지스트리…]** 버튼을 누릅니다.
목록에서 모델을 고르고 "지금 운영 중인 버전" 또는 "특정 버전 고정" 을 정하면
참조 문자열이 들어갑니다. 저장될 값을 창 아래에서 미리 볼 수 있습니다.

목록은 그 도구의 작업 유형으로 걸러 보여 줍니다. 검출 도구 설정에 분류 모델이 뜨지 않습니다.

## 언제 무엇이 일어나는가

| 시점 | 하는 일 |
|---|---|
| 앱 시작 | 해석기를 만들고 캐시를 상한까지 정리 (쓰는 파일은 보호) |
| 레시피 로드 | 참조를 풀어 없는 파일을 내려받고, 엔진을 예열 |
| 검사 중 | 캐시에서 꺼내 쓰기만 함 — **네트워크를 쓰지 않음** |

검사 경로가 네트워크를 쓰지 않는 것이 핵심입니다. 검사 한 장 도는 사이에 HTTP 를 기다릴 수 없습니다.
그래서 레시피를 열 때 미리 받아 둡니다.

레지스트리에 닿지 못해도 캐시에 이미 있으면 그 파일로 이어 갑니다. 서버가 죽어도 검사는 돕니다.
캐시에도 없으면 그 도구는 "모델 참조 … 를 아직 내려받지 못했습니다" 로 실패합니다.

## 캐시

`%LocalAppData%\BODA VISION AI\models\{앞두글자}\{sha256}.onnx`

파일 이름이 곧 내용의 SHA-256 입니다. 같은 모델을 두 번 받지 않고, 롤백해도 이전 파일이 남아 있어
네트워크 없이 되돌릴 수 있습니다.

받을 때는 임시 파일에 쓰고 해시를 확인한 뒤에만 옮깁니다. 중간에 전원이 나가도 반쯤 받은 파일이
정상 이름으로 남지 않습니다 — 그 파일을 ONNX 로 열면 무슨 일이 날지 모릅니다.

`ModelCacheMaxGB` 를 넘으면 앱 시작 때 오래 안 쓴 파일부터 지웁니다. 지금 레시피가 쓰는 파일은 지우지 않습니다.

## 관련 코드

| 무엇 | 어디 |
|---|---|
| 참조 형식 파서 | `VMS.Core.Contracts/DeepLearning/ModelReference.cs` |
| 로컬 캐시 | `VMS.Core/Services/ModelArtifactCache.cs` |
| 레지스트리 클라이언트 | `VMS.Core/Services/ModelRegistryClient.cs` |
| 참조 → 경로 | `VMS.Core/Services/ModelReferenceResolver.cs` |
| 엔진 적재 연결 | `VMS.VisionSetup/VisionTools/DeepLearning/OnnxEngineCache.cs` |
| 레시피 로드 시 준비 | `VMS.VisionSetup/Services/RecipeService.cs` |
| 고르는 창 | `VMS.VisionSetup/Views/Dialogs/ModelRegistryPickerWindow.xaml` |

## 시험

```bash
dotnet test VMS.Core.Tests --filter ModelReference
```

살아 있는 MLOps 서버에 붙는 통합 시험은 환경 변수가 있을 때만 돕니다.

```bash
set MLOPS_URL=http://localhost:5310
set MLOPS_LINE_TOKEN=ln_...
set MLOPS_MODEL_ID=<운영 단계로 승격된 모델 ID>
dotnet test VMS.Core.Tests --filter MlopsRegistryIntegrationTests
```
