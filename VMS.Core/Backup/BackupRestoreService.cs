using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using VMS.Core.Security;

namespace VMS.Core.Backup
{
    /// <summary>
    /// 백업 ZIP 안에 들어가는 manifest.json — 향후 호환성 / 검증 / 감사 추적용.
    /// </summary>
    public sealed class BackupManifest
    {
        [JsonPropertyName("schemaVersion")]
        public int SchemaVersion { get; init; } = 1;

        [JsonPropertyName("createdAtUtc")]
        public DateTime CreatedAtUtc { get; init; } = DateTime.UtcNow;

        [JsonPropertyName("productVersion")]
        public string ProductVersion { get; init; } = string.Empty;

        [JsonPropertyName("includesAudit")]
        public bool IncludesAudit { get; init; }

        [JsonPropertyName("includesUsersDb")]
        public bool IncludesUsersDb { get; init; }

        [JsonPropertyName("fileCount")]
        public int FileCount { get; init; }

        [JsonPropertyName("sourceDir")]
        public string SourceDir { get; init; } = string.Empty;
    }

    /// <summary>CreateBackup 옵션.</summary>
    public sealed class BackupOptions
    {
        /// <summary>audit/ JSONL 도 포함할지. 기본 false — 운영 백업이 커지지 않게 함.</summary>
        public bool IncludeAudit { get; init; }

        /// <summary>manifest 에 기록할 제품 버전 문자열.</summary>
        public string ProductVersion { get; init; } = string.Empty;
    }

    /// <summary>RestoreBackup 옵션.</summary>
    public sealed class RestoreOptions
    {
        /// <summary>대상 디렉토리에 이미 있는 파일을 덮어쓸지. false 면 충돌 시 skip + 카운트.</summary>
        public bool Overwrite { get; init; } = true;

        /// <summary>audit/ 도 복원할지. 백업에 포함되어 있을 때만 의미 있음.</summary>
        public bool RestoreAudit { get; init; } = true;
    }

    public sealed class BackupResult
    {
        public bool Success { get; init; }
        public string ZipPath { get; init; } = string.Empty;
        public int FileCount { get; init; }
        public long BytesWritten { get; init; }
        public string? ErrorMessage { get; init; }
    }

    public sealed class RestoreResult
    {
        public bool Success { get; init; }
        public int FilesRestored { get; init; }
        public int FilesSkipped { get; init; }
        public BackupManifest? Manifest { get; init; }
        public string? ErrorMessage { get; init; }
    }

    /// <summary>
    /// VMS 운영 데이터(시스템 설정, 사용자 DB, 레시피) 의 백업 / 복원 서비스.
    ///
    /// 백업 대상:
    ///   system_config.json / layout_config.json / plc_signals.json
    ///   BodaVision.db (사용자 DB)
    ///   recipes/ (전체)
    ///   audit/ (옵션 — 기본 제외)
    ///
    /// 포맷: 표준 ZIP + 루트에 manifest.json. 압축 = Optimal.
    /// 보안:
    ///   - 복원 시 ZIP entry 경로가 target 디렉토리 밖으로 escape 하지 않도록 검증
    ///   - 모든 백업 / 복원 행위를 AuditCategory.Configuration 이벤트로 기록
    /// </summary>
    public static class BackupRestoreService
    {
        private const string ManifestEntryName = "backup_manifest.json";

        // ─── 백업 대상 화이트리스트 ────────────────────────────────

        private static readonly string[] _topLevelFiles = new[]
        {
            "system_config.json",
            "layout_config.json",
            "plc_signals.json",
            "BodaVision.db",
            // 로컬 검사 이력 (기본 저널 모드 — 사이드카 없이 .db 하나로 일관)
            "inspection_history.db"
        };

        private static readonly string[] _alwaysSubdirs = new[]
        {
            "recipes"
        };

        // ─── CreateBackup ─────────────────────────────────────────

