# 저장 점군(.vpc) 3D 도구 검증 리포트

**작성** 2026-09-23 · **대상 빌드** v1.41.1 (master `c2ce089`)
**검증 파일** `C:\Users\vinos\Downloads\pipe-fittings-18.snapshot.1\PipeFitting_synthetic_scan.vpc`
**배경** GS 시험인증 3번 항목 — 3D 기능을 **저장 점군(.vpc) 오프라인 경로**로 시험하겠다고 TTA 에 회신할 예정
([[GS인증_TTA_추가확인_회신_초안]] 2번). 그 경로가 실제로 성립하는지 확인한 기록이다.

---

## 요약

| | 결과 |
|---|---|
| 점군 로드 | ✅ 정상 (11,433점, 좌표 mm) |
| 3D 도구 실행 | ✅ 7종 중 6종 정상 · ⚠ 1종 결함(MaskCrop) |
| 3D 치수 측정 | ✅ mm 로 정확 (직접 계산값과 소수점 둘째 자리까지 일치) |
| **화면(UI)에서 이 파일로 시험** | ❌ **막힘** — [Height Map 생성] 이 아무 반응 없음 |
| CAD(STEP) 대조 | ❌ 미지원 — STL 변환 필요 |

**결론**: 엔진은 이 파일로 3D 검사를 정상 수행한다. 그러나 **화면 조작만으로는 시험을 진행할 수 없다** —
비격자(unorganized) 점군에서 Height Map 생성이 막혀 있어, 그 위에 올라가는 Plane Fit · Geometry 3D 를
화면에서 실행할 방법이 없다. 심사원 앞에서 이 파일로 시연하려면 **아래 3-1 을 먼저 고쳐야 한다.**

---

## 1. 파일 분석

| 항목 | 값 |
|---|---|
| 형식 | `VPC1` (VMS 자체 포맷) — 헤더 16B + 이름 + float32 XYZ + RGB |
| 점 수 | 11,433 |
| 격자 | **0 × 0 → 비격자(unorganized)** |
| 이름 | `WeldTeach` (BODA.RoboTeach 계열이 만든 합성 스캔) |
| 좌표 범위 | X −153.9 ~ 139.2 · Y −16.4 ~ 228.6 · Z −52.2 ~ 97.2 |
| 점 간격 | 약 1.7 mm (샘플 추정) |
| 색상 | 단색(200,200,200) — 색 기반 도구에는 쓸 수 없음 |
| 이상값 | NaN · Inf 없음 |

**좌표 단위는 mm 가 맞다.** 같은 폴더의 `Pipe Fitting.STEP` 이 `SI_UNIT(.MILLI., .METRE.)` 이고,
주축(PCA) 길이가 점군 337.1 × 203.6 × 149.9 mm, CAD 330.5 × 227.0 × 142.8 mm 로 같은 크기대다.
차이(최대 23mm)는 ① 합성 스캔이 한쪽 면만 담고 있고 ② CAD 값이 B-스플라인 제어점 기준이라
실제 표면보다 크게 잡히기 때문으로 보인다 — **절대 정확도 대조는 STL 로 변환해 Deviation 도구로 해야 한다**(3-3).

---

## 2. 도구별 실행 결과

엔진 경로(`VisionService` + 각 도구 `Execute`)로 직접 실행한 결과다.

| 도구 | 결과 | 비고 |
|---|---|---|
| **Height Map 생성**(서비스) | ✅ | 117×98, 정사투영 비닝. 유효 픽셀 3,632 |
| **PointCloud Cluster** | ✅ | 1덩어리 11,433점, 크기 **293.1 × 245.0 × 149.4 mm**, 중심(27.3, 99.4, 14.3), MmPerPx=1.0 |
| **Plane Fit** | ✅ | 평면 −0.097x −0.046y +0.994z −25.26 = 0, 인라이어 473/3,632, 평면도 146.4mm |
| **PointCloud Filter** | ✅ | 11,433 → 11,098 (voxel 1.0mm, 2.9% 감소) |
| **PointCloud Registration** | ✅ | 자기 자신 기준 ICP 1회 수렴, 평균오차 0.000mm (sanity 통과) |
| **PointCloud Deviation** | ✅ | 자기 자신 대비 편차 0.000mm, 불량 0/11,433 (sanity 통과) |
| **Height Slicer** | ✅ | 0~1000mm 슬라이싱 정상 |
| **Geometry 3D** | ⚠ 조건부 | 기본 설정으로는 실패. **수동 두 점 지정 시 293.642mm** — 좌표로 직접 계산한 293.64mm 와 일치 ✅ |
| **PointCloud MaskCrop** | ❌ **결함** | 11,433 → 2,123 점으로 잘못 잘림 (3-2) |

