# Mech-Vision 3D 기능 대조 리포트 — VMS 3D 분석 툴 고증

- 작성일: 2026-07-21
- 조사 방법: 웹 조사 워크플로(공식 문서 25개 소스, 주장 추출 후 교차 검증) + 핵심 항목 공식 문서 직접 확인
- 기준 버전: Mech-Vision **2.2.1** (docs.mech-mind.net suite-software-manual latest 기준) / VMS v1.4.13 + PR #213
- 검증 등급: ✅ 교차 검증 통과(3-0/2-0) · 🔎 공식 문서 직접 확인 · ⚠️ 단일 출처 또는 버전별 상이(미확정)

## 요약 (TL;DR)

1. VMS의 3D 전처리(Filter)·클러스터링(Cluster)은 Mech-Vision의 대응 스텝과 **알고리즘·파라미터 구성이 사실상 동일 계열**이다 (VoxelGrid/SOR ↔ Down-Sample/Point Filter, 유클리드 클러스터 ↔ Point Cloud Clustering).
2. 가장 큰 격차는 **3D Coarse Matching(초기 자세 추정)의 부재**다. Mech-Vision은 Coarse(후보 자세) → Fine(정밀 정합) 2단 구조인데 VMS Registration은 Fine(ICP)만 있어 초기 자세 차이가 크면 실패한다.
3. 정합 품질 지표 철학이 다르다: Mech-Vision은 **정규화 신뢰도(Confidence 0~?, Matching Score)** 중심, VMS는 PR #213부터 **절대 오차(MeanError mm)** 중심. 상호 보완적이며 VMS에 inlier 비율 기반 신뢰도를 추가하면 판정 기준을 잡기 쉬워진다.
4. Mech-Vision의 전처리 출력은 **법선 포함(XYZNormal)** 포맷이고 RegionGrowing 클러스터링·에지 매칭 등 법선 의존 기능이 많다. VMS `PointCloudData`는 법선이 없어 이 계열 기능의 전제가 안 되어 있다 (중장기 과제).
5. 측정(갭 폭·홀 직경·단차·평탄도)은 Mech-Vision이 개별 스텝/전용 측정 모드로 세분화. VMS는 PlaneFit/HeightSlicer/Geometry3D로 부분 대응.

---

## 1. Mech-Vision 개요와 버전

- Mech-Mind 소프트웨어 스위트: **Mech-Vision**(그래픽 머신비전, 스텝 그래프), Mech-Viz(로봇 경로계획), Mech-DLK(딥러닝 학습), Mech-Eye Viewer/SDK(카메라 — VMS가 사용하는 것).
- 문서 최신 버전: **2.2.1** (구버전 2.1.x, 2.0.0, 1.8.x, 1.7.x 문서 병행 제공). 🔎
- 버전에 따라 스텝 이름·구성이 달라짐 (예: 1.8 스텝 인덱스의 "Point Cloud Filter" → 최신 "Point Filter"). 본 리포트는 latest(2.2.1) 기준으로 쓰고, 구버전에만 있는 내용은 버전을 명시.

## 2. 포인트클라우드 전처리 대조

### 2.1 Mech-Vision 명세

| 스텝 (영문 원명) | 기능 | 주요 파라미터 | 검증 |
|---|---|---|---|
| **Point Cloud Preprocessing** (Procedure) | 필터링·병합·이미지 필터링 묶음 프로시저. 간섭 포인트 제거로 후속 스텝 가속 | 3D ROI, **Noise Removal Level** (None/Weak/Strong/Customized, 기본 Strong), **Edge Extraction Effect** (Fine/Standard/Rough/Extra Rough/Customized, 기본 Standard). 수치 파라미터는 Customized 선택 시에만 노출 | ✅ |
| 〃 출력 | **Merged Point Cloud / Point Cloud in ROI (PointCloud/XYZNormal)**, Color Point Cloud (XYZRGB) — 법선 포함 포맷 | | ✅ |
| **Down-Sample Point Cloud** | 다운샘플로 점 수 감소 | **Sampler Type**: UniformSampler / **VoxelGridSampler(기본)** — 그리드 내 위치·법선 평균. **Sampling Interval** 기본 **10.000 mm** | ✅ |
| **Point Filter** | 규칙 기반 포인트 제거 (아웃라이어) | **StatisticalOutlierFilter**(명백한 이상점 권장) / **NormalsFilter**(기준 방향 대비 법선 각도) | ✅ |
| **Point Cloud Clustering** | 군집화로 객체 분할 | **Cluster Algorithm**: **EuclideanCluster(기본)** — 거리 임계 기반 / **RegionGrowingSeg** — 법선·곡률 차이 기반 | ✅ |
| Calc Normals of Point Cloud and Filter It | 법선 계산 + 아웃라이어 제거 | | ✅ |
| 기타 (latest 인덱스) | Merge Point Clouds, Extract 3D Points in 3D ROI, From Depth Map to Point Cloud, Get Highest Layer Clouds, Transform Point Clouds, Estimate Point Cloud Edges by 3D Method 등 | | 🔎 |

