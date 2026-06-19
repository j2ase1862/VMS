#!/usr/bin/env python3
"""
BODA.VMS.Web 데모 DB 시드 — 홍보 영상 캡처용 현실적인 MES/품질 데이터.
대상: C:\\ProgramData\\BODA\\VMS\\BodaVision_demo.db  (실제 DB와 분리)

대시보드/파레토/SPC/OEE/MTBF/예방보전/알람이 '살아있는' 차트로 보이도록 시계열 생성.
시간은 UTC(앱이 DateTime.UtcNow.Date 로 '금일' 판정) 기준, 자정 롤오버 대비 +8h 까지 확장.
"""
import sqlite3
import random
import math
from datetime import datetime, timedelta

DB = r"C:\ProgramData\BODA\VMS\BodaVision_demo.db"
random.seed(2026)

now = datetime.utcnow()
START = now - timedelta(days=7)
# '오늘(UTC)' 전체 일자를 채워 대시보드 금일/추세가 가득 차게 (다음 UTC 자정까지)
END = datetime(now.year, now.month, now.day) + timedelta(days=1)
STEP = timedelta(seconds=120)           # 클라이언트당 검사 간격


def iso(dt):
    return dt.strftime('%Y-%m-%dT%H:%M:%S')


con = sqlite3.connect(DB)
con.execute("PRAGMA busy_timeout=8000")
cur = con.cursor()

# 재실행 가능하도록 기존 시드 데이터 정리 (Shifts/Users 는 보존)
for _t in ["AlarmEvents", "MaintenanceRecords", "MaintenanceSchedules", "EquipmentStatusLogs",
           "ParameterMeasurements", "InspectionHistories", "Lots", "WorkOrders", "OperatorSessions",
           "Operators", "Products", "DefectCodes", "RecipeParameters", "Recipes", "Clients"]:
    cur.execute(f"DELETE FROM {_t}")

# ── 1) 클라이언트(라인) ─────────────────────────────────────────
clients = [
    (1, "라인 A · 캡 라벨", "192.168.0.101", 1),
    (2, "라인 B · 변속 기어", "192.168.0.102", 2),
    (3, "라인 C · 도어 패널", "192.168.0.103", 3),
]
for cid, name, ip, idx in clients:
    cur.execute(
        'INSERT INTO Clients (Id,Name,IpAddress,"Index",IsActive,CreatedAt,LastSeenAt,HostName,SwName) '
        'VALUES (?,?,?,?,1,?,?,?,?)',
        (cid, name, ip, idx, iso(now - timedelta(days=40)), iso(now - timedelta(seconds=5)),
         f"VMS-PC-{idx:02d}", "VMS.VisionSetup 1.0"))

# ── 2) 레시피 + 파라미터(USL/LSL) ──────────────────────────────
recipes = [
    (1, "캡 라벨 OCR/코드 검사", 1),
    (2, "클러스터 기어 치수 검사", 2),
    (3, "도어 패널 표면 검사", 3),
]
for rid, rname, cid in recipes:
    cur.execute('INSERT INTO Recipes (RecipeID,RecipeName,ClientID,RecipeIndex,Description,CreatedAt) '
                'VALUES (?,?,?,?,?,?)', (rid, rname, cid, rid, f"{rname} 레시피", iso(now - timedelta(days=30))))

# (RecipeId, ParamCode, nominal, LSL, USL, desc, unit)
params = [
    (1, 1, 10.00, 9.85, 10.15, "라벨 외경 Ø", "mm"),
    (1, 2, 25.00, 24.70, 25.30, "라벨 높이 H", "mm"),
    (2, 1, 46.00, 45.80, 46.20, "기어 팁 외경", "mm"),
    (3, 1, 1.20, 0.00, 2.00, "표면 결함 깊이", "mm"),
]
for rid, pc, nom, lsl, usl, desc, unit in params:
    cur.execute('INSERT INTO RecipeParameters (RecipeId,ParamCode,ParamValue,Description,Category,Unit,IsActive,CreatedAt,LowerLimit,UpperLimit) '
                'VALUES (?,?,?,?,?,?,1,?,?,?)',
                (rid, pc, nom, desc, "Dimension", unit, iso(now - timedelta(days=30)), lsl, usl))

