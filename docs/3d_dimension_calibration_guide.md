# 3D 치수 XY 캘리브레이션 가이드 (mm/px)

> 대상 독자: 현장 설정 담당자 · VisionSetup 사용자
> 관련 도구: PointCloud Cluster (Dimensions), PointCloud Mask Crop

## 1. 왜 필요한가

Mech-Mind 3D 카메라로 grab한 점군은 좌표 단위가 섞여 있습니다.

| 축 | 단위 | 비고 |
|----|------|------|
| Z (높이) | **mm** | 카메라가 직접 측정한 깊이 — 그대로 사용 가능 |
| X, Y (가로·세로) | **픽셀** | 뎁스맵 픽셀 인덱스 — mm로 쓰려면 환산 필요 |

그래서 PointCloud Cluster의 치수 결과 중 `SizeZ`(높이차)는 바로 mm이지만,
`SizeX/SizeY/Length/Width`(가로·세로)는 환산 배율(mm/px)을 곱해야 실측 mm가 됩니다.

1픽셀의 실제 폭은 **카메라와 물체 사이 거리(Z)에 비례**합니다 (핀홀 카메라 모델):

```
mm/px = Z / fx        (fx: 카메라 초점거리, 픽셀 단위)
```

즉 카메라를 더 멀리 설치하면 같은 물체도 더 적은 픽셀로 찍히므로, mm/px가 커집니다.

## 2. 자동 모드 — AutoFromCamera (권장)

**v1.4.25부터** PointCloud Cluster 설정 → Dimensions → **Scale Mode = AutoFromCamera**.

- 카메라 연결 시 SDK에서 depth 내부 파라미터(fx, fy)를 자동으로 읽어 점군에 실어 보냅니다
- 클러스터마다 **실측 높이 Z로 mm/px를 자동 계산** — 카메라 설치 높이가 바뀌거나
  트레이 높이가 달라져도 재캘리브레이션이 필요 없습니다
- 실제 적용된 배율은 결과의 `Cluster{i}_MmPerPx` 키로 확인

**자동 모드가 안 되는 경우** (결과 메시지에 ⚠ 표시, XyScale로 자동 폴백):
- 저장된 `.vpc` 파일을 불러와 작업할 때 (카메라 정보 없음)
- VMS 앱에서 공유 프레임으로 넘어온 점군
- Registration(정합)으로 좌표가 회전된 점군 — 픽셀 공간이 깨져 자동 환산 불가

## 3. 수동 절차 — XyScale 산출 (폴백·검증 겸용)

기지 치수 물체(게이지 블록, 자, 치수를 아는 부품)로 1회 측정해 배율을 구합니다.

### 준비물
- 폭을 정확히 아는 물체 1개 (예: 100.0 mm 게이지 블록). 클수록 오차가 줄어듭니다.

### 절차

1. **배치**: 물체를 실제 검사 대상과 같은 높이(같은 작동 거리)에 평평하게 놓습니다.
   ⚠ mm/px는 거리에 비례하므로, 검사면과 다른 높이에서 재면 그만큼 오차가 생깁니다.
2. **체인 구성**: VisionSetup에서 `Grab → (Threshold 또는 Height Slicer 마스크)
   → PointCloud Mask Crop → PointCloud Cluster` 를 구성하고 물체만 클러스터로 잡히게 합니다.
3. **픽셀 치수 측정**: Cluster 설정에서 Scale Mode = Manual, XyScale = **1.0** 으로 Run
   → 결과의 `Cluster0_Length` (또는 SizeX)가 **픽셀 단위 폭**입니다.
4. **배율 계산**:

   ```
   XyScale = 실제 폭(mm) ÷ 측정된 픽셀 폭
   예) 100.0 mm 블록이 512.8 px 로 측정 → XyScale = 100.0 / 512.8 = 0.1950 mm/px
   ```

5. **입력·저장**: 구한 값을 XyScale에 입력하고 레시피를 저장합니다.
6. **검증**: 같은 물체로 다시 Run → `Cluster0_Length`가 실제 폭 ±공차 이내인지 확인.
   가능하면 다른 치수의 물체 하나로 교차 확인합니다.

### 재캘리브레이션이 필요한 경우 (Manual 모드만 해당)
- 카메라 설치 높이/각도를 바꿨을 때
- 검사 대상의 기준 높이가 크게 달라졌을 때 (수 mm 수준은 통상 무시 가능)
- Downsample Stride 설정 변경은 영향 **없음** (점군 X/Y는 stride 이전 픽셀 좌표 유지)

## 4. 오차 요인과 팁

- **높이 차이**: Manual 배율은 캘리브레이션한 높이에서만 정확합니다. 검사면과 Z가 ΔZ 만큼
  다르면 치수 오차는 약 ΔZ/Z 비율로 생깁니다 (예: Z=1000mm에서 ΔZ=20mm → 2%).
  높이가 다양한 부품을 다루면 AutoFromCamera를 사용하세요.
- **기울어진 물체**: Length/Width는 XY 평면 투영 기준입니다. 물체가 크게 기울어 있으면
  투영 폭이 실제보다 작게 나옵니다 — 평면 안착 상태에서 측정하세요.
- **마스크 경계**: 세그멘테이션/Threshold 마스크가 물체 가장자리를 못 덮으면 치수가 작게
  나옵니다. Mask Crop의 Dilate(2~5px)로 보정 후, 기지 물체 검증으로 확인하세요.
- **AutoFromCamera의 fx/fy 확인**: 카메라 연결 후 `%LocalAppData%\BODA VISION AI\logs\camera.log`
  에 `Depth intrinsics: fx=..., fy=...` 로 기록됩니다.

## 5. 관련 문서

- 도구 도움말: PointCloud Cluster / PointCloud Mask Crop (도구 설정 창 ? 버튼)
- `docs/msi_build_guide.md` — 배포·설치
