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
    ///   system_config.json / layout_config.json / plc_signals.json / process_sequence.json
    ///   BodaVision.db (사용자 DB) / inspection_history.db (로컬 검사 이력) — 실행 중에도 일관 복사
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
            // 시스템 프로세스 시퀀스 (IO 보드 · PLC 순서) — 빠지면 복원 뒤 시퀀스가 사라짐
            "process_sequence.json",
            "BodaVision.db",
            // 로컬 검사 이력 (기본 저널 모드 — 사이드카 없이 .db 하나로 일관)
            "inspection_history.db"
        };

        private static readonly string[] _alwaysSubdirs = new[]
        {
            "recipes"
        };

        /// <summary>
        /// VMS 가 실행 중에 연결을 열어 두는 SQLite DB. 파일을 통째로 읽거나 덮어쓰면
        /// 사용 중 오류가 나거나 쓰는 중인 페이지가 섞이므로 <see cref="DatabaseCopier"/> 로 복사한다.
        /// </summary>
        private static readonly HashSet<string> _databaseFiles = new(StringComparer.OrdinalIgnoreCase)
        {
            "BodaVision.db",
            "inspection_history.db"
        };

        /// <summary>
        /// SQLite DB 를 일관된 상태로 복사하는 함수 (원본 경로, 대상 경로).
        /// VMS 앱이 시작할 때 SQLite 온라인 백업으로 설정한다 — 연결이 열려 있어도 동작.
        /// null 이면 공유 읽기로 파일을 그대로 복사한다 (테스트 · 앱 밖 사용).
        /// </summary>
        public static Action<string, string>? DatabaseCopier { get; set; }

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
                        if (_databaseFiles.Contains(name))
                            AddDatabaseEntry(archive, src, name);
                        else
                            AddFileEntry(archive, src, name);
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
                // 실패한 ZIP 은 manifest 가 없어 복원할 수 없음 — 백업처럼 보이지 않게 지운다.
                try { if (File.Exists(outputZipPath)) File.Delete(outputZipPath); }
                catch (Exception delEx) { Debug.WriteLine($"[Backup] 실패 ZIP 삭제: {delEx.Message}"); }
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
                AddFileEntry(archive, file, rel);
                count++;
            }
            return count;
        }

        /// <summary>
        /// 다른 프로세스 · 스레드가 쓰기용으로 열어 둔 파일도 읽는다 (CreateEntryFromFile 은 읽기 공유만 허용).
        /// </summary>
        private static void AddFileEntry(ZipArchive archive, string sourcePath, string entryName)
        {
            using var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
            entry.LastWriteTime = File.GetLastWriteTime(sourcePath);
            using var target = entry.Open();
            source.CopyTo(target);
        }

        /// <summary>SQLite DB 를 임시 파일로 일관 복사한 뒤 ZIP 에 넣는다.</summary>
        private static void AddDatabaseEntry(ZipArchive archive, string sourcePath, string entryName)
        {
            if (DatabaseCopier == null)
            {
                AddFileEntry(archive, sourcePath, entryName);
                return;
            }

            var tempPath = Path.Combine(Path.GetTempPath(), $"vms-backup-{Guid.NewGuid():N}.db");
            try
            {
                DatabaseCopier(sourcePath, tempPath);
                AddFileEntry(archive, tempPath, entryName);
            }
            finally
            {
                try { if (File.Exists(tempPath)) File.Delete(tempPath); }
                catch (Exception ex) { Debug.WriteLine($"[Backup] 임시 DB 삭제: {ex.Message}"); }
            }
        }

        /// <summary>
        /// 복원 — 실행 중인 VMS 가 연 DB 는 덮어쓸 수 없으므로 임시 파일로 풀어 DB 안으로 복사한다.
        /// </summary>
        private static void ExtractDatabaseEntry(ZipArchiveEntry entry, string destPath)
        {
            if (DatabaseCopier == null || !File.Exists(destPath))
            {
                entry.ExtractToFile(destPath, overwrite: true);
                return;
            }

            var tempPath = Path.Combine(Path.GetTempPath(), $"vms-restore-{Guid.NewGuid():N}.db");
            try
            {
                entry.ExtractToFile(tempPath, overwrite: true);
                DatabaseCopier(tempPath, destPath);
            }
            finally
            {
                try { if (File.Exists(tempPath)) File.Delete(tempPath); }
                catch (Exception ex) { Debug.WriteLine($"[Backup] 임시 DB 삭제: {ex.Message}"); }
            }
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

                    if (_databaseFiles.Contains(entry.FullName))
                        ExtractDatabaseEntry(entry, destPath);
                    else
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
