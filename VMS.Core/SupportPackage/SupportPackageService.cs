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

                    // 6) Manifest
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
