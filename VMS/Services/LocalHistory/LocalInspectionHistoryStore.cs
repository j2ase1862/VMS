using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using Microsoft.Data.Sqlite;
using VMS.Camera.Configuration;
using VMS.Core.Retention;
using VMS.Core.Security;

namespace VMS.Services.LocalHistory
{
    /// <summary>
    /// 로컬 검사 이력 SQLite 저장소 — <c>{AppDataPaths.Root}\inspection_history.db</c>.
    ///
    /// 설계 원칙:
    /// - **택트 임계경로 무관**: <see cref="Record"/>/<see cref="SetImagePath"/> 는 큐 push 만 하고 반환.
    ///   단일 백그라운드 스레드가 배치(트랜잭션 1회)로 기록한다.
    /// - **계정 DB(BodaVision.db)와 분리**: 그 파일은 Web 과 공유·소유자 ACL·WAL 이라 섞지 않는다.
    /// - **저널 모드는 기본(DELETE)**: WAL 사이드카(-wal/-shm)가 백업 ZIP 에 빠지는 문제를 피한다.
    ///   같은 프로세스의 뷰어 읽기와 writer 배치가 겹치면 busy_timeout 으로 대기.
    /// - **이미지 경로 후속 갱신**: 이미지는 검사마다 저장되지만 행은 사이클 끝에 1건 삽입되므로
    ///   순서가 뒤바뀐다. 행이 없으면 키별로 보류했다가 삽입 시 붙이고, NG 이미지를 OK 보다 우선한다.
    /// </summary>
    public sealed class LocalInspectionHistoryStore : ILocalInspectionHistoryStore
    {
        public const string DbFileName = "inspection_history.db";
        private const int SchemaVersion = 1;
        private const int MaxBatch = 256;
        private const int MaxPendingImages = 2000;
        private const string UtcFormat = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";

        private static readonly Lazy<LocalInspectionHistoryStore> _instance =
            new(() => new LocalInspectionHistoryStore(AppDataPaths.Root));
        public static LocalInspectionHistoryStore Instance => _instance.Value;

        private readonly string _connectionString;
        private readonly BlockingCollection<WriteOp> _queue = new(new ConcurrentQueue<WriteOp>());
        private readonly Thread _writer;
        private readonly Dictionary<string, (string Path, bool IsNg)> _pendingImages = new(StringComparer.Ordinal);
        private readonly Queue<string> _pendingOrder = new();
        private int _disposed;

        public string DbPath { get; }

        /// <summary>
        /// 임의 디렉토리로 격리 인스턴스를 만드는 테스트 친화 ctor. internal — VMS.Tests 전용.
        /// 운영 코드는 <see cref="Instance"/> 싱글톤 사용.
        /// </summary>
        internal LocalInspectionHistoryStore(string dbFolder)
        {
            Directory.CreateDirectory(dbFolder);
            DbPath = Path.Combine(dbFolder, DbFileName);
            _connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = DbPath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                DefaultTimeout = 5
            }.ToString();

            InitializeDatabase();

            _writer = new Thread(WriterLoop)
            {
                IsBackground = true,
                Name = "InspectionHistoryWriter"
            };
            _writer.Start();
        }

        // ─── 스키마 ─────────────────────────────────────────────

