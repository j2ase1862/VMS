# 혼합 레시피 WorkOrder 설계 (Phase A)

작성: 2026-08-19 · 상태: **확정** (사용자 결정 반영) · 관련: Web #68/#70 (완료 기준), VMS #321 (1사이클=1개)

## 1. 배경

현재 WorkOrder 는 "한 라인 = 한 시점에 한 오더 + 한 레시피"인 배치 생산 모델이다.
자동차 양산형 혼류 라인(여러 품종이 한 라인에 섞여 흐르고 품종별 수량이 계획되는 형태)에서는
사이클 결과가 선택된 WO 하나에 고정 귀속되어 품종별 수량 집계가 불가능하다.

## 2. 확정된 결정

| 항목 | 결정 |
|---|---|
| 모델링 | **하나의 혼합 WO** — WO 가 레시피별 계획 수량 라인을 가진다 (여러 WO 동시 진행 아님) |
| 품종 식별 | **기존 로직 그대로** — RecipeChange 노드가 PLC 워드 또는 IO 보드 DI 포트에서 인덱스 수신 (엔진 추가 개발 없음, `SequenceEngine.ReadIndexAsync` 가 이미 양쪽 지원) |
| 범위 | **Phase A** = 혼합 WO 스키마 + 레시피별 귀속 + UI. Barcode/RFID 개체 추적은 Phase B |
| 상위 MES | 범위 외 — MES 존재 현장은 WO 수신(미러)으로 이 구조를 컨테이너로 재사용 |

## 3. 데이터 모델 (Web)

```
WorkOrderItem
  Id             INTEGER PK
  WorkOrderId    INTEGER FK → WorkOrders (CASCADE)
  RecipeId       INTEGER FK → Recipes
  PlannedQty     INTEGER NOT NULL
  ProducedQty    INTEGER NOT NULL DEFAULT 0
  PassQty        INTEGER NOT NULL DEFAULT 0
  NgQty          INTEGER NOT NULL DEFAULT 0
  UNIQUE (WorkOrderId, RecipeId)      -- 같은 레시피 중복 라인 금지
```

- WO 본체의 `PlannedQuantity/ProducedQuantity/PassQuantity/NgQuantity` 는 **라인 합계(롤업)** 로 유지
  — 기존 대시보드/칩/API 호환.
- WO 본체의 `RecipeId` 는 유지하되 "대표 레시피(첫 라인)"로 의미 축소 (기존 화면 호환용).
- **마이그레이션**: 기존 WO 는 시작 시 1회, 라인이 없으면 `(RecipeId, PlannedQuantity)` 로
  라인 1개 자동 생성 (단일 = 라인 1개짜리 특수 케이스로 통일).
- 스키마 관리는 기존 관례(Program.cs `CREATE TABLE IF NOT EXISTS` + PRAGMA 보강)를 따른다.

## 4. 귀속 규칙 (업로드 엔드포인트)

사이클 판정 업로드(`/api/parameters/results`)의 `RecipeId` 가 귀속 키다.

1. `WorkOrderId` 의 WO 에서 `RecipeId` 와 일치하는 라인을 찾는다.
2. 있으면: 라인 카운트 +1 (Produced/Pass/Ng) + WO 롤업 +1.
3. **없으면: 어느 라인에도 넣지 않고 WO 롤업도 올리지 않는다.** 응답에 `unmatchedRecipe: true`
   를 실어 VMS 가 경고 로그("WO 에 없는 레시피로 검사됨")를 남긴다. 조용히 다른 라인에
   귀속하는 것 금지 — 수량 오염 방지.
4. 검사 이력(InspectionHistory)·Lot 카운터는 기존과 동일하게 기록 (귀속 실패와 무관).

## 5. 완료 판정

- **모든 라인이 각자 계획 수량에 도달**하면 WO 자동 Completed.
- 완료 기준(`CompletionBasis`)은 WO 단위 설정을 **모든 라인에 동일 적용**:
  Produced 기준(기본) = 라인별 ProducedQty ≥ PlannedQty / Pass 기준(opt-in) = 라인별 PassQty ≥ PlannedQty.
- 진행률 브로드캐스트에 라인별 스냅샷 배열을 동봉 (VMS 사이드 패널 표시용).

## 6. UI

**Web (WorkOrders)**
- 생성/편집 다이얼로그: 레시피+수량 행 추가/삭제 미니 테이블 (기본 1행 = 기존과 동일한 UX).
- 목록: 진척률은 합계 기준(기존 그대로), 행 확장/상세에서 라인별 진행.

**VMS**
- 헤더 WO 칩: 합계 기준 표시(변경 없음).
- 사이드 패널(작업지시 상세): 라인별 `레시피 · 진행/계획 (OK/NG)` 목록.
- 미귀속 경고: 시스템 로그(WorkOrder 출처) Warning.

## 7. 운전 흐름 (Phase A 완성 시)

```
PLC/IO 보드: 품종 인덱스 세팅 → 트리거
시퀀스: WaitTrigger → RecipeChange(인덱스 수신·레시피 전환) → Inspection → Repeat
업로드: 사이클 판정 + 현재 RecipeId → 서버가 해당 라인에 귀속
완료: 모든 라인 계획 도달 → 자동 Completed + 브로드캐스트
```

## 8. Phase B 개요 (범위 외, 이 구조 위에 증축)

- 시리얼(Barcode/RFID) → WO 라인 사전 매핑 테이블, 업로드 `SerialNumber`(기존 필드)로 귀속을
  레시피 대신 개체 기준으로 상향. 재작업·개체 이력 추적 제공.
- 카메라 판독(CodeReaderTool) 또는 외부 스캐너 입력 경로는 그때 결정.

## 9. 구현 작업 목록

**Web (PR 1개)**
- [ ] WorkOrderItem 테이블 + 시작 시 마이그레이션(기존 WO → 라인 1개)
- [ ] WorkOrderService: 라인 CRUD(생성/편집 시 라인 배열), 롤업 계산
- [ ] 업로드 엔드포인트: 라인 귀속 + 완료 판정(전 라인) + unmatchedRecipe 응답 + 라인 스냅샷 브로드캐스트
- [ ] WO 다이얼로그 라인 편집 UI + 목록 상세, i18n
- [ ] 테스트: 라인 CRUD/롤업/귀속/미스매치/완료 판정

**VMS (PR 1개)**
- [ ] WorkOrderDto/ProgressDto 라인 배열 수신 + Sanitize
- [ ] 사이드 패널 라인별 진행 표시, 미귀속 경고 로그
- [ ] 테스트: DTO 라인 롤업/미귀속 처리

**엔진/시퀀스**: 변경 없음 (RecipeChange 기존 기능 사용).
