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

같은 모델을 둘이 동시에 받는 일이 있습니다 — VMS 메인과 VisionSetup 이 같은 레시피를 같은 시점에 열면
둘 다 프리페치합니다. 늦은 쪽은 자기 임시 파일을 버리고 먼저 옮겨진 파일을 그대로 씁니다. 해시를 다시 재지 않는
근거는 "캐시의 정상 이름 파일은 해시까지 맞춘 것만 옮겨진다" 는 규칙이므로, **캐시 폴더에 직접 쓰는 코드를 만들지
마십시오** — 그 순간 늦은 쪽이 반쪽 파일을 받게 됩니다.

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

## 받는 쪽 — AI 학습 도구의 [웹 데이터셋 내려받기]

모델만 오가는 것이 아닙니다. 웹에서 라벨링한 데이터셋도 학습 도구로 내려옵니다.
학습 도구의 Export 구역에서 **[웹 데이터셋 내려받기]** 를 누릅니다.

받는 것은 **내보내기 폴더**입니다 — 학습 스크립트가 그대로 읽는 형식(yolo·imagefolder·mvtec·
ppocr·coco)으로 서버가 굽습니다. 라벨을 학습 도구의 데이터셋 형식으로 되돌리지 **않습니다**.
두 형식을 오가며 옮겨 적는 길을 열면 어느 쪽이 정본인지 흐려지고, 조용히 어긋난 라벨이
다음 학습에 들어갑니다. 웹에서 라벨링한 것은 웹이 정본이고, 학습 도구는 그것으로 학습만 합니다.
그래서 이 창이 끝내 놓는 것은 Export 버튼들과 같은 것 하나입니다 — **학습 데이터셋 경로**.

| 무엇 | 어떻게 |
|---|---|
| 판(버전) | 데이터셋은 계속 바뀌므로 학습은 "그때 그 판" 을 받아야 재현됩니다. 이미 뜬 판을 고르거나 지금 상태로 새로 뜹니다. |
| 미라벨 포함 | 기본은 라벨이 붙은 것만 담습니다. 검출에서 배경 샘플이 필요할 때만 켭니다 — 분류·이상탐지에서 켜면 라벨 없는 이미지가 학습에 섞입니다. |
| 폴더 이름 | `{판 이름}-{매니페스트 해시 앞 8자리}`. 어느 학습이 어느 판으로 돌았는지 폴더 이름만 봐도 압니다. |
| 다시 받으면 | 대상 폴더를 비우고 새로 풉니다. 이전 판의 라벨 파일이 남아 섞이면 지운 라벨이 학습에 되살아납니다. |
| 다 받았는지 | 받으면서 SHA-256 을 계산해 서버가 준 값과 대조하고, `Content-Length` 로 길이도 봅니다. 끊긴 연결로 반쯤 받은 zip 을 풀면 이미지 몇 장이 빠진 채 학습이 돌고, 그건 아무 데도 남지 않습니다. |

**`X-Content-Sha256` 은 내보내기 zip 파일의 SHA-256 입니다** (MLOps `DatasetVersion.ExportSha256`).
한동안 서버가 여기에 **매니페스트 해시**를 실었습니다 — 그것은 이미지·라벨·분할 목록 JSON 의 해시,
즉 내용의 신원이라 zip 바이트와 무관하고, 같은 내용이라도 zip 은 구울 때마다 바이트가 달라집니다.
그래서 이 헤더로 파일을 검증하던 워커는 스냅샷으로 만든 판을 받을 때마다 실패했습니다
(MLOps main `cd5ed15` 에서 고쳤습니다). 옛 서버에 붙으면 헤더 값이 `manifestHash` 와 같게 오므로,
그때는 대조를 건너뛰고 길이만 봅니다.

## 세그멘테이션 — RF-DETR 규약

세그멘테이션 모델은 `rfdetrseg` 규약입니다. 검출에서 D-FINE 을 고른 것과 같은 이유로,
Ultralytics(AGPL-3.0) 없이 상용 배포를 하기 위해서입니다. 학습은 MLOps 의
`scripts/train_rfdetr_seg.py`(원본은 `VMS.DeepLearning/scripts/`)가 하고,
라인에서는 `RfdetrSegTool` 이 읽습니다.

| 이름 | 모양 | 뜻 |
|---|---|---|
| `input` | `[N,3,H,W]` float32 | RGB, ImageNet 정규화. **레터박스가 아니라 늘려 맞춘 리사이즈**입니다. H·W 는 `patch_size × num_windows` 의 배수여야 합니다 (nano 12, preview 56). |
| `dets` | `[N,Q,4]` float32 | 정규화 cxcywh. 늘려 맞춘 리사이즈라 원본 크기를 그대로 곱하면 픽셀 좌표입니다. |
| `labels` | `[N,Q,C]` float32 | 클래스 로짓 — **시그모이드**(소프트맥스 아님). `C = 클래스 수 + 1` 이고 **마지막 열이 배경**입니다. |
| `masks` | `[N,Q,mh,mw]` float32 | 마스크 로짓, 압축 해상도(입력의 1/4). 이중선형으로 키운 뒤 **0 에서** 자릅니다. |

**배경 열을 빼세요.** 안 빼면 배경이 최고 점수인 질의가 물체로 나옵니다.
학습 스크립트가 ONNX 메타데이터에 `background_class_id` 를 새기고, 없으면 마지막 열로 봅니다.