# ── 3) 불량 코드(파레토용, 내림차순 가중치) ────────────────────
defects = [
    ("SCR-01", "표면 스크래치", "Surface", "Major", 0.34),
    ("DNT-02", "프레스 덴트", "Surface", "Major", 0.22),
    ("PRT-03", "인쇄 번짐/불량", "Print", "Minor", 0.15),
    ("DIM-04", "치수 규격 초과", "Dimension", "Critical", 0.10),
    ("LBL-05", "라벨 정렬 불량", "Pattern", "Major", 0.08),
    ("BLB-06", "이물/얼룩", "Blob", "Minor", 0.05),
    ("COD-07", "코드 판독 불가", "Code", "Critical", 0.04),
    ("CHP-08", "치손/모서리 깨짐", "Surface", "Critical", 0.02),
]
for code, desc, cat, sev, _w in defects:
    cur.execute('INSERT INTO DefectCodes (Code,Description,Category,Severity,IsActive,CreatedAt) '
                'VALUES (?,?,?,?,1,?)', (code, desc, cat, sev, iso(now - timedelta(days=30))))
ng_codes = [d[0] for d in defects]
ng_weights = [d[4] for d in defects]

# ── 4) 제품 ────────────────────────────────────────────────────
products = [
    ("PRD-001", "백신 바이알 라벨", 1),
    ("PRD-002", "변속기 클러스터 기어", 2),
    ("PRD-003", "도어 패널 ASSY", 3),
]
for code, name, rid in products:
    cur.execute('INSERT INTO Products (Code,Name,Specification,DefaultRecipeId,IsActive,CreatedAt) '
                'VALUES (?,?,?,?,1,?)', (code, name, f"{name} 사양", rid, iso(now - timedelta(days=30))))

# ── 5) 작업자 ──────────────────────────────────────────────────
ops = [("EMP-001", "김현우", "조립1팀", "Lead"), ("EMP-002", "이서연", "검사1팀", "Operator"),
       ("EMP-003", "박지훈", "검사1팀", "Operator"), ("EMP-004", "최민지", "조립2팀", "Operator"),
       ("EMP-005", "정우성", "품질팀", "Supervisor")]
DUMMY_PIN = "$2a$11$abcdefghijklmnopqrstuv0123456789ABCDEFGHIJKLMNOPQR"
for emp, name, dept, role in ops:
    cur.execute('INSERT INTO Operators (EmployeeNumber,Name,PinHash,Department,IsActive,CreatedAt,Role) '
                'VALUES (?,?,?,?,1,?,?)', (emp, name, DUMMY_PIN, dept, iso(now - timedelta(days=20)), role))
op_ids = [r[0] for r in cur.execute("SELECT Id FROM Operators").fetchall()]

# ── 6) 작업지시 + 로트 (라인별 진행중 1 + 완료 1) ──────────────
client_wo = {}   # client -> 진행중 WO id
client_lot = {}  # client -> 로트 id
for cid, _n, _ip, _i in clients:
    rid = cid
    pid = cid
    # 완료 WO
    on1 = f"WO-{(now-timedelta(days=2)).strftime('%Y%m%d')}-{cid:02d}1"
    cur.execute('INSERT INTO WorkOrders (OrderNo,ProductId,ClientId,RecipeId,PlannedQuantity,Status,PlannedStartAt,ActualStartAt,ActualEndAt,CreatedAt) '
                'VALUES (?,?,?,?,?,?,?,?,?,?)',
                (on1, pid, cid, rid, 5000, "Completed", iso(now - timedelta(days=3)),
                 iso(now - timedelta(days=3)), iso(now - timedelta(days=1)), iso(now - timedelta(days=3))))
    # 진행중 WO
    on2 = f"WO-{now.strftime('%Y%m%d')}-{cid:02d}1"
    cur.execute('INSERT INTO WorkOrders (OrderNo,ProductId,ClientId,RecipeId,PlannedQuantity,Status,PlannedStartAt,ActualStartAt,CreatedAt) '
                'VALUES (?,?,?,?,?,?,?,?,?)',
                (on2, pid, cid, rid, 8000, "InProgress", iso(now - timedelta(hours=20)),
                 iso(now - timedelta(hours=20)), iso(now - timedelta(hours=20))))
    wo_id = cur.lastrowid
    client_wo[cid] = wo_id
    lot_no = f"{now.strftime('%Y%m%d')}-{on2}-001"
    cur.execute('INSERT INTO Lots (LotNumber,WorkOrderId,Sequence,Quantity,Status,CreatedAt) '
                'VALUES (?,?,?,?,?,?)', (lot_no, wo_id, 1, 8000, "Open", iso(now - timedelta(hours=20))))
    client_lot[cid] = cur.lastrowid

# ── 7) 검사 이력(핵심 시계열) ──────────────────────────────────
def shift_for(dt):
    h = dt.hour
    if 8 <= h < 16: return 1
    if 16 <= h <= 23: return 2
    return 3

