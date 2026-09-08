# 배포 정책 및 라이선스 운영 (BODA Vision AI)

문서 버전: v1.0
대상 빌드: master @ 2026-05-29 (PR1~25)
범위: VMS 자체 라이선스, 제3자 컴포넌트 의무, 벤더 SDK 정책, 고객사 배포 절차

> 본 문서는 BODA Vision AI 가 어떤 조건으로 배포 가능한지를 정리합니다.
> `LICENSE` (VMS 자체 사용 조건), `NOTICE` (Apache 2.0 / MIT / BSD 제3자 표기), 본 문서 (운영 절차) 세 문서가 함께 GS 인증 §5 부록의 배포 정책 근거입니다.

---

## 1. 문서 상태

| 항목 | 상태 | 비고 |
|---|---|---|
| `LICENSE` | **DRAFT — 법무 검토 필요** | 외부 첫 배포 전 VASIM 법무팀 / 외부 자문 검토 필수 |
| `NOTICE` | 검증 완료 | Apache 2.0 / MIT / BSD 제3자 컴포넌트 사실 기반 표기 |
| 본 문서 (운영 정책) | v1.0 초안 | 법무 검토 후 v1.1 확정 |
| EULA (고객 동의서) | §3 템플릿 — 법무 검토 후 확정 | MSI 인스톨러 첫 화면에 표시 권장 |

배포 전 작업 체크리스트는 §7 참고.

---

## 2. 의존성 라이선스 매트릭스

PR1~25 시점 기준. 라이선스 분류는 자체 코드(GROUP A) → 제3자 라이브러리(GROUP B/C/D) → 옵션 벤더 SDK(GROUP E) 순.

### 2.1 GROUP A — VMS 자체 코드
| 컴포넌트 | 라이선스 | 보유 |
|---|---|---|
| VMS / VMS.Core / VMS.AppSetup / VMS.VisionSetup / VMS.DeepLearning / VMS.PLC / VMS.Camera / VMS.MasterSetup | 상용 (LICENSE 참조) | VASIM Co., Ltd. |
| NativeVision (C++ AVX2 가속) | 상용 | VASIM Co., Ltd. |

### 2.2 GROUP B — Apache License 2.0 (NOTICE 표기 의무)
| 패키지 | 버전 | 의무 |
|---|---|---|
| OpenCvSharp4 / OpenCvSharp4.runtime.win / OpenCvSharp4.Extensions / OpenCvSharp4.WpfExtensions | 4.11.0 | 저작권 / 라이선스 / NOTICE 보존 |
| OpenCV (네이티브, 위 런타임 패키지 번들) | 4.x | 동일 |
| Tesseract.Net SDK | 5.2.0 | 동일 |
| ZXing.Net | 0.16.11 | 동일 |

배포 의무 — Apache 2.0 §4: 본 라이선스 사본 + NOTICE 사본 동봉.

### 2.3 GROUP C — MIT License (저작권 표기 의무)
| 패키지 | 버전 | 의무 |
|---|---|---|
| CommunityToolkit.Mvvm | 8.4.0 | 저작권 + 라이선스 보존 |
| HelixToolkit.Wpf.SharpDX | 3.1.2 | 동일 |
| Microsoft.Data.Sqlite | 8.0.0 | 동일 |
| Microsoft.AspNetCore.SignalR.Client | 8.x | 동일 |
| Microsoft.ML.OnnxRuntime.Gpu | 1.21.0 | 동일 (NVIDIA cuDNN 별도 라이선스) |
| System.Drawing.Common | 8.0.11 | 동일 |

### 2.4 GROUP D — BSD-2-Clause
| 패키지 | 버전 | 의무 |
|---|---|---|
| BCrypt.Net-Next | 4.0.3 | 저작권 + 2조항 보존 |

### 2.5 GROUP E — 옵션 벤더 SDK (별도 라이선스 필요)
컴파일 심볼로 조건부 빌드 — 기본 빌드는 GROUP E 없음 → GROUP A~D 만으로 100% compliant.

