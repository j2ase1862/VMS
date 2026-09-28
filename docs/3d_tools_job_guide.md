# 3D 도구로 할 수 있는 일 — 예제 템플릿 · 현장 레시피 · 조합 가이드

기준: VMS v1.42.7 (2026-09-28). 현장 데이터는 Mech-Mind 3D 카메라 촬영본(`D:\3D Image-1`, 8장).

이 문서는 "지금 구현된 3D 도구로 **어떤 검사·측정 작업(job)** 을 할 수 있는가"를
① 도구 한 장 요약 → ② 예제 템플릿 6종 → ③ 현장 레시피(Field3D Frame Demo) → ④ 조합 가능한 job → ⑤ 한계 순서로 정리한다.

> v1.42.7 에서 달라진 것: **3D 도구 판정**(3D Geometry·Plane Fit·Cluster·Registration) · **VMS 본체 수동 검사·AUTO RUN 에서 3D 레시피 실행** ·
> **새 도구 PointCloud Line Fit**(3D 직선·직진도, 3D Geometry 점-직선 거리). v1.42.6 판에서 "안 된다"고 적었던 세 가지다.

---

## 0. 먼저 알아둘 전제 4가지

1. **3D 레시피는 VisionSetup 과 VMS 본체(수동 검사·AUTO RUN) 모두에서 돈다** (v1.42.7~). 본체는 스텝에 3D 도구가 있으면
   촬영 점군으로 VisionSetup 과 같은 3D 실행 환경(점군·깊이맵·높이맵 메타데이터)을 세운다 — 현장 8장에서 두 쪽 결과가 소수점까지 같다.
   레시피 높이 범위(Height Slicing)는 VisionSetup 에서 저장한 값을 그대로 쓴다.
2. **좌표 단위가 섞여 있다.** 3D 카메라 점군은 X/Y 가 **화소**, Z 만 **mm** 다.
   - mm 로 환산되는 것: PointCloud Cluster 의 치수·중심(Scale Mode = AutoFromCamera, 카메라 내부 파라미터 필요 — v1.42.4 이상 촬영본),
     3D Geometry 의 점-점 거리(클러스터 mm 중심 사용).
   - 화소 그대로인 것: Registration·Deviation·Plane Fit·Line Fit 의 X/Y. 작동 거리 1.8m 장면에서는 1px ≈ 1.0mm 라 실용상 큰 차이는 없지만,
     거리가 달라지면 X/Y 방향 편차·이동량·길이는 mm 가 아니다.
3. **판정은 도구마다 "Enable Judgment" 로 켠다** (기본 꺼짐 — 끈 레시피는 종전처럼 값만 낸다). 판정 결과는 도구의 성공/실패에 반영돼
   Result 도구가 그대로 집계한다. 결과 키 `JudgmentValue/JudgmentLow/JudgmentHigh/JudgmentPass`.
4. **PointCloud Registration 은 판정을 꺼 두면 정합이 틀려도 OK 다.** Min Confidence 판정을 켜야 틀린 정합이 NG 가 된다 —
   제대로 맞으면 0.65 이상, 실패하면 0.3 아래(현장). 예제 템플릿 2종은 0.3 이 켜져 있다.

---

## 1. 3D 도구 한 장 요약 (9종 + 보조)

