# GS 시험 PC(실증 PC) 사전 준비 체크리스트

작성 2026-10-26 · 개정 2026-10-29 · 시험 대상 **VMS v1.47.3** (동봉 Web 1.11.0) · 시험자료 동결본 `D:\30_Releases\GS_Freeze\v1.47.3\GS_TestData_v1.47.3.zip`

시험 PC = **실증 PC** (Basler acA2440-20gc · ADLink PCI-7432 장착, 라이선스 LIC-2026-0004 · 지문 `T61TV-FYA3G-74N81`).
순서대로 진행하고, 끝난 칸에 `x` 를 친다.

---

## A. 시작 전에 정할 것

- [ ] **시험 시작 버전을 v1.47.2 로 둔다** — 인앱 업데이트 시험이 "직전 공개 버전(v1.47.2) → v1.47.3" 이다.
      그래서 PC 는 **v1.47.2 로 준비**하고, 시험 첫 순서로 인앱 업데이트를 해 v1.47.3 으로 올린 뒤 나머지를 시험한다.
      (레시피 · 설정 · 라이선스 · 이력은 업데이트 후에도 그대로 남는다. v1.47.2 ↔ v1.47.3 은 검사 코드가 같아 판정 · 값이 같다 — 동결정보.md)
      → 이미 v1.47.3 이 깔려 있으면 제거 후 v1.47.2 설치 (MSI 는 내려 설치가 안 된다).
- [ ] **지금부터 시험이 끝날 때까지 공개 배포(release) 중지** — 새 버전이 나오면 업데이트 도착 버전이 바뀐다.
- [x] **시험용 라이선스를 새로 발급한다**(결정 10-26) — Production · 고객명 `BODA Vision AI (GS 시험)` · 실증 PC 지문 · 좌석 1 · 실행 만료 없음. 기존 LIC-2026-0004 파일은 B절에서 백업해 두고 시험 후 되돌린다 (발급은 `docs/licgen_admin_manual.md`)

## B. 현장 데이터 백업 (실증 PC 를 공장에서 빼기 전)

시험 뒤 공장 상태로 되돌리기 위해 **폴더째** USB 로 복사한다.

- [ ] `%LOCALAPPDATA%\BODA VISION AI\` 전체 — 현장 레시피(`Recipes\`) · `system_config.json` · `layout_config.json` · `inspection_history.db` · 로그
- [ ] `C:\ProgramData\BODA\VMS\` 전체 — 특히 현장 라이선스 `license.lic`(LIC-2026-0004). Web 서버를 깔면 Web DB 도 여기 생긴다
- [ ] 현재 설치 버전 · AppSetup 화면(카메라 · IO 보드 · 서버 주소)을 사진으로 남긴다
- [ ] ⚠ 사무실에서 리허설할 때 실증 PC 가 **운영 Web 서버(dev PC 5292)에 붙지 않게** 한다 — 시험 검사 이력이 운영 DB 에 섞인다 (D절에서 서버 주소를 바꾼다)

## C. 설치

- [ ] Windows: 절전 · 화면 꺼짐 끄기, Windows 업데이트 **일시 중지**(시험 중 자동 재시작 방지)
- [ ] D: 드라이브 확인 (시험자료가 `D:\GS_TestData` 에 있어야 한다), 여유 공간 5GB 이상
- [ ] Basler pylon Runtime · ADLink DASK x64 드라이버 설치 확인
- [ ] **VMS v1.47.2** 설치 — 설치 구성 선택에서 **"Web 서버 포함"**
- [ ] 설치 폴더에 `VMS.DeepLearning.exe` · `scripts\` 가 **없는지** 확인 (AI 학습 범위 제외 근거)
- [ ] AppSetup 7단계: 시험용 라이선스(`BODA Vision AI (GS 시험)`) [라이선스 파일 가져오기] → 상태 **정상** · 고객명 확인
- [ ] Web 관리자 계정 생성(시스템 설정 마법사) → 브라우저 `http://localhost:5292` 로그인 확인
- [ ] Web [SW 라이선스] 화면에 라이선스 · 좌석 표시 확인

## D. 시스템 설정 (AppSetup)