Plane Fit 의 평면도 146mm 는 결함이 아니다 — 파이프 피팅은 평면이 아니므로 전체에 평면을 맞추면 당연한 값이다.
평면 검사를 시연하려면 ROI 로 평평한 면 하나를 지정해야 한다.

---

## 3. 발견된 문제

### 3-1. ❌ [차단] 비격자 점군에서 [Height Map 생성] 이 아무 일도 하지 않는다

`MainViewModel.GenerateHeightMap()` (`VMS.VisionSetup/ViewModels/MainViewModel.cs:4019`)

```csharp
if (CurrentPointCloud == null || !CurrentPointCloud.IsOrganized)
    return;          // ← 조용히 끝난다. 상태 메시지도 없다
```

- 버튼 활성 조건은 `CanGenerateHeightMap() => PointCount > 0` (같은 파일 3948) 이라 **버튼은 눌린다.**
  누르면 아무 반응이 없고 사유도 표시되지 않는다.
- 점군을 열 때의 자동 생성도 `if (value != null && value.IsOrganized)` (3888) 로 막혀 있다.
- 그런데 **서비스 계층은 이미 지원한다** — `VisionService.GenerateHeightMap` (`VisionService.cs:1340`) 은
  비격자면 `OrthographicToDepthMap32F` 로 정사투영 비닝을 한다. 실제로 이 파일에서 117×98 맵이 정상 생성됐다.
  `CanGenerateHeightMap` 바로 위 주석도 *"정사투영 지원으로 비격자 점군도 허용"* 이라고 적혀 있다.
- **즉 UI 만 옛 제약을 들고 있다.** 뷰모델의 조기 반환을 걷어내면 해결된다(서비스는 손댈 것 없음).

**영향** — Height Map 이 없으면 `CurrentHeightMapMetadata` 가 비어 **Plane Fit · Geometry 3D 가
"3D 데이터 없음" 으로 실패**한다. 화면에서 이 파일로 3D 시험을 진행할 수 없다.

### 3-2. ❌ [결함] MaskCrop 이 비격자 점군 좌표를 마스크 픽셀과 1:1 로 가정한다

`PointCloudMaskCropTool.ComputeMaskScale` (`…/PointCloud/PointCloudMaskCropTool.cs:302`)

```csharp
if (!src.IsOrganized || src.GridWidth <= 1 || src.GridHeight <= 1)
    return (1f, 1f);   // "unorganized면 동일 공간 가정(1:1)"
```

점군 XY 가 **mm**(−153.9~139.2, −16.4~228.6)인데 마스크는 117×98 **픽셀**이다. 1:1 로 보면 마스크는
부품의 왼쪽 아래 한 귀퉁이(0~117mm, 0~98mm)만 덮는다. 실행 결과 **11,433 → 2,123 점(18.6%)** 만 남고,
메시지는 `coverage 100.0%` 로 **성공처럼 보인다**. 남은 점의 좌표 범위도 X −0.5~115.6 / Y −0.5~97.5 로
마스크 크기와 정확히 일치해 잘못 잘린 것이 확인된다.

**고치려면** Height Map 메타데이터가 이미 갖고 있는 정사투영 원점·mm/px 로 점→마스크 픽셀을 변환해야 한다
(3-1 과 같은 메타데이터를 쓰면 된다).

### 3-3. ⚠ [제약] CAD 참조가 STL 만 된다 — 제공된 파일은 STEP

`StlMeshLoader.LoadReferenceCloud` 는 확장자가 `.stl` 이 아니면 `.vpc` 로 간주해 읽는다.
`Pipe Fitting.STEP` 을 Registration 참조로 주면 **`Failed to load reference: Invalid VPC file format.`** 이 난다(실측).

