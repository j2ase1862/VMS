using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using VMS.Core.Health;
using VMS.Core.Security;

namespace VMS.Core.SupportPackage
{
    /// <summary>
    /// 지원/원격 진단용 패키지 — 시스템 구성 / 최근 감사 로그 / 환경 정보 / 백업 인덱스 /
    /// 헬스 스냅샷을 단일 ZIP 으로 묶는 운영 도구.
    ///
    /// 백업 (BackupRestoreService) 과의 차이:
    /// - 백업 = 복원용 (BodaVision.db, recipes/ 포함)
    /// - Support Package = 지원/엔지니어링 분석용 (BodaVision.db / recipes/ 제외,
    ///   환경·인덱스 추가)
    ///
    /// 의도적 제외:
    /// - BodaVision.db (사용자 BCrypt 해시 — 외부 공유 금지)
    /// - recipes/ (IP — 공유 시점 별도 동의)
    ///
    /// CI 필터 호환: 클래스명에 "Diagnostic" 미포함 (CI 가 ML diagnostic 테스트만 제외).
    /// </summary>
    public static class SupportPackageService
    {
        private const string ManifestEntryName = "support_manifest.json";

        public static SupportPackageResult Export(
            string appDataDir, string outputZipPath, SupportPackageOptions? options = null)
        {
            options ??= new SupportPackageOptions();

            if (string.IsNullOrWhiteSpace(appDataDir) || !Directory.Exists(appDataDir))
            {
                return AuditedFail(outputZipPath, $"AppData 디렉토리 없음: {appDataDir}");
            }
            if (string.IsNullOrWhiteSpace(outputZipPath))
            {
                return AuditedFail(outputZipPath, "outputZipPath 비어 있음");
            }

            try
            {
                if (File.Exists(outputZipPath)) File.Delete(outputZipPath);
                var outDir = Path.GetDirectoryName(outputZipPath);
                if (!string.IsNullOrEmpty(outDir)) Directory.CreateDirectory(outDir);
            }
            catch (Exception ex)
            {
                return AuditedFail(outputZipPath, $"출력 디렉토리 준비 실패: {ex.Message}");
            }

            int fileCount = 0;
            int auditDays = ClampAuditDays(options.AuditDays);

            try
            {
                using var stream = new FileStream(outputZipPath, FileMode.Create, FileAccess.Write, FileShare.None);
                using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
                {
                    // 1) Config 파일들
                    foreach (var name in new[] { "system_config.json", "layout_config.json", "plc_signals.json" })
                    {
                        var src = Path.Combine(appDataDir, name);
                        if (!File.Exists(src)) continue;
                        archive.CreateEntryFromFile(src, $"config/{name}", CompressionLevel.Optimal);
                        fileCount++;
                    }

                    // 2) 최근 감사 로그
                    var auditDir = Path.Combine(appDataDir, "audit");
                    if (Directory.Exists(auditDir))
                    {
                        var threshold = DateTime.UtcNow.Date.AddDays(-auditDays);
                        foreach (var file in Directory.EnumerateFiles(auditDir, "*.jsonl"))
                        {
                            var name = Path.GetFileNameWithoutExtension(file);
                            if (!DateTime.TryParseExact(name, "yyyy-MM-dd",
                                    CultureInfo.InvariantCulture, DateTimeStyles.None,
                                    out var fileDate))
                            {
                                continue;
                            }
                            if (fileDate < threshold) continue;
                            archive.CreateEntryFromFile(file, $"audit/{name}.jsonl", CompressionLevel.Optimal);
                            fileCount++;
                        }
                    }

                    // 3) Environment
                    if (options.IncludeEnvironment)
                    {
                        var envEntry = archive.CreateEntry("environment.txt", CompressionLevel.Optimal);
                        using var w = new StreamWriter(envEntry.Open(), Encoding.UTF8);
                        WriteEnvironmentInfo(w, options.IncludeMachineName);
                        fileCount++;
                    }

                    // 4) Backup 인덱스
                    if (options.IncludeBackupIndex)
                    {
                        var backupsDir = Path.Combine(appDataDir, "backups");
                        var entry = archive.CreateEntry("backups_index.txt", CompressionLevel.Optimal);
                        using var w = new StreamWriter(entry.Open(), Encoding.UTF8);
                        WriteBackupIndex(w, backupsDir);
                        fileCount++;
                    }

                    // 5) Health snapshot
                    {
                        var entry = archive.CreateEntry("health_snapshot.json", CompressionLevel.Optimal);
                        using var w = new StreamWriter(entry.Open(), Encoding.UTF8);
                        WriteHealthSnapshot(w, appDataDir, auditDir: Path.Combine(appDataDir, "audit"));
                        fileCount++;
                    }

                    // 6) MLOps 학습 이미지 전송 큐
                    {
                        var entry = archive.CreateEntry("mlops_upload_queue.txt", CompressionLevel.Optimal);
                        using var w = new StreamWriter(entry.Open(), Encoding.UTF8);
                        WriteMlopsQueueStatus(w, appDataDir);
                        fileCount++;
                    }

                    // 7) Manifest
                    var manifest = new SupportPackageManifest
                    {
                        ProductVersion = options.ProductVersion,
                        MachineName = options.IncludeMachineName ? Environment.MachineName : null,
                        AuditDaysIncluded = auditDays,
                        IncludesEnvironment = options.IncludeEnvironment,
                        IncludesBackupIndex = options.IncludeBackupIndex,
                        FileCount = fileCount
                    };
                    var mEntry = archive.CreateEntry(ManifestEntryName, CompressionLevel.Optimal);
                    using (var ms = mEntry.Open())
                    using (var w = new StreamWriter(ms))
                    {
                        w.Write(JsonSerializer.Serialize(manifest,
                            new JsonSerializerOptions { WriteIndented = true }));
                    }

                    var bytes = stream.Length;
                    AuditLogger.Instance.Log(
                        AuditCategory.System, "SupportPackageCreated", AuditOutcome.Success,
                        source: nameof(SupportPackageService),
                        details: $"Path={outputZipPath}, Files={fileCount}, AuditDays={auditDays}, " +
                                 $"IncludesEnv={options.IncludeEnvironment}, IncludesBackupIndex={options.IncludeBackupIndex}, " +
                                 $"MachineName={(options.IncludeMachineName ? "yes" : "no")}, Bytes={bytes}");

                    return new SupportPackageResult
                    {
                        Success = true,
                        ZipPath = outputZipPath,
                        FileCount = fileCount,
                        BytesWritten = bytes
                    };
                }
            }
            catch (Exception ex)
            {
                return AuditedFail(outputZipPath, $"{ex.GetType().Name}: {ex.Message}");
            }
        }