- [ ] 카메라: Basler 1대 등록 · 연결 확인 · 라이브 영상 확인
- [ ] 카메라 설정(노출 · 화이트 밸런스)은 **10-11 시험자료 촬영 때 값 그대로** — Color 레시피가 그 색으로 학습됐다
- [ ] Basler 트리거: Wait Trigger 를 **BitRisingEdge** 로 (자동 전환 안 됨)
- [ ] IO 보드: PCI-7432 연결, **DI · DO 모두 채널 0번부터** (DO 를 32부터 매기면 출력이 조용히 실패)
- [ ] PLC: **내장 시뮬레이션 모드**
- [ ] 서버 연동: Web 서버 주소 = `http://localhost:5292` (VMS → Web 연동 시험용. 단독 모드 시험 때만 비운다)
- [ ] MLOps 연동(서버 주소 · 라인 토큰): **비움**
- [ ] 시험원용 계정(VMS 로그인 · Web)과 비밀번호를 정리해 전달표로 만든다

## E. 시험자료 넣기

- [ ] 동결본 `GS_TestData_v1.47.3.zip` (1,914,957,244 B) 을 USB 로 옮긴다
- [ ] zip 해시 확인 — `6e4e96254c887fc7b165dacff60b1a588a5132c0b96e868749ed154b92008f13`
      ```powershell
      (Get-FileHash .\GS_TestData_v1.47.3.zip -Algorithm SHA256).Hash
      ```
