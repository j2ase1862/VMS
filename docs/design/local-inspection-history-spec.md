# 설계 문서 — 단독 모드 로컬 생산이력 (inspection_history.db)

> 상태: **1·2단계 구현 (PR #417, 2026-09-04)** · 3단계(조회 창) 후속 · 대상 레포: `VMS`

## 1. 배경

- 단독(Standalone) 모드(#407, `webServerUrl` 빈 값)에서는 Web 의 Production History 가 없다.
- 기존 VMS 에 남는 흔적은 전부 휘발이거나 부분적이었다: Dashboard KPI·NG 썸네일(메모리), Recent Inspections 200건(메모리, 그나마 단독 모드에서는 가드 버그로 0건), 감사 로그 JSONL(NG 만), 판정 이미지 파일(이미지만).
- 목표: **단독 모드에서도 생산 이력이 PC 에 영구 저장되고 조회·내보내기가 가능**할 것. Web 이 있는 현장에서는 오프라인 백업 역할.

## 2. 단계

| 단계 | 내용 | 상태 |
|---|---|---|
| A | Recent Inspections 단독 모드 공백 수정 — 로컬 이력 push 를 Web 가드 밖으로, 실패 도구 이름을 NG 코드 대체, 로컬 레시피 이름 | PR #417 |
| B | SQLite 저장소 + 기록 + 이미지 경로 연결 + 보존 설정(Retention Settings) + 백업 포함 | PR #417 |
| C | 생산 이력 조회 창 (목록·상세 도구 결과·이미지·일별 집계·NG 파레토·CSV/Excel) | 후속 |

## 3. 저장소

- 파일: `{AppDataPaths.Root}\inspection_history.db` (계정 DB `BodaVision.db` 와 **분리** — 그 파일은 Web 공유·소유자 ACL·WAL).
- 저널 모드: 기본(DELETE). WAL 사이드카(-wal/-shm)가 백업 ZIP 에 빠지는 문제를 피한다. `busy_timeout=5000`, `synchronous=NORMAL`.
- 스키마 v1 (`PRAGMA user_version=1`):

```sql
InspectionHistory(Id PK, InspectedAtUtc TEXT 'yyyy-MM-ddTHH:mm:ss.fffZ', IsPass INT, RecipeId INT, RecipeName TEXT,
                  NgCodes TEXT(콤마), ToolResults TEXT(JSON), CorrelationKey TEXT, ImagePath TEXT, ImageIsNg INT,
                  WorkOrderId INT?, LotId INT?, SerialNumber TEXT?, CycleTimeMs INT?, Mode INT(0 Manual/1 Cycle))
IX: InspectedAtUtc / (IsPass, InspectedAtUtc) / CorrelationKey
```

- 쓰기: `Record`/`SetImagePath` 는 `BlockingCollection` 에 push 만. 단일 writer 스레드가 최대 256건을 트랜잭션 1회로 기록. 배치 실패 시 그 배치만 폐기(운전 우선).
- 이미지 경로: `InspectionImageSaver.ImageSaved(ctx, path)` → `SetImagePath(corrKey, path, isNg)`. AUTO RUN 은 이미지가 행보다 먼저 오므로 키별 보류(최대 2000)→삽입 시 부착. NG 이미지가 OK 를 덮고 그 반대는 없음.
- 읽기 API (3단계용): `Query(filter/paging)`, `Count`, `GetDailySummary(from,to)`, `GetNgCodeCounts(from,to,top)`.
- 보존: `PurgeOlderThan(days)` — 부팅 시 1회(App.xaml.cs, upload_queue 정리 직후). 감사 `System · InspectionHistoryRetention`.
- 용량 감: 행당 ~1KB. 1초 택트 24h ≈ 86k행/일 → 90일 ≈ 8M행, 수 GB. 인덱스로 기간·판정 조회는 즉시.

## 4. 기록 지점

`InspectionService.RecordLocalInspection` — Recent Inspections 와 같은 지점. Web 업로드 가드 **바깥**이라 단독/연동 무관.

| 모드 | 언제 | 내용 |
|---|---|---|
| Cycle (AUTO RUN) | `FlushCycleResultAsync` (사이클 완료) | 사이클 내 검사들의 도구 결과·실패 도구·상관 키·스텝 ms 합산 |
| Manual | `ExecuteStep` 직후 `RecordInspectionOutcome` | 검사 1건 |

NG 코드 규약: Web 파라미터 NG 코드가 있으면 그것, 없으면 실패 도구 이름 (Recent Inspections 와 동일).

## 5. 설정

`system_config.json`:

```json
"inspectionHistory": { "enabled": true, "retentionDays": 90 }
```

- 읽기: `InspectionHistoryOptions.LoadFromAppData()` (JsonDocument 수동 파싱, 손상 시 기본값).
- 쓰기: Retention Settings 창(JsonNode 격리 편집). AppSetup/ConfigurationService 는 `SystemConfigMerge` 로 미지 키를 보존하므로 수정 불필요.
- 프리셋: Conservative 365 / Standard 90 / Minimal 30 (`RetentionPresets.InspectionHistory`, GS 개요 §5.10 표 InspHistory 열, `ManualPresetConsistencyTests` 가 일치 검증).

## 6. 운영 연계

- 백업/복원: `BackupRestoreService._topLevelFiles` 에 포함. 복원은 ZIP 전체 순회라 자동.
- 지원 패키지: **미포함** (생산 데이터). 요약 통계만 필요 시 health snapshot 에 추가 후보.
- 종료: `mainWindow.Closed` 에서 `Dispose` — 잔여 배치 반영 후 writer 종료(최대 5초).

## 7. 3단계(조회 창) 메모

- `AuditLogViewerWindow` 패턴 재사용: 기간·판정·레시피·NG 코드 필터, 페이징, CSV 내보내기, 상세(도구 결과 표 + 이미지 열람), 일별 집계 탭, NG 파레토.
- 이미지가 없을 때(저장 옵션 off) 안내 문구. 이미지 파일이 보존 정리로 삭제됐을 수 있으므로 존재 여부 확인 후 표시.
- Web 연동 모드에서는 헤더에 "전체 이력은 Web Production History" 링크 안내.