EuclideanCluster 세부 파라미터(Max Distance Between Neighboring Points, Min/Max Points Number in Output Clusters)와 StatisticalOutlierFilter의 Mean K는 문서 인용은 확보됐으나 교차 검증 미완. ⚠️

### 2.2 VMS 대조

| Mech-Vision | VMS | 평가 |
|---|---|---|
| Down-Sample Point Cloud (VoxelGrid, 10mm 기본) | **PointCloudFilter** EnableVoxelGrid + VoxelSize (기본 1.0mm) | ✅ 동일 계열. VMS 기본값이 더 정밀(1mm vs 10mm) — 용도 차이(검사 vs 피킹)로 합리적 |
| Point Filter — StatisticalOutlierFilter | **PointCloudFilter** SOR (K=30, Stddev=2.0) | ✅ 동일 알고리즘 |
| Point Filter — NormalsFilter | 없음 | ❌ 법선 부재로 전제 미충족 |
| Point Cloud Clustering — EuclideanCluster | **PointCloudCluster** (Tolerance 5mm, Min/MaxPoints, OutputMode) | ✅ 동일 알고리즘 + VMS는 OutputMode(LargestOnly 등) 후처리가 오히려 더 풍부 |
| Point Cloud Clustering — RegionGrowingSeg | 없음 | ❌ 법선·곡률 기반 — 법선 부재 |
| 법선 계산 스텝 / XYZNormal 출력 | 없음 (`PointCloudData` = XYZ+RGB) | ❌ 구조적 격차 |
| Noise Removal Level 프리셋 UX | 수치 파라미터 직접 노출 (+HelpIcon 권장값) | 참고 — 프리셋(약/중/강) + Customized 패턴은 오퍼레이터 UX에 차용할 만함 |

## 3. 3D 매칭 대조 (핵심 격차)

### 3.1 Mech-Vision 명세

- 매칭 스텝 패밀리 (1.8.0 인덱스 기준 ✅): 3D Coarse Matching / **3D Coarse Matching V2** / 3D Coarse Matching (Multiple Models) / **3D Fine Matching** / 3D Fine Matching Lite / 3D Fine Matching (Multiple Models) / 3D Matching and Classification (Multiple Models). 최신 버전엔 coarse+fine 통합형 "3D Matching" 스텝도 존재. ⚠️(통합형 세부는 미검증)
- **3D Coarse Matching V2** 🔎: 모델 점군과 씬 점군을 개략 정합해 후보 자세 산출. **Matching Method = Edge matching(기본) / Surface matching**. 출력 = **Coarsely Calculated Poses (Pose[][]) + Matching Scores (Number[][])**.
- **3D Fine Matching** ✅🔎: Coarse 결과(초기 자세)를 받아 정밀 정합. **Matching Method = GMM(기본) / nearest-neighbor(ICP 계열)** — "대부분 시나리오에서 GMM이 내간섭성·속도 우수". 출력 6종 = Object Center Points (Pose[]) / Object Point Clouds with normals (XYZNormal[]) / Model Transformations (Pose[]) / Object Labels (String[]) / **Object Confidences (Number[]) / Pose Confidence Values (Number[])**.
- 품질 판정: **Confidence Threshold** 기반 필터링 (1.7 문서 기준 기본 0.500, Search Radius 10mm, nearest-neighbor 모드는 MSE Threshold 기본 0.001). ⚠️(수치는 1.7 문서, 교차 검증 미완)
- 1.7 구버전 Coarse Matching은 PPF(Point Pair Feature) 계열 파라미터(Distance/Angle Quantification, Max Vote Ratio)를 노출했음. ⚠️

### 3.2 VMS 대조

