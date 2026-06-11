# 설계 문서 — 검사 이미지 아카이브 & Web 연동

> 상태: **APPROVED (2026-06-11)** · 작성: 2026-06-11 · 대상 레포: `VMS`(데스크톱) + `BODA.VMS.Web`(웹)
>
> 검토 완료. 양쪽 레포에 feature 브랜치로 **동시 구현**.

## 확정 결정 (2026-06-11)

1. **모드 A 보존 단일화** 채택 — 같은 머신/공유경로일 때 로컬 폴더 = 아카이브, 보존기간 길게.
2. **Web 전송 기본 = 썸네일.** OK/NG 각각 **Web 전송 여부**와 **VMS 로컬 저장 여부**는 독립 설정.
   목적: Web 생산 이력에서 이미지 열람(따라서 썸네일로 충분).
3. **저장 모델 = `InspectionHistory` 확장** (별도 테이블 X).
4. **OK 이미지 Web 표시** = 별도 갤러리 없이 생산 이력 상세에서 열람(NG 상세 UI를 OK까지 확장). (#2 목적으로 해결)
5. **기본값 (담당자 결정):**
   - 썸네일: **장변 1024px, JPEG q80** (~100–250KB, 결함 식별 가능 + 풀 대비 10–20× 절감).
   - 로컬 업로드 큐 상한: **5,000개 또는 2GB** 중 먼저. 초과 시 **오래된 OK부터 폐기**, NG는 보존(단 NG만으로 상한 초과 시 오래된 NG 폐기 + 경고 로그).
   - throttle: **동시 2개**, 재시도 백오프 5s→×2→최대 5분. 대역폭 cap 기본 off(설정 가능, 1차 완화는 NIC 분리).
   - 업로드 트리거: 기존 5초 드레인 타이머 재사용 + enqueue 시 즉시 kick.
6. **진행 순서 = 동시** (예약작업 §7 / VMS 업로드 §9 / Web §9 병행).
7. **BODA.VMS.Web 구현 포함** — `feat/inspection-image-archive` 브랜치.

---

## 1. 배경 / 목표

- VMS(WPF)는 검사 판정 시 OK/NG 이미지를 **로컬 폴더**에 저장한다(`imageSave`, 이미 구현).
  현재 구조: `{BaseDir}\{yyyy-MM-dd}\{OK|NG}\{규칙 파일명}.{ext}`
- 책임 분리 합의:
  - **로컬 = 단기 롤링 버퍼** → 오래된 것 자동 삭제(예약 작업, 별도 합의 §7)
  - **BODA.VMS.Web = 관리/조회 아카이브** → 저장·조회·보존
- 본 문서는 **이미지를 Web에서 보이게 하는 방식**과 그로 인한 **택트 타임/네트워크/디스크 리스크**를 확정한다.

### 1차 범위(합의)
- OK + NG **둘 다** 대상.
- 이미지 저장 설정 창에 **썸네일 / 풀(원본) 선택** 옵션 추가.
- Web 표시는 기존 `NgDetailDialog`(NG 상세) 재사용 + OK 표시 위치는 §6에서 정의.

---

## 2. 현황 조사 결과 (2026-06-11, 코드 확인)

### VMS → Web 업로드 (이미지 없음)
- 검사 결과는 **POST `/api/parameters/results`** 로 **JSON만** 전송: 판정/측정값/WO·Lot·S/N/영상파생지표.
  이미지 바이트는 **전혀 없음**. (`ParameterResultUploadRequest`)
- 실패 시 `%LocalAppData%\BODA VISION AI\upload_queue\*.json` 에 적재 후 **5초 타이머 재시도** (fire-and-forget). 보존정책 존재.

### BODA.VMS.Web (이미지 미처리 — 그러나 표시 자리 준비됨)
- 이미지 ingest/저장/serving 엔드포인트 **없음**, 보존정책 **없음**.
- **잠재 인프라 존재**:
  - DB `InspectionHistory.ImagePath`(max 500) — **항상 NULL**.
  - `HistoryDetailDto.ImagePath` 흐름 연결됨(HistoryService).
  - Blazor `NgDetailDialog.razor` 가 **이미지 표시 UI 이미 구현**(`<MudImage Src="@Detail.ImagePath">`, 비면 placeholder).

→ "레코드별 이미지 1장 표시"는 **데이터만 채우면 동작**.

---

## 3. 지배 원칙

> **이미지 전달은 검사 택트 임계경로에 절대 두지 않는다.**

검사 사이클은 **로컬 저장(빠름) + 작업 enqueue(참조만, 쌈)** 까지만 수행하고 즉시 다음 검사로 진행.
실제 전송은 **독립 백그라운드 워커**가 담당. 네트워크가 느리거나 끊겨도 택트 영향 0, 이미지는 나중에 따라잡음(큐 상한 내).

---

## 4. 토폴로지 2모드

배포 환경이 같은 머신일 수도, 다른 머신일 수도 있으므로 모드를 명시적으로 둔다.
설정 키 `imageDelivery`: `auto`(기본) | `sharedPath` | `upload`.
`auto` = Web base URL 호스트가 localhost/자기 호스트면 sharedPath, 아니면 upload. (오탐 방지를 위해 수동 override 허용)

### 모드 A — 같은 머신 / 공유 경로 (전송 없음) ✅ 권장(같은 머신)
- VMS는 로컬에 쓰기만 함. **업로드/큐/NIC 부하 없음.**
- Web은 **그 경로를 직접 읽어** 서빙하거나 `ImagePath`에 그 경로(또는 매핑된 가상경로)를 저장.
- 다른 머신이어도 **UNC 공유**가 Web 서버에서 읽기 가능하면 이 모드 사용 가능.
- **보존 주의(중요)**: 이 모드에서는 로컬 폴더가 곧 Web이 읽는 아카이브다. → "로컬 단기 버퍼 + 짧은 삭제"를 그대로 적용하면 **Web 아카이브를 지우는 셈**.
  - 정책: 모드 A에서는 **보존이 단일 정책**(그 폴더 = 아카이브). 예약작업 보존기간을 **아카이브 요구에 맞게 길게** 설정하고 Web은 in-place 참조.

### 모드 B — 다른 머신 (업로드)
- VMS는 로컬 저장 후 **백그라운드 업로더**가 Web으로 전송. Web은 **자기 저장소에 사본** 보관.
- **보존이 분리됨**: 로컬=단기 버퍼(짧게 삭제), Web=장기 아카이브(자체 보존).
- 모든 §5 리스크 완화가 이 모드에 적용됨.

---

## 5. 리스크 검토 & 완화 (모드 B 중심)

| # | 리스크 | 영향 | 완화 |
|---|--------|------|------|
| A | 업로드가 택트 임계경로에 끼임 | 택트 직접 악화 | 로컬우선 저장 + 비동기 큐 + **독립 업로더**(§3) |
| B | **카메라 NIC 대역폭 경합** (GigE Vision 추정) | **이미지 획득 흔들림** | 카메라 NIC와 LAN/Web NIC **분리**(권장) · 업로드 **대역폭 throttle** · **썸네일/압축** · 유휴 우선 전송 |
| C | Web 다운 시 로컬 큐/디스크 폭증 | 디스크 풀 | 큐 **상한(개수·MB)** + **드롭정책**(OK 먼저 폐기, NG 우선 보존) |
| D | Web 수신 부하 | Web 지연 | 비동기 ingest · **파일시스템 저장(DB blob 비추천)** · 배치/병렬 상한 |
| E | 신뢰성/순서/중복 | 데이터 정합 | 레코드 **상관키 + 멱등 ingest + 지수 백오프** |

> ⚠️ B(NIC 경합)는 백그라운드 분리로 해결되지 않는 **물리 네트워크 경합**이다. GigE 카메라 환경에서 가장 주의.

---

## 6. API 계약 (모드 B)

### 6.1 업로드
- **신규** `POST /api/inspection-images` — `multipart/form-data`
  - parts: `image`(파일, png/jpg/...), `meta`(JSON)
  - `meta`: `{ clientIndex, recipeId, verdict(OK|NG), capturedAt, workOrderId?, lotId?, serialNumber?, cameraName, step?, variant(full|thumb), correlationKey }`
  - 인증: 기존 `ClientApiKeyEndpointFilter` 재사용.
  - **멱등**: `correlationKey`(예: `clientIndex|capturedAt|cameraName|step|verdict`) 중복 시 200 + 기존 참조 반환(중복 저장 안 함).
- 결과 레코드(`/api/parameters/results`)와의 연결: 동일 `correlationKey`를 양쪽에 실어 Web이 `InspectionHistory` 행에 이미지를 매칭. (레코드 선행/이미지 후행 순서 비보장 → Web이 키로 지연 매칭)

### 6.2 서빙
- **신규** `GET /api/inspection-images/{id}` (+ `?variant=thumb|full`) — 저장된 파일 스트림 반환.
- `InspectionHistory.ImagePath` 는 이 서빙 URL(또는 내부 식별자)로 채움. NgDetailDialog 가 그대로 표시.

### 6.3 저장 (Web)
- 파일시스템: `{ImageStoreRoot}\{yyyy-MM-dd}\{verdict}\{correlationKey}.{ext}` (DB blob 비사용).
- 서빙: `UseStaticFiles` + `PhysicalFileProvider`(RequestPath=`/images`). `ImagePath` = `/images/...` 상대 URL.
- DB: **`InspectionHistory` 확장**(결정 #3) — `CorrelationKey`(인덱스) 컬럼 추가 + 기존 `ImagePath` 사용. 별도 테이블 없음.
  - 스키마는 EF 마이그레이션이 아니라 **Program.cs 부트스트랩 SQL**(CREATE TABLE IF NOT EXISTS + PRAGMA table_info + ALTER TABLE ADD COLUMN, 멱등)로 관리 → `CorrelationKey` 컬럼 추가 분기만 넣으면 됨.

### 6.4 상관(correlation) & 순서 보장 — 핵심
이미지 업로드(`/api/inspection-images`)와 결과 업로드(`/api/parameters/results`)는 **독립 경로**라 도착 순서가 보장되지 않는다. 해결:
- **공유 `CorrelationKey`** (구현: 방식 A): `InspectionService.ExecuteStep` 이 검사 1회당 **GUID 키 1개**를 생성해 `StepInspectionResult.CorrelationKey` 에 싣는다. 이 키가 두 경로로 흐른다:
  - 결과 업로드: `UploadResultsAsync(..., correlationKey)` → `ParameterResultUploadRequest.CorrelationKey` → 결과 엔드포인트가 `InspectionHistory.CorrelationKey` 저장.
  - 이미지 업로드: `InspectionCompleted(..., correlationKey)` 이벤트 → `InspectionImageContext.CorrelationKey` → 이미지 meta.
  - (GUID 사용 — 판정값이 키에 안 들어가므로 "결과 업로드 시점엔 판정 확정 전" 같은 순환 의존 없음. 동일 키 보장.)
- **순서 무관 매칭(별도 테이블 없이)**: 이미지 ingest가 `CorrelationKey`로 `InspectionHistory`를 조회 —
  - **있으면**: 파일 저장 + `ImagePath` 세팅 → 200.
  - **없으면**(결과가 아직 도착 전): **409 반환**(디스크 미기록) → **VMS 업로더가 백오프 재시도**(이미 구현한 큐가 그대로 처리). 결과 레코드 생성 후 재시도에서 매칭됨. → 고아 파일 없음, 사이드 테이블 불필요.

---

## 7. 로컬 삭제 = 예약 작업 (별도 합의, 본 설계와 독립 진행 가능)

- 현재 **인프로세스 startup 정리**(MainViewModel)는 **제거**됨. 재사용 가능한 `ImageRetentionCleaner` 로직은 유지.
- **헤드리스 모드 + Windows 예약 작업**(야간)으로 호출 → 디스크 I/O 경합 회피.
  - 구현: `VMS.exe --cleanup-images` → `App.OnStartup` 가 인자 감지 → UI 없이 `ImageRetentionCleaner.Cleanup(ImageSaveOptions.LoadFromAppData())` 실행 후 즉시 종료. 보존 기간/경로는 설정 창의 imageSave 값 사용.
- 모드 A: 보존기간을 아카이브 요구에 맞게 **길게**. 모드 B: 로컬은 **짧게**(단기 버퍼).

### 7.1 예약 작업 등록 (관리자 PowerShell, 매일 03:00)
```powershell
$exe = "C:\Program Files\BODA VISION AI\VMS.exe"   # 실제 설치 경로로 교체
$action  = New-ScheduledTaskAction -Execute $exe -Argument "--cleanup-images"
$trigger = New-ScheduledTaskTrigger -Daily -At 3:00am
$set     = New-ScheduledTaskSettingsSet -StartWhenAvailable -DontStopOnIdleEnd
Register-ScheduledTask -TaskName "BODA VMS Image Cleanup" `
    -Action $action -Trigger $trigger -Settings $set `
    -Description "오래된 검사 이미지 날짜 폴더 정리 (imageSave.retentionDays 기준)" -RunLevel Highest
```
- 보존 기간이 0(무제한)이면 정리는 no-op — 작업이 돌아도 아무것도 삭제 안 함.
- 검사 이미지 저장 경로/보존일은 VMS 설정 창(Image Save Settings)에서 변경.

---

## 8. 이미지 저장 설정 창 변경 (VMS)

- 추가: **`Web 전송 화질`** = `풀(원본)` | `썸네일` (1차 범위)
  - 썸네일이면 업로드 전 리사이즈(예: 장변 640/1024px, 설정값) → 전송량·Web 저장 대폭 절감.
- 추가(모드 노출): `imageDelivery` = 자동/공유경로/업로드 (고급, 기본 자동).
- 기존 항목(OK/NG 저장, 경로, 포맷/품질, 파일명 규칙, 보존기간) 유지.

---

## 9. 작업 분해 (합의 후)

### VMS (`feat/image-web-upload`)
1. 업로드 작업 큐 + 독립 백그라운드 업로더(상한·드롭·throttle·백오프).
2. multipart 업로드 클라이언트(`/api/inspection-images`), correlationKey 생성.
3. 썸네일 생성 옵션 + 설정 창 UI(§8).
4. 모드 감지(`imageDelivery=auto`) + sharedPath 시 무전송 분기.
5. 인프로세스 startup 정리 제거(§7).
6. **(신규) VMS.Core**: `ParameterResultUploadRequest`에 `CorrelationKey` 추가 + 결과 업로드 시 이미지와 동일 키 생성·전송(§6.4).

### BODA.VMS.Web (`feat/inspection-image-archive`)
1. ingest 엔드포인트(멱등) + 저장 서비스(파일시스템).
2. 서빙 엔드포인트 + `ImagePath`(또는 이미지 메타 테이블) 채우기 + correlationKey 매칭.
3. Web측 보존정책(스케줄/일수).
4. UI: NG=NgDetailDialog 재사용, **OK 이미지 표시 위치 정의**(History 상세를 OK까지 확장).

### 예약 작업 (별도, 선행 가능)
5. 콘솔 cleanup 모드 + 예약작업 등록 가이드(MSI 가이드 절차에 추가).

---

## 10. 검증 한계 (솔직히)

- **다른 머신 간 end-to-end**(특히 NIC 경합·실측 택트·대용량 OK 풀해상도 전송 부하)는 이 개발 환경에서 완전 검증 불가.
- 코드/로직/계약/가정은 책임지되, **실장비 실측은 함께** 확인 필요.

---

## 11. 미결 / 검토 요청 항목

1. **모드 A 보존 단일화** 방식 OK? (같은 머신일 때 로컬 폴더 = 아카이브, 보존 길게)
2. **OK 풀해상도 + 다른 머신** 은 최대 부하 케이스 — 기본을 **썸네일**로 둘지, 사용자가 선택만 하게 둘지.
3. Web 저장: `InspectionHistory` 확장 vs **별도 이미지 메타 테이블** 선호?
4. OK 이미지 Web 표시 위치(History 상세 확장 vs 별도 갤러리).
5. 큐 상한/드롭 기본값, 썸네일 기본 해상도, throttle 기본 대역폭.
6. 진행 순서: 예약작업(§7) 먼저 / Web+VMS 업로드 먼저 / 동시.
