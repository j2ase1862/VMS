# GS 인증 관련 문서

VMS 솔루션 (BODA Vision AI) 의 한국 TTA GS(Good Software) / ISO/IEC 25051 인증 대응 자료. 심사관 / SI 인계 / 운영 인수에 사용.

## 폴더 구조

```
docs/gs/
├── README.md            ← 이 파일 (색인)
├── VMS_*.docx           ← GS 제출물 5종 (아래 생성기로 재생성)
├── screenshots/         ← 매뉴얼·제품설명서에 삽입되는 선별 캡처
├── guides/              ← 인증·운영 가이드 문서 (md)
└── pipeline/            ← 제출물 docx 생성 스크립트 + 중간 산출물
```

## 제출물 (docx)

| 파일 | 생성기 |
|------|--------|
| `VMS_사용자매뉴얼_v2.0.docx` (+ 동명 `.pdf` 내보내기본) | `pipeline/parse_manual.py` → `pipeline/gen_user_manual.js` |
| `../manuals/BODA-VMS-AI-Tools-Manual.html` (별책 — AI 학습 도구, 인증 범위 외) | 수동 편집 HTML (본편 6장 분리, 2026-09-08). docx 생성기 없음 — 필요 시 브라우저 인쇄로 PDF |
| `VMS_제품설명서_v1.0.docx` | `pipeline/gen_product_description.js` |
| `VMS_OSS_라이선스_확인서_v1.0.docx` `VMS_GS_신청서_템플릿_v1.0.docx` `VMS_GS_신청_체크리스트_v1.0.docx` | `pipeline/gen_gs_supporting_docs.js` |

**docx 를 직접 편집하지 말 것** — 재생성 시 유실된다. 매뉴얼 본문은
`docs/manuals/BODA-VMS-User-Manual.html`, 그림·표는 생성기 스크립트가 진실이다.

재생성:

```powershell
cd docs/gs/pipeline
python parse_manual.py    # 매뉴얼 HTML → _manual_blocks.json
node gen_user_manual.js   # → ../VMS_사용자매뉴얼_v2.0.docx
```

## 매뉴얼 반영 대기 — SW 업데이트 로그

사용자에게 보이는 기능이 master 에 머지되면 **그때마다 아래 표에 한 줄 기재**한다.
매뉴얼을 한꺼번에 갱신할 때 코드를 다시 파헤치지 않기 위한 장부다.

- 기재 시점: 기능 PR 머지 직후 (릴리즈 발행 시 몰아서 적어도 됨)
- 기재 내용: PR 번호 · 매뉴얼에 들어갈 요지(사용자 관점 한 줄) · 들어갈 절(§)
- 매뉴얼 갱신 시: HTML 본문(`docs/manuals/BODA-VMS-User-Manual.html`)에 반영하고
  §8.4 변경 이력에 요약 추가 → docx 재생성 → 아래 표 비우기 (반영 완료 기준선 갱신)
- **상세 기록은 [`manual_change_history.md`](manual_change_history.md)** — 매뉴얼 검토 기간(2026-09-02~) 동안의
  변경을 화면·문구·스크린샷·대상 절 단위로 풀어 쓴 장부. 아래 표는 요지, 상세는 그 문서 (한꺼번에 반영용)