| 도구 | 하는 일 | 입력 | 주요 결과 | 판정 (v1.42.7) |
|---|---|---|---|---|
| **Height Slicer** | 높이(mm) 범위 안 화소만 남긴 2D 마스크 | 깊이맵(자동) | 마스크 영상 | 없음 |
| **PointCloud Mask Crop** | 2D 마스크/ROI 안의 점만 남김 (2D→3D 다리) | 마스크 영상(Image 연결) 또는 도구 ROI | `OutputPoints`, `KeptRatio` | 남은 점 0 이면 실패 |
| **PointCloud Filter** | 복셀 다운샘플 · 튄 점 제거(SOR) | 현재 점군 | `OutputPoints`, `ReductionRatio` | 없음 |
| **PointCloud Cluster** | 점을 덩어리(물체)별로 나눔 · 개수·치수·중심 | 현재 점군 | `ClusterCount`, `Cluster{i}_Length/Width/SizeZ/Angle/Center*` , `Cluster{i}_{j}_DistanceMm` | **개수**(같음/이상/이하/범위, 0개도 판정) · **치수**(모든 덩어리 길이·폭 ± 공차) |
| **PointCloud Registration** | 기준 점군(.vpc/CAD .stl)에 겹쳐 맞춤(ICP) | 현재 점군 + 기준 파일 | `TranslationX/Y/Z`, `RotationX/Y/Z`, `Confidence`, `MeanError` | **Confidence 하한** (끄면 틀려도 OK) |
| **PointCloud Deviation** | 기준 대비 점 단위 편차 · 히트맵 | 현재 점군 + 기준 파일 | `MeanDeviation`, `MaxDeviation`, `DefectRatio` | 편차 > Tolerance 인 점 비율 ≤ Max Defect Ratio (종전부터) |
| **Plane Fit** | ROI 안 점으로 평면 방정식 | 높이맵 메타데이터(자동) + ROI | `NormalX/Y/Z`, `PlaneD`, `Flatness`, `AvgError` | **평탄도 상한** |
| **3D Geometry** | 점·평면·직선 사이 거리·각도 | Plane Fit(평면) · Cluster(점) · Line Fit(직선) Result 연결 | `Distance3D`, `SignedDistance`, `AngleDeg` | **기준값 ± 공차** (거리 mm · 각도 °) |
| **PointCloud Line Fit** 🆕 | 점군에 3D 직선 · 방향·길이·직진도 | 현재 점군 (보통 Crop 으로 봉·모서리만) | `LinePoint*`, `LineDir*`, `Length`, `Straightness`, `AngleXY`, `Elevation` | **직진도 상한** (상위 1% 제외) |
| (보조) 2D 도구 on Height Map | Blob·Caliper 등을 높이맵 위에서 | 높이맵(8bit) | 면적·위치·에지 | 도구별 |

- 3D Geometry 연산: 점-점 거리 · 점-평면 거리 · 평면-평면 각도 · 평면-평면 거리(평행면) · 점-직선 거리(직선 = Line Fit).
- 판정만 NG 인 Plane Fit(평탄도 초과)·Cluster(치수 공차 밖)도 3D Geometry 는 기하 데이터로 계속 쓴다 — 높이 측정이 엉뚱하게 실패하지 않는다.
- PointCloud Line Fit 은 **가는 대상**(봉·모서리·파이프)에 맞다. 현장 프레임처럼 윗면이 폭 95mm 띠면 인라이어 비율이 낮고(4~37%)
  방향·길이만 참고값(독립 계산 대비 방향 ±2°, 길이 −3%)이다. 인라이어 거리(Distance Threshold)는 허용 직진도보다 크게 둔다 —
  작으면 휜 부분이 직선 밖으로 빠져 직진도가 작게 나온다(결과 메시지에 경고).
- Registration·Deviation 의 기준은 **양품 스캔(.vpc)** 또는 **CAD(.stl, 표면을 약 20만 점으로 샘플링)** 둘 다 된다.

---

## 2. 예제 템플릿 6종 — 무엇을 하는 job 인가

갤러리(예제 Template)에서 고르면 도구·연결·기본 설정이 한 번에 생긴다. 모두 "3D 카메라 필요" 배지.

### 2.1 3D 객체 카운트·치수 (기본) — `pc-count-basic`

```
Height Slicer ──Image──▶ PointCloud Mask Crop ──Result──▶ PointCloud Cluster (KeepOriginal · AutoFromCamera)
```

- **job 예:** 트레이 위 박스가 몇 개인가 / 각 박스 가로·세로·높이차 / 놓인 방향(각도).
- 결과: `ClusterCount`(개수), `Cluster{i}_Length/Width`(mm), `Cluster{i}_SizeZ`(높이차 mm), `Cluster{i}_Angle`.
- 조정: Slicer 의 MinZ/MaxZ 를 **부품 윗면 높이대**로(바닥·배경이 빠지게), Cluster Tolerance 를 "같은 물체로 볼 점 간격(mm)"으로.
- 판정: Cluster 의 Judgment 를 켜면 **개수**(예: Equal 6) · **치수**(모든 덩어리 길이·폭 ± 공차) 로 OK/NG. 판정을 꺼 두면
  덩어리 0개일 때만 실패("부품 없음").

### 2.2 DL 세그 기반 3D 객체 검출 — `pc-dl-detect`