        // ─── Helpers ──────────────────────────────────────────────

        private static void WriteEnvironmentInfo(StreamWriter w, bool includeMachineName)
        {
            w.WriteLine("# BODA Vision AI — Environment");
            w.WriteLine($"GeneratedAtUtc:    {DateTime.UtcNow:o}");
            if (includeMachineName)
            {
                w.WriteLine($"MachineName:       {Environment.MachineName}");
                w.WriteLine($"UserDomainName:    {Environment.UserDomainName}");
            }
            w.WriteLine($"OSVersion:         {Environment.OSVersion}");
            w.WriteLine($".NETVersion:       {Environment.Version}");
            w.WriteLine($"Is64BitProcess:    {Environment.Is64BitProcess}");
            w.WriteLine($"ProcessorCount:    {Environment.ProcessorCount}");
            w.WriteLine($"WorkingSetMB:      {Environment.WorkingSet / 1024 / 1024}");
            w.WriteLine($"CurrentDirectory:  {Environment.CurrentDirectory}");
            w.WriteLine();
            w.WriteLine("# Drives");
            try
            {
                foreach (var drive in DriveInfo.GetDrives())
                {
                    try
                    {
                        if (!drive.IsReady)
                        {
                            w.WriteLine($"{drive.Name}  (not ready)");
                            continue;
                        }
                        var freeGB = drive.AvailableFreeSpace / 1024.0 / 1024.0 / 1024.0;
                        var totalGB = drive.TotalSize / 1024.0 / 1024.0 / 1024.0;
                        w.WriteLine($"{drive.Name}  {drive.DriveType}  Free {freeGB:F1} GB / Total {totalGB:F1} GB  ({drive.DriveFormat})");
                    }
                    catch (Exception ex)
                    {
                        w.WriteLine($"{drive.Name}  (error: {ex.Message})");
                    }
                }
            }
            catch (Exception ex)
            {
                w.WriteLine($"Drives 열거 실패: {ex.Message}");
            }
        }