**질의마다 최고 클래스 하나만 고르지 마세요.** 한 질의가 두 클래스에서 문턱을 넘을 때 하나가
조용히 사라집니다. (질의 × 클래스) 쌍을 점수로 줄 세우고 상위 몇 개만 남깁니다.
DETR 계열은 집합 예측이라 NMS 는 쓰지 않습니다.

## 라인 NG 이미지 수집 — 학습 데이터가 라인에서 올라온다

모델이 라인에 내려가는 길의 반대 방향입니다. 라인 PC 가 불량으로 판정한 사진을 원본 해상도 그대로
MLOps 데이터 풀(`POST /api/images/line-ng`)에 올리고, 웹에서 그것을 라벨링해 다음 모델을 학습합니다.
Web 생산 이력으로 가는 NG 썸네일 전송과는 별개의 길입니다 — 그쪽은 사람이 보는 용도라 썸네일이고,
이쪽은 학습 재료라 원본이어야 합니다.

| 항목 | 값 |
|---|---|
| 켜는 곳 | VMS 메인 → 이미지 저장 설정 창 → "MLOps 학습 데이터 수집" 카드 → [NG 이미지 MLOps 전송] |
| 양품 샘플 | 같은 카드의 "양품 샘플 비율" — N 장에 1 장 (기본 200, 0 = 양품 안 보냄). 첫 장은 바로 보내고 그 뒤로 N 장마다 |
| 전제 | `system_config.json` 의 `mlopsServerUrl` · `mlopsLineToken` (설정 마법사 2단계 고급 설정). 없으면 토글을 켜도 나가지 않고 카드가 그 사실을 알려 준다 |
| 형식 | 로컬 저장 포맷 그대로(기본 PNG). 서버가 받지 않는 TIFF 만 PNG 로 바꾼다 |
| 보내는 것 | 파일 + `inspectionId`(결과 업로드와 같은 상관 키 — 생산 이력과 이어 붙이는 열쇠) + `capturedAt`(UTC) + `tags`(`verdict:ng|ok` · `recipe:` · `camera:` · `step:`) |
| 보내지 않는 것 | `lineId` — 라인 토큰이 그 라인에 발급된 것이라 서버가 토큰에서 읽는다. 라인 PC 가 다른 라인 이름을 댈 수 없다 |
| 큐 | `%LocalAppData%\BODA VISION AI\mlops_line_ng_queue` — 디스크 큐라 재시작해도 남고, 2000 장 / 4 GB 를 넘으면 양품 샘플부터 오래된 순으로 버린다 |
| 재시도 | 네트워크 오류·5xx·401·403·408·429 는 지수 백오프(5초 → 최대 5분). 한 번에 한 장만 보내 라인 네트워크를 독점하지 않는다 |
| 거절 | 그 밖의 4xx 와 "200 인데 errors 만 있는 응답"(서버가 파일 하나를 받지 않은 것)은 다시 보내지 않고 `rejected/` 에 사유와 함께 둔다 — 큐 맨 앞에 박혀 뒤를 막지 않게(#444 와 같은 규칙, 최대 200 쌍) |
| 검사 택트 | 영향 없음 — 판정 직후 frozen 클론만 넘기고 인코딩·기록·전송은 전부 뒤에서 |

같은 사이클의 여러 카메라·스텝은 상관 키를 공유하므로 큐 파일 이름은 `{상관 키}|{카메라}|{스텝}|{판정}` 입니다.
서버는 같은 사진(해시)을 다시 받으면 새로 만들지 않고 태그만 합치고 200 을 주므로, 재시도로 두 번 올라가도 데이터가 겹치지 않습니다.

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
| 데이터셋 받기 | `VMS.Core/Services/DatasetRegistryClient.cs` |
| 받는 창 | `VMS.DeepLearning/Views/DatasetDownloadWindow.xaml` |
| 받는 창의 상태 | `VMS.Core/ViewModels/DatasetDownloadViewModel.cs` |
| 학습에 쓴 클래스 되읽기 | `VMS.Core/Services/TrainingExportClasses.cs` |
| 세그멘테이션 런타임 도구 | `VMS.VisionSetup/VisionTools/DeepLearning/RfdetrSegTool.cs` |
| 라인 NG 이미지 송신 | `VMS/Services/ImageUpload/LineNgImageUploader.cs` (큐·샘플링·거절 보관) |
| 송신 토글·샘플 비율 | `VMS.Core/Imaging/ImageSaveOptions.cs` (`mlopsSendNg` · `mlopsOkSampleRate`), 설정 창 `VMS/ViewModels/ImageSaveSettingsViewModel.cs` |
| 판정 → 송신 배선 | `VMS/ViewModels/MainViewModel.cs` (`InspectionCompleted`), 생성은 `VMS/App.xaml.cs` |

## 시험

```bash
dotnet test VMS.Core.Tests --filter ModelReference
dotnet test VMS.Tests --filter LineNgImageUploaderTests     # 라인 NG 송신부 — 가짜 서버로 큐 한 바퀴
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

데이터셋 받기도 같은 JWT 로 확인합니다. 라벨이 붙은 이미지가 한 장은 있어야 판이 떠집니다.

```bash
set MLOPS_DATASET_ID=<웹에서 라벨링한 검출 데이터셋 ID>
```
