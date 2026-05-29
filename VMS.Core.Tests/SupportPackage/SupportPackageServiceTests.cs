using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using VMS.Core.SupportPackage;
using Xunit;

namespace VMS.Core.Tests.SupportPackage
{
    /// <summary>
    /// SupportPackageService 의 ZIP 구성, 옵션 동작, 민감 파일 제외 검증.
    /// 임시 디렉토리 격리.
    ///
    /// 클래스명 / 네임스페이스에 "Diagnostic" 미포함 → CI 필터 (ML diagnostic 제외) 와 무관.
    /// </summary>
    public class SupportPackageServiceTests : IDisposable
    {
        private readonly string _tempBase;
        private readonly string _appDataDir;
        private readonly string _outputZip;

        public SupportPackageServiceTests()
        {
            _tempBase = Path.Combine(Path.GetTempPath(), $"supportpkg_{Guid.NewGuid():N}");
            _appDataDir = Path.Combine(_tempBase, "appdata");
            _outputZip = Path.Combine(_tempBase, "support.zip");
            Directory.CreateDirectory(_appDataDir);
        }

        public void Dispose()
        {
            try { if (Directory.Exists(_tempBase)) Directory.Delete(_tempBase, recursive: true); }
            catch { /* 무시 */ }
        }

        private void Seed(string relPath, string content)
        {
            var path = Path.Combine(_appDataDir, relPath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        }

        // ─── Happy path ───────────────────────────────────────────

        [Fact]
        public void Export_HappyPath_CreatesZipWithManifest()
        {
            Seed("system_config.json", "{\"a\":1}");
            Seed("plc_signals.json", "{\"b\":2}");
            Seed("audit/2026-05-29.jsonl", "{}\n");

            var result = SupportPackageService.Export(_appDataDir, _outputZip);

            Assert.True(result.Success);
            Assert.True(File.Exists(_outputZip));
            Assert.True(result.BytesWritten > 0);

            using var zip = ZipFile.OpenRead(_outputZip);
            Assert.Contains(zip.Entries, e => e.FullName == "support_manifest.json");
            Assert.Contains(zip.Entries, e => e.FullName == "config/system_config.json");
            Assert.Contains(zip.Entries, e => e.FullName == "config/plc_signals.json");
            Assert.Contains(zip.Entries, e => e.FullName == "environment.txt");
            Assert.Contains(zip.Entries, e => e.FullName == "backups_index.txt");
            Assert.Contains(zip.Entries, e => e.FullName == "health_snapshot.json");
        }

        // ─── 민감 파일 제외 ───────────────────────────────────────

        [Fact]
        public void Export_NeverIncludes_BodaVisionDb()
        {
            Seed("BodaVision.db", "fake-db-bytes");
            Seed("system_config.json", "{}");

            SupportPackageService.Export(_appDataDir, _outputZip);

            using var zip = ZipFile.OpenRead(_outputZip);
            Assert.DoesNotContain(zip.Entries, e =>
                e.FullName.EndsWith("BodaVision.db", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void Export_NeverIncludes_RecipesFolder()
        {
            Seed("recipes/r1.json", "{\"recipe\":1}");
            Seed("system_config.json", "{}");

            SupportPackageService.Export(_appDataDir, _outputZip);

            using var zip = ZipFile.OpenRead(_outputZip);
            Assert.DoesNotContain(zip.Entries, e =>
                e.FullName.StartsWith("recipes/", StringComparison.OrdinalIgnoreCase));
        }

        // ─── Audit days 필터 ──────────────────────────────────────

        [Fact]
        public void Export_AuditDays_FiltersOldLogs()
        {
            var today = DateTime.UtcNow.Date;
            Seed($"audit/{today:yyyy-MM-dd}.jsonl", "{}\n");
            Seed($"audit/{today.AddDays(-3):yyyy-MM-dd}.jsonl", "{}\n");
            Seed($"audit/{today.AddDays(-30):yyyy-MM-dd}.jsonl", "{}\n");
            Seed("system_config.json", "{}");

            SupportPackageService.Export(_appDataDir, _outputZip,
                new SupportPackageOptions { AuditDays = 7 });

            using var zip = ZipFile.OpenRead(_outputZip);
            var auditEntries = zip.Entries.Where(e => e.FullName.StartsWith("audit/")).ToList();
            Assert.Equal(2, auditEntries.Count);  // today + -3d
        }

        [Fact]
        public void Export_AuditDays_BelowMin_Clamped()
        {
            var today = DateTime.UtcNow.Date;
            Seed($"audit/{today:yyyy-MM-dd}.jsonl", "{}\n");
            Seed("system_config.json", "{}");

            SupportPackageService.Export(_appDataDir, _outputZip,
                new SupportPackageOptions { AuditDays = 0 });

            using var zip = ZipFile.OpenRead(_outputZip);
            Assert.Contains(zip.Entries, e => e.FullName.StartsWith("audit/"));
        }

        [Fact]
        public void Export_AuditDays_AboveMax_Clamped()
        {
            Seed("system_config.json", "{}");

            var result = SupportPackageService.Export(_appDataDir, _outputZip,
                new SupportPackageOptions { AuditDays = 99999 });

            using var zip = ZipFile.OpenRead(_outputZip);
            var manifest = ReadManifest(zip);
            Assert.Equal(90, manifest!.AuditDaysIncluded);
            Assert.True(result.Success);
        }

        // ─── 옵션 ────────────────────────────────────────────────

        [Fact]
        public void Export_IncludeEnvironmentFalse_SkipsEnvironmentEntry()
        {
            Seed("system_config.json", "{}");

            SupportPackageService.Export(_appDataDir, _outputZip,
                new SupportPackageOptions { IncludeEnvironment = false });

            using var zip = ZipFile.OpenRead(_outputZip);
            Assert.DoesNotContain(zip.Entries, e => e.FullName == "environment.txt");
        }

        [Fact]
        public void Export_IncludeBackupIndexFalse_SkipsBackupIndex()
        {
            Seed("system_config.json", "{}");

            SupportPackageService.Export(_appDataDir, _outputZip,
                new SupportPackageOptions { IncludeBackupIndex = false });

            using var zip = ZipFile.OpenRead(_outputZip);
            Assert.DoesNotContain(zip.Entries, e => e.FullName == "backups_index.txt");
        }

        // ─── MachineName 기본 제외 ────────────────────────────────

        [Fact]
        public void Export_MachineName_DefaultExcluded()
        {
            Seed("system_config.json", "{}");

            SupportPackageService.Export(_appDataDir, _outputZip);

            using var zip = ZipFile.OpenRead(_outputZip);
            var manifest = ReadManifest(zip);
            Assert.Null(manifest!.MachineName);
        }

        [Fact]
        public void Export_MachineName_OptIn_Included()
        {
            Seed("system_config.json", "{}");

            SupportPackageService.Export(_appDataDir, _outputZip,
                new SupportPackageOptions { IncludeMachineName = true });

            using var zip = ZipFile.OpenRead(_outputZip);
            var manifest = ReadManifest(zip);
            Assert.False(string.IsNullOrEmpty(manifest!.MachineName));
        }

        // ─── Environment / health snapshot 내용 ──────────────────

        [Fact]
        public void Export_EnvironmentContent_HasOsAndDotNet()
        {
            Seed("system_config.json", "{}");

            SupportPackageService.Export(_appDataDir, _outputZip);

            using var zip = ZipFile.OpenRead(_outputZip);
            var env = zip.GetEntry("environment.txt");
            Assert.NotNull(env);
            using var sr = new StreamReader(env!.Open());
            var text = sr.ReadToEnd();

            Assert.Contains("OSVersion:", text);
            Assert.Contains(".NETVersion:", text);
            Assert.Contains("ProcessorCount:", text);
            Assert.Contains("# Drives", text);
        }

        [Fact]
        public void Export_HealthSnapshotContent_ValidJson()
        {
            Seed("system_config.json", "{}");

            SupportPackageService.Export(_appDataDir, _outputZip);

            using var zip = ZipFile.OpenRead(_outputZip);
            var snapshot = zip.GetEntry("health_snapshot.json");
            Assert.NotNull(snapshot);
            using var sr = new StreamReader(snapshot!.Open());
            var json = sr.ReadToEnd();

            using var doc = JsonDocument.Parse(json);
            Assert.True(doc.RootElement.TryGetProperty("overallStatus", out _));
            Assert.True(doc.RootElement.TryGetProperty("items", out _));
        }

        // ─── Edge cases ───────────────────────────────────────────

        [Fact]
        public void Export_NonExistentAppDataDir_Fails()
        {
            var bogus = Path.Combine(_tempBase, "missing");
            var result = SupportPackageService.Export(bogus, _outputZip);

            Assert.False(result.Success);
            Assert.Contains("AppData", result.ErrorMessage ?? "");
        }

        [Fact]
        public void Export_OverwritesExistingZip()
        {
            Seed("system_config.json", "{}");

            SupportPackageService.Export(_appDataDir, _outputZip);
            var first = new FileInfo(_outputZip).Length;

            Seed("plc_signals.json", "{}");
            var result = SupportPackageService.Export(_appDataDir, _outputZip);
            var second = new FileInfo(_outputZip).Length;

            Assert.True(result.Success);
            Assert.True(second >= first);
        }

        [Fact]
        public void Export_BackupsIndex_IncludesAutoBackupZips()
        {
            Seed("system_config.json", "{}");
            Seed("backups/auto-backup-20260101-120000.zip", "fake");

            SupportPackageService.Export(_appDataDir, _outputZip);

            using var zip = ZipFile.OpenRead(_outputZip);
            var idx = zip.GetEntry("backups_index.txt");
            Assert.NotNull(idx);
            using var sr = new StreamReader(idx!.Open());
            var text = sr.ReadToEnd();
            Assert.Contains("auto-backup-20260101-120000.zip", text);
        }

        // ─── Helper ───────────────────────────────────────────────

        private static SupportPackageManifest? ReadManifest(ZipArchive zip)
        {
            var entry = zip.GetEntry("support_manifest.json");
            if (entry == null) return null;
            using var sr = new StreamReader(entry.Open());
            return JsonSerializer.Deserialize<SupportPackageManifest>(sr.ReadToEnd());
        }
    }
}