CAD 대비 편차 검사(CAD Compare)를 시연하려면 **STEP → STL 변환본을 미리 준비**해야 한다.
도움말·매뉴얼에도 `.vpc`/`.stl` 로만 적혀 있어 문서와 동작은 일치한다(결함 아님, 준비물 문제).

### 3-4. ⚠ [주의] VMS 자체 저장 .vpc 는 XY 가 화소다 — 이 파일과 좌표계가 다르다

Mech-Mind 촬영 점군은 `new Vector3(srcCol, srcRow, z)` (`MechMindCameraAcquisition.cs:496`) 로,
**X/Y = 뎁스맵 화소 · Z = mm** 인 격자(organized) 점군이다. 저장 시 카메라 내부 파라미터를 함께 넣지 않으므로
불러오면 화소를 mm 로 환산할 근거가 없다(`PointCloudMeasurement.IsMetric = false`).

| | 이 파일(WeldTeach 합성) | VMS 촬영 저장본 |
|---|---|---|
| 격자 | 비격자 | 격자(organized) |
| XY 단위 | **mm** | **화소** |
| Z 단위 | mm | mm |
| UI Height Map | ❌ 막힘(3-1) | ✅ 자동 생성 |
| XY 치수 측정 | 그대로 mm | 수동 배율(mm/px) 필요 |

**시험 준비 시 어느 쪽을 쓸지 먼저 정해야 한다.** 둘은 장단점이 정반대다 —
이 파일은 좌표가 진짜 mm 지만 화면에서 못 돌리고, VMS 저장본은 화면에서 돌아가지만 XY 가 화소다.

### 3-5. ℹ [정보] Height Map 을 거치면 점의 68% 가 버려진다

11,433점 → 117×98 맵의 유효 픽셀 **3,632개**. 한 칸(약 2.5mm)에 여러 점이 들어가면 하나만 남기 때문이다.
점군을 직접 보는 도구(Cluster · Filter · Registration · Deviation)는 전체 11,433점을 쓰지만,
**Height Map 을 거치는 도구(Plane Fit · Geometry 3D)는 3,632점만 본다.** 정밀도가 필요한 시연에서는
이 차이를 알고 있어야 한다.

---

## 4. GS 시험 관점 권고

1. **3-1 을 고치고 시험에 들어갈 것.** 뷰모델 한 줄 제약을 걷어내는 일이고 서비스는 이미 지원한다.
   고치지 않으면 "저장 점군으로 3D 기능을 시험한다" 는 회신 내용이 **화면에서 재현되지 않는다.**
2. **시험용 점군은 격자(organized) VMS 저장본으로 준비하는 편이 안전하다** — 지금 코드에서 화면 흐름이
   끊기지 않는 유일한 경로다. 단 XY 가 화소이므로 **mm/px 배율값과 기대 결과표를 함께 제출**해야 한다(3-4).
   이 파일처럼 mm 좌표 점군을 쓰려면 3-1 수정이 전제다.
3. **MaskCrop 은 시험 범위에서 빼거나 3-2 를 고칠 것.** 지금은 잘못 잘라 놓고 성공으로 보고한다.
4. **CAD 대비 검사를 시연하려면 STEP 을 STL 로 변환**해 두어야 한다(3-3).
5. Plane Fit 은 평평한 면을 ROI 로 지정한 시나리오로 준비할 것 — 부품 전체에 평면을 맞추면 평면도가 146mm 로 나온다.
6. 시연 가능한 항목(현재 상태 그대로): **점군 로드 · 클러스터 치수 측정 · 필터 · 정합(ICP) · 편차 검사 ·
   높이 슬라이싱 · 3D 두 점 거리**. 이 중 3D 두 점 거리는 mm 정확도가 확인됐다(293.642mm, 직접 계산과 일치).

---

## 부록 — 검증 방법

임시 xunit 하네스로 `PointCloudData.LoadFromFile` → `VisionService.GenerateHeightMap` →
각 도구 `Execute` 를 화면 없이 실행했다(하네스는 리포에 남기지 않았다). 파일 파싱·CAD 대조는
파이썬(numpy)으로 별도 확인했다. 전체 실행 로그는 `%TEMP%\vpc_3d_probe.txt`.