```
YOLOv8-seg (마스크 출력 켬) ──Image──▶ PointCloud Mask Crop ──Result──▶ PointCloud Cluster
```

- **job 예:** 높이만으로는 구분 안 되는 물체(같은 높이의 서로 다른 부품, 겹친 봉지 등)를 **딥러닝으로 먼저 찾고** 그 물체의 점군만 잘라 개수·좌표·치수를 잰다.
- 전제: 학습된 ONNX 모델 경로 지정.

### 2.3 3D 기준 형상 편차 검사 — `pc-deviation`

```
PointCloud Filter ──Result──▶ PointCloud Registration ──Result──▶ PointCloud Deviation
```

- **job 예:** 양품(또는 CAD) 대비 변형·휨·찌그러짐·돌출·결손 검사.
- 템플릿 기본값: Registration 신뢰도 하한 0.3 켜짐 — 정합이 틀리면 Registration 이 NG 가 되고 Deviation 은 건너뛴다.
- 절차: 양품 촬영 → Registration 에서 [Save Current as Reference] → Deviation 도 같은 기준 지정 → 검사품 촬영·Run.
- 판정: `DefectRatio`(Tolerance 보다 크게 벗어난 점의 비율) ≤ Max Defect Ratio 면 OK.
- 불량 부위 개수·크기까지 보려면 Deviation OutputMode = **DefectsOnly** 로 두고 뒤에 Cluster 를 붙인다(→ §4 J9).

### 2.4 표준 3D 얼라인 (6DOF) — `3d-registration-align`

```
PointCloud Filter ──Result──▶ PointCloud Registration (Apply Transform 끔) ──Result──▶ Result
```

- **job 예:** 로봇 픽킹·가공 전 부품 자세 보정 — 기준 대비 ΔX/ΔY/ΔZ(mm)·RX/RY/RZ(도)를 로봇에 넘긴다.
- 결과: `TranslationX/Y/Z`, `RotationX/Y/Z`, 품질은 `Confidence`·`MeanError`. 템플릿 기본값으로 Confidence 0.3 미만이면 NG —
  틀린 변위를 로봇에 넘기지 않는다.
- 주의(현장 실측): 위에서 내려다본 **평평한 부품은 Coarse Alignment 를 끈다**(켜면 180° 뒤집어 맞춤).
  끈 상태의 ICP 는 기준 대비 **약 ±45°** 안쪽 자세만 따라간다.

### 2.5 평면 틸트 얼라인 — `3d-plane-tilt-align`

```
Plane Fit Ref ────┐
                  ├──Result──▶ 3D Geometry (평면-평면 각도) ──Result──▶ Result
Plane Fit Target ─┘
```

- **job 예:** 척·스테이지 레벨링, 부품 윗면과 지그면의 평행도, 안착 들뜸(한쪽이 떠 있음) 확인.
- 결과: `AngleDeg`(0° = 평행). 두 Plane Fit ROI 를 각 면 위에 둔다. 합불은 3D Geometry Judgment(기준 0° ± 허용 각도).

### 2.6 2D+3D 하이브리드 얼라인 — `3d-hybrid-align`

```
Grayscale ─Image─▶ Feature Match ─Result─▶ Match Align ──┐
                                                         ├─Result─▶ Result
                             Plane Fit Tilt ─────────────┘
```

- **job 예:** 평면 내 위치·회전(XYθ)은 2D 패턴이 정밀하고, 높이·기울기는 3D 가 정확한 부품 — 둘을 합쳐 보정값을 낸다.
- 결과: Match Align 의 ΔX/ΔY/Δθ + Plane Fit 의 법선(기울기)·`PlaneD`(높이 오프셋). 6DOF 정합보다 가볍다.

---

## 3. 현장 레시피 — Field3D Frame Demo (파란 시트 위 3칸 금속 프레임)

