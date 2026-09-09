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
| `model://{modelId}@candidate` | 후보 단계의 최신 버전 | 새 학습 결과를 바로 보고 싶을 때 |
| `model://{modelId}@7` | 7번 버전 고정 | 검증이 끝나 못 박을 때 |

단계 참조는 가리키는 파일이 바뀝니다. 버전 참조는 언제 열어도 같은 파일입니다.

레지스트리의 단계는 `candidate` · `staging` · `production` · `retired` 네 가지지만,
레시피가 가리킬 수 있는 것은 앞의 셋뿐입니다. `retired` 는 "이제 쓰지 말라" 는 뜻이라
라인이 그것을 가리키는 것 자체가 사고이므로, 참조를 만들 때 막습니다.

## 설정

설정 마법사(AppSetup) 2단계 › **고급 설정 — Web Client API Key · MLOps 모델 레지스트리** 에서
**MLOps 서버 주소**와 **MLOps 라인 토큰**을 입력하고 저장합니다. 저장되면 `%LocalAppData%\BODA VISION AI\system_config.json` 에
아래 두 값이 camelCase 키로 기록됩니다(손으로 넣을 때는 PascalCase 도 읽힙니다). VMS 메인과 VisionSetup 이 시작할 때
같은 파일을 읽어 참조 해석기를 만듭니다.

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

## 올리는 쪽 — AI 학습 도구의 [레지스트리에 등록]

여기까지가 "받아 쓰는" 이야기였고, 그 앞에 "올리는" 자리가 있습니다.
학습이 끝나면 AI 학습 도구(`VMS.DeepLearning`)의 학습 패널에서 **[레지스트리에 등록]** 을 누릅니다.
지금까지는 ONNX 파일을 사람이 라인 PC 로 복사했고, 그래서 어느 PC 가 어느 모델을 쓰는지 아무도 몰랐습니다.

| 무엇 | 어떻게 |
|---|---|
| 누가 올리나 | **사람의 계정**. BODA.VMS.Web 에 로그인해 받은 JWT 를 MLOps 가 그대로 받습니다 (같은 키·발급자). |
| 왜 라인 토큰이 아닌가 | `ln_` 토큰은 받아 가는 쪽 자격입니다. 등록·승격은 엔지니어의 일이라 서비스 계정에 열지 않습니다. |
| 권한이 모자라면 | 서버가 403 으로 답하고, 창이 "모델 등록은 엔지니어 이상 계정이어야 합니다" 로 옮겨 보여 줍니다. |
| 어디로 들어가나 | **Candidate**. 바로 라인에 나가지 않습니다. 사람이 레지스트리에서 확인하고 승격해야 합니다. |
| 클래스는 | 학습에 쓴 이름을 그대로 보냅니다. 서버가 ONNX 안의 이름과 대조해 어긋나면 거절합니다. |
| 라이선스는 | YOLO 계열은 라이선스 없이는 등록도 Production 승격도 되지 않습니다 (`AGPL-3.0`). |

올릴 파일은 방금 학습한 산출물이 우선이고, 없으면 데이터셋에 남아 있는 마지막 산출물을 씁니다 —
앱을 다시 켠 뒤에도 올릴 수 있어야 하기 때문입니다.

서버 주소는 `system_config.json` 의 `MlopsServerUrl`·`WebServerUrl` 에서 읽습니다.
둘 중 하나라도 없으면 창을 띄우지 않고 무엇이 빠졌는지 말해 줍니다.

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
| 올리는 창 | `VMS.DeepLearning/Views/ModelUploadWindow.xaml` |
| 올리는 창의 상태 | `VMS.Core/ViewModels/ModelUploadViewModel.cs` |
| 작업 유형 이름 대응 | `VMS.Core/Services/RegistryTaskType.cs` |
| 서버 주소 읽기 | `VMS.Core/Services/SystemConfigReader.cs` |

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

올리는 쪽은 사람의 JWT 가 필요해 따로 켭니다. 서버에 진짜 계열과 버전을 남기므로 개발 서버에서만 돌리세요.

```bash
set MLOPS_JWT=<엔지니어 이상 계정의 토큰>
set MLOPS_ONNX=D:\Repo\VMS\VMS.DeepLearning\best.onnx
set MLOPS_ONNX_CLASSES=object,logo
```
