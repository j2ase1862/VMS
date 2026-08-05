# BODA VMS — Vision Management System

산업용 머신 비전 검사 플랫폼. PLC / IO 보드 / 카메라 / 딥러닝 모델을 통합한 .NET 8.0 WPF 솔루션.

[![Version](https://img.shields.io/badge/version-1.1.0-blue.svg)](Directory.Build.props)
[![.NET](https://img.shields.io/badge/.NET-8.0%20WPF-512BD4.svg)](https://dotnet.microsoft.com/)
[![Platform](https://img.shields.io/badge/platform-Windows%20x64-0078D4.svg)](#)

---

## 주요 기능

- **비전 도구 워크스페이스** — 30 + 종 도구(Blob / Caliper / FeatureMatch / OCR / Code / DL Detection·Segmentation·Anomaly 등) 의 드래그-드롭 파이프라인 편집
- **Sequence Editor** — 노드 기반 시퀀스 그래프 (Input·Output·Inspection·Branch·Delay·Repeat·Recipe·Step Change)
- **PLC + IO 보드 통합** — Modbus / Siemens S7 / Omron FINS / LS / Mitsubishi + ADLink DASK / Advantech DAQNavi
- **카메라 다중 벤더** — GigE Vision / USB3 Vision / Matrox / Dalsa / 3D 라인스캔
- **딥러닝 추론** — ONNX Runtime + TensorRT FP16, YOLOv8-seg / Classification / Anomaly / Custom Ensemble
- **운영 워크플로** — Operator 로그인(Web), Work Order 자동 레시피 로드, 실시간 SignalR 푸시
- **3D 비전** — 포인트 클라우드(필터 / 정합 / 클러스터), Hand-Eye 캘리브레이션, 로봇 다중뷰 측정
- **인스톨러** — WiX v4 MSI (한국어 패키지)

---

## 솔루션 구조

```
VMS-Solution/
├── VMS/                      메인 런처 + 시퀀스 엔진 + Inspection 서비스 + 운영 UI
├── VMS.VisionSetup/          비전 도구 워크스페이스 (가장 큰 컴포넌트)
├── VMS.AppSetup/             시스템 설정 마법사 (PLC / 카메라 / 로봇 / IO 보드)
├── VMS.DeepLearning/         딥러닝 모델 학습 GUI
├── VMS.MasterSetup/          WiX MSI 인스톨러
├── VMS.Core/                 공용 인터페이스 / 모델 / Web 서비스 클라이언트
├── VMS.PLC/                  PLC + IO 보드 추상화 (IPlcConnection, IIoBoardConnection)
├── VMS.Camera/               카메라 / 로봇 / 캘리브레이션 추상화
├── VMS.*.Tests/              xUnit 테스트 (PLC / AppSetup / VisionSetup)
└── NativeVision/             C++ DLL (AVX2/FMA 가속 비전)
```

자세한 아키텍처 가이드는 [`VMS.VisionSetup/CLAUDE.md`](VMS.VisionSetup/CLAUDE.md) 참고.

---

## 시스템 요구사항

| 항목 | 사양 |
|------|------|
| OS | Windows 10 / 11 (x64) |
| .NET | .NET 8.0 Desktop Runtime |
| GPU (옵션) | NVIDIA CUDA 11.8+ (TensorRT FP16) |
| 메모리 | 8 GB 이상 (DL 추론 시 16 GB 권장) |
| 디스크 | 2 GB 이상 (모델/이미지 별도) |

---

## 빌드 & 실행

### 사전 준비

```bash
# .NET 8.0 SDK + Workload
dotnet --version  # 8.0 이상 확인
```

### 빌드

```bash
# 전체 솔루션
dotnet build VMS.sln

# Release
dotnet build VMS.sln -c Release
```

### 실행

```bash
# 메인 앱 (런처 → MainView)
dotnet run --project VMS

# 비전 도구 워크스페이스 (standalone)
dotnet run --project VMS.VisionSetup

# 시스템 설정 마법사
dotnet run --project VMS.AppSetup

# 딥러닝 학습 GUI
dotnet run --project VMS.DeepLearning
```

### 테스트

```bash
dotnet test VMS.sln
```

### 인스톨러 빌드 (WiX MSI)

```bash
# WiX Toolset v4 + wix .NET tool 사전 설치
dotnet build VMS.MasterSetup/VMS.MasterSetup.wixproj -c Release

# 산출물: installer/BODA-VMS-1.1.0.msi
```

---

## 디렉토리 데이터 경로

런타임 설정/큐는 `%LocalAppData%/BODA VISION AI/` 에 저장됩니다.

| 파일 / 폴더 | 용도 |
|-----------|------|
| `system_config.json` | AppSetup 마법사 결과 (PLC / 카메라 / IO 보드 / 로봇 / 웹 서버) |
| `expert_mode.json` | 도구 타입별 Expert Mode 토글 |
| `process_sequence.json` | 시스템 레벨 시퀀스 그래프 |
| `recipes/*.recipe.json` | 검사 레시피 |
| `upload_queue/*.json` | 검사 결과 업로드 디스크 큐 (재시도용) |
| `trt_cache/` | TensorRT 엔진 캐시 |

---

## 버전 관리

- **단일 버전 소스**: [`Directory.Build.props`](Directory.Build.props) (모든 프로젝트 자동 상속)
- 릴리스 시 `Version` / `AssemblyVersion` / `FileVersion` 한 곳만 갱신

---

## 문서

| 문서 | 위치 |
|------|------|
| 사용자 매뉴얼 (전체) | [`docs/manuals/BODA-VMS-User-Manual.html`](docs/manuals/BODA-VMS-User-Manual.html) |
| VisionSetup 매뉴얼 | [`docs/manuals/VMS-VisionSetup-Manual.html`](docs/manuals/VMS-VisionSetup-Manual.html) |
| 비전 도구 운용 가이드 | [`docs/manuals/VMS_VisionTools_Operating_Manual.html`](docs/manuals/VMS_VisionTools_Operating_Manual.html) |
| Hand-Eye 캘리브레이션 | [`docs/manuals/VMS_HandEye_Calibration_Manual.md`](docs/manuals/VMS_HandEye_Calibration_Manual.md) |
| GS 인증 자료 | [`docs/gs/`](docs/gs/) (히스토리 + ISO/IEC 25051 매핑 + MSI 서명 + SIEM 가이드 + 배포 정책) |
| WeldTeach 그라인딩 리뷰 지시서 | [`docs/design/weldteach-grinding-review-2026-08.md`](docs/design/weldteach-grinding-review-2026-08.md) (P0 착수 전 필수 항목 ①~③ 포함) |
| 변경 이력 | [`CHANGELOG.md`](CHANGELOG.md) |
| 아키텍처 가이드 | [`CLAUDE.md`](CLAUDE.md), [`VMS.VisionSetup/CLAUDE.md`](VMS.VisionSetup/CLAUDE.md), [`VMS.WeldTeach/CLAUDE.md`](VMS.WeldTeach/CLAUDE.md) (기하 불변식) |

---

## 라이선스 / 저작권

Copyright © BODA Vision AI 2026. All rights reserved.
