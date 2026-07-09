# 다중 인스턴스 지원 설계 — 한 PC 에서 VMS 두 라인 운용

> 상태: 승인됨 (2026-07-09) · 구현: PR 1 (core) + PR 2 (UX/문서)

## 1. 배경 / 목표

설비 PC 한 대에 GigE 카메라 여러 대가 연결된 환경에서, VMS 앱 2개를 동시에 실행하고
각각 다른 카메라 구성·라인 번호(ClientIndex)로 운용하고 싶다는 요구.

기존 구조의 차단 요인 (전수 조사 결과):

1. **AppData 경로 하드코딩** — `%LocalAppData%\BODA VISION AI\` 아래
   system_config.json / layout_config.json / plc_signals.json / process_sequence.json /
   BodaVision.db(사용자) / Recipes/ / audit/ / upload_queue/ / image_upload_queue/ /
   Backups/ / trt_cache/ / expert_mode.json 이 VMS·AppSetup·VisionSetup 약 35곳에 분산 하드코딩.
   두 인스턴스가 같은 설정을 읽으므로 "각각 다른 카메라"가 성립 불가.
2. **Named IPC 고정 이름** — VMS↔VisionSetup 실시간 프레임 공유용
   `Local\VMS_SharedFrame_{Mmf,Mutex,FrameReady,WriterAlive}` (VMS.Camera SharedFrameConstants).
   VMS 2개가 뜨면 서로의 프레임 채널을 밟는다.
3. VMS 는 시작 시 config 의 활성 카메라를 전부 자동 연결 — GigE 제어 채널은
   프로세스 1개만 잡을 수 있어 두 번째 인스턴스가 연결 실패.

로컬 TCP 포트 / named pipe / 전역 단축키는 없음 — 위 두 축(경로, IPC 이름)만 분리하면 된다.

## 2. 핵심 설계

### 2.1 인스턴스 식별 — CLI 인자 + 상속 환경변수

```
VMS.exe --instance line2        (AppSetup.exe / VisionSetup.exe 동일)
```

해석 우선순위: `--instance` 인자 > `BODA_VMS_INSTANCE` 환경변수 > 기본 인스턴스.

각 앱은 시작 시 `AppDataPaths.Initialize(args)` 를 호출한다. Initialize 는 해석 결과를
**프로세스 환경변수로 export** 하므로, VMS 에서 External Tools 로 뜨는
VisionSetup/AppSetup/DeepLearning 이 인자 전달 코드 없이 같은 인스턴스를 자동 상속한다.

인스턴스 이름 규칙: `[A-Za-z0-9_-]{1,32}` — 파일시스템/IPC 이름에 안전한 문자만.
위반 시 시작 단계에서 명확한 메시지로 즉시 종료 (조용히 기본 인스턴스로 폴백하면
두 인스턴스가 카메라를 공유하는 사고로 이어지므로 fail-fast).

### 2.2 단일 경로 제공자 — `VMS.Camera.Configuration.AppDataPaths`

```
기본 인스턴스:  %LocalAppData%\BODA VISION AI\                    (기존과 동일 — 마이그레이션 불필요)
명명 인스턴스:  %LocalAppData%\BODA VISION AI\instances\<이름>\
```

모든 설정/데이터 경로가 `AppDataPaths.Root` 에서 파생된다 — "무엇을 분리할지" 선별 없이
전부 따라 분리되는 것이 단순하고 안전하다 (감사 로그·업로드 큐처럼 공유 가능한 것 포함).

배치를 VMS.Camera 로 한 이유: VMS.Core 에는 OpenCvSharp/Helix 등 무거운 의존성이 있어
AppSetup(경량 유지가 명시된 설계)이 참조할 수 없고, VMS.Camera 는 4개 앱이 모두
직간접 참조하며 SharedFrameConstants(IPC 이름)도 이미 여기에 있다.

**퇴행 방지**: `AppDataPathsSourceScanTests` 가 `"BODA VISION AI"` 리터럴로 경로를
조립하는 코드가 AppDataPaths 밖에 생기면 빌드(테스트)를 실패시킨다.
`appDataOverride` 테스트 매개변수가 필요한 로더는 `AppDataPaths.RootFolderName` 상수를 사용.

### 2.3 IPC 이름 인스턴스 접미사

`SharedFrameConstants` 의 4개 이름이 `AppDataPaths.QualifyIpcName()` 을 경유해
`Local\VMS_SharedFrame_Mutex.line2` 형태로 파생된다. 기본 인스턴스는 기존 이름
그대로 — 구버전 프로세스와의 호환 유지.

### 2.4 자연히 해결되는 것

- **카메라 분리**: 인스턴스별 system_config.json 에 각자의 카메라만 등록 →
  시작 시 자동 연결도 자기 카메라만. `AppSetup.exe --instance line2` 로 2번 구성 저장.
- **ClientIndex / Web 연동**: 인스턴스별 config → 라인 번호 자연 분리.
  Web 서버는 ClientIndex 로 클라이언트를 구분한다.
- **창 구분**: VMS 창 제목이 config 의 ApplicationName 에서 오므로 인스턴스별로 다르게 표시.

## 3. 명시적 결정 사항

| 결정 | 선택 | 근거 |
|------|------|------|
| users.db(로컬 사용자) | **인스턴스별 분리** | "인스턴스 = 독립 장비" 의미 일치. 작업자 공용 요구는 Web SSO 가 커버 |
| DeepLearning 데이터셋 | **공유 유지** (Initialize 미적용) | 학습 데이터셋은 라인 종속이 아닌 모델 자산. 필요 시 후속 확장 |
| 인스턴스 폴더 위치 | Root **하위** `instances\<이름>` | 단일 루트 유지(지원 패키지/백업 도구 파일 목록 기반이라 안전), 인스턴스 열거 용이 |
| 잘못된 인스턴스 이름 | **fail-fast 종료** | 조용한 기본 폴백은 카메라 공유 사고로 직결 |

## 4. 운용 절차 (요약 — 상세는 msi_build_guide / 매뉴얼 §5.5)

1. 바로가기 2개 생성: `VMS.exe` (기본), `VMS.exe --instance line2`.
2. `AppSetup.exe --instance line2` 실행 → 2번 라인의 카메라(IP 겹치지 않게)·ClientIndex·PLC 구성 저장.
3. 각 바로가기로 VMS 실행 — 서로 다른 카메라에 연결, Web 에는 두 라인으로 표시.
4. 주의: GigE 대역폭은 NIC 공유 — 패킷 크기/인터패킷 딜레이는 두 인스턴스 합산으로 튜닝.

## 5. 남은 것 (PR 2 범위)

- AppSetup 헤더에 인스턴스 배지 (어느 인스턴스를 설정 중인지 시각화)
- StartupHealthCheck 결과에 인스턴스 이름 표기
- 매뉴얼 §5.5 "한 PC 두 라인" 절 + msi_build_guide 바로가기 절차
- (선택) AppSetup 저장 시 다른 인스턴스 config 와 카메라 IP 중복 경고

## 6. 검증

- 단위 테스트 15종 (해석 우선순위 / 이름 검증 / 경로·IPC 파생) + 소스 스캔 가드
- 전체 스위트 687 테스트 통과, `--instance` 미지정 시 경로가 기존과 바이트 동일
- 스모크: `AppSetup.exe --instance smoketest` → `instances\smoketest\` 격리 확인