        private static void WriteBackupIndex(StreamWriter w, string backupsDir)
        {
            w.WriteLine("# BODA Vision AI — Backup Index");
            w.WriteLine($"GeneratedAtUtc: {DateTime.UtcNow:o}");
            w.WriteLine($"BackupsDir:     {backupsDir}");
            w.WriteLine();
            if (!Directory.Exists(backupsDir))
            {
                w.WriteLine("(backups 디렉토리 없음)");
                return;
            }
            try
            {
                var files = Directory.EnumerateFiles(backupsDir, "*.zip").ToList();
                if (files.Count == 0)
                {
                    w.WriteLine("(zip 파일 없음)");
                    return;
                }
                w.WriteLine("Name                                          Size (MB)  Modified (UTC)");
                w.WriteLine("--------------------------------------------- ---------- -------------------");
                foreach (var path in files.OrderByDescending(f => new FileInfo(f).LastWriteTimeUtc))
                {
                    try
                    {
                        var fi = new FileInfo(path);
                        var mb = fi.Length / 1024.0 / 1024.0;
                        w.WriteLine($"{Truncate(fi.Name, 45),-45} {mb,10:F2} {fi.LastWriteTimeUtc:yyyy-MM-dd HH:mm:ss}");
                    }
                    catch (Exception ex)
                    {
                        w.WriteLine($"{Path.GetFileName(path)}  (error: {ex.Message})");
                    }
                }
            }
            catch (Exception ex)
            {
                w.WriteLine($"backups 열거 실패: {ex.Message}");
            }
        }