| 항목 | Mech-Vision | VMS (PointCloudRegistration) | 평가 |
|---|---|---|---|
| 구조 | **Coarse → Fine 2단** | Fine(ICP point-to-point) 단독 | ❌ **최대 격차** — 초기 자세 차이 크면 수렴 실패 |
| 정밀 정합 알고리즘 | GMM(기본) / nearest-neighbor | point-to-point ICP (SVD) | △ ICP는 nearest-neighbor 계열과 동급. GMM류 강건성은 없음 |
| 기준 모델 | CAD/STL 또는 스캔 모델 + 다중 모델 | .vpc 스캔 1개 | ❌ CAD 매칭·다중 모델 없음 |
| 품질 지표 | **Confidence(정규화) + Matching Score** | **MeanError(mm) + Iterations + Converged** (PR #213) | △ 철학 차이 — 절대 오차(VMS)는 공차 판정에 직관적, 신뢰도(MV)는 임계 설정이 쉬움. 상호 보완 |
| 자세 출력 | Pose(위치+회전) 배열 | 4x4 행렬 + Translation/RotationX/Y/Z (PR #213) | ✅ 단일 자세 기준 동등 |

## 4. 측정 기능 대조

- Mech-Vision 측정은 두 갈래: ① 파이프라인 measure 스텝들 — 1.8.0 인덱스에 Measure Height Difference (Point to Point / Points to Plane / Points to Baseline), **Measure Gap Width**, **Calc Flatness Error**, Detect and Measure Circle/Oblong Hole, Measure Distances 계열 + Measure Result 집계 스텝 ⚠️(인용 확보, 교차 검증 미완) ② **Measurement Mode** — 전용 3D 측정 모드(별도 3d-measurement-manual 존재, Measure Surface Flatness 등). latest(2.2.1) 일반 스텝 인덱스에는 2D 측정 계열(Edge-to-Edge Width, Feature-to-Feature Distance, Angle Between Segments)만 남고 3D 측정은 전용 모드/매뉴얼로 분리된 것으로 보임. 🔎
- VMS 대응: **PlaneFit**(평면 피팅 ≈ 평탄도 기초), **HeightSlicer**(높이 단면 ≈ 단차 기초), **Geometry3D**(3D 기하 측정). 갭 폭·홀 직경·점-평면 거리 같은 **목적별 측정 툴 세분화는 미치지 못함**.

## 5. 딥러닝 연동 (개요) ⚠️

- Mech-Vision은 "Deep Learning Model Package Inference" 스텝(인스턴스 세그먼테이션/결함 세그먼테이션 등 변형)으로 Mech-DLK에서 학습한 모델 패키지를 파이프라인 안에서 추론. (소스 확보, 세부 교차 검증 미완)
- VMS 대응: DeepLearning 카테고리(Yolo/Segmentation/Anomaly/Classify/Detection/Ensemble, ONNX 기반) — 구조상 동등한 접근. 3D 점군 직접 추론(픽킹용 세그먼테이션)은 양쪽 다 2D 이미지 경유가 기본.

## 6. 격차 분석 및 도입 우선순위 제안

| 순위 | 항목 | 근거 | 난이도 |
|---|---|---|---|
| 1 | **3D Coarse Matching 도입** — ICP 앞단의 초기 자세 추정 (1안: 중심+주성분(PCA) 정렬 간이 coarse / 2안: PPF 계열) | Registration 실패 모드의 근본 원인. Mech-Vision은 이를 위해 스텝 패밀리 전체를 둠 | 1안 中 / 2안 高 |
| 2 | **정합 신뢰도(Confidence) 지표 추가** — 대응점 inlier 비율(距離< τ 비율) 기반 0~1 값. MeanError와 병행 노출 | 판정 임계 설정이 쉬워짐. Mech-Vision 방식과 수렴 | 低 |
| 3 | **측정 툴 세분화** — Geometry3D 확장으로 갭 폭·홀 직경·점-평면 거리·평탄도 오차 | 검사 SW로서의 상품성. GS 인증 데모에도 유효 | 中 |
| 4 | **법선(Normal) 파이프라인** — PointCloudData에 법선 추가 + 법선 추정 → RegionGrowing 클러스터·NormalsFilter·에지 매칭의 전제 | Mech-Vision 3D 기능 다수의 기반. 포맷(.vpc) 확장 필요 | 高 (중장기) |
| 5 | **프리셋 UX** — Noise Removal Level(None/Weak/Strong/Customized) 패턴을 Filter/Cluster 설정에 적용 | 오퍼레이터 튜닝 부담 감소. 기존 HelpIcon 권장값을 프리셋으로 승격 | 低 |

## 출처

주요 공식 문서 (docs.mech-mind.net):

- Point Cloud Preprocessing: `/en/suite-software-manual/latest/vision-steps/point-cloud-preprocessing.html`
- Down-Sample Point Cloud: `/en/suite-software-manual/latest/vision-steps/down-sample-point-cloud.html`
- Point Filter: `/en/suite-software-manual/latest/vision-steps/point-filter.html`
- Point Cloud Clustering: `/en/suite-software-manual/latest/vision-steps/point-cloud-clustering.html`
- 3D Coarse Matching V2: `/en/suite-software-manual/latest/vision-steps/3d-coarse-matching-v2.html`
- 3D Fine Matching: `/en/suite-software-manual/latest/vision-steps/3d-fine-matching.html`
- 스텝 인덱스 (1.8.0 / latest): `/en/suite-software-manual/1.8.0/vision-steps/steps.html`, `/en/suite-software-manual/latest/vision-steps/steps.html`
- 측정 모드: `/en/suite-software-manual/1.8.1/vision-measure-mode/getting-started.html`, `/en/3d-measurement-manual/latest/software-steps/measure-surface-flatness.html`
- 딥러닝 추론: `/en/suite-software-manual/latest/vision-steps/deep-learning-model-package-inference.html`
- 릴리즈 노트: `/en/suite-software-manual/latest/vision-release-notes.html` (버전 2.2.1 확인)

VMS 측 기준 코드: `VMS.VisionSetup/VisionTools/PointCloud/*` (Filter/Registration/Cluster), `VMS.Camera/Utils/TransformUtils.cs` (ICP + IcpStats, PR #213), `PlaneFitTool`, `HeightSlicerTool`, `Geometry3DTool`.