NG_RATE = 0.035
client_weight = {1: 1.0, 2: 0.85, 3: 0.7}   # 라인별 가동률 차이
insp_rows = []
sn = {1: 0, 2: 0, 3: 0}
for cid, _n, _ip, _i in clients:
    rname = recipes[cid - 1][1]
    t = START
    while t <= END:
        # 가동률에 따라 일부 스텝 스킵
        if random.random() > client_weight[cid]:
            t += STEP
            continue
        is_ng = random.random() < NG_RATE * (1.0 + 0.3 * math.sin(t.timestamp() / 86400))
        ngcode = random.choices(ng_codes, ng_weights)[0] if is_ng else None
        sn[cid] += 1
        insp_rows.append((
            cid, rname, 0 if is_ng else 1, ngcode, iso(t),
            client_wo[cid], client_lot[cid], random.choice(op_ids),
            f"SN-{cid:02d}-{sn[cid]:06d}", shift_for(t),
            random.randint(750, 1500),
            round(random.gauss(125, 12), 1), round(random.gauss(31, 4), 1),
            round(min(1.0, max(0.4, random.gauss(0.82, 0.07))), 3),
            random.randint(0, 4), round(random.uniform(0, 40), 1),
            round(min(1.0, max(0.5, random.gauss(0.93, 0.04))), 3),
        ))
        t += STEP

cur.executemany(
    'INSERT INTO InspectionHistories '
    '(ClientId,RecipeName,IsPass,NgCode,InspectedAt,WorkOrderId,LotId,OperatorId,SerialNumber,ShiftId,CycleTimeMs,'
    'Brightness,ContrastStd,FocusScore,BlobCount,MaxBlobAreaPx,DlConfidence) '
    'VALUES (?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?)', insp_rows)

# ── 8) 파라미터 측정값(SPC, 라인A/레시피1/파라미터1, 최근 3일) ──
since = iso(now - timedelta(days=3))
rows = cur.execute(
    "SELECT Id,InspectedAt,WorkOrderId,LotId FROM InspectionHistories "
    "WHERE ClientId=1 AND InspectedAt>=? ORDER BY InspectedAt", (since,)).fetchall()
pm = []
for hid, iat, woid, lotid in rows:
    # 평균 10.00, 표준편차 0.035, 완만한 드리프트
    drift = 0.02 * math.sin(datetime.fromisoformat(iat).timestamp() / 43200)
    val = round(random.gauss(10.00 + drift, 0.035), 3)
    judg = "OK" if 9.85 <= val <= 10.15 else "NG"
    pm.append((hid, 1, 1, val, judg, iat, 1, woid, lotid))
cur.executemany('INSERT INTO ParameterMeasurements (HistoryId,RecipeId,ParamCode,MeasuredValue,Judgment,InspectedAt,ClientId,WorkOrderId,LotId) '
                'VALUES (?,?,?,?,?,?,?,?,?)', pm)

# ── 9) WO/로트 집계 업데이트 ───────────────────────────────────
for cid in (1, 2, 3):
    woid = client_wo[cid]
    tot, pas = cur.execute("SELECT COUNT(*),SUM(IsPass) FROM InspectionHistories WHERE WorkOrderId=?", (woid,)).fetchone()
    pas = pas or 0
    cur.execute("UPDATE WorkOrders SET ProducedQuantity=?,PassQuantity=?,NgQuantity=? WHERE Id=?", (tot, pas, tot - pas, woid))
    cur.execute("UPDATE Lots SET Quantity=?,PassCount=?,NgCount=? WHERE WorkOrderId=?", (tot, pas, tot - pas, woid))

# ── 10) 설비 상태 로그(OEE/MTBF) ───────────────────────────────
# 라인별로 7일간 가동/대기/정지 세그먼트. 마지막 세그먼트는 EndedAt NULL(현재 가동).
status_rows = []
for cid, _n, _ip, _i in clients:
    t = START
    while t < END:
        r = random.random()
        if r < 0.78:
            st, dur, reason = "Running", random.uniform(1.5, 4.0), None
        elif r < 0.93:
            st, dur, reason = "Idle", random.uniform(0.2, 0.8), "자재 교체/대기"
        else:
            st, dur, reason = "Down", random.uniform(0.3, 1.2), random.choice(["설비 점검", "센서 알람", "비전 오류 복구"])
        en = t + timedelta(hours=dur)
        last = en >= END
        status_rows.append((cid, st, iso(t), None if last else iso(min(en, END)), reason))
        t = en
cur.executemany('INSERT INTO EquipmentStatusLogs (ClientId,Status,StartedAt,EndedAt,Reason) VALUES (?,?,?,?,?)', status_rows)