**반영 완료 기준선: SW v1.37.3 / Web v1.10.2 — 2026-09-15 반영(§7.1 로그인 유지 운용 기준·§7.2 화면 공통 동작·§7.4 안돈 보드 여는 방법·§7.7 생산 시작 후 제품·라인 잠금·§7.10 키오스크 안내/세션 종료·§7.13 불량 코드 잠금·§7.15 감사 로그 야간 조회·§7.16 PDF 보고서/파레토 기간·§10.1 업데이트 후 Web 접속 불가 신설·§10.3 안돈 로그인 화면 신설·§10.3 포트 5292 정정·§10.4 연결 신호 거부·§11.4 v1.37.1~v1.37.3 + Web v1.10.1·v1.10.2 · §8.5 라인 번호 대역(다중 서버) · §5.11 카메라 캘리브레이션 신설 · §10.2 캘리브레이션 트러블슈팅 3건 · docx 재생성)** (직전 기준선 SW v1.37.0 / Web v1.10.0 — 2026-09-14 반영(§4.2.10 MLOps 비활성 칩 신설·§4.3 MLOps 카드(원본·비활성 사유)·§7.6 검사 사진 열람 권한·§8.3 업로드 큐(검사 시각·보관함·사진 자동 축소)·§8.5 라인 번호 0~99·§9.4 백업 동작·§10.1 트러블슈팅 4건 신설·§11.4 v1.35~v1.37 + Web v1.10.0 · 별책 §2 웹에서 학습하기·학습 결과 파일 · 설정 마법사 표 3행(Client Index·MLOps 주소·API Key) · 신규 캡처 2장(`vms_ctl/status_mlops_issue_chip.png`·`vms_ctl/sec_system_log_wo_warning.png`, ControlCapturer 장면 S4·S5 추가) · docx/PDF 재생성)**) (직전 기준선 SW v1.33.0 / Web v1.9.0 — 2026-09-10 §9~§12 반영(#429 D-FINE·#437 레지스트리 참조·#439/#440 등록·내려받기·#441/#442 RF-DETR-seg·#446 MLOps 수집·#427/#445/#447 수정, 본편 §3 고급 설정·§4.3·§5.10 신설·§11.4 + 별책 §1·§2.1·§3·§4 v1.1 + 스크린샷 21_dlg_imagesave·51_rfdetr_seg_panel·AppSetup 2단계 MLOps 컨트롤) (직전 기준선 v1.29.0 / 2026-09-04 Match Align 원점·각도(#424) + Web 로그인 유지(Web #96)) (직전 기준선 v1.28.1 / 2026-09-04 단독 모드 로컬 생산이력(#417) 반영) (직전 기준선 v1.24.0 / 2026-09-01 매뉴얼 v2.0 전면 개편 (앱별 11장 구성: 개요→설치→AppSetup→VMS 운전→VisionSetup→DeepLearning→Web→워크플로→관리자→트러블슈팅(5절 확장)→부록. 산출물 `VMS_사용자매뉴얼_v2.0.docx`. 생성기: 캡처는 '첫 문단 뒤' 삽입 규칙 + 삽입/누락 검증 로그 + 마법사 §3.2·관리자 다이얼로그 §4.9 앵커 이동 — ⚠ 앵커 = 4장/5장 h2 제목 문자열, 제목 변경 시 gen_user_manual.js 동기화 필수) (§3.2.9 예측 칩·§3.9 Feature Match 학습/다중 인스턴스·복사 3종·얼라인 템플릿·재연결 창·§4.9 Web 복사·§8.4 이력 반영. ⚠ gen_user_manual.js 의 WIZARD_BEFORE 앵커가 HTML §2.6 제목 문자열 — 제목 변경 시 앵커도 함께))
(직전 기준선 v1.13.0 의 스크린샷 안내는 이력에서 유지 — 데모 캡처 절차 web_capture_partial.js/seed_demo.js, 운영 DB 무접촉)

| PR | 매뉴얼에 들어갈 내용 | 대상 절 |
|----|--------------------|--------|
| #500 (v1.39.1 이후) | **캘리브레이션 창의 VMS 프레임 받기가 실제로 동작** — 예전에는 공유 메모리를 읽기만 해서 창을 열고 처음 누르면 늘 빈손이었다(VMS 창으로 가 Grab 을 누르고 돌아와야 했다). 이제 **VMS 에 "지금 찍어 달라" 고 요청**한다. **버튼 이름 `Receive Frame from VMS` → `Grab from VMS`** (실제 촬영이 일어나므로) + 안내 문구 신설 + 운전·라이브 중 거절 사유 표시. 받은 사진의 카메라를 알 수 있어 **그 카메라의 스텝만 자동 체크**된다 → §5.11 의 "VMS 화면으로 했으면 전체 체크" 설명에서 **VMS 를 빼고 파일만 남길 것**. 상세: manual_change_history.md §26 | §5.11 §11.4 |
| #495 #496 #497 (v1.39.1) | **비전 설정 결함 3건 (2026-09-16)** — ① **캘리브레이션 창에서 카메라 촬영이 되게** 함: 메인 화면이 카메라를 연결해 둔 채여도 촬영된다(예전엔 연결을 끊어야 했고, 안 끊으면 눌러도 무반응처럼 보였다). 못 여는 상황은 팝업으로 사유·할 일 안내 → **§5.11 의 '연결을 끊으라' 는 종전 안내가 있으면 삭제**. ② **VMS 실행 중 이미지가 한 번에 안 들어오던 문제** 수정(주기적 재발) — 새 프레임이 없으면 옛 이미지를 새 것인 양 넘기지 않고 사유를 표시 → §10.2 트러블슈팅 신설. ③ ⚠ **FeatureMatch 각도 전달** — ROI 가 이동만 따라가고 기울지 않아 부품이 돌면 **다른 자리를 보고 있었다**. 이제 위치·기울기 모두 따라간다(회전 ROI 로 그리면 화면 사각형도 기움). 각도를 안 내보내는 소스(Blob 등)는 종전과 동일. **FeatureMatch 로 ROI 를 따라가게 한 레시피는 판정이 달라질 수 있고 새 결과가 맞다 — 기준값 재확인 필요** → §5.x 도구 연결 + §10.2. ④ (함께) 운전(AUTO RUN)에서 Shape Match/Color/OCV 의 **탐색 영역이 안 따라가던 공백** 해소 — 설정 화면과 운전이 이제 같다. ⑤ **단독 모드에서도 MLOps 주소·토큰 입력 가능**(예전엔 Web 입력란과 함께 잠겨 '재설치해야 하는 줄' 알았다) + **설정 마법사 다시 여는 법**(VMS 메인 [시스템 설정] · 시작 메뉴 'BODA VMS 설정 마법사') 명시 → §3.x 마법사 2단계. 상세: manual_change_history.md §25 | §5.11 §5.x(도구 연결) §3.x(마법사 2단계) §10.2 §11.4 |
| (반영 완료 2026-09-15) 캘리브레이션 적용 | **캘리브레이션 적용이 어디에 반영되는지 보이도록** — Camera Calibration 창의 Apply 구역에 ① 누르면 일어나는 세 가지(레시피 저장 → 스텝 Resolution 갱신 → 파일 저장) 안내 ② 지금 레시피에 저장돼 있는 캘리브레이션 요약 줄 ③ **Resolution 을 갱신할 스텝 목록**(체크 + `현재값 → 적용값`, 카메라 직접 촬영이면 그 카메라 스텝만 기본 선택 — 다중 카메라 레시피 오적용 방지)을 추가. 예전에는 Apply 가 **스텝 Resolution 을 아예 갱신하지 않아** 왼쪽 Steps 표에 결과가 보이지 않았고, 표시 형식도 `F3` 이라 고배율 값이 `0.000` 으로 뭉개졌다 → 이제 갱신 + `F5` 표시. [Clear Calibration from Recipe] 안내도 정정(지워도 **스텝 Resolution 은 남아** mm 환산이 계속된다 — 예전 문구 '픽셀 단위로 돌아간다' 는 틀린 설명). 상세: manual_change_history.md §24. **본편 §5.11 5단계 전면 개정 + §5.6 연결 + §10.2 트러블슈팅 1건 신설 + §11.4 반영 완료**, 스크린샷 `53_calib_apply.png` 신규(원형 타겟 20점 검출 + Apply 구역, 생성기 POST 등록) | §5.11 §5.6 §10.2 §11.4 |
| (반영 완료 2026-09-15) 캘리브레이션 | **카메라 캘리브레이션 사용법 신설 + 도구 도움말 정정** — Image Rectify 도움말이 존재하지 않는 항목 3개를 설명하고 실제 항목(ApplyHomography)은 비어 있던 것을 바로잡고, Calibration Manager 창에 없던 물음표 도움말 11항목을 신설. 본편 §5.11 에 창 여는 법·방식 3종 선택·체커보드 절차(안쪽 교차점 수·칸 크기 실측·10~20장)·결과 읽는 법·레시피 적용·Image Rectify 와의 관계 표, §10.2 트러블슈팅 3건. ⚠ 절 번호는 밀지 않고 §5.11 로 덧붙임(생성기가 5.10 제목 문자열을 키로 씀). 상세: manual_change_history.md §23 | §5.11 §10.2 §11.4 |
| (반영 완료 2026-09-15) 운영 규칙 | **다중 Web 서버 현장의 라인 번호 대역** — 공장 네트워크 공사 제약으로 서버를 여러 대 두고 라인을 나누어 붙이는 구성이 실제로 발생한다. 라인 번호는 그 서버 DB 안에서만 유일해, 규칙 없이 쓰면 서버마다 "1번 라인" 이 생겨 나중에 합칠 수 없다. **십의 자리 = 서버 번호**(0~9 시험·예비, 10~19 1번 서버, … 90~99 9번 서버)로 정함. 서버를 나누면 대시보드·작업지시·품질 분석·백업·관리자 계정·기준 정보도 서버마다 따로. 중앙 서버 한 대 구성도 그대로 가능(라인 100대). 상세: manual_change_history.md §22 | §8.5 |
| (반영 완료 2026-09-15) Web #154 + **운영 정책** | **안돈 보드 새 탭이 로그인을 잃던 문제 (Web v1.10.2, 2026-09-15)** — 현장 모니터링 → 안돈 보드로 들어가면 새 탭이 열리는데 "로그인 유지" 를 끈 세션에서는 미인증으로 열렸고, 로그인해도 대시보드가 떠서 **끝내 안돈 보드에 도달하지 못했다**. ① 로그인이 그대로 따라가는 방식으로 새 탭을 열도록 수정(메뉴 + 대시보드 "전체 보기" 두 경로) ② 로그인 후 **원래 가려던 화면으로 복귀**(북마크·새로고침·주소 직접 입력도 함께 해결). **운용 정책(사용자 결정): 안돈 보드를 벽걸이 모니터에 상시 표시하는 PC 는 "로그인 유지" 를 켜고 운용**한다 — 브라우저를 닫았다 켜는 경우는 이 수정으로 덮이지 않기 때문. 공용·사무용 PC 는 기본값(끄기) 유지. 상세: manual_change_history.md §21 | §7.1 §7 (안돈) §10.1 §11.4 |
| (반영 완료 2026-09-15) #478 | **업데이트 후 Web 접속이 끊기던 문제 (v1.37.2, 2026-09-15)** — 업데이트가 Web 서버를 끄지 않은 채 파일을 갈아 끼워 Web 폴더가 반쪽으로 남았고, 서버가 켜지자마자 죽어 설정 마법사의 [서비스 시작] 으로도 복구되지 않았다(설치 파일 재실행으로만 복구). 이제 ① 설치 전에 Web 서버를 확실히 내리고 ② 그래도 안 뜨면 설치 파일로 자동 복구하며 ③ 실패 시 설치 파일을 남기고 사유를 로그에 적는다. **⚠ 배포 순서: 이미 v1.37.1 이 깔린 라인은 다음 업데이트 때 한 번은 예전 방식으로 진행되므로 업데이트 후 Web 접속 확인 필요.** 상세: manual_change_history.md §20 | §10.1 §11.4 |
| (반영 완료 2026-09-15) Web #144~#152 · VMS #475 | **전수 점검 낮음 68건 조치 (2026-09-15)** — 사용자에게 보이는 변화만 추리면: ① 화면 확대가 막혀 있던 것을 풀고 다크 모드 작은 글씨 대비를 올림, 관리 화면에 로그인 요구, 권한이 없을 때 "로그인하라" 대신 "권한이 없다" 안내 ② 키오스크 입력에 라벨을 붙이고 오류를 영문 원문 대신 할 일로 안내(429 → "1분 뒤 다시") ③ 세션·보전 이력이 200건에서 잘리면 그 사실을 화면에 표시 ④ 마스터 수정 시 중복을 "서버 오류" 가 아니라 이유와 함께 거절, 생산이 시작된 작업지시의 제품·라인은 잠금 ⑤ 파레토 조회 기간 기본 30일·최대 366일 ⑥ 라인 PC 가 정전으로 사라지면 작업자 세션을 마지막 하트비트 시각으로 자동 종료, 퇴사 처리해도 종료 ⑦ **PDF 보고서가 실제로는 생성 불가였던 것 수정**(QuestPDF 라이선스 미설정 — Community 로 지정, 회사 매출 기준 확인 필요) ⑧ **감사 로그가 KST 00~09시에 조회되지 않던 것 수정**. 상세: `D:/Temp/web-audit-20260914/remaining_2026-09-14_evening.md` | §4 §7 §9 §10.1 §11.4 |
| (반영 완료 2026-09-14) #462 #464~#468 | **Web 연동 정합성 마무리 (v1.37.0, 동봉 Web v1.10.0)** — ① 다른 라인에 배정된 WO·다른 WO 소속 Lot 으로 올라간 검사를 현장에 경고(수량 미집계 사유 표시) ② 큐에 묶였다 올라온 검사가 **검사한 시각**으로 기록(예전엔 도착 시각 — 교대·일별 집계 어긋남) ③ 정상 종료 시 작업자 세션도 함께 종료(종료 통지 키 누락 수정) ④ 이미지 업로드: 받을 수 없는 응답은 보관함으로, 30MB 초과는 JPEG→썸네일로 낮춰 전송 ⑤ 16bit 카메라에서 0 으로만 기록되던 이미지 품질 지표 수정, 설정 마법사 라인 번호 범위 0~99 ⑥ Web: 검사 이미지 로그인 필요·강등/초기화 즉시 반영·검사 0건이면 지표 빈 값·백업 상태 /health. 상세: manual_change_history.md §19 | §3.2 §4 §7 §9 §11.4 |
| (반영 완료 2026-09-14) MLOps §14~§17 | **MLOps 학습 워커 · 학습 곡선 · 비활성 사유 · 원본 전송** — 별책 §2 '웹에서 학습하기'(워커 설치·토큰·작업 제출·승격)와 '학습이 끝나면 생기는 파일'(metrics.json·curves.png), 본편 §4.2.10 MLOps 비활성 칩·§4.3 카드(결과 그래픽 없는 원본·빨간 안내 문구). 상세: manual_change_history.md §14~§17. **스크린샷 잔여 없음** — `vms_ctl/status_mlops_issue_chip.png`·`vms_ctl/sec_system_log_wo_warning.png`·`mlops/worker_install_wizard.png`·`mlops/worker_register_dialog.png`·`mlops/worker_card.png` 전부 반영 | 별책 §2 §4.2 §4.3 §11.4 |
| (반영 완료 2026-09-14) #459 | **Web 연동 계약 결함 3건 (v1.36.0)** — ① 작업자 로그인·작업지시 목록·Lot 자동채움·예측 위젯이 Web API 키를 보내지 않던 것 수정(서버를 키 강제 모드로 바꾸기 전 **전 라인 배포 필요**), 로그인 실패 메시지가 "사번/PIN 오류" 와 "서버가 이 PC 의 키를 거부" 로 구분됨 ② Web 이 라인을 모른다고 답하면(404) 검사 결과를 버리던 것 → 30분까지 재시도, 그래도 안 되면 격리 + 감사 로그 ③ 검사 이미지 업로드만 보안 정책을 우회하던 것 수정 — 원격 http + Production 모드면 업로드가 꺼지고 로그에 사유. 상세: manual_change_history.md §18 | §4.9 (Web 연동 설정) §7.6 §9 (트러블슈팅) §11.4 |
| (반영 완료 2026-09-10, 문구) #452 | **AI 학습 도구 기본 제외** — 설치 구성 선택 화면 "AI 학습 도구 포함" 기본 해제(라벨링·학습은 MLOps 가 기본, 서버 없는 PC 만 체크), 업그레이드는 이전 선택 유지. 본편 §6 안내·§11.4·별책 안내 반영 + `71_msi_weboption.png` 재캡처 완료(체크 해제 상태, `tools\capture-msi-weboption.ps1`) | §2.1 §6 §11.4 별책 |
| (반영 완료 2026-09-10) #446 | **불량 이미지 MLOps 학습 데이터 수집** — VMS 메인 이미지 저장 설정 창에 "MLOps 학습 데이터 수집" 카드 신설: [NG 이미지 MLOps 전송] 체크 + 양품 샘플 비율(N 장에 1 장, 기본 200, 0 = 양품 안 보냄). 켜면 NG 사진이 원본 해상도로 MLOps 데이터 풀에 올라가 웹에서 라벨링·재학습 재료가 됨(Web 생산 이력 전송과 별개). 전제: 설정 마법사 2단계 고급 설정의 MLOps 서버 주소·라인 토큰 — 없으면 카드가 안내 문구로 알려 줌. 검사 택트 영향 없음, 서버가 꺼져 있어도 큐에 쌓였다가 나중에 감. 상세: manual_change_history.md §12 | §4 (이미지 저장 설정) §3.2 (고급 설정 한 줄) §11.4 |
| (반영 완료 2026-09-10) #441 #442 | **RF-DETR-seg 인스턴스 분할 도구(Apache 2.0) 신설** — VisionSetup Deep Learning 팔레트에 [RF-DETR-seg] 추가(YOLOv8-seg 와 같은 일, 라이선스만 다름). 파라미터: Model Path(레지스트리 참조 가능)·Input Size(모델을 열면 ONNX 값으로 자동 설정되고 칸 잠김)·Confidence·Max Instances·오버레이·Output Mask Image. IoU·마스크 임계 없음(NMS 미사용). 학습은 별책 §2 학습 스크립트 `train_rfdetr_seg.py`(세그멘테이션 데이터셋 자동 매칭). 덤: YOLOv8-seg 의 Output Mask Image 가 레시피 재열기 시 꺼지던 결함 수정. 상세: manual_change_history.md §11 | §5 (Deep Learning 도구 표·소절) 별책 §2 §11.4 |
| (반영 완료 2026-09-10) #439 #440 | **AI 학습 도구 ↔ MLOps 레지스트리 연동** — 학습 패널 [레지스트리에 등록](학습 직후 ONNX 를 Candidate 버전으로 올림, 웹 계정 로그인 필요 — 현재 Web Admin 만 통과) + Export 구역 [웹 데이터셋 내려받기](웹에서 라벨링한 데이터셋의 판을 골라 받으면 학습 데이터셋 경로가 자동 설정). 설정 마법사 2단계 고급 설정 MLOps 서버 주소·웹 서버 주소 필요. 상세: manual_change_history.md §11 | 별책 (학습 패널·Export) §3.2 (설정 마법사 고급 설정) §11.4 |
| (반영 완료 2026-09-08) | AI 학습 도구 GS 범위 제외 — 본편 6장을 별책 `BODA-VMS-AI-Tools-Manual.html` 로 분리(§2 학습 환경 준비에 #429 D-FINE 사전 준비·학습률·증강 반영), 본편 §6 안내 절·§11.4·목차 갱신. `71_msi_weboption.png` 재캡처 완료(2026-09-10, #452) | §6 §11.4 별책 |
| (반영 완료 2026-09-10) #429 | Detection 학습 기본 백본을 D-FINE(Apache 2.0) 으로 전환 — DeepLearning 앱에서 Detection 데이터셋을 열면 자동 스크립트가 `train_dfine.py`(이전 `train_yolo.py`), 사전 준비 pip 목록이 `torch torchvision transformers onnx` 로 바뀜 (ultralytics 불필요). 증강 패널 안내 문구 변경 (mosaic/mixup 은 YOLO 전용). VisionSetup Detection 도구는 D-FINE / YOLO ONNX 를 자동 판별 — 사용자 조작 동일. 상세: manual_change_history.md §9 | §6 (DeepLearning 학습·사전 준비) §5 (Detection 도구) §11.4 |
| (반영 완료 2026-09-10) #427 | (버그 수정·본문 변경 없음) 회전 ROI(RectangleAffine)로 학습한 Feature Match 가 레시피 재로드 후 [Show ROI] 에서 축 정렬 Rect 로 표시되던 결함 수정 — 이제 각도 그대로 복원. §11.4 변경 이력 한 줄만 | §11.4 |
| (반영 완료 2026-09-04) | #424 Feature Match 재학습 원점 + Match Align 기준 각도 + 재열기 원점 복원 + 재학습 알림 — HTML §5.1 콜아웃 · §5.7 소절 신설 · §11.4 + 신규 그림 `50_fm_retrain_origin.png`(툴 패널 캡처 크롭) + docx 재생성. 상세: manual_change_history.md §8 | §5.1 §5.7 §11.4 |
| (반영 완료 2026-09-04) | Web #96 로그인 유지 체크박스 — HTML §7.1 3번 항목 교체 + §11.4 + `40_web_login.png` 재캡처(v1.8.0 라이브) + docx 재생성. 상세: manual_change_history.md §7 | §7.1 §11.4 |
| (잔여·스크린샷) | 3D 점군 뷰어 조작 스크린샷 갱신 (#275 밀도 버튼·툴바) — **실점군 로드 상태에서 수동 캡처 필요** (자동 캡처 도구는 점군 화면 미포함, 시뮬레이션 점군은 부적합 판단). 본문 서술은 반영 완료 | §3.9 (점군 화면 조작) |
| (잔여·스크린샷) | 안내 창 다크 디자인(#364 #366) — 매뉴얼 내 안내 창 스크린샷 재캡처 (동작 동일, 본문 변경 없음) | 전반 |
| (잔여·스크린샷) | Feature Match 설정 화면 재캡처 — Basic 신규 파라미터(Max Instances·Min Coverage)·[기준 이미지 불러오기] 버튼·마스크 편집기 신 UI(#375)·다중 인스턴스 오버레이(#번호) | §3.9 (Feature Match) |
| (잔여·스크린샷) | Recipe Manager [Duplicate]/[Rename]·Steps 패널 복제 버튼·도구 우클릭 메뉴(#400), VisionSetup 툴바 [Acquire]·연결 토글(#397), 갤러리 얼라인 탭(#395) | §3.9 |
| (잔여·스크린샷) | Web 검사항목 툴바 [레시피 복사]/[이름 변경] (Web #94) | §4.9 |
| (반영 완료 2026-09-04) | #417 단독 모드 로컬 생산이력 1~3단계 — HTML §4.3(생산 이력 조회 창 소절 신설)·§4.5·§7.6·§9.4·§11.3·§11.4 + docx 재생성 + 캡처 `36_dlg_inspection_history.png`(신규)·`28_dlg_retention.png`(교체)·`37_dlg_inspection_history_summary.png`·`38_dlg_inspection_history_pareto.png`(탭 장면, `--capture-dialogs` 자동)·`vms_ctl/sec_recent.png`([전체 이력] 버튼, 재캡처) — 스크린샷 잔여 없음 | §4.3 §4.5 §4.9 §7.6 §9.4 §11.3 §11.4 |
| (반영 완료 2026-09-02) | AppSetup 2단계 재캡처 — 단독 모드 체크박스(#407) + "고급 설정 — Web Client API Key" 접이식 전환 + 설정 마법사 전용 아이콘. 본문·§3.2 표·§11.4 이력·docx 재생성 완료 (capture 도구에 ToggleButton 추가, HelpIcon ? 토글은 제외) | §3.2 |
| (반영 완료 2026-09-04) | 사이드 패널 버튼 아이콘 세로 정렬 수정(시각만) — `vms_ctl/sec_*.png` 전부 재캡처·docx 재생성. 상세: manual_change_history.md §6 | §4.3 (스크린샷) |
| (반영 완료 2026-09-04) | #416 AppSetup 3단계 카메라 카드 입력란 제거 — §3.1 콜아웃 + 생성기 §3.2 표 3행 삭제·안내 카드 행 + §5.3 상호 참조 + §11.4 한 줄, `10_appsetup_step3.png` 재캡처 + `appsetup_ctl/P3_*` 전부 재캡처·재대조(번호 이동), docx 재생성. 상세: manual_change_history.md §1·§2 | §3.1 §3.2 §5.3 §11.4 |
| #369 #370 | **라이선스 발급 GUI LicGen.App + 백업 자동화** — ⚠ 사내 전용(MSI 비동봉·고객 비노출)이라 **사용자 매뉴얼 대상 아님, Admin 매뉴얼만**: 발급 담당자 절차를 CLI 예시에서 LicGen.App 화면 기준으로 교체(발급 탭·발급 대장 탭 비고·백업 배너/[백업 실행] 스크린샷), `licgen backup` USB 2부 규칙·issue 백업 경고 소개. 상세 절차 원본은 docs/license_operations.md (반영 완료) | Admin 매뉴얼 라이선스 절 (§2.6 발급 측 상대편) |

## guides/ — 가이드 문서

| 파일 | 역할 | 작성 시점 |
|------|------|----------|
| [`GS_History.md`](guides/GS_History.md) | **시간순 히스토리** — 45 PR 의 14 phase 분류, Web 짝 매핑, 인증 시점 운영 스냅샷 | v1.0 (2026-06-04) |
| [`gs_compliance_overview_v1.0.md`](guides/gs_compliance_overview_v1.0.md) | **ISO/IEC 25051 항목별 매핑** — 구현 위치 / 검증 절차 / 코드 경로. §5.10 프리셋 표는 `ManualPresetConsistencyTests` 가 자동 대조 | PR21 |
| [`gs_msi_code_signing_guide.md`](guides/gs_msi_code_signing_guide.md) | MSI 코드 서명 (Authenticode) 운영 절차 | PR24 |
| [`gs_audit_siem_integration_guide.md`](guides/gs_audit_siem_integration_guide.md) | 감사 로그 SIEM 외부 전송 통합 가이드 | PR25 |
| [`gs_distribution_policy.md`](guides/gs_distribution_policy.md) | 라이선스 / NOTICE / 배포 정책 (루트의 `LICENSE`, `NOTICE` 와 짝) | PR26 |
| [`SSO_Migration_Plan.md`](guides/SSO_Migration_Plan.md) | VMS ↔ Web SSO 통합 마이그레이션 설계 (구현 완료 — 이력 참고) | PR1~5 |

## pipeline/ — 생성 스크립트

| 파일 | 역할 |
|------|------|
| `parse_manual.py` | 매뉴얼 HTML → `_manual_blocks.json` |
| `gen_user_manual.js` | 사용자매뉴얼 docx 생성 (그림 앵커 POST · 마법사/관리자 다이얼로그 섹션 포함) |
| `gen_product_description.js` | 제품설명서 docx 생성 |
| `gen_gs_supporting_docs.js` | OSS 확인서 / 신청서 템플릿 / 체크리스트 docx 생성 |
| `extract_tool_params.py` | VisionSetup ToolSettings XAML → `_tool_params.json` (매뉴얼 도구 파라미터 표) |
| `web_capture.js` | BODA.VMS.Web 화면 캡처 (Chrome DevTools Protocol) → `../screenshots/` |
| `_gen_vms_gs_schedule.py` | GS 인증 세부일정 보고서 xlsx 생성 (2026-07-08 확정본) |
| `_gen_verification_checklist.py` | GS 자체검증 체크리스트 xlsx 생성 |
| `_manual_blocks.json` `_tool_params.json` | 중간 산출물 (재생성 가능하지만 diff 추적을 위해 커밋) |

## 컨트롤 캡처 도구 (UI 변경 시 재캡처)

| 앱 | 명령 | 산출 |
|----|------|------|
| VMS.AppSetup | `VMS.AppSetup.exe --capture-controls [폴더]` / `--capture-fullpage` | 마법사 7페이지 컨트롤 (7페이지 = Security Mode) / 2·5단계 풀페이지 |
| VMS.VisionSetup | `--capture-controls` / `--capture-fullpage` / `--capture-toolpanels` / `--capture-dialogs` | MainView 컨트롤 / 전체화면 / 툴 패널 34종 / 다이얼로그 |
| VMS | `VMS.exe --capture-controls [폴더]` / `--capture-dialogs` | 헤더 칩·사이드 패널 (장면 3개) / 관리자 다이얼로그 |
| VMS.DeepLearning | `VMS.DeepLearning.exe --capture-controls [폴더]` | 라벨링/학습 화면 (Detection·Segmentation·Anomaly 장면 3개 + 섹션별 컴포지트) |

- 전부 `#if DEBUG` 전용 (Release 무영향). Debug 빌드 후 실행. 원본 출력은 `docs/control-capture/`.
- 매뉴얼에 쓰는 선별본은 `screenshots/{appsetup_ctl, visionsetup_ctl, vms_ctl, deeplearning_ctl}/` 에 커밋됨.
- UI 를 바꾸면 해당 앱 캡처 재실행 → 선별본 교체 → docx 재생성.

## 검증 렌더 (docx 육안 확인)

Word COM 으로 docx→XPS 변환(`ExportAsFixedFormat` 포맷 18) → XPS fpage 는 UTF-16 으로 읽고
`UnicodeString` 속성을 이어붙여 텍스트 검색(글리프 런으로 분절되어 있음) → 대상 페이지만
`docs/control-capture/_work/render_xps.ps1` 로 PNG 렌더 후 육안 확인.

## 짝 솔루션 (Web 서버)

BODA.VMS.Web 측 GS 작업 → `D:\Project\BODA.VMS.Web\docs\GS_Certification_Baseline.md` (v1.1, 609 줄). VMS PR119 ↔ Web PR #10 (X-API-Key) 짝.

## 어느 문서부터 봐야 할까

- **처음 접하는 심사관/SI**: `guides/GS_History.md` (개요 + 흐름) → `guides/gs_compliance_overview_v1.0.md` (구체적 항목)
- **MSI 배포 운영자**: `guides/gs_msi_code_signing_guide.md` + 루트 `docs/msi_build_guide.md`
- **SIEM 통합 담당**: `guides/gs_audit_siem_integration_guide.md`
- **법무/계약**: `guides/gs_distribution_policy.md` + 루트 `LICENSE` / `NOTICE`
