using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using VMS.Core.Security;

namespace VMS.Core.Backup
{
    /// <summary>
    /// 주기 자동 백업 스케줄러 — Timer 기반.
    ///
    /// 동작:
    /// - Start() 호출 시 last-backup 시각을 디스크에서 읽어 다음 실행 시간 계산
    ///   (intervalHours 이내면 잔여 시간 후 시작, 이미 지났으면 즉시).
    /// - 매 tick 마다 BackupRestoreService.CreateBackup 호출 + RetentionDays 초과
    ///   백업 파일 자동 정리.
    /// - 백업 파일명: auto-backup-YYYYMMDD-HHmmss.zip
    /// - last-backup 시각은 backupDir/last_backup_at.txt 에 ISO 8601 로 기록.
    ///
    /// 감사:
    /// - 매 실행마다 AuditCategory.Configuration · "AutoBackup" — Success / Failure.
    /// - 정리 작업은 BackupCreated audit 의 details 에 "CleanedUp=N" 포함.
    ///
    /// 호출 시점: VMS App.xaml.cs OnStartup (StartupHealthCheck 직후).
    /// </summary>
    public sealed class AutoBackupScheduler : IDisposable
    {
        private const string LastBackupFile = "last_backup_at.txt";
        private const string BackupFilePrefix = "auto-backup-";
        private const string BackupFileSuffix = ".zip";

        private readonly string _appDataDir;
        private readonly string _backupDir;
        private readonly AutoBackupOptions _options;
        private readonly object _lock = new();

        private Timer? _timer;
        private bool _disposed;

        public bool IsRunning { get; private set; }

        public AutoBackupScheduler(string appDataDir, AutoBackupOptions options)
        {
            _appDataDir = appDataDir;
            _options = options;
            _backupDir = string.IsNullOrWhiteSpace(options.BackupDir)
                ? Path.Combine(appDataDir, "backups")
                : options.BackupDir;
        }

        public void Start()
        {
            lock (_lock)
            {
                if (_disposed || IsRunning) return;
                if (!_options.Enabled) return;

                try { Directory.CreateDirectory(_backupDir); }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[AutoBackup] backupDir 생성 실패: {ex.Message}");
                    return;
                }

                var intervalMs = (long)_options.IntervalHours * 60 * 60 * 1000;
                var dueMs = ComputeInitialDueMs(intervalMs);

                _timer = new Timer(OnTick, state: null,
                    dueTime: dueMs,
                    period: intervalMs);
                IsRunning = true;

                AuditLogger.Instance.Log(
                    AuditCategory.System, "AutoBackupStarted", AuditOutcome.Success,
                    source: nameof(AutoBackupScheduler),
                    details: $"BackupDir={_backupDir}, Interval={_options.IntervalHours}h, " +
                             $"Retention={_options.RetentionDays}d, IncludeAudit={_options.IncludeAudit}, " +
                             $"FirstRunInMs={dueMs}");
            }
        }

