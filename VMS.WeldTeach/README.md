# VMS.WeldTeach — CAD 용접 경로 오프라인 티칭 (PoC)

`docs` 외부의 개발 명세서(`CAD_Welding_Path_Specification.pdf` v1.0.0)에 대한 **기술 검증(PoC) 프로젝트**.
목표는 전체 시스템에서 유일하게 검증되지 않았던 **CAD 커널(STEP B-Rep) 리스크 제거**다.
점군 취득·ICP 정합은 VMS 기존 자산(멀티뷰 스캔, Point Cloud Registration 툴)을 재사용하므로 PoC 범위에서 제외.

## PoC 범위

| 명세서 단계 | 포함 여부 | 구현 |
|---|---|---|
| Step 1 — STEP 로드 · B-Rep 렌더링 · Ray-Casting 엣지 피킹 | O | `CadKernelService` + `RayCaster` + Helix 뷰어 |
| Step 2 — 위상 추적 · C1(15°) 엣지 체이닝 | O | `EdgeChainService` |
| Step 3 — 6-DoF 토치 포즈(이등분 벡터) + ZYX 오일러 | O | `TorchPoseService` (JSON 내보내기 포함) |
| Step 3 — ICP 비전 정합 | X (기존 VMS 툴 재사용 예정) | — |
| 로봇 벤더 전송 | X (벤더 미정) | JSON 포즈 파일까지만 |

## 사용법

1. 실행 후 **[샘플 시편 생성]** — T-필릿 용접 시편(베이스 100×60×8 + 리브 60×8×30)이 STEP 으로 생성·로드된다. 또는 **[STEP 열기]** 로 실제 도면 로드.
2. 모서리에 마우스 호버 → 주황색 강조. 클릭 → 연속 모서리 자동 체이닝 + 경로 목록에 추가.
   **여러 모서리를 순차 클릭하면 경로가 쌓이고, 목록 순서(순번 뱃지·3D 라벨) = 용접 순서.**
   같은 경로의 모서리를 다시 클릭하면 해제. 목록에서 경로 선택 → 토치 화살선(하늘색)과
   6-DoF 포즈 표 표시. [전체 지우기]/항목 ✕ 로 정리.
3. **[경로 내보내기 (JSON)]** — 모든 경로를 용접 순서대로 저장 (CAD 좌표계 기준, ICP 의 `T_align` 적용 전).
4. **포즈 간격(mm)** — 로봇에게 넘길 목표점(웨이포인트) 사이 거리. 경로를 호 길이 기준으로
   균일 재샘플링해 포즈를 만든다 (예: 314mm 원형 심 — 1.5mm→211개, 8mm→40개).
   - 툴바 **기본 포즈 간격**: 새로 추가하는 경로에 적용, [전체 적용]으로 일괄 반영.
   - 패널 **선택 경로 간격**: 경로마다 개별 설정 (직선 넓게 / 곡선 좁게) — 그 경로만 즉시 재계산.
   - 각 경로의 현재 간격은 목록 요약 `@값`과 JSON(`spacingMm`)에 기록, 포즈 위치는 노란 점 표시.
5. 피킹 임계값·체이닝 각도·포즈 간격 옆 파란 ? 아이콘에 마우스를 올리면 도움말 팝업이 열린다.
5. 창은 VMS 공통 커스텀 크롬(WindowStyle=None + 자체 타이틀바/닫기 버튼), 기본 1536×864(FHD 80%).

헤드리스 검증: `VMS.WeldTeach.exe --selftest` → `%TEMP%\weldteach_selftest.log`
(샘플 생성→로드→위상→메싱→심 체이닝→이등분 벡터→포즈→GC 해머 3라운드까지 자동 확인,
마지막 줄 `SELFTEST OK`).

GUI 진단 모드: `VMS.WeldTeach.exe --open <step경로> [--pick <엣지Id>] [--capture <png경로>] [--stay]`
— GUI 와 동일한 로드 경로를 타고, `--pick` 은 클릭과 동일하게 엣지를 선택(체이닝+포즈),
캡처 지정 시 렌더 결과 PNG 저장 후 종료(`--stay` 시 유지).
연결성 분석: `--analyze <step경로> [엣지Id]` → `%TEMP%\weldteach_analyze.log` (끝점 간격 분포·체이닝 덤프).
트레이스는 `%TEMP%\weldteach_diag.log`, 로드 실패 상세는 `%TEMP%\weldteach.log`.