        /// <summary>
        /// MLOps 학습 이미지 전송 큐의 현재 상태.
        ///
        /// <para>현장에서 "NG 사진이 MLOps 에 안 올라간다" 는 말이 나왔을 때 먼저 볼 것들이다 —
        /// 큐에 쌓여만 있는지(네트워크·서버 문제), 거절되고 있는지(서버가 받지 않는 상태),
        /// 아니면 애초에 큐가 비어 있는지(전송 토글이 꺼져 있거나 NG 가 없었다).</para>
        ///
        /// <para>보낸 장수는 성공하면 파일이 지워져 큐를 세어서는 알 수 없으므로 업로더가
        /// <c>stats.json</c> 에 누적해 둔다. 여기서는 그 파일을 그대로 싣는다.</para>
        /// </summary>
        private static void WriteMlopsQueueStatus(StreamWriter w, string appDataDir)
        {
            w.WriteLine("# MLOps 학습 이미지 전송 큐");
            w.WriteLine($"GeneratedAtUtc:    {DateTime.UtcNow:o}");
            w.WriteLine();

            // 업로더의 LineNgImageUploader.QueueDirName 과 같은 이름 (VMS.Core 는 VMS 를 참조하지 않는다)
            var queueDir = Path.Combine(appDataDir, "mlops_line_ng_queue");
            if (!Directory.Exists(queueDir))
            {
                w.WriteLine("큐 폴더가 없습니다 — [NG 이미지 MLOps 전송] 이 한 번도 켜지지 않았거나,");
                w.WriteLine("켠 뒤 보낼 이미지가 아직 없었습니다.");
                w.WriteLine($"(찾은 경로: {queueDir})");
                return;
            }

            try
            {
                // 드레인이 큐 폴더의 *.json 을 모두 큐 항목으로 보므로, 통계는 state\ 하위에 따로 있다
                var pending = Directory.GetFiles(queueDir, "*.json", SearchOption.TopDirectoryOnly).ToList();
                long pendingBytes = 0;
                DateTime? oldest = null;
                foreach (var f in pending)
                {
                    var img = Path.ChangeExtension(f, ".img");
                    try
                    {
                        if (File.Exists(img)) pendingBytes += new FileInfo(img).Length;
                        var at = File.GetLastWriteTimeUtc(f);
                        if (oldest is null || at < oldest) oldest = at;
                    }
                    catch { /* 드레인이 지우는 중일 수 있다 */ }
                }

                var rejectedDir = Path.Combine(queueDir, "rejected");
                var rejected = Directory.Exists(rejectedDir)
                    ? Directory.GetFiles(rejectedDir, "*.json").Length
                    : 0;

                w.WriteLine($"대기 중:           {pending.Count} 장 ({pendingBytes / 1024.0 / 1024.0:F1} MB)");
                w.WriteLine($"거절됨(보관):      {rejected} 장");
                w.WriteLine(oldest is { } o
                    ? $"가장 오래된 대기:  {o:o} ({(DateTime.UtcNow - o).TotalHours:F1} 시간 전)"
                    : "가장 오래된 대기:  (없음)");
                w.WriteLine();

                var statsPath = Path.Combine(queueDir, "state", "stats.json");
                if (File.Exists(statsPath))
                {
                    w.WriteLine("## 누적 통계 (stats.json)");
                    try { w.WriteLine(File.ReadAllText(statsPath)); }
                    catch (Exception ex) { w.WriteLine($"(읽기 실패: {ex.Message})"); }
                }
                else
                {
                    w.WriteLine("## 누적 통계");
                    w.WriteLine("아직 없습니다 — 한 장도 보내거나 거절되지 않았습니다.");
                }

                if (rejected > 0)
                {
                    w.WriteLine();
                    w.WriteLine("## 거절 사유 (최근 20건)");
                    try
                    {
                        var reasons = Directory.GetFiles(rejectedDir, "*.reason.txt")
                            .OrderByDescending(File.GetLastWriteTimeUtc).Take(20);
                        foreach (var r in reasons)
                        {
                            string text;
                            try { text = File.ReadAllText(r).Trim(); }
                            catch { continue; }
                            w.WriteLine($"- {File.GetLastWriteTimeUtc(r):o}  {text}");
                        }
                    }
                    catch (Exception ex) { w.WriteLine($"(읽기 실패: {ex.Message})"); }
                }
            }
            catch (Exception ex)
            {
                w.WriteLine($"큐 상태를 읽지 못했습니다: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private static void WriteHealthSnapshot(StreamWriter w, string appDataDir, string auditDir)
        {
            try
            {
                var report = StartupHealthCheck.Run(appDataDir, auditDir, auditAfter: false);
                var dto = new
                {
                    overallStatus = report.OverallStatus.ToString(),
                    passCount = report.PassCount,
                    warnCount = report.WarnCount,
                    failCount = report.FailCount,
                    items = report.Items.Select(i => new
                    {
                        name = i.Name,
                        status = i.Status.ToString(),
                        message = i.Message
                    }).ToList()
                };
                w.Write(JsonSerializer.Serialize(dto, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception ex)
            {
                w.Write(JsonSerializer.Serialize(new { error = $"{ex.GetType().Name}: {ex.Message}" }));
            }
        }

        private static string Truncate(string value, int max)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return value.Length <= max ? value : value[..max];
        }

        private static int ClampAuditDays(int days)
        {
            if (days < 1) return 1;
            if (days > 90) return 90;
            return days;
        }

        private static SupportPackageResult AuditedFail(string path, string message)
        {
            try
            {
                AuditLogger.Instance.Log(
                    AuditCategory.System, "SupportPackageCreated", AuditOutcome.Failure,
                    source: nameof(SupportPackageService),
                    details: $"Path={path}, {message}");
            }
            catch { /* 무시 */ }
            return new SupportPackageResult
            {
                Success = false,
                ZipPath = path,
                ErrorMessage = message
            };
        }
    }

    // ─── DTO 들 ───────────────────────────────────────────────────

    public sealed class SupportPackageOptions
    {
        /// <summary>포함할 최근 감사 로그 일수 (1~90). 기본 7.</summary>
        public int AuditDays { get; init; } = 7;

        public bool IncludeEnvironment { get; init; } = true;
        public bool IncludeBackupIndex { get; init; } = true;

        /// <summary>호스트명 / 도메인명 포함 여부. 기본 false (사이트 식별 PII 보호).</summary>
        public bool IncludeMachineName { get; init; }

        public string ProductVersion { get; init; } = string.Empty;
    }

    public sealed class SupportPackageResult
    {
        public bool Success { get; init; }
        public string ZipPath { get; init; } = string.Empty;
        public int FileCount { get; init; }
        public long BytesWritten { get; init; }
        public string? ErrorMessage { get; init; }
    }

    public sealed class SupportPackageManifest
    {
        [JsonPropertyName("schemaVersion")]
        public int SchemaVersion { get; init; } = 1;

        [JsonPropertyName("createdAtUtc")]
        public DateTime CreatedAtUtc { get; init; } = DateTime.UtcNow;

        [JsonPropertyName("productVersion")]
        public string ProductVersion { get; init; } = string.Empty;

        [JsonPropertyName("machineName")]
        public string? MachineName { get; init; }

        [JsonPropertyName("auditDaysIncluded")]
        public int AuditDaysIncluded { get; init; }

        [JsonPropertyName("includesEnvironment")]
        public bool IncludesEnvironment { get; init; }

        [JsonPropertyName("includesBackupIndex")]
        public bool IncludesBackupIndex { get; init; }

        [JsonPropertyName("fileCount")]
        public int FileCount { get; init; }
    }
}