| SDK | 컴파일 심볼 | 라이선스 출처 | 배포 조건 |
|---|---|---|---|
| Mech-Mind Mech-Eye SDK | `MECHMIND_AVAILABLE` | Mech-Mind Robotics | 도입 고객사가 별도 라이선스 취득 |
| Basler Pylon SDK | `BASLER_AVAILABLE` | Basler AG | 동일 |
| Matrox Imaging Library | `MIL_AVAILABLE` | Matrox Electronic Systems | 동일 — MIL-Lite / MIL-Full 구분 |
| ADLink DASK | (런타임 DLL) | ADLink Technology | 보드 구매 시 동봉 |
| Advantech DAQNavi | (런타임 DLL) | Advantech | 보드 구매 시 동봉 |

> 벤더 SDK 가 활성화된 빌드는 **고객 사이트마다 별도 EULA 동의** 필요. VMS.MasterSetup 의 첫 설치 다이얼로그에 해당 벤더 EULA 표시 권장.

### 2.6 GROUP F — 모델 / 가중치 / 데이터
| 항목 | 책임 |
|---|---|
| ONNX 모델 파일 (`*.onnx`) | 운영자 / 통합사 — repo 미포함 (`.gitignore`), 모델 출처별 라이선스 확인 의무 |
| **D-FINE 사전학습 가중치** (`ustc-community/dfine-*`, HGNetV2 백본) | Apache 2.0 — **Detection 기본 백본** (`train_dfine.py`, 학습 시 HuggingFace `transformers` Apache 2.0 사용, 배포본에는 미포함) |
| MobileSAM 가중치 (라벨링 보조) | Apache 2.0 (ChaoningZhang/MobileSAM) |
| anomalib · torchvision (Anomaly / Classification 학습) | Apache 2.0 · BSD-3 |
| YOLOv8 사전학습 가중치 (`train_yolo.py`, **옵션 — 기본 아님**) | Ultralytics — **AGPL-3.0**. Ultralytics 는 fine-tuning 한 가중치와 ONNX 변환본까지 AGPL 파생물로 본다. **Ultralytics Enterprise License 를 보유한 사이트에서만 선택 사용** — https://www.ultralytics.com/license |
| PP-OCRv4 가중치 | Apache 2.0 (PaddleOCR) |
| 학습 데이터셋 | 사이트별 — VASIM 책임 영역 밖 |
| 학습 스택(torch · transformers · anomalib · paddlepaddle 등 pip 패키지) | **인증 제품 배포물 외** — 인스톨러 `AiTools` Feature(선택) 사용자가 별도 설치, 인증 제출 빌드(`-p:ExcludeAiTools=true`)에는 학습 도구 자체가 없음 ([gs_scope_ai_tools.md](gs_scope_ai_tools.md)) |

> **AGPL-3.0 (Ultralytics YOLO) 해석 — 2026-09-08 정정.** Ultralytics 는 사전학습 가중치로 fine-tuning 한 모델(.pt) 과 그 ONNX 변환본까지 AGPL 파생물로 본다. VASIM 이 학습한 모델을 고객에게 전달하는 것은 '배포'에 해당하므로 **온프레미스 납품이라도 AGPL 의무가 면제되지 않는다** (이전 판 "온프레미스는 trigger 안 됨" 서술은 모델을 우리가 전달하는 경우에 맞지 않아 폐기). 고객이 자기 PC 에서 직접 학습해 자체 사용만 하는 경우는 배포가 아니므로 의무가 발생하지 않는다.
> 대응: **Detection 기본 백본을 D-FINE (Apache 2.0) 으로 전환** (`train_dfine.py`, VisionSetup Detection 도구는 D-FINE / YOLO ONNX 규약을 자동 판별). `train_yolo.py` 는 Ultralytics Enterprise License 를 보유한 사이트에서만 선택 사용하며, 그 외에는 YOLO 로 학습한 모델을 납품하지 않는다. 최종 해석은 법무 검토 필요.

---

## 3. EULA 템플릿 (고객 설치 시 동의)

> 이하는 **DRAFT 템플릿** — 법무 검토 / 한영 이중언어 / 한국 약관규제법 적합성 검토 필요.

