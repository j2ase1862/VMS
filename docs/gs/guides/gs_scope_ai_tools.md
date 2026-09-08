# GS 인증 범위 정의 — AI 학습 도구(VMS.DeepLearning) 제외

문서 버전: v1.0 (2026-09-08)
대상: GS 인증 2차 준비 (목표 2026-10-30) — 제출 제품 구성과 시험 범위의 경계
관련: [gs_compliance_overview_v1.0.md](gs_compliance_overview_v1.0.md) §1.1 · [gs_distribution_policy.md](gs_distribution_policy.md) §2.6 · [msi_build_guide.md](../../msi_build_guide.md) §14

---

## 1. 결정

**GS 인증 대상은 "검사 런타임 제품"으로 한정한다.** 딥러닝 모델을 *만드는* 기능(라벨링·학습·모델 내보내기)은
인증 범위에서 제외하고, 별도 제품군("AI 학습 도구", 향후 웹 기반 MLOps 플랫폼으로 전환)으로 분리한다.
딥러닝 모델을 *사용하는* 기능(ONNX 추론 검사 도구)은 검사 런타임의 일부이므로 범위에 남는다.

| 구분 | 구성 요소 | 인증 범위 | 비고 |
|---|---|---|---|
| 포함 | VMS (메인 운전) · VMS.AppSetup (설정 마법사) · VMS.VisionSetup (검사 도구·시퀀스) | **포함** | 제품 본체 |
| 포함 | VMS.Core · VMS.PLC · VMS.Camera (라이브러리) | **포함** | 본체 의존 |
| 포함 | BODA.VMS.Web (MSI 동봉 Web 서버, 생산 이력·작업지시) | **포함** | 선택 Feature 이나 제품 기능 |
| 포함 | VisionSetup 딥러닝 **추론** 도구 — Detection / Classify / Anomaly / YOLO-seg / OCR / Ensemble | **포함** | ONNX 파일만 있으면 결정적으로 동작. 시험용 샘플 모델 제공 (§5) |
| 포함 | VisionSetup OCR Synth Data 창의 **합성 데이터 생성** (C#) | **포함** | 파이썬 무관 |
| **제외** | VMS.DeepLearning 앱 (데이터셋·라벨링·MobileSAM 보조·학습 실행·Inference 미리보기·Active Learning) | **제외** | 인스톨러 선택 Feature `AiTools`, 인증 빌드에서 미포함 |
| **제외** | 파이썬 학습 스크립트 `scripts\*.py` (train_dfine / train_yolo / train_classifier / train_anomaly / train_ppocr / export_mobile_sam) | **제외** | 동일 Feature |
| **제외** | VisionSetup OCR Synth Data 창의 **PP-OCR 학습** 카드·[Generate & Train] | **제외** | `train_ppocr.py` 부재 시 자동 숨김 |
| **제외** | 파이썬 런타임·pip 패키지(torch, transformers, paddlepaddle, anomalib 등)·GPU 드라이버 | **제외** | 제품 배포물에 포함되지 않음 |

## 2. 근거

1. **재현성.** 학습 기능은 제품 밖의 파이썬 환경, pip 패키지, GPU, 인터넷(사전학습 가중치 다운로드)에 의존하고
   학습 결과가 비결정적이다. 시험 기관에서 동일 환경을 구성해 재현하기 어렵고, 환경 요인으로 인한 실패가 제품
   결함으로 기록될 위험이 크다.
2. **제품 구조.** 검사 런타임(VMS·VisionSetup)과 모델 생성 도구는 사용자·설치 PC·라이프사이클이 다르다. 학습은
   GPU 가 있는 엔지니어 PC 에서, 검사는 라인 PC 에서 수행된다. 인증은 라인에 배포되는 제품을 대상으로 한다.
3. **라이선스.** 학습 스택은 Apache 2.0 계열로 정리했으나(D-FINE 전환, #429), 파이썬 생태계 의존성 목록이 크고
   변동이 잦다. 인증 문서(배포 정책 §2.6)에서 제품 배포물의 제3자 구성요소 표를 안정적으로 유지하려면 학습
   스택을 별도 문서로 분리하는 편이 정확하다.
4. **로드맵.** AI 학습 도구는 웹 기반 MLOps 플랫폼(데이터 관리 → 라벨링 → 학습 → 배포)으로 전환 예정이다.
   WPF 앱을 인증에 포함하면 전환 시 인증 범위와 어긋난다.

## 3. 구현 (v1.31.0 — PR feat/aitools-installer-feature)

| 항목 | 내용 |
|---|---|
| 인스톨러 Feature | `AiTools` ("AI 학습 도구 (VMS.DeepLearning)") — `VMS.DeepLearning.*` 4개 파일 + `scripts\*.py`. 기본 포함, 설치 마법사 "설치 구성 선택" 두 번째 체크박스 또는 `INSTALLAITOOLS=0` 으로 제외 |
| 인증 제출 빌드 | `dotnet build VMS.MasterSetup\VMS.MasterSetup.wixproj -c Release -t:Rebuild -p:ExcludeAiTools=true` → `VMS-<버전>-cert.msi`. Feature·속성·체크박스가 컴파일에서 제거되어 MSI 에 파일이 존재하지 않음 |
| VisionSetup | Tools › Deep Learning 메뉴는 `VMS.DeepLearning.exe` 부재 시 숨김. OCR Synth Data 창은 `train_ppocr.py` 부재 시 학습 카드·[Generate & Train] 숨김 |
| 추론 도구 | 변경 없음. Detection 도구는 D-FINE / YOLO ONNX 규약 자동 판별 (#429) |

검증 방법: 인증 MSI 설치 후 설치 폴더에 `VMS.DeepLearning.exe`·`scripts\` 가 없고, VisionSetup Tools 메뉴에
Deep Learning 항목이 없으며, Detection 도구가 샘플 ONNX 로 검출을 수행하면 된다.

## 4. 문서 처리

| 문서 | 처리 |
|---|---|
| 사용자 매뉴얼 v2.0 (`docs/manuals/BODA-VMS-User-Manual.html` → docx) | 6장 "딥러닝 모델 학습" 본문을 **별책** `BODA-VMS-AI-Tools-Manual.html` 로 분리. 본편 6장은 "AI 학습 도구 (별책·선택 구성)" 안내 절로 축약해 장 번호와 이후 장 앵커를 유지 |
| 제품 설명서 / 기능 명세 | 제품 구성표에서 VMS.DeepLearning 을 "선택 구성 요소 — 인증 범위 외" 로 표기 |
| 자체 검증 체크리스트 (`_gen_verification_checklist.py`) | F 그룹(DeepLearning 라벨링·학습) 제거 → 추론 도구 항목은 E 그룹(VisionSetup)에 유지 |
| 배포 정책 §2.6 | 학습 스택(torch·transformers·paddle·anomalib)을 "AiTools Feature 전용, 인증 제품 배포물 외" 로 주석 |
| GS 컴플라이언스 개요 §1.1 | 구성표 비고 갱신 |
| 시험 신청서 제품 구성 | VMS / AppSetup / VisionSetup / Web (+ 라이브러리) 로 기재, DeepLearning 미기재 |

## 5. 심사용 샘플 모델·데이터

추론 도구 시험을 위해 시험 기관에 제공하는 자료 — `D:\Models\gs_samples\` (repo 외부, 릴리즈 보관 폴더와 함께 전달):

| 파일 | 용도 | 출처·라이선스 |
|---|---|---|
| `detection_dfine.onnx` | Detection 도구 시험 (2 클래스) | D-FINE small (Apache 2.0) 을 사내 샘플 데이터로 fine-tuning — **AGPL 무관** |
| `images\*.bmp` + `expected.csv` | 입력 이미지와 기대 검출(클래스·박스) | 사내 촬영 샘플 |
| `README.md` | 도구 설정값(Input Size 640 · Confidence · IoU)과 기대 결과 | — |

YOLO(Ultralytics, AGPL-3.0)로 학습한 모델은 시험 기관 제공이 곧 배포이므로 **사용하지 않는다**.

## 6. 심사 대응 문답

- **Q. 딥러닝 기능이 있는데 왜 학습은 범위 밖인가?** — 제품은 학습된 모델을 *사용*해 검사한다. 모델 생성은 별도
  엔지니어링 도구이며 선택 설치 구성으로 분리되어 있고 인증 제출 빌드에는 포함되지 않는다.
- **Q. 모델은 어디서 오나?** — 통합사·고객 엔지니어가 AI 학습 도구(별도 제품)로 만들거나, 외부에서 만든 ONNX 를
  가져온다. 제품은 ONNX 규약(D-FINE / YOLO / 분류 / 이상탐지 / PP-OCR)만 정의한다.
- **Q. 모델 파일이 손상되면?** — OnnxLoadException 으로 격리되어 시퀀스 엔진이 중단되지 않는다 (GS P1-#4, 결함 허용성).

## 7. 잔여 리스크

- 매뉴얼 별책 분리 후 본편에서 "VMS.DeepLearning" 언급이 남아 있는지 전수 점검 필요 (§5 도구 도움말 문구 포함).
- 시험 기관이 "선택 구성"의 존재 자체를 물을 수 있음 — 인증 빌드에는 Feature 가 없다는 점(§3)으로 답변.
- 향후 웹 MLOps 플랫폼은 별도 제품으로 인증 여부를 결정한다 (`D:\MLOps 개발 문서.md`).

## 변경 이력

| 버전 | 날짜 | 내용 |
|---|---|---|
| v1.0 | 2026-09-08 | 초안 — 범위 결정·근거·구현·문서 처리·샘플 자료·문답 |