- [ ] **탐색기 또는 `Expand-Archive` 로 `D:\` 에 푼다** → `D:\GS_TestData` (Git Bash `unzip` 금지 — 한글 이름이 깨진다)
      ```powershell
      Expand-Archive .\GS_TestData_v1.47.3.zip -DestinationPath D:\
      ```
- [ ] 파일 479개 해시 확인 — 아무것도 출력되지 않으면 정상
      ```powershell
      cd D:\GS_TestData
      Get-Content SHA256SUMS.txt -Encoding UTF8 | ForEach-Object {
        $h, $p = $_ -split ' \*', 2
        if ((Get-FileHash -LiteralPath $p -Algorithm SHA256).Hash -ne $h.ToUpper()) { "불일치: $p" }
      }
      ```

## F. 레시피 가져오기 + 카메라 재연결

레시피 25개는 dev PC 의 카메라 ID 로 만들어져 있다. 실증 PC 에서 처음 열면 레시피마다 **"미등록 카메라 재연결"** 창이 뜬다 —
미리 한 번씩 열어 연결해 두면 시험 중에는 뜨지 않는다. (재연결은 VMS 레시피 저장 폴더의 사본에 저장되고, `D:\GS_TestData` 원본은 바뀌지 않는다)

- [ ] VisionSetup → Recipe Manager → **[Import]** 로 아래 25개를 가져온다
- [ ] 하나씩 **[Load]** → 재연결 창에서 **이 PC 의 Basler** 선택 → 상태 표시줄 "재연결하고 저장했습니다" 확인
- [ ] 다시 [Load] 해서 창이 안 뜨는지 확인

| # | 레시피 (`D:\GS_TestData\...`) | 재연결 | 완료 |
|---|---|---|---|
| 1 | `Blob\recipe\recipe_GS_Blob.json` | Basler | [ ] |
| 2 | `Measure\recipe\recipe_GS_Measure.json` | Basler | [ ] |
| 3 | `DataMatrix\recipe\recipe_GS_DataMatrix.json` | Basler | [ ] |
| 4 | `OCR\recipe\recipe_GS_OCR.json` | Basler | [ ] |
| 5 | `OCV\recipe\recipe_GS_OCV.json` | Basler | [ ] |
| 6 | `Color\recipe\recipe_GS_Color.json` | Basler | [ ] |
| 7 | `ColorExtract\recipe\recipe_GS_ColorExtract.json` | Basler | [ ] |
| 8 | `Preprocess\recipe\recipe_GS_Preprocess.json` | Basler | [ ] |
| 9 | `PatternMatch\recipe\recipe_GS_PatternMatch_Fitting.json` | Basler | [ ] |
| 10 | `Align\MatchAlign\recipe\recipe_GS_MatchAlign.json` | Basler | [ ] |
| 11 | `Align\MultiStepAlign\recipe\recipe_GS_MultiStepAlign.json` | Basler | [ ] |
| 12 | `ImageRectify\recipe\recipe_GS_ImageRectify.json` | Basler | [ ] |
| 13 | `Result\recipe\recipe_GS_Result.json` | Basler | [ ] |
| 14 | `PolarUnwrap\recipe\recipe_GS_PolarUnwrap.json` | Basler | [ ] |
| 15 | `PhotometricStereo\recipe\recipe_GS_PhotometricStereo.json` | Basler | [ ] |
| 16 | `DeepLearning\Classification\recipe\recipe_GS_DL_Classification.json` | Basler | [ ] |
| 17 | `DeepLearning\Detection\recipe\recipe_GS_DL_Detection.json` | Basler | [ ] |
| 18 | `DeepLearning\InstanceSeg\recipe\recipe_GS_DL_InstanceSeg.json` | Basler | [ ] |
| 19 | `DeepLearning\SemanticSeg\recipe\recipe_GS_DL_SemanticSeg.json` | Basler | [ ] |
| 20 | `DeepLearning\Anomaly\recipe\recipe_GS_DL_Anomaly.json` | Basler | [ ] |
| 21 | `DeepLearning\OCR\recipe\recipe_GS_DL_OCR.json` | Basler | [ ] |
| 22 | `DeepLearning\Ensemble\recipe\recipe_GS_DL_Ensemble.json` | Basler | [ ] |
| 23 | `DeepLearning\FaultTolerance\recipe\recipe_GS_FaultTolerance.json` | Basler | [ ] |
| 24 | `3D\recipe\recipe_GS_3D.json` | **안 함** (아래) | [ ] |
| 25 | `3D\LineFit\recipe\recipe_GS_3D_LineFit.json` | **안 함** (아래) | [ ] |

- [ ] **3D 레시피 2개는 재연결하지 않는다** — 3D 카메라는 반입하지 않고 저장 점군(.vpc)으로 시험한다.
      열 때마다 "미등록 카메라 참조" 경고가 뜨는 것이 정상이며, 시험 안내서에 이유를 적어 둔다.
      (Basler 로 연결하면 2D 카메라가 3D 스텝을 맡는 어색한 구성이 된다)
- [ ] FaultTolerance 레시피는 손상 · 0바이트 모델을 일부러 넣은 시험 — 로드 시 오류 표시가 **정상**

## G. 리허설 (사무실에서, 공장 반출 전)

- [ ] 저장 이미지: 그룹 2~3개를 골라 배치 테스트 결과 CSV 가 `expected\expected_results.csv` 와 같은지 확인 (Blob · Measure · DL Classification 권장)
- [ ] 3D: `3D` 레시피로 [3D 데이터 열기] → Run, 기대 결과표와 대조
- [ ] 라이브 촬영: 인쇄 타겟 · 피팅 실물로 촬영 → 판정 확인
- [ ] IO 보드: 외부 트리거(DI) → 촬영 → 판정 → 경광등 OK/NG 점등, **Busy ON→OFF 토글, 연속 10 사이클 밀림 없음** (Release 설치본으로)
- [ ] AUTO RUN 연속 운전 · WO 집계 확인
- [ ] Web: 검사 결과가 대시보드 · 생산 이력에 올라오는지 확인
- [ ] 인앱 업데이트: 무선랜 동글로 인터넷 연결 → `api.github.com` 접속 확인
      (실제 업데이트까지 해 봤다면 **v1.47.2 로 되돌려 놓는다** — 제거 후 v1.47.2 재설치, 레시피 · 설정은 남는다)

## H. 반출 · 시험 당일

- [ ] 지참물: 실증 PC · Basler 카메라 · 렌즈 · 조명 · 경광등 · 하이박스 · 인쇄 타겟 · 피팅 실물 · 무선랜 동글 · 시험자료 zip USB · 계정 전달표
- [ ] 시험 첫 순서: 인앱 업데이트 v1.47.2 → v1.47.3 → 메인 화면 버전 1.47.3 확인
- [ ] 업데이트 후 레시피 1개 Load → 재연결 창 안 뜨는지 확인

## I. 시험 후 복구

- [ ] B절 백업으로 `%LOCALAPPDATA%\BODA VISION AI\` 를 되돌린다 (시험 레시피는 지워도 된다)
- [ ] 서버 연동 주소를 운영 Web 서버로 되돌린다
- [ ] 실증 PC 에 Web 서버가 필요 없으면 서비스 `BodaVmsWeb` 을 멈추거나 Web 없이 다시 설치
- [ ] 라이선스를 현장용 LIC-2026-0004 로 되돌린다 (B절 백업의 `license.lic` → `C:\ProgramData\BODA\VMS\`)
- [ ] 공개 배포 재개