```
BODA Vision AI 최종 사용자 라이선스 계약 (EULA)
Version: [버전] / 발효일: [날짜]
저작권자: VASIM Co., Ltd.

본 소프트웨어 (이하 "본 SW") 를 설치 / 사용함으로써, 귀하는 본 EULA 의
모든 조건에 동의하는 것으로 간주됩니다.

1. 사용 허가
   별도로 체결된 상용 라이선스 계약서에 명시된 사이트 수 / PC 수
   범위에서 본 SW 를 설치 · 사용할 수 있습니다.

2. 사용 제한
   귀하는 본 SW 를 다음 행위로 사용할 수 없습니다:
   (1) 역공학 · 디컴파일 · 디스어셈블 (관계법령 허용 범위 제외)
   (2) 제3자에게 재배포 · 재판매 · 양도 · 임대
   (3) 경쟁제품 개발 목적의 사용
   (4) 저작권 표기 / 브랜드 / 라이선스 표기 제거 또는 변경

3. 제3자 컴포넌트
   본 SW 는 NOTICE 파일에 표시된 오픈소스 컴포넌트를 포함합니다.
   해당 컴포넌트는 각각의 라이선스 (Apache 2.0, MIT, BSD-2-Clause) 를
   따르며, 본 EULA 는 VASIM 이 작성한 코드 및 전체 통합본에만 적용됩니다.

   선택적 벤더 SDK 통합 (Mech-Mind, Basler, Matrox, ADLink, Advantech)
   은 활성화 시 해당 벤더의 별도 EULA 동의가 필요합니다.

4. 데이터 / 개인정보
   본 SW 는 다음 데이터를 사이트 로컬에 저장합니다:
   - 사용자 계정 (BCrypt 해시, ACL 보호)
   - 감사 로그 (기본 365일 보존, 사이트 설정 가능)
   - 검사 결과 / 레시피 / 작업지시 추적성 데이터
   VASIM 은 사이트의 사용자 데이터 / 검사 결과를 직접 수집하지 않으며,
   원격 진단을 위해 데이터 전송이 필요한 경우 별도 동의를 받습니다.

5. 보증의 면책
   본 SW 는 "있는 그대로 (AS IS)" 제공되며, 명시적 또는 묵시적 보증
   (상품성, 특정 목적 적합성, 비침해 등) 을 제공하지 않습니다.

6. 책임의 제한
   VASIM 의 손해배상 책임은 귀하가 본 SW 라이선스로 지불한 금액을
   초과하지 않습니다. 간접 / 결과 / 우발적 손해에 대해서는 책임지지
   않습니다 (관계법령에서 면책 불가한 경우 제외).

7. 종료
   귀하가 본 EULA 조항을 위반하는 경우, VASIM 은 사전 통지 없이
   라이선스를 종료할 수 있으며, 귀하는 즉시 본 SW 의 모든 사본을
   파기해야 합니다.

8. 준거법 및 분쟁
   본 EULA 는 대한민국 법을 준거법으로 하며, 분쟁 발생 시 서울중앙
   지방법원을 1심 합의관할 법원으로 합니다.

9. 문의
   VASIM Co., Ltd.
   [주소] / [연락처]
```

> 한국 약관규제법: 면책 조항 일부는 소비자 보호법에 의해 무효화될 수 있음 → 법무 검토 필수. 산업 B2B 도입 시에는 별도 마스터 계약서가 EULA 보다 우선.

---

## 4. 배포 채널별 정책

### 4.1 통합사 (SI) 경유 사이트 배포
- VASIM ↔ 통합사 마스터 계약 + 통합사 ↔ 최종 고객 SLA
- VMS MSI 파일 (서명된 빌드 — PR24 가이드) 만 전달
- 소스 코드 / 빌드 스크립트 / .pfx 인증서 전달 금지
- 벤더 SDK 가 포함된 빌드는 사이트별 SDK 라이선스 확인 후 발급

### 4.2 OEM 라이선스 (브랜드 변경 배포)
- 별도 마스터 계약서 — UI 브랜드 변경, 매뉴얼 OEM 표기 권한
- NOTICE 의 저작권 표기는 변경 불가 (라이선스 의무)
- 로열티 / 사이트 수 한도 등은 계약서 별표

### 4.3 평가판 / 데모
- 30일 / 90일 시한 라이선스 — 시한 경과 후 자동 비활성화
- 평가판 EULA 별도 — 운영 사용 / 상업적 결과물 사용 금지 문구
- 감사 로그 / 사용자 DB 는 평가 종료 후 사용자가 직접 정리

### 4.4 사내 / 데모 빌드 (직접 영업 활동)
- 외부 미배포 — 영업 PC / 데모 부스 PC 에 한정
- 미서명 빌드 허용 (CI artifact 그대로)
- 사내 PC 에서 외부 반출 시 마스터 계약서 검토 후 EULA 동의 절차

---

## 5. 라이선스 의무 운영 (정기)

