using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using VMS.Core.Security;

namespace VMS.Core.Retention
{
    /// <summary>
    /// 단일 보존 정책 dry-run 결과.
    /// </summary>
    public sealed class RetentionPreviewSummary
    {
        public string PolicyName { get; init; } = string.Empty;
        public int FilesAffected { get; init; }
        public long BytesAffected { get; init; }
        public int FilesRemaining { get; init; }
        public DateTime? OldestAffectedUtc { get; init; }
        public DateTime? OldestRemainingUtc { get; init; }
        public string? Note { get; init; }
    }

    /// <summary>
    /// 카테고리별 라인 필터 dry-run 결과.
    /// </summary>
    public sealed class CategoryFilterPreview
    {
        public IReadOnlyDictionary<AuditCategory, int> LinesRemovedByCategory { get; init; }
            = new Dictionary<AuditCategory, int>();
        public int TotalLinesRemoved { get; init; }
        public int FilesWithRemovals { get; init; }
        public int FilesScanned { get; init; }
        public int FilesEmptiedAndDeletable { get; init; }
    }

    /// <summary>
    /// 보존 정책 적용 전 영향 미리보기 — 운영자가 Save 누르기 전에
    /// "현재 설정으로 N 파일 / M 라인이 사라진다" 를 사전 확인.
    ///
    /// 모든 메서드는 read-only — 파일을 절대 수정하지 않음.
    /// </summary>
    public static class RetentionPreviewService
    {
        // ─── Audit log 전역 정리 (whole-file) ────────────────────

        public static RetentionPreviewSummary PreviewAuditLogCleanup(string auditDir, int retentionDays)
        {
            return PreviewDailyJsonlCleanup(auditDir, retentionDays, "AuditLogRetention");
        }

        // ─── Upload queue (timestamp prefix) ─────────────────────

        public static RetentionPreviewSummary PreviewUploadQueueCleanup(string queueDir, int retentionDays)
        {
            return PreviewQueueFiles(queueDir, retentionDays, "UploadQueueRetention");
        }

        // ─── Auto-backup ZIP ─────────────────────────────────────

        public static RetentionPreviewSummary PreviewAutoBackupCleanup(string backupDir, int retentionDays)
        {
            return PreviewAutoBackupFiles(backupDir, retentionDays, "AutoBackupRetention");
        }

        // ─── 카테고리별 라인 필터 ────────────────────────────────