        public static BackupResult CreateBackup(string sourceAppDataDir, string outputZipPath, BackupOptions? options = null)
        {
            options ??= new BackupOptions();

            if (string.IsNullOrWhiteSpace(sourceAppDataDir) || !Directory.Exists(sourceAppDataDir))
            {
                return AuditedFail(outputZipPath,
                    $"source dir 없음: {sourceAppDataDir}", isCreate: true);
            }
            if (string.IsNullOrWhiteSpace(outputZipPath))
            {
                return AuditedFail(outputZipPath, "outputZipPath 비어 있음", isCreate: true);
            }

            // 같은 위치 덮어쓰기 허용 — 기존 ZIP 은 ZipFile.CreateFromDirectory 가 처리하지 못해 사전 삭제.
            try
            {
                if (File.Exists(outputZipPath)) File.Delete(outputZipPath);
                var outputDir = Path.GetDirectoryName(outputZipPath);
                if (!string.IsNullOrEmpty(outputDir)) Directory.CreateDirectory(outputDir);
            }
            catch (Exception ex)
            {
                return AuditedFail(outputZipPath, $"출력 디렉토리 준비 실패: {ex.Message}", isCreate: true);
            }

            try
            {
                using var stream = new FileStream(outputZipPath, FileMode.Create, FileAccess.Write, FileShare.None);
                using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
                {
                    int fileCount = 0;
                    bool dbIncluded = false;

                    // 1) 최상위 파일들
                    foreach (var name in _topLevelFiles)
                    {
                        var src = Path.Combine(sourceAppDataDir, name);
                        if (!File.Exists(src)) continue;
                        archive.CreateEntryFromFile(src, name, CompressionLevel.Optimal);
                        fileCount++;
                        if (string.Equals(name, "BodaVision.db", StringComparison.OrdinalIgnoreCase))
                            dbIncluded = true;
                    }

                    // 2) recipes/ — 전체 (재귀 1단계 — 현재 구조는 평탄)
                    foreach (var sub in _alwaysSubdirs)
                    {
                        fileCount += AddSubdir(archive, sourceAppDataDir, sub);
                    }

                    // 3) audit/ — 옵션
                    if (options.IncludeAudit)
                    {
                        fileCount += AddSubdir(archive, sourceAppDataDir, "audit");
                    }

                    // 4) manifest
                    var manifest = new BackupManifest
                    {
                        ProductVersion = options.ProductVersion,
                        IncludesAudit = options.IncludeAudit,
                        IncludesUsersDb = dbIncluded,
                        FileCount = fileCount,
                        SourceDir = sourceAppDataDir
                    };
                    var manifestEntry = archive.CreateEntry(ManifestEntryName, CompressionLevel.Optimal);
                    using (var entryStream = manifestEntry.Open())
                    using (var writer = new StreamWriter(entryStream))
                    {
                        writer.Write(JsonSerializer.Serialize(manifest,
                            new JsonSerializerOptions { WriteIndented = true }));
                    }

                    var bytes = stream.Length;
                    AuditLogger.Instance.Log(
                        AuditCategory.Configuration, "BackupCreated", AuditOutcome.Success,
                        source: nameof(BackupRestoreService),
                        details: $"Path={outputZipPath}, Files={fileCount}, IncludesAudit={options.IncludeAudit}, IncludesDb={dbIncluded}, Bytes={bytes}");

                    return new BackupResult
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
                return AuditedFail(outputZipPath, $"{ex.GetType().Name}: {ex.Message}", isCreate: true);
            }
        }

        private static int AddSubdir(ZipArchive archive, string sourceAppDataDir, string subdirName)
        {
            var subdir = Path.Combine(sourceAppDataDir, subdirName);
            if (!Directory.Exists(subdir)) return 0;

            int count = 0;
            foreach (var file in Directory.EnumerateFiles(subdir, "*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(sourceAppDataDir, file).Replace('\\', '/');
                archive.CreateEntryFromFile(file, rel, CompressionLevel.Optimal);
                count++;
            }
            return count;
        }

        // ─── RestoreBackup ────────────────────────────────────────

        public static RestoreResult RestoreBackup(string zipPath, string targetAppDataDir, RestoreOptions? options = null)
        {
            options ??= new RestoreOptions();

            if (string.IsNullOrWhiteSpace(zipPath) || !File.Exists(zipPath))
            {
                return AuditedRestoreFail(zipPath, $"백업 파일 없음: {zipPath}");
            }
            if (string.IsNullOrWhiteSpace(targetAppDataDir))
            {
                return AuditedRestoreFail(zipPath, "target dir 비어 있음");
            }

            try
            {
                Directory.CreateDirectory(targetAppDataDir);
                var targetFull = Path.GetFullPath(targetAppDataDir);
                if (!targetFull.EndsWith(Path.DirectorySeparatorChar))
                    targetFull += Path.DirectorySeparatorChar;

                using var stream = File.OpenRead(zipPath);
                using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

                // manifest 우선 검증
                var manifestEntry = archive.GetEntry(ManifestEntryName);
                BackupManifest? manifest = null;
                if (manifestEntry != null)
                {
                    using var ms = manifestEntry.Open();
                    using var reader = new StreamReader(ms);
                    var json = reader.ReadToEnd();
                    try { manifest = JsonSerializer.Deserialize<BackupManifest>(json); }
                    catch (Exception ex) { Debug.WriteLine($"[Backup] manifest parse: {ex.Message}"); }
                }
                else
                {
                    return AuditedRestoreFail(zipPath, "manifest 없음 — 유효한 백업 아님");
                }

                int restored = 0;
                int skipped = 0;

                foreach (var entry in archive.Entries)
                {
                    if (entry.FullName == ManifestEntryName) continue;
                    if (string.IsNullOrEmpty(entry.Name)) continue;  // pure directory entry

                    // audit/ skip 옵션
                    if (entry.FullName.StartsWith("audit/", StringComparison.OrdinalIgnoreCase)
                        && !options.RestoreAudit)
                    {
                        skipped++;
                        continue;
                    }

                    var destPath = Path.GetFullPath(Path.Combine(targetAppDataDir, entry.FullName));

                    // Path traversal 방어 — 정규화된 경로가 target 안에 있는지 확인.
                    if (!destPath.StartsWith(targetFull, StringComparison.OrdinalIgnoreCase))
                    {
                        AuditLogger.Instance.Log(
                            AuditCategory.Security, "BackupEntryRejected", AuditOutcome.Denied,
                            source: nameof(BackupRestoreService),
                            details: $"Entry={entry.FullName} escapes target — skipped");
                        skipped++;
                        continue;
                    }

                    if (File.Exists(destPath) && !options.Overwrite)
                    {
                        skipped++;
                        continue;
                    }

                    var destDir = Path.GetDirectoryName(destPath);
                    if (!string.IsNullOrEmpty(destDir)) Directory.CreateDirectory(destDir);

                    entry.ExtractToFile(destPath, overwrite: true);
                    restored++;
                }

                AuditLogger.Instance.Log(
                    AuditCategory.Configuration, "BackupRestored", AuditOutcome.Success,
                    source: nameof(BackupRestoreService),
                    details: $"Path={zipPath}, Restored={restored}, Skipped={skipped}, ManifestVersion={manifest!.SchemaVersion}, Created={manifest.CreatedAtUtc:o}");

                return new RestoreResult
                {
                    Success = true,
                    FilesRestored = restored,
                    FilesSkipped = skipped,
                    Manifest = manifest
                };
            }
            catch (Exception ex)
            {
                return AuditedRestoreFail(zipPath, $"{ex.GetType().Name}: {ex.Message}");
            }
        }

        // ─── 실패 audit + return ──────────────────────────────────

        private static BackupResult AuditedFail(string path, string message, bool isCreate)
        {
            AuditLogger.Instance.Log(
                AuditCategory.Configuration,
                isCreate ? "BackupCreated" : "BackupRestored",
                AuditOutcome.Failure,
                source: nameof(BackupRestoreService),
                details: $"Path={path}, {message}");
            return new BackupResult { Success = false, ZipPath = path, ErrorMessage = message };
        }

        private static RestoreResult AuditedRestoreFail(string path, string message)
        {
            AuditLogger.Instance.Log(
                AuditCategory.Configuration, "BackupRestored", AuditOutcome.Failure,
                source: nameof(BackupRestoreService),
                details: $"Path={path}, {message}");
            return new RestoreResult { Success = false, ErrorMessage = message };
        }
    }
}