## 검증 결과 (2026-07-28)

- STEP 왕복(쓰기→읽기), 위상(1솔리드/11면/24엣지), 메싱, 엣지 이산화 정상.
- T-필릿 심 이등분 벡터 = (0, −0.707, 0.707) — 정확히 45°. 포즈 R−90/P−45/Y−90.
- GUI 로드·렌더링(캡처 검증)·연속 3회 진단 왕복 정상, 강제 GC 3라운드 안정.

## 기술 선택과 리스크

- **CAD 커널: NuGet `Occt.NET 7.9.0`** (OpenCASCADE 7.9 C++/CLI 래퍼, x64 전용).
  - **라이선스 리스크**: 패키지에 라이선스 명시가 없다(중국 개인/업체 배포, 기부 요청만 존재).
    PoC 용도로만 사용하고, **제품화 시에는 OCCT 원본(LGPL-2.1 + 예외) 직접 빌드 + 자체 C++/CLI 래퍼**
    (NativeVision 경험 활용) 또는 상용 Eyeshot 으로 교체할 것. `ICadKernelService` 인터페이스로 격리해 둠.
  - **배포 무게**: 네이티브 DLL 104개(≈230MB, ffmpeg/Qt 등 불필요 종속 포함). 제품화 시 자체 빌드로 축소.
  - **초기화 함정**: XS(STEP) 계열 DLL 은 이름 기반 동적 로드라 네이티브 폴더가 **프로세스 PATH** 에
    있어야 한다. `CadKernelService` 정적 생성자에서 처리(패키지 기본 `OcctConfiguration` 은 불충분).
  - **파이널라이저 결함 (치명)**: 래퍼의 GC 파이널라이저가 내부 참조(NoRelease) 객체까지 네이티브
    delete 를 호출해 **힙 손상(0xC0000374)·UI 행**이 발생한다 — 스택 덤프로 확인(2026-07-28).
    `OcctLifetime.Keep()` 으로 모든 OCCT 객체의 `DeleteOnFinalize` 를 꺼서 회피. 부작용으로
    로드당 소량(수백 KB~수 MB) 네이티브 누수 — PoC 허용, 자체 래퍼 교체 시 해소.
  - **벤더 홍보 팝업**: STEP 리더/라이터 생성 시 배포사(北京腾雪科技) 홍보 팝업
    (클래스 `Comet_PopupWindow`) 이 뜬다. 소유 스레드가 메시지를 펌프하지 않아 WM_CLOSE /
    ShowWindow / SetWindowPos 가 전부 무효(실측) — 소유 스레드와 무관하게 동작하는
    **DWM 클로킹**(`DwmSetWindowAttribute` + `DWMWA_CLOAK`, 자기 프로세스 창은 허용)으로
    화면에서 제거한다 (`VendorPopupSuppressor`, 0.1s 감시 스레드).
- 토치 법선은 **지점별 pcurve UV 평가** — 원통 등 곡면 심에서도 경로를 따라 방향이 회전한다
  (파이프 피팅 원형 심으로 검증, 2026-07-28). pcurve 가 없는 특수 면만 면 중심 법선으로 대체.
- **Helix LinesVisual3D 함정**: 보이는 상태에서 `Points.Add` 반복 시 점마다 라인 메쉬 전체를
  재생성해 O(N²) — 실파일(242엣지)에서 UI 가 14초+ 정지했다. 컬렉션을 채운 뒤
  **한 번에 교체 할당**할 것 (MainWindow 렌더 코드 참조).
- UI 는 VMS 본체 디자인 토큰(`Themes/` — Colors·Typography·Spacing·Controls)을 이식해
  다른 VMS 앱과 동일한 룩(Indigo/Slate 다크, ModernButton/Button.Subtle, 다크 DataGrid).
- 진행각(push/drag)·작업각 오프셋 파라미터, 로봇 벤더별 오일러 규약 변환은 명세 보강 후 구현.

## 다음 단계 (PoC 통과 후)

1. VMS 점군 파이프라인과 연결: 멀티뷰 스캔 점군 ↔ CAD 샘플링 점군 ICP → `T_align` 을 포즈에 적용.
2. 진행각/작업각 파라미터화 + 접근·후퇴 포인트 생성.
3. 대상 로봇 1종(협동로봇 우선) 선정 후 전송 드라이버.
4. CAD 커널 라이선스 확정(자체 OCCT 빌드 or Eyeshot).