        private void InitializeDatabase()
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS InspectionHistory (
                    Id             INTEGER PRIMARY KEY AUTOINCREMENT,
                    InspectedAtUtc TEXT    NOT NULL,
                    IsPass         INTEGER NOT NULL,
                    RecipeId       INTEGER NOT NULL DEFAULT 0,
                    RecipeName     TEXT,
                    NgCodes        TEXT,
                    ToolResults    TEXT,
                    CorrelationKey TEXT,
                    ImagePath      TEXT,
                    ImageIsNg      INTEGER NOT NULL DEFAULT 0,
                    WorkOrderId    INTEGER,
                    LotId          INTEGER,
                    SerialNumber   TEXT,
                    CycleTimeMs    INTEGER,
                    Mode           INTEGER NOT NULL DEFAULT 0
                );
                CREATE INDEX IF NOT EXISTS IX_InspectionHistory_At    ON InspectionHistory(InspectedAtUtc);
                CREATE INDEX IF NOT EXISTS IX_InspectionHistory_Pass  ON InspectionHistory(IsPass, InspectedAtUtc);
                CREATE INDEX IF NOT EXISTS IX_InspectionHistory_Corr  ON InspectionHistory(CorrelationKey);
                PRAGMA user_version = " + SchemaVersion + ";";
            cmd.ExecuteNonQuery();
        }

        private SqliteConnection Open()
        {
            var conn = new SqliteConnection(_connectionString);
            conn.Open();
            using var pragma = conn.CreateCommand();
            // busy_timeout: 뷰어 읽기와 writer 배치 커밋이 겹칠 때 즉시 SQLITE_BUSY 대신 대기.
            // synchronous=NORMAL: 전원 단절 시 마지막 배치 손실 허용 (검사 이력은 재생성 불가지만
            // 트랜잭션 원자성은 유지되므로 파일 손상은 없음) — FULL 대비 fsync 비용 절감.
            pragma.CommandText = "PRAGMA busy_timeout=5000; PRAGMA synchronous=NORMAL;";
            pragma.ExecuteNonQuery();
            return conn;
        }

        // ─── 쓰기 API (큐) ───────────────────────────────────────

        public void Record(LocalInspectionEntry entry)
        {
            if (entry == null || Volatile.Read(ref _disposed) != 0) return;
            if (entry.InspectedAtUtc.Kind != DateTimeKind.Utc)
                entry.InspectedAtUtc = entry.InspectedAtUtc.ToUniversalTime();
            TryEnqueue(new WriteOp { Kind = OpKind.Insert, Entry = entry });
        }

        public void SetImagePath(string correlationKey, string imagePath, bool isNg)
        {
            if (string.IsNullOrWhiteSpace(correlationKey) || string.IsNullOrWhiteSpace(imagePath)) return;
            if (Volatile.Read(ref _disposed) != 0) return;
            TryEnqueue(new WriteOp { Kind = OpKind.Image, Key = correlationKey, Path = imagePath, IsNg = isNg });
        }

        public bool Flush(TimeSpan timeout)
        {
            if (Volatile.Read(ref _disposed) != 0) return true;
            using var done = new ManualResetEventSlim(false);
            if (!TryEnqueue(new WriteOp { Kind = OpKind.Flush, Signal = done })) return false;
            return done.Wait(timeout);
        }

        private bool TryEnqueue(WriteOp op)
        {
            try
            {
                _queue.Add(op);
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[InspectionHistory] enqueue 실패: {ex.Message}");
                return false;
            }
        }

        // ─── writer 스레드 ───────────────────────────────────────

        private void WriterLoop()
        {
            var batch = new List<WriteOp>(MaxBatch);
            while (true)
            {
                batch.Clear();
                WriteOp first;
                try { first = _queue.Take(); }
                catch (InvalidOperationException) { break; }   // CompleteAdding 후 비어 있음 (ObjectDisposed 포함)

                batch.Add(first);
                while (batch.Count < MaxBatch && _queue.TryTake(out var more))
                    batch.Add(more);

                var stop = false;
                var signals = new List<ManualResetEventSlim>();
                try
                {
                    using var conn = Open();
                    using var tx = conn.BeginTransaction();
                    foreach (var op in batch)
                    {
                        switch (op.Kind)
                        {
                            case OpKind.Insert: Insert(conn, op.Entry!); break;
                            case OpKind.Image: ApplyImage(conn, op.Key!, op.Path!, op.IsNg); break;
                            case OpKind.Flush: signals.Add(op.Signal!); break;
                            case OpKind.Stop: stop = true; break;
                        }
                    }
                    tx.Commit();
                }
                catch (Exception ex)
                {
                    // 배치 전체를 버린다 — 검사 운전을 막는 것보다 이력 일부 손실이 낫다.
                    Debug.WriteLine($"[InspectionHistory] batch write 실패 ({batch.Count}건 폐기): {ex.Message}");
                    foreach (var op in batch)
                    {
                        if (op.Kind == OpKind.Flush) signals.Add(op.Signal!);
                        if (op.Kind == OpKind.Stop) stop = true;
                    }
                }
                foreach (var s in signals)
                {
                    try { s.Set(); } catch { /* 호출자가 이미 타임아웃으로 dispose 했을 수 있음 */ }
                }
                if (stop) break;
            }
        }

        private void Insert(SqliteConnection conn, LocalInspectionEntry e)
        {
            // 먼저 도착한 이미지 경로가 있으면 붙인다.
            if (!string.IsNullOrEmpty(e.CorrelationKey) && _pendingImages.Remove(e.CorrelationKey, out var img))
            {
                if (string.IsNullOrEmpty(e.ImagePath) || (!e.ImageIsNg && img.IsNg))
                {
                    e.ImagePath = img.Path;
                    e.ImageIsNg = img.IsNg;
                }
            }

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO InspectionHistory
                    (InspectedAtUtc, IsPass, RecipeId, RecipeName, NgCodes, ToolResults, CorrelationKey,
                     ImagePath, ImageIsNg, WorkOrderId, LotId, SerialNumber, CycleTimeMs, Mode)
                VALUES
                    (@at, @pass, @rid, @rname, @ng, @tools, @corr,
                     @img, @imgNg, @wo, @lot, @sn, @cycle, @mode)";
            cmd.Parameters.AddWithValue("@at", FormatUtc(e.InspectedAtUtc));
            cmd.Parameters.AddWithValue("@pass", e.IsPass ? 1 : 0);
            cmd.Parameters.AddWithValue("@rid", e.RecipeId);
            cmd.Parameters.AddWithValue("@rname", (object?)e.RecipeName ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ng", e.NgCodes.Count == 0 ? DBNull.Value : e.NgCodesText);
            cmd.Parameters.AddWithValue("@tools", e.ToolResults.Count == 0
                ? DBNull.Value
                : JsonSerializer.Serialize(e.ToolResults, JsonOpts));
            cmd.Parameters.AddWithValue("@corr", (object?)e.CorrelationKey ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@img", (object?)e.ImagePath ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@imgNg", e.ImageIsNg ? 1 : 0);
            cmd.Parameters.AddWithValue("@wo", (object?)e.WorkOrderId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@lot", (object?)e.LotId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@sn", (object?)e.SerialNumber ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@cycle", (object?)e.CycleTimeMs ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@mode", (int)e.Mode);
            cmd.ExecuteNonQuery();
        }

        private void ApplyImage(SqliteConnection conn, string key, string path, bool isNg)
        {
            using var upd = conn.CreateCommand();
            upd.CommandText = @"
                UPDATE InspectionHistory
                   SET ImagePath = @p, ImageIsNg = @ng
                 WHERE CorrelationKey = @k
                   AND (ImagePath IS NULL OR (ImageIsNg = 0 AND @ng = 1))";
            upd.Parameters.AddWithValue("@p", path);
            upd.Parameters.AddWithValue("@ng", isNg ? 1 : 0);
            upd.Parameters.AddWithValue("@k", key);
            if (upd.ExecuteNonQuery() > 0) return;

            using var exists = conn.CreateCommand();
            exists.CommandText = "SELECT 1 FROM InspectionHistory WHERE CorrelationKey = @k LIMIT 1";
            exists.Parameters.AddWithValue("@k", key);
            if (exists.ExecuteScalar() != null) return;   // 행은 있으나 이미 NG 이미지 보유 — 무시

            // 행이 아직 없음 (사이클 플러시 전) — 보류. NG 가 OK 를 덮는다.
            if (_pendingImages.TryGetValue(key, out var cur) && cur.IsNg && !isNg) return;
            if (!_pendingImages.ContainsKey(key))
            {
                _pendingOrder.Enqueue(key);
                while (_pendingOrder.Count > MaxPendingImages)
                    _pendingImages.Remove(_pendingOrder.Dequeue());
            }
            _pendingImages[key] = (path, isNg);
        }

        // ─── 조회 ───────────────────────────────────────────────

        public IReadOnlyList<LocalInspectionEntry> Query(LocalInspectionQuery query)
        {
            query ??= new LocalInspectionQuery();
            var list = new List<LocalInspectionEntry>();
            try
            {
                using var conn = Open();
                using var cmd = conn.CreateCommand();
                var sb = new StringBuilder(@"
                    SELECT Id, InspectedAtUtc, IsPass, RecipeId, RecipeName, NgCodes, ToolResults,
                           CorrelationKey, ImagePath, ImageIsNg, WorkOrderId, LotId, SerialNumber,
                           CycleTimeMs, Mode
                      FROM InspectionHistory");
                AppendWhere(sb, cmd, query);
                sb.Append(" ORDER BY InspectedAtUtc DESC, Id DESC LIMIT @limit OFFSET @offset");
                cmd.Parameters.AddWithValue("@limit", Math.Max(1, query.Limit));
                cmd.Parameters.AddWithValue("@offset", Math.Max(0, query.Offset));
                cmd.CommandText = sb.ToString();

                using var r = cmd.ExecuteReader();
                while (r.Read()) list.Add(Read(r));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[InspectionHistory] Query 실패: {ex.Message}");
            }
            return list;
        }

        public long Count(LocalInspectionQuery query)
        {
            query ??= new LocalInspectionQuery();
            try
            {
                using var conn = Open();
                using var cmd = conn.CreateCommand();
                var sb = new StringBuilder("SELECT COUNT(*) FROM InspectionHistory");
                AppendWhere(sb, cmd, query);
                cmd.CommandText = sb.ToString();
                return Convert.ToInt64(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[InspectionHistory] Count 실패: {ex.Message}");
                return 0;
            }
        }

        public IReadOnlyList<LocalInspectionDailySummary> GetDailySummary(DateTime fromLocal, DateTime toLocalExclusive)
        {
            var list = new List<LocalInspectionDailySummary>();
            try
            {
                using var conn = Open();
                using var cmd = conn.CreateCommand();
                // SQLite 의 'localtime' 은 OS 시간대를 쓴다 — 화면 날짜(로컬)와 일치.
                cmd.CommandText = @"
                    SELECT date(InspectedAtUtc, 'localtime') AS D,
                           COUNT(*) AS Total,
                           SUM(IsPass) AS Pass
                      FROM InspectionHistory
                     WHERE InspectedAtUtc >= @from AND InspectedAtUtc < @to
                     GROUP BY D
                     ORDER BY D";
                cmd.Parameters.AddWithValue("@from", FormatUtc(fromLocal.ToUniversalTime()));
                cmd.Parameters.AddWithValue("@to", FormatUtc(toLocalExclusive.ToUniversalTime()));
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    var total = r.GetInt32(1);
                    var pass = r.IsDBNull(2) ? 0 : r.GetInt32(2);
                    list.Add(new LocalInspectionDailySummary
                    {
                        Date = DateOnly.ParseExact(r.GetString(0), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                        Total = total,
                        Pass = pass,
                        Ng = total - pass
                    });
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[InspectionHistory] DailySummary 실패: {ex.Message}");
            }
            return list;
        }

        public IReadOnlyList<LocalNgCodeCount> GetNgCodeCounts(DateTime fromLocal, DateTime toLocalExclusive, int top)
        {
            // NgCodes 는 콤마 결합 문자열 — 행 수가 많아도 NG 행만 읽으므로 앱 측에서 분해.
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            try
            {
                using var conn = Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    SELECT NgCodes FROM InspectionHistory
                     WHERE IsPass = 0 AND NgCodes IS NOT NULL
                       AND InspectedAtUtc >= @from AND InspectedAtUtc < @to";
                cmd.Parameters.AddWithValue("@from", FormatUtc(fromLocal.ToUniversalTime()));
                cmd.Parameters.AddWithValue("@to", FormatUtc(toLocalExclusive.ToUniversalTime()));
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    foreach (var code in r.GetString(0).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                        counts[code] = counts.TryGetValue(code, out var c) ? c + 1 : 1;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[InspectionHistory] NgCodeCounts 실패: {ex.Message}");
            }
            var list = new List<LocalNgCodeCount>(counts.Count);
            foreach (var kv in counts) list.Add(new LocalNgCodeCount { Code = kv.Key, Count = kv.Value });
            list.Sort((a, b) => b.Count != a.Count ? b.Count.CompareTo(a.Count) : string.CompareOrdinal(a.Code, b.Code));
            if (top > 0 && list.Count > top) list.RemoveRange(top, list.Count - top);
            return list;
        }

        // ─── 보존 ───────────────────────────────────────────────

        public long CountOlderThan(int retentionDays)
        {
            try
            {
                using var conn = Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT COUNT(*) FROM InspectionHistory WHERE InspectedAtUtc < @cut";
                cmd.Parameters.AddWithValue("@cut", FormatUtc(Cutoff(retentionDays)));
                return Convert.ToInt64(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[InspectionHistory] CountOlderThan 실패: {ex.Message}");
                return 0;
            }
        }

        public int PurgeOlderThan(int retentionDays)
        {
            retentionDays = InspectionHistoryOptions.ClampRetention(retentionDays);
            int deleted = 0;
            long remaining = 0;
            try
            {
                using var conn = Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "DELETE FROM InspectionHistory WHERE InspectedAtUtc < @cut";
                    cmd.Parameters.AddWithValue("@cut", FormatUtc(Cutoff(retentionDays)));
                    deleted = cmd.ExecuteNonQuery();
                }
                using (var cnt = conn.CreateCommand())
                {
                    cnt.CommandText = "SELECT COUNT(*) FROM InspectionHistory";
                    remaining = Convert.ToInt64(cnt.ExecuteScalar(), CultureInfo.InvariantCulture);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[InspectionHistory] Purge 실패: {ex.Message}");
            }

            try
            {
                AuditLogger.Instance.Log(
                    AuditCategory.System, "InspectionHistoryRetention", AuditOutcome.Success,
                    source: nameof(LocalInspectionHistoryStore),
                    details: $"RetentionDays={retentionDays}, Deleted={deleted}, Remaining={remaining}");
            }
            catch { /* 감사 로그 실패는 정리 결과에 영향 없음 */ }
            return deleted;
        }

        /// <summary>
        /// 보존 설정 창 미리보기용 — DB 파일이 없으면 열지 않고 안내만 반환 (파일 생성 부작용 없음).
        /// </summary>
        public static RetentionPreviewSummary PreviewCleanup(string dbFolder, int retentionDays)
        {
            const string policy = "InspectionHistoryRetention";
            var path = Path.Combine(dbFolder, DbFileName);
            if (!File.Exists(path))
                return new RetentionPreviewSummary { PolicyName = policy, Note = "로컬 검사 이력 DB 없음 — 영향 없음" };

            try
            {
                var cs = new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly }.ToString();
                using var conn = new SqliteConnection(cs);
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    SELECT SUM(CASE WHEN InspectedAtUtc < @cut THEN 1 ELSE 0 END),
                           SUM(CASE WHEN InspectedAtUtc >= @cut THEN 1 ELSE 0 END),
                           MIN(CASE WHEN InspectedAtUtc < @cut THEN InspectedAtUtc END),
                           MIN(CASE WHEN InspectedAtUtc >= @cut THEN InspectedAtUtc END)
                      FROM InspectionHistory";
                cmd.Parameters.AddWithValue("@cut", FormatUtc(Cutoff(InspectionHistoryOptions.ClampRetention(retentionDays))));
                using var r = cmd.ExecuteReader();
                r.Read();
                var affected = r.IsDBNull(0) ? 0 : r.GetInt32(0);
                var remaining = r.IsDBNull(1) ? 0 : r.GetInt32(1);
                var size = new FileInfo(path).Length;
                return new RetentionPreviewSummary
                {
                    PolicyName = policy,
                    FilesAffected = affected,
                    FilesRemaining = remaining,
                    // 행 단위라 바이트는 파일 크기 비례 추정 — 표시용.
                    BytesAffected = affected + remaining > 0 ? (long)(size * ((double)affected / (affected + remaining))) : 0,
                    OldestAffectedUtc = r.IsDBNull(2) ? null : ParseUtc(r.GetString(2)),
                    OldestRemainingUtc = r.IsDBNull(3) ? null : ParseUtc(r.GetString(3)),
                    Note = affected == 0 ? $"삭제 예정 없음 (보관 {remaining}건)" : null
                };
            }
            catch (Exception ex)
            {
                return new RetentionPreviewSummary { PolicyName = policy, Note = $"미리보기 실패: {ex.Message}" };
            }
        }

        public long FileSizeBytes
        {
            get
            {
                try { return File.Exists(DbPath) ? new FileInfo(DbPath).Length : 0; }
                catch { return 0; }
            }
        }

        // ─── 종료 ───────────────────────────────────────────────

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            try
            {
                _queue.Add(new WriteOp { Kind = OpKind.Stop });
                _queue.CompleteAdding();
                if (!_writer.Join(TimeSpan.FromSeconds(5)))
                    Debug.WriteLine("[InspectionHistory] writer 종료 대기 초과 — 잔여 배치 폐기");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[InspectionHistory] Dispose: {ex.Message}");
            }
            // 풀에 남은 연결의 파일 핸들 해제 — 백업/테스트 임시 폴더 삭제가 막히지 않게.
            SqliteConnection.ClearAllPools();
        }

        // ─── 내부 유틸 ──────────────────────────────────────────

        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };

        private static void AppendWhere(StringBuilder sb, SqliteCommand cmd, LocalInspectionQuery q)
        {
            var clauses = new List<string>();
            if (q.FromLocal.HasValue)
            {
                clauses.Add("InspectedAtUtc >= @from");
                cmd.Parameters.AddWithValue("@from", FormatUtc(q.FromLocal.Value.ToUniversalTime()));
            }
            if (q.ToLocalExclusive.HasValue)
            {
                clauses.Add("InspectedAtUtc < @to");
                cmd.Parameters.AddWithValue("@to", FormatUtc(q.ToLocalExclusive.Value.ToUniversalTime()));
            }
            if (q.IsPass.HasValue)
            {
                clauses.Add("IsPass = @pass");
                cmd.Parameters.AddWithValue("@pass", q.IsPass.Value ? 1 : 0);
            }
            if (!string.IsNullOrWhiteSpace(q.RecipeName))
            {
                clauses.Add("RecipeName = @rname");
                cmd.Parameters.AddWithValue("@rname", q.RecipeName);
            }
            if (!string.IsNullOrWhiteSpace(q.NgCodeContains))
            {
                clauses.Add("NgCodes LIKE @ng ESCAPE '\\'");
                var escaped = q.NgCodeContains.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
                cmd.Parameters.AddWithValue("@ng", $"%{escaped}%");
            }
            if (clauses.Count > 0)
                sb.Append(" WHERE ").Append(string.Join(" AND ", clauses));
        }

        private static LocalInspectionEntry Read(SqliteDataReader r)
        {
            var e = new LocalInspectionEntry
            {
                Id = r.GetInt64(0),
                InspectedAtUtc = ParseUtc(r.GetString(1)),
                IsPass = r.GetInt32(2) != 0,
                RecipeId = r.GetInt32(3),
                RecipeName = r.IsDBNull(4) ? null : r.GetString(4),
                CorrelationKey = r.IsDBNull(7) ? null : r.GetString(7),
                ImagePath = r.IsDBNull(8) ? null : r.GetString(8),
                ImageIsNg = r.GetInt32(9) != 0,
                WorkOrderId = r.IsDBNull(10) ? null : r.GetInt32(10),
                LotId = r.IsDBNull(11) ? null : r.GetInt32(11),
                SerialNumber = r.IsDBNull(12) ? null : r.GetString(12),
                CycleTimeMs = r.IsDBNull(13) ? null : r.GetInt32(13),
                Mode = (LocalInspectionMode)r.GetInt32(14)
            };
            if (!r.IsDBNull(5))
                e.NgCodes = new List<string>(r.GetString(5).Split(',', StringSplitOptions.RemoveEmptyEntries));
            if (!r.IsDBNull(6))
            {
                try { e.ToolResults = JsonSerializer.Deserialize<List<LocalToolResult>>(r.GetString(6), JsonOpts) ?? new(); }
                catch { e.ToolResults = new(); }
            }
            return e;
        }

        private static DateTime Cutoff(int retentionDays) => DateTime.UtcNow.AddDays(-retentionDays);

        internal static string FormatUtc(DateTime utc)
            => utc.ToUniversalTime().ToString(UtcFormat, CultureInfo.InvariantCulture);

        internal static DateTime ParseUtc(string s)
            => DateTime.ParseExact(s, UtcFormat, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

        private enum OpKind { Insert, Image, Flush, Stop }

        private sealed class WriteOp
        {
            public OpKind Kind;
            public LocalInspectionEntry? Entry;
            public string? Key;
            public string? Path;
            public bool IsNg;
            public ManualResetEventSlim? Signal;
        }
    }
}
