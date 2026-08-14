# VMS VisionSetup — 신규 도구 검증 가이드

**문서 버전**: v1.1 (2026-08-14 — VMS v1.5.12 기준 검증·정정)

이 문서는 Cognex VisionPro/ViDi 대체 작업으로 추가된 도구들의 동작을 실제 이미지로 검증하기 위한 단계별 절차를 정리한다. 각 시나리오는 `docs/sample-recipes/` 의 JSON 레시피와 함께 사용한다.

## 0. 공통 준비

1. `dotnet build VMS.sln` 으로 솔루션 빌드.
2. `dotnet run --project VMS` 실행 후 메인 런처에서 **VisionSetup** 진입.
3. 검증용 이미지/포인트 클라우드를 미리 준비 (각 시나리오에 명시).

### 샘플 레시피 가져오기

- VisionSetup 메뉴 → **Recipe → Recipe Manager** 창의 **Import** 버튼에서 `docs/sample-recipes/*.json` 선택.
- Import 시 새 GUID가 부여되며 `%LocalAppData%\BODA VISION AI\Recipes\` 로 복사된다.
- ShapeMatchTool은 템플릿 PNG가 포함되지 않은 샘플이므로 UI에서 **Train Template** 으로 학습 후 저장해야 실제 매칭이 동작한다.

---

## 1. ShapeMatchTool (PatMax-like)

대응 Cognex 도구: **CogPMAlignTool** (PatMax/PatQuick).

### 1.1 단일 인스턴스

| 단계 | 동작 | 기대 결과 |
|---|---|---|
| 1 | 학습 이미지 로드 → Tool 추가 → **Training ROI** 드래그 (앵커 패턴 영역) | ROI에 노란 사각형 표시 |
| 2 | **Train Template** 클릭 | "템플릿 학습 완료" 토스트, 템플릿 미리보기 갱신 |
| 3 | 검색 이미지 로드 → **Search Region** 활성화, 영역 드래그 | 노란 점선 사각형 |
| 4 | Run | Score ≥ ScoreThreshold(0.7) 인 매칭 1건, 결과 오버레이 표시 |

### 1.2 다중 인스턴스 (PR #12)

- `MaxInstances=5`, `NmsDistanceFactor=0.5` 로 설정.
- 동일 패턴이 여러 개 있는 이미지에서 Run → NMS로 중복 제거된 최대 5개 결과 반환.
- 결과 패널에 Instance 0..N 별 좌표/각도/스코어 출력.

### 1.3 성능 검증

- 예시 조건 NumPyramidLevels=3, AngleStep=2° 는 기본값(NumPyramidLevels=2, AngleStep=5°)과 다르다. 재현하려면 두 값을 먼저 변경할 것.
- 위 조건 기준 1280×1024 이미지에서 **1.5–2.5초** 이내 (참고치 — 환경별 상이).
- 회전 캐시(PR #8e) 효과로 동일 템플릿 반복 Run 시 첫 회 외 추가 단축.

### 1.4 전문가 모드 (PR #12b)

- 우측 패널 **Expert Mode** 체크 → Expert 전용 항목인 **Search Range** 와 **Speed** 노출.
- 체크 해제(OFF) 상태에서도 **Acceptance** / **Multi-Instance** Expander 는 항상 표시됨 — Max Instances · NMS Distance Factor 포함.

---

## 2. ColorExtractTool (HSV In-Range)

대응 Cognex 도구: **CogColorExtractTool**.

### 2.1 기본 워크플로

| 단계 | 동작 | 기대 결과 |
|---|---|---|
| 1 | 컬러 이미지 로드 → Tool 추가 → Training ROI 영역 드래그 | ROI 노란 사각형 |
| 2 | **Train Selected Model** | ROI 내부 HSV 평균 ± 2.5σ 로 Model 자동 채움 |
| 3 | (선택) Search Region 활성화 → 검사 영역 지정 | 시안 점선 표시 |
| 4 | Run | 추출된 마스크 오버레이, "Pixel Count" 인라인 표시 |

### 2.2 다중 모델 (PR #10e/f)

- **+ Add** 버튼으로 모델 추가 → 각 모델별 학습 가능.
- ModelItem 우측에 학습 색상 미니 패치 + 마스크 픽셀 수 인라인 표시.
- 각 모델 IsEnabled 체크박스로 ON/OFF, Run 시 활성 모델 OR 합집합.

### 2.3 전문가 모드 (PR #10g)

- Expert Mode OFF → Training ROI/Search Region/모델 목록만 노출.
- Expert Mode ON → H/S/V min·max 슬라이더 노출.

### 2.4 샘플 레시피

`sample_color_label_inspection.json` 임포트 → 컬러 라벨 이미지 로드 → Run → BlobTool 카운트가 1이면 Pass.

---

## 3. ColorMatchTool (Lab ΔE76)

대응 Cognex 도구: **CogColorMatchTool**.

### 3.1 단일 픽셀 학습 (PR #11c)

| 단계 | 동작 | 기대 결과 |
|---|---|---|
| 1 | Tool 추가 → **Pick from Image** 클릭 | 캔버스 모드가 PickPoint로 전환, 십자 커서 |
| 2 | 이미지 상의 기준 색상 위치 클릭 | 해당 픽셀 BGR → Lab 변환 후 Selected Model 의 MeanL/A/B 갱신 |
| 3 | ColorTolerance(ΔE) 조정 | 일반 산업 검사 8–15 권장 |
| 4 | Run | ΔE ≤ Tolerance 인 픽셀이 마스크로, Pixel Count 결과 출력 |

### 3.2 ROI 평균 학습

- Training ROI 드래그 후 **Train Selected Model** → ROI 평균 Lab가 SelectedModel에 반영.

### 3.3 다중 모델

- 같은 컬러의 미세 변형(조명/질감 차) 을 모델로 추가하면 Run 시 OR 합산.

---

## 4. ImageRectifyTool + Calibration (PR #1–#4)

대응 Cognex 도구: **CogCalibCheckerboardTool / CogCalibNPointToNPointTool + CogUndistortImageTool**.

### 4.1 캘리브레이션 데이터 생성

1. VisionSetup 메뉴 → **Camera** 메뉴 하위의 **Calibration Manager** 창 열기.
2. **Image Source** 선택 (File / Camera / VMS Shared Frame).
3. **Calibration Mode** = `Checkerboard` 선택, 패턴 크기(가로 x 세로) 입력, 패턴 1장당 mm 입력.
4. 여러 각도에서 캡쳐/로드 → **Run Calibration** 클릭.
5. 결과 패널에 RMS reprojection error 출력 → 1.0px 이하 권장.
6. **Apply to Current Recipe** → Recipe.Calibration 슬롯에 저장.

### 4.2 ImageRectifyTool 사용

- 파이프라인 첫 도구로 ImageRectifyTool 배치.
- Parameters: `Undistort=true`, `ApplyHomography=true` (필요 시).
- Run → 왜곡 보정 + 평면 정합된 이미지가 후속 도구로 전달.

### 4.3 N-Point 모드 + 픽 (PR #3)

- Calibration Mode = `N-Point (planar)` 선택 → ImageCanvas에서 점 클릭 → World mm 좌표 입력.
- 4점 이상 필요. **Run Calibration** 시 Homography 행렬 계산.

### 4.4 mm 변환 검증 (PR #5)

- Caliper / CircleFit / Geometry / Geometry3D 결과 패널에 픽셀과 함께 **mm** 값 동시 표시.
- Calibration이 적용된 레시피에서만 mm 출력 (없으면 픽셀만).

---

## 5. PointCloud 도구 (PR #13 / #13b / #13c)

대응 Cognex 도구: VisionPro 3D Toolkit (Filter / Align / Cluster).

### 5.1 PointCloudFilterTool

| 단계 | 동작 | 기대 결과 |
|---|---|---|
| 1 | `.vpc` 포인트 클라우드 로드 (Acquire 또는 File) | 3D 뷰에 포인트 표시 |
| 2 | Tool 추가, `EnableVoxelGrid=true`, `VoxelSize=2.0mm` | 다운샘플링된 포인트 수 출력 |
| 3 | `EnableSor=true`, `SorK=20`, `SorStddev=1.0` | 이상치 제거된 포인트 수 출력 |

검증: 노이즈가 있는 클라우드에서 SOR 후 잔여 노이즈 시각적으로 감소.

### 5.2 PointCloudRegistrationTool

- **ReferencePath** 에 정답 클라우드 `.vpc` 경로 지정 (`.stl` CAD 파일도 지원 — 표면을 자동 샘플링).
- `MaxIterations=50`, `Tolerance=0.01` (기본값). Tolerance 하한은 `0.0001` 이며 `1e-5` 처럼 더 작은 값은 하한으로 잘린다.
- Run → ICP 수렴, 변환 행렬 출력, `ApplyTransformToSource=true` 면 정합된 클라우드가 VisionService.CurrentPointCloud 에 반영.

### 5.3 PointCloudClusterTool

- `Tolerance=5mm`, `MinPoints=100`, `MaxPoints=100000`.
- Run → 검출된 클러스터 수 + 클러스터별 포인트 수.
- `OutputMode=LargestOnly` 면 가장 큰 클러스터를 CurrentPointCloud 로 출력 (선택지: LargestOnly / AllMerged / KeepOriginal).

---

## 6. Deep Learning 분할 도구

### 6.1 SegmentationTool (Semantic, PR #7)

- ONNX 모델 경로 지정 (1×3×H×W 입력, 1×C×H×W 출력).
- `InputSize=512`, `UseImageNetNormalization=true` (학습 정규화에 맞춤).
- Run → 클래스별 argmax 마스크 오버레이.

### 6.2 YoloSegTool (Instance, PR #14)

- YOLOv8-seg ONNX 모델 사용.
- `InputSize=640`, `ConfidenceThreshold=0.25`, `IouThreshold=0.45`, `MaskThreshold=0.5`.
- Run → NMS 후 인스턴스별 마스크 + 박스 오버레이.

검증: 동일 이미지에 객체가 N개 있을 때 결과 패널의 Instance 수가 일치하는지 확인.

---

## 7. 회귀 테스트 — ToolSerializer Round-Trip

```bash
dotnet test VMS.VisionSetup.Tests
```

- 모든 등록 ToolType에 대해 Theory 테스트가 자동 실행됨.
- 새 도구 추가 시 `ToolSerializer` switch 누락이 있으면 `DeserializeTool` 이 null 을 반환 → 테스트 실패.
- 기대: 모든 케이스 Pass.

---

## 8. 샘플 레시피 — End-to-End

### 8.1 `sample_color_label_inspection.json`

| 검증 항목 | 방법 |
|---|---|
| Import 성공 | Recipe Manager 에 항목 추가 |
| Pipeline 구성 | Step 1 → ColorExtract → Blob → Result 순으로 표시 |
| Run | 오렌지 로고 1개 인식 시 Blob Pass → Result Pass |
| Fail Case | 로고가 가려진 이미지 → Blob 카운트 0 → Result Fail |

### 8.2 `sample_shape_match_measurement.json`

| 검증 항목 | 방법 |
|---|---|
| Import 성공 | Recipe Manager 에 항목 추가 |
| ShapeMatch 학습 필요 | 앵커 영역 Training ROI 학습 후 저장 (TemplatePngBase64 채워짐) |
| Pipeline | ShapeMatch → Caliper(Coordinates 연결) → Result |
| 좌표 전달 | ShapeMatch 결과 좌표가 Caliper Fixture 로 이동 |
| Run | 100±10px 폭 검출 시 Pass |

---

## 9. 일반 트러블슈팅

| 증상 | 원인 / 조치 |
|---|---|
| Recipe Load 후 ShapeMatchTool 사라짐 | 구버전 직렬화 누락 → PR #9 이후로 해결, 최신 빌드인지 확인 |
| Search Region 좌표가 0 으로 초기화 | UseSearchRegion 토글 직후 영역 드래그가 누락 — 다시 드래그 후 저장 |
| ColorExtract 마스크가 비어있음 | HSV 범위 너무 좁음 — Training ROI 재학습 또는 H/S/V min 값 완화 |
| ColorMatch 모두 매칭됨 | ColorTolerance(ΔE) 너무 큼 — 5~10 으로 감소 |
| mm 결과가 안 나옴 | Recipe.Calibration 미설정 — Calibration Manager에서 Apply 필요 |
| ONNX 도구 첫 Run 5초+ | 모델 워밍업 — Recipe Load 시 자동 PrefetchDeepLearningModels 가 동작하므로 두 번째 Run부터 정상 |