### 5.1 분기 1회
- NuGet 의존성 신규 / 갱신 — 추가 패키지의 라이선스 확인
- `NOTICE` 파일 업데이트 (필요 시)
- AGPL / GPL 등 카피레프트 의존성 발생 여부 검사

### 5.2 반기 1회
- `LICENSE` / EULA 템플릿의 사업 환경 변화 반영 검토
- 벤더 SDK 라이선스 비용 / 조건 갱신 확인 (특히 Matrox MIL / Basler 등)

### 5.3 연 1회
- 외부 법무 자문 — 한국 / 주요 수출 국가 (일본, 베트남 등) 의 약관 적합성 재검토
- GS / ISO 25051 / ISO 27001 인증 갱신 시 본 문서 첨부

---

## 6. 알려진 함정

| 증상 | 원인 | 해결 |
|---|---|---|
| 빌드 폴더에 `*.onnx` 누락 | `.gitignore` 로 제외되어 사이트마다 모델 별도 배포 필요 | MSI 별도 모델 패키지 분리 (`bodavms-models-*.zip`) |
| Apache 2.0 NOTICE 미동봉 | MSI 패키징 단계에서 NOTICE 파일 누락 | `VMS.MasterSetup.wixproj` 에 NOTICE → INSTALLFOLDER\NOTICE.txt 컴포넌트 추가 (배포 체크리스트 §7) |
| YOLOv8 가중치 상업적 사용 미신고 | Ultralytics AGPL → 상용 라이선스 필요 | 통합사가 모델 출처별 라이선스 사전 확인 |
| OEM 빌드에서 VASIM 표기 제거 | 라이선스 위반 | EULA / 계약서에 NOTICE / 저작권 표기 보존 의무 명시 |
| GPL 라이브러리 우발적 추가 | 개발자가 PackageReference 추가 시 라이선스 미확인 | 분기 점검 + PR 머지 정책 — 신규 NuGet 패키지 시 라이선스 라벨 필수 |

---

## 7. 배포 전 작업 체크리스트

### 7.1 첫 외부 배포 시 (1회)
- [ ] `LICENSE` 법무 검토 — 한국 약관규제법 / 대상 수출국 적합성
- [ ] `NOTICE` 검증 — 첨부된 모든 Apache 2.0 컴포넌트 누락 / 오기 없음
- [ ] EULA (§3 템플릿) 법무 검토 → 정식 문서로 확정
- [ ] MSI 패키지에 NOTICE / LICENSE / EULA 파일 포함
- [ ] MSI 첫 화면에 EULA 동의 다이얼로그 추가 (WiX UI extension)
- [ ] 코드 서명 — `docs/gs/guides/gs_msi_code_signing_guide.md` (PR24) 적용

### 7.2 각 빌드 / PR 마다
- [ ] NuGet 의존성 추가 / 갱신 시 라이선스 검증
- [ ] AGPL / GPL / LGPL 의존성 도입 여부 검사
- [ ] `NOTICE` 의 패키지 / 버전 일치 확인 (분기 보고서)

### 7.3 사이트 첫 설치 시
- [ ] 통합사 → 고객 마스터 계약 + EULA 동의 (사이트 책임자)
- [ ] 벤더 SDK 활성화 빌드일 경우 — 해당 벤더 EULA 동시 동의
- [ ] 평가판 시한 / 라이선스 키 / ClientIndex 부여
- [ ] 현장 설치 체크리스트(`docs/field_install_checklist.md`) 통과 → 정식 가동

---

## 8. GS / ISO 인증 매핑

| 항목 | 본 정책 적용 |
|---|---|
| GS 1.6 — 라이선스 / 저작권 표시 | LICENSE + NOTICE 동봉, MSI 표시 |
| GS 2.3 — 제3자 컴포넌트 관리 | §2 매트릭스 + 분기 검증 |
| ISO 25051 — 상품화 SW 품질 요구 | EULA / 사용자 매뉴얼 (PR17, BODA-VMS-User-Manual.html) |
| ISO 27001 A.18 — Compliance | 라이선스 의무 정기 운영 §5 |

---

## 변경 이력
| 버전 | 날짜 | 변경 |
|---|---|---|
| v1.0 | 2026-05-29 | 초안 — 의존성 매트릭스 (5 그룹), EULA 템플릿, 배포 채널별 정책, 정기 운영, 함정, 체크리스트, GS 매핑 |