        public static CategoryFilterPreview PreviewAuditCategoryFilter(
            string auditDir, IReadOnlyDictionary<AuditCategory, int> perCategoryDays)
        {
            var perCat = new Dictionary<AuditCategory, int>();
            int totalRemoved = 0;
            int filesWith = 0;
            int filesScanned = 0;
            int filesEmptied = 0;

            if (string.IsNullOrWhiteSpace(auditDir) || !Directory.Exists(auditDir))
                return new CategoryFilterPreview { LinesRemovedByCategory = perCat };

            var nowUtc = DateTime.UtcNow;

            try
            {
                foreach (var file in Directory.EnumerateFiles(auditDir, "*.jsonl"))
                {
                    filesScanned++;
                    int removedInFile = 0;
                    int totalLines = 0;

                    try
                    {
                        foreach (var line in File.ReadLines(file))
                        {
                            if (string.IsNullOrWhiteSpace(line)) continue;
                            totalLines++;
                            var (drop, cat) = AnalyzeLine(line, perCategoryDays, nowUtc);
                            if (!drop) continue;

                            removedInFile++;
                            totalRemoved++;
                            if (cat.HasValue)
                            {
                                if (perCat.TryGetValue(cat.Value, out var n))
                                    perCat[cat.Value] = n + 1;
                                else
                                    perCat[cat.Value] = 1;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[RetentionPreview] {file} 읽기 실패: {ex.Message}");
                    }

                    if (removedInFile > 0)
                    {
                        filesWith++;
                        if (removedInFile == totalLines) filesEmptied++;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[RetentionPreview] 카테고리 dry-run 열거 실패: {ex.Message}");
            }

            return new CategoryFilterPreview
            {
                LinesRemovedByCategory = perCat,
                TotalLinesRemoved = totalRemoved,
                FilesWithRemovals = filesWith,
                FilesScanned = filesScanned,
                FilesEmptiedAndDeletable = filesEmptied
            };
        }

        // ─── 내부 — Daily JSONL (audit) 패턴 ─────────────────────

        private static RetentionPreviewSummary PreviewDailyJsonlCleanup(string dir, int retentionDays, string policyName)
        {
            if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
                return new RetentionPreviewSummary
                {
                    PolicyName = policyName,
                    Note = "디렉토리 없음 — 영향 없음"
                };

            var today = DateTime.UtcNow.Date;
            var threshold = today.AddDays(-retentionDays);
            int affected = 0, remaining = 0;
            long bytesAffected = 0;
            DateTime? oldestAffected = null;
            DateTime? oldestRemaining = null;

            try
            {
                foreach (var file in Directory.EnumerateFiles(dir, "*.jsonl"))
                {
                    var name = Path.GetFileNameWithoutExtension(file);
                    if (!DateTime.TryParseExact(name, "yyyy-MM-dd",
                            CultureInfo.InvariantCulture, DateTimeStyles.None, out var fileDate))
                    {
                        continue;
                    }
                    if (fileDate >= today)
                    {
                        // 오늘 파일은 보존 정책 무관 항상 유지.
                        remaining++;
                        if (!oldestRemaining.HasValue || fileDate < oldestRemaining.Value)
                            oldestRemaining = fileDate;
                        continue;
                    }
                    if (fileDate < threshold)
                    {
                        affected++;
                        try { bytesAffected += new FileInfo(file).Length; } catch { }
                        if (!oldestAffected.HasValue || fileDate < oldestAffected.Value)
                            oldestAffected = fileDate;
                    }
                    else
                    {
                        remaining++;
                        if (!oldestRemaining.HasValue || fileDate < oldestRemaining.Value)
                            oldestRemaining = fileDate;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[RetentionPreview] {policyName} dry-run 실패: {ex.Message}");
            }

            return new RetentionPreviewSummary
            {
                PolicyName = policyName,
                FilesAffected = affected,
                BytesAffected = bytesAffected,
                FilesRemaining = remaining,
                OldestAffectedUtc = oldestAffected,
                OldestRemainingUtc = oldestRemaining
            };
        }

        // ─── 내부 — Upload queue 패턴 (yyyyMMddHHmmssfff_{guid}.json) ──

        private static RetentionPreviewSummary PreviewQueueFiles(string dir, int retentionDays, string policyName)
        {
            if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
                return new RetentionPreviewSummary { PolicyName = policyName, Note = "디렉토리 없음 — 영향 없음" };

            const string format = "yyyyMMddHHmmssfff";
            var thresholdUtc = DateTime.UtcNow.AddDays(-retentionDays);
            int affected = 0, remaining = 0;
            long bytesAffected = 0;
            DateTime? oldestAffected = null;
            DateTime? oldestRemaining = null;

            try
            {
                foreach (var file in Directory.EnumerateFiles(dir, "*.json"))
                {
                    var name = Path.GetFileNameWithoutExtension(file);
                    if (string.IsNullOrEmpty(name)) continue;
                    var underscore = name.IndexOf('_');
                    if (underscore != format.Length) continue;
                    var tsPart = name.Substring(0, underscore);
                    if (!DateTime.TryParseExact(tsPart, format, CultureInfo.InvariantCulture,
                            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var fileUtc))
                    {
                        continue;
                    }

                    if (fileUtc < thresholdUtc)
                    {
                        affected++;
                        try { bytesAffected += new FileInfo(file).Length; } catch { }
                        if (!oldestAffected.HasValue || fileUtc < oldestAffected.Value)
                            oldestAffected = fileUtc;
                    }
                    else
                    {
                        remaining++;
                        if (!oldestRemaining.HasValue || fileUtc < oldestRemaining.Value)
                            oldestRemaining = fileUtc;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[RetentionPreview] {policyName} dry-run 실패: {ex.Message}");
            }

            return new RetentionPreviewSummary
            {
                PolicyName = policyName,
                FilesAffected = affected,
                BytesAffected = bytesAffected,
                FilesRemaining = remaining,
                OldestAffectedUtc = oldestAffected,
                OldestRemainingUtc = oldestRemaining
            };
        }

        // ─── 내부 — auto-backup ZIP 패턴 (auto-backup-YYYYMMDD-HHmmss.zip) ──

        private static RetentionPreviewSummary PreviewAutoBackupFiles(string dir, int retentionDays, string policyName)
        {
            if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
                return new RetentionPreviewSummary { PolicyName = policyName, Note = "디렉토리 없음 — 영향 없음" };

            const string prefix = "auto-backup-";
            const string format = "yyyyMMdd-HHmmss";
            var thresholdUtc = DateTime.UtcNow.AddDays(-retentionDays);
            int affected = 0, remaining = 0;
            long bytesAffected = 0;
            DateTime? oldestAffected = null;
            DateTime? oldestRemaining = null;

            try
            {
                foreach (var file in Directory.EnumerateFiles(dir, $"{prefix}*.zip"))
                {
                    var name = Path.GetFileNameWithoutExtension(file);
                    if (!name.StartsWith(prefix, StringComparison.Ordinal)) continue;
                    var tsPart = name.Substring(prefix.Length);
                    if (!DateTime.TryParseExact(tsPart, format, CultureInfo.InvariantCulture,
                            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                            out var fileUtc))
                    {
                        continue;
                    }

                    if (fileUtc < thresholdUtc)
                    {
                        affected++;
                        try { bytesAffected += new FileInfo(file).Length; } catch { }
                        if (!oldestAffected.HasValue || fileUtc < oldestAffected.Value)
                            oldestAffected = fileUtc;
                    }
                    else
                    {
                        remaining++;
                        if (!oldestRemaining.HasValue || fileUtc < oldestRemaining.Value)
                            oldestRemaining = fileUtc;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[RetentionPreview] {policyName} dry-run 실패: {ex.Message}");
            }

            return new RetentionPreviewSummary
            {
                PolicyName = policyName,
                FilesAffected = affected,
                BytesAffected = bytesAffected,
                FilesRemaining = remaining,
                OldestAffectedUtc = oldestAffected,
                OldestRemainingUtc = oldestRemaining
            };
        }

        // ─── 카테고리 라인 분석 (parse 실패 / 누락은 keep) ───────

        private static (bool Drop, AuditCategory? Category) AnalyzeLine(
            string line, IReadOnlyDictionary<AuditCategory, int> perCategoryDays, DateTime nowUtc)
        {
            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;

                if (!root.TryGetProperty("timestamp", out var tsProp) || !tsProp.TryGetDateTime(out var ts))
                    return (false, null);

                AuditCategory cat = AuditCategory.System;
                if (root.TryGetProperty("category", out var catProp) &&
                    catProp.ValueKind == JsonValueKind.String &&
                    Enum.TryParse<AuditCategory>(catProp.GetString(), ignoreCase: true, out var parsed))
                {
                    cat = parsed;
                }

                int days = perCategoryDays.TryGetValue(cat, out var d) ? d :
                    (AuditCategoryRetention.Defaults.TryGetValue(cat, out var defD) ? defD : 365);

                var threshold = nowUtc.AddDays(-days);
                return (ts < threshold, cat);
            }
            catch
            {
                return (false, null);
            }
        }
    }
}