# ── 11) 예방 보전 일정 + 수행 기록 ─────────────────────────────
scheds = [
    (None, "주간 일상 점검", 7, 30),
    (None, "월간 정밀 점검", 30, 120),
    (1, "라인 A 카메라 캘리브레이션", 14, 45),
    (3, "라인 C 조명 점검", 21, 60),
]
sched_ids = []
for cid, name, interval, dur in scheds:
    last_perf = now - timedelta(days=random.randint(1, interval - 1))
    nxt = last_perf + timedelta(days=interval)
    cur.execute('INSERT INTO MaintenanceSchedules (ClientId,Name,Description,IntervalDays,EstimatedDurationMinutes,LastPerformedAt,NextDueAt,IsActive,CreatedAt) '
                'VALUES (?,?,?,?,?,?,?,1,?)',
                (cid, name, f"{name} 표준 절차", interval, dur, iso(last_perf), iso(nxt), iso(now - timedelta(days=60))))
    sched_ids.append((cur.lastrowid, cid, interval, dur))
for sid, cid, interval, dur in sched_ids:
    for k in range(1, 4):
        perf = now - timedelta(days=interval * k + random.randint(0, 2))
        cur.execute('INSERT INTO MaintenanceRecords (ScheduleId,ClientId,PerformedAt,ActualDurationMinutes,PerformedByName,Notes,PreviousDueAt,NewDueAt) '
                    'VALUES (?,?,?,?,?,?,?,?)',
                    (sid, cid, iso(perf), dur + random.randint(-10, 15), random.choice(["김현우", "정우성", "박지훈"]),
                     "정상 완료", iso(perf - timedelta(days=interval)), iso(perf + timedelta(days=interval))))

# ── 12) 알람 이벤트(최근) ──────────────────────────────────────
alarms = [
    (1, "NG", "Major", "연속 NG 3회 발생", "라인 A 캡 라벨 정렬 불량 연속 검출", 0.4, True, True),
    (3, "Down", "Critical", "설비 정지 - 비전 오류", "라인 C 카메라 통신 오류로 정지", 1.5, True, True),
    (2, "NG", "Critical", "치수 규격 초과", "라인 B 기어 외경 USL 초과", 2.2, True, False),
    (1, "Quality", "Major", "불량률 임계 초과", "라인 A 시간당 불량률 5% 초과", 5.0, False, False),
    (3, "Down", "Major", "예방보전 임박", "라인 C 조명 점검 D-1", 8.0, True, False),
    (2, "NG", "Minor", "이물 검출", "라인 B 이물/얼룩 검출", 12.0, True, True),
    (1, "Info", "Minor", "교대 전환", "주간조 → 저녁조 전환 완료", 14.0, True, True),
    (3, "NG", "Major", "표면 스크래치 다발", "라인 C 도어 패널 스크래치 군집", 0.8, False, False),
]
for cid, atype, sev, title, msg, hago, ack, res in alarms:
    occ = now - timedelta(hours=hago)
    ackat = iso(occ + timedelta(minutes=random.randint(2, 15))) if ack else None
    resat = iso(occ + timedelta(minutes=random.randint(20, 90))) if res else None
    cur.execute('INSERT INTO AlarmEvents (ClientId,AlarmType,Severity,Title,Message,OccurredAt,AcknowledgedAt,AcknowledgedByName,ResolvedAt,ResolvedByName,Resolution) '
                'VALUES (?,?,?,?,?,?,?,?,?,?,?)',
                (cid, atype, sev, title, msg, iso(occ), ackat, "정우성" if ack else None,
                 resat, "김현우" if res else None, "조치 완료" if res else None))

con.commit()

# ── 요약 ───────────────────────────────────────────────────────
def cnt(t):
    return cur.execute(f"SELECT COUNT(*) FROM {t}").fetchone()[0]

today = iso(now.replace(hour=0, minute=0, second=0, microsecond=0))
tot_today = cur.execute("SELECT COUNT(*) FROM InspectionHistories WHERE InspectedAt>=?", (today,)).fetchone()[0]
pass_today = cur.execute("SELECT COUNT(*) FROM InspectionHistories WHERE InspectedAt>=? AND IsPass=1", (today,)).fetchone()[0]
print("seeded:")
for t in ["Clients", "Recipes", "RecipeParameters", "Products", "DefectCodes", "Operators",
          "WorkOrders", "Lots", "InspectionHistories", "ParameterMeasurements",
          "EquipmentStatusLogs", "MaintenanceSchedules", "MaintenanceRecords", "AlarmEvents"]:
    print(f"  {t:22s} {cnt(t):,}")
print(f"\n금일(UTC) 생산={tot_today:,}  합격={pass_today:,}  불량률={100*(tot_today-pass_today)/max(1,tot_today):.2f}%")
con.close()
print("done.")