파일: `D:\3D Image-1\Recipe\recipe_Field3D_Frame_Demo.json` (+ `frame_reference.vpc`, `README.md`, 독립 기준값 `ref\`).
골든 샘플 `A533EG03_20260928_102949`. 권장 설정은 8장 실측으로 정했다.

### 3.1 스텝 1-1 — 프레임 높이 (job: "부품 윗면이 받침면에서 몇 mm 위에 있나")

```
Frame Slicer (1780~1825mm) ─┬─Image─▶ Frame Crop ─Result─▶ Frame Cluster (Tol 70 · AutoFromCamera) ─┐
                            └─Image─▶ Frame Locator (Blob) ═Coordinates═▶ Panel Plane (ROI 600, RANSAC 10mm) ─┴─▶ Frame Height (3D Geometry 점-평면)
```

- 어떻게: 높이대로 프레임 윗면만 잘라(Slicer→Crop) 프레임 점군 중심을 구하고(Cluster),
  프레임 **둘레의 받침면**에 평면을 맞춰(Plane Fit) 중심에서 평면까지 수직 거리를 잰다(3D Geometry).
- **2D 도구가 3D 도구를 도와주는 부분:** Blob 이 높이 마스크에서 프레임 위치를 찾아 **Plane Fit ROI 를 부품 따라 옮긴다**(Coordinates 연결).
  받침 시트가 휘어 있어서, 시트 전체에 평면 하나를 맞추면 가장자리 부품은 높이가 크게 틀린다 — ROI 를 부품 둘레로 좁힌 이유.
- 부가 결과: Frame Cluster 의 치수 **374~379 × 250~255mm**(8장) — 지난 도구값 377×250mm 와 일치.
- 결과: 높이 89.3~93.8mm. 받침이 휘는 시트라 "받침면"의 정의가 평면 영역 크기에 따라 달라진다(같은 장을 400/600/800px 창으로 재면 ~93/89~91/85~88mm).
  캘리퍼 실측으로 확정 필요.
- 판정(v1.42.7 예제 레시피): Frame Height **90 ± 5mm**, Frame Cluster 치수 **376×252 ± 6mm** — 8장 모두 OK.

### 3.2 스텝 1-2 — 골든 샘플 정합·편차 (job: "양품과 같은 모양인가 + 얼마나 돌아가 놓였나")

```
Frame Slicer → Frame Crop → Frame Cluster (가장 큰 것만) → Downsample (복셀 4mm) → Frame Registration (Coarse 끔) → Frame Deviation (5mm / 15%)
```

- 어떻게: 프레임 점군만 남겨(Slicer→Crop→Cluster LargestOnly) 줄이고(Filter) 골든에 정합(Registration)한 뒤 점 단위 편차 비율로 판정(Deviation).
- 결과(8장): 7장 OK — 회전각이 독립 계산과 1° 이내(최대 44.6° 까지 따라감), Confidence 0.67~0.80, 편차 비율 4~10%.
  **103138 은 골든 대비 57° 돌아가 있어 정합 실패 → NG** (ICP 포획 범위 밖). 현장에서는 놓임 각도를 ±45° 안으로 관리해야 한다.
  v1.42.7 예제 레시피는 Registration 신뢰도 하한 0.3 을 켜 둬서, 103138 은 Registration 이 "판정 NG (Confidence 0.168)" 을 내고
  Deviation 은 건너뛴다 — 종전에는 이 틀린 정합이 OK 로 표시됐다.
- **VMS 본체에서도 같은 결과**: 이 레시피를 본체 검사 엔진(수동 검사·AUTO RUN)에 점군과 함께 태우면 8장 모두 VisionSetup 과 소수점까지 같다.
- 기준 설정 근거: 같은 부품도 측정 잡음·화소 단위 X/Y 때문에 평균 편차 2.5~2.8mm · 5mm 초과 점 4~10% 가 나온다 → Tolerance 5mm / 15%.
  이보다 좁히면 양품이 NG. 즉 **이 장면(1.8m, 1px≈1mm)에서 잡을 수 있는 결함은 대략 5mm 이상 변형**이다.

### 3.3 이 레시피에서 배운 설정 요령

| 상황 | 요령 |
|---|---|
| 받침(시트)이 말려 올라와 높이대에 섞임 | Slicer 상한을 부품 윗면까지만(1845 → 1825) |
| 상한을 낮추니 부품이 두 덩어리(레일 2줄)로 갈라짐 | Cluster Tolerance 를 틈보다 크게(45 → 70) |
| 받침면이 휘어 높이가 틀어짐 | Plane Fit ROI 를 부품 둘레로 좁히고 Blob 좌표로 따라가게, RANSAC 임계는 휨(±5mm)보다 크고 부품 높이보다 작게(10mm) |
| 평평한 부품 정합이 180° 뒤집힘 | Registration Coarse Alignment 끄기 |
| 도구를 하나씩 실행하면 멈춤 | v1.42.5 에서 수정 — 앞 도구 실패 시 뒤 도구 건너뜀 |

---

## 4. 조합 가능한 job 목록

"입력 준비 → 대상 분리 → 측정 → 판정" 네 칸에 도구를 끼운다고 생각하면 된다.

| 칸 | 쓸 수 있는 도구 |
|---|---|
| 입력 준비 | PointCloud Filter(속도·노이즈) |
| 대상 분리 | Height Slicer → Mask Crop · 딥러닝 세그 → Mask Crop · Mask Crop 수동 ROI(여러 개는 Union) · Cluster(LargestOnly) |
| 측정 | Cluster(개수·치수·중심) · Plane Fit(평면·평탄도) · 3D Geometry(거리·각도) · Line Fit(직선·직진도) · Registration(변위) · Deviation(편차) |
| 판정 | 각 도구의 Judgment (v1.42.7) · Result(성공 플래그 합산) · 보정값(변위)은 PLC 로 전송 |

| # | job | 조합 | 핵심 결과 | 판정 방법 (v1.42.7) |
|---|---|---|---|---|
| J1 | 부품 유무 | Slicer → Crop → Cluster | Cluster 성공/실패 | Cluster 개수 판정 (≥1) — 판정 꺼도 0개면 실패 |
| J2 | 부품 개수 · 이물 없음 | Slicer(또는 DL 세그) → Crop → Cluster(KeepOriginal) | `ClusterCount` | **Cluster 개수 판정** (이물 없음 = LessOrEqual 0) |
| J3 | 외형 치수(길이·폭·높이차) | J2 + AutoFromCamera | `Cluster{i}_Length/Width/SizeZ` (mm) | **Cluster 치수 판정** (길이·폭) · 높이차는 PLC |
| J4 | 부품 높이 · 단차(점-면) | Cluster + Plane Fit → 3D Geometry 점-평면 | `Distance3D`, `SignedDistance` | **3D Geometry 판정** |
| J5 | 두 부품 간 거리 · 피치 | Cluster(1개로 충분, A/B 번호 지정) → 3D Geometry 점-점 | `Distance3D` (mm) | **3D Geometry 판정** |
| J6 | 면 평탄도 | Plane Fit (ROI = 검사면) | `Flatness`, `AvgError` | **Plane Fit 평탄도 판정** |
| J7 | 기울기 · 평행도 | Plane Fit ×2 → 3D Geometry 평면-평면 각도 | `AngleDeg` | **3D Geometry 판정** (°) |
| J8 | 평행한 두 면의 단차 | Plane Fit ×2 → 3D Geometry 평면-평면 거리 | `Distance3D`, `IsParallel` | **3D Geometry 판정** |
| J9 | 형상 편차 · CAD 비교 | Filter → Registration → Deviation (기준 .vpc/.stl) | `DefectRatio`, `MaxDeviation` | Deviation (+ Registration 신뢰도) |
| J10 | 불량 부위 개수·크기·위치 | J9 (Deviation DefectsOnly) → Cluster | 불량 덩어리 `ClusterCount`, 치수·중심 | Deviation + Cluster 개수 판정(≤ 허용 수) |
| J11 | 6DOF 자세 보정값 | Filter → Registration(Apply Transform 끔) | `Translation*`, `Rotation*`, `Confidence` | Registration 신뢰도 판정 · 변위는 PLC |
| J12 | XYθ + 기울기 보정값 | Feature Match → Match Align + Plane Fit | ΔX/ΔY/Δθ + 법선·`PlaneD` | Match Align 판정 · 기울기는 PLC |
| J13 | 특정 영역만 3D 검사 | Mask Crop 수동 ROI(영역 여러 개면 두 번째부터 Union) → 이후 아무 측정 | — | — |
| J14 | 부품 따라가는 3D 측정 | Height Slicer → Blob(위치) ═Coordinates═▶ Plane Fit | ROI 가 부품 위치로 이동 | — |
| J15 | 높이맵 위 2D 검사 | Load 3D Data 후 높이맵(8bit)에 Blob·Caliper 등 | 면적·에지 위치(화소) | 2D 도구 판정 |
| J16 🆕 | 봉·모서리 직진도 · 방향 | Slicer → Crop(대상 부위만) → **Line Fit** | `Straightness`, `Length`, `AngleXY`, `Elevation` | **Line Fit 직진도 판정** |
| J17 🆕 | 기준 모서리에서 부품까지 거리 | Line Fit(모서리) + Cluster(부품) → 3D Geometry 점-직선 | `Distance3D` | **3D Geometry 판정** |
| J18 🆕 | 라인 운전 중 3D 검사 | 위 조합 아무거나 — VMS 본체 수동 검사·AUTO RUN | VisionSetup 과 같은 값 | 스텝 판정 → 시퀀스 OutputAction(PLC/IO) |

조합 시 주의:

- **순서 보장은 Result 연결로.** 점군을 바꾸는 도구(Crop·Cluster LargestOnly·Filter·Registration Apply Transform)는
  "현재 점군"을 이어받는 방식이라, 뒤 도구는 Result 연결로 순서를 묶어야 한다. 앞 도구가 실패하면 뒤 도구는 건너뛴다
  (단 3D Geometry·Result 는 실패 정보를 모아야 해서 예외로 실행된다 — 소스가 실패하면 3D Geometry 는 "평면 데이터가 필요합니다" 같은 메시지로 실패).
- **Plane Fit 과 3D Geometry 는 높이맵 메타데이터**를 쓰므로 점군을 자른 뒤에도 원래 화소 좌표로 동작한다(Crop 영향 없음).
- 3D Geometry 로 클러스터 중심을 쓰려면 **Use Manual Points 를 끈다**(켜 두면 연결한 점을 무시한다).
- 한 스텝을 실행할 때마다 점군은 **원본에서 다시 시작**한다 — 스텝 1-1 이 자른 점군이 1-2 로 넘어가지 않는다.

---

## 5. 지금은 안 되거나 어려운 job

| job | 상태 | 대안·필요 작업 |
|---|---|---|
| 체적(부피) | 전용 결과 없음 | Cluster 치수(길이×폭×SizeZ)로 근사만 가능 |
| 높이차(SizeZ)·변위·기울기의 공차 판정 | Cluster 는 길이·폭만, Registration 은 신뢰도만, Plane Fit 은 평탄도만 판정 | PLC 로 값 전송 후 판정 |
| 넓은 띠(레일 윗면)의 직진도 | Line Fit 은 가는 대상용 — 폭 95mm 띠는 인라이어 4~37% | 모서리만 남기도록 Crop ROI 를 좁히거나 참고값으로만 |
| 구멍·원·모서리 같은 3D 특징 측정 | 전용 도구 없음 | 높이맵 위 2D 도구(J15)로 화소 단위 근사 |
| 여러 방향 스캔 합치기 | 도구 없음 (Registration 은 한 점군 정렬만) | 다중 시점 병합 기능 필요 |
| 기준 대비 45° 넘게 돌아간 부품 정합 | ICP 가 못 따라감, Coarse 는 평평한 부품에서 뒤집힘 | 놓임 각도 관리 또는 골든을 여러 자세로 |
| 수 mm 미만의 작은 결함 | 이 장면(1.8m, 1px≈1mm, 측정 잡음 ~2.5mm)에서는 잡히지 않음 | 작동 거리 단축·고해상도 카메라 |

v1.42.6 판의 한계 중 **라인 운전(AUTO RUN) 3D 검사 · 높이/각도/개수/치수 공차 판정 · 점-직선 거리** 세 가지는 v1.42.7 에서 해소됐다.
본체 3D 검사는 ⏳ 실제 3D 카메라 AUTO RUN 1사이클과 "카메라 2D 영상 해상도 = 깊이 격자" 여부를 현장에서 확인해야 한다
(다르면 2D 영상 위에 직접 그린 ROI 가 3D 좌표와 어긋난다 — VisionSetup 도 같은 조건).

---

## 부록 — 참고 파일

- 현장 예제: `D:\3D Image-1\Recipe\README.md` (8장 결과표·다시 만들기 명령), 독립 기준값 `D:\3D Image-1\Recipe\ref\ref3d.py`
- 치수 mm 환산 절차: `docs/3d_dimension_calibration_guide.md`
- 도구별 파라미터·결과 설명: VisionSetup 각 도구 설정의 물음표(?) 도움말 (`VMS.VisionSetup/Models/HelpContent.cs`)
- 템플릿 정의: `VMS.VisionSetup/Services/RecipeTemplateCatalog.cs`