        public void Stop()
        {
            lock (_lock)
            {
                _timer?.Dispose();
                _timer = null;
                IsRunning = false;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            Stop();
            _disposed = true;
        }

        // ─── Timer tick — 실제 백업 + 정리 ────────────────────────

        private void OnTick(object? state)
        {
            try
            {
                var fileName = $"{BackupFilePrefix}{DateTime.UtcNow:yyyyMMdd-HHmmss}{BackupFileSuffix}";
                var zipPath = Path.Combine(_backupDir, fileName);

                var result = BackupRestoreService.CreateBackup(_appDataDir, zipPath,
                    new BackupOptions
                    {
                        IncludeAudit = _options.IncludeAudit,
                        ProductVersion = _options.ProductVersion
                    });

                int cleaned = CleanupOldBackups();

                if (result.Success)
                {
                    PersistLastBackupAt(DateTime.UtcNow);
                    AuditLogger.Instance.Log(
                        AuditCategory.Configuration, "AutoBackup", AuditOutcome.Success,
                        source: nameof(AutoBackupScheduler),
                        details: $"Path={zipPath}, Files={result.FileCount}, Bytes={result.BytesWritten}, CleanedUp={cleaned}");
                }
                else
                {
                    AuditLogger.Instance.Log(
                        AuditCategory.Configuration, "AutoBackup", AuditOutcome.Failure,
                        source: nameof(AutoBackupScheduler),
                        details: $"Path={zipPath}, Error={result.ErrorMessage}, CleanedUp={cleaned}");
                }
            }
            catch (Exception ex)
            {
                // 한 tick 의 실패가 이후 tick 을 멈추지 않게 — 다음 주기에 다시 시도.
                Debug.WriteLine($"[AutoBackup] tick 실패: {ex.Message}");
                try
                {
                    AuditLogger.Instance.Log(
                        AuditCategory.Configuration, "AutoBackup", AuditOutcome.Failure,
                        source: nameof(AutoBackupScheduler),
                        details: $"Exception: {ex.GetType().Name}: {ex.Message}");
                }
                catch { /* 감사 로깅 자체 실패도 무시 */ }
            }
        }

        // ─── 보존 정책 — 오래된 auto-backup-*.zip 정리 ──────────

        /// <summary>
        /// backupDir 내 auto-backup-*.zip 중 RetentionDays 초과분 삭제.
        /// auto-backup-YYYYMMDD-HHmmss.zip 패턴만 정밀 매칭 — 사용자 임의 ZIP 보호.
        /// </summary>
        public int CleanupOldBackups()
        {
            if (!Directory.Exists(_backupDir)) return 0;

            var thresholdUtc = DateTime.UtcNow.AddDays(-_options.RetentionDays);
            int deleted = 0;

            try
            {
                foreach (var file in Directory.EnumerateFiles(_backupDir, $"{BackupFilePrefix}*{BackupFileSuffix}"))
                {
                    var name = Path.GetFileNameWithoutExtension(file);
                    if (!name.StartsWith(BackupFilePrefix, StringComparison.Ordinal)) continue;

                    var tsPart = name.Substring(BackupFilePrefix.Length);
                    if (!DateTime.TryParseExact(tsPart, "yyyyMMdd-HHmmss",
                            CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal,
                            out var fileUtc))
                    {
                        continue; // 예상 패턴 외 — skip
                    }
                    fileUtc = fileUtc.ToUniversalTime();

                    if (fileUtc < thresholdUtc)
                    {
                        try
                        {
                            File.Delete(file);
                            deleted++;
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"[AutoBackup] 삭제 실패 {file}: {ex.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AutoBackup] 정리 열거 실패: {ex.Message}");
            }

            return deleted;
        }

        // ─── last-backup 시각 영속 ───────────────────────────────

        private long ComputeInitialDueMs(long intervalMs)
        {
            try
            {
                var path = Path.Combine(_backupDir, LastBackupFile);
                if (File.Exists(path))
                {
                    var text = File.ReadAllText(path).Trim();
                    if (DateTime.TryParse(text, CultureInfo.InvariantCulture,
                            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                            out var lastUtc))
                    {
                        var elapsed = DateTime.UtcNow - lastUtc;
                        var remaining = intervalMs - (long)elapsed.TotalMilliseconds;
                        return remaining > 0 ? remaining : 0;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AutoBackup] lastBackup 읽기 실패: {ex.Message}");
            }
            return 0;  // 기록 없음 → 즉시 첫 백업
        }

        private void PersistLastBackupAt(DateTime utc)
        {
            try
            {
                File.WriteAllText(Path.Combine(_backupDir, LastBackupFile),
                    utc.ToString("o", CultureInfo.InvariantCulture));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AutoBackup] lastBackup 쓰기 실패: {ex.Message}");
            }
        }
    }
}
