using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace VMS.Core.Security
{
    /// <summary>
    /// 카테고리별 차등 보존 정책 — 같은 jsonl 파일 안의 항목을 카테고리별로
    /// 다른 보존 기간으로 필터링. 전역 AuditLogRetention (whole-file 삭제) 뒤에
    /// 호출되어, 남은 파일에서 카테고리별 만료 라인을 제거.
    ///
    /// 정책:
    /// - 기본 보존 일수 카테고리별 차등 (GS 권장):
    ///   · Security / UserManagement / Configuration: 1095 (3년) — 침해 / CRUD / 설정 변경
    ///   · Authentication / Authorization / RecipeChange: 730 (2년)
    ///   · SequenceControl / Inspection: 365 (1년)
    ///   · System: 90 (3개월) — 내부 운영 이벤트
    /// - system_config.json 의 "auditCategoryRetentionDays" 객체로 override 가능
    /// - 각 키는 [1, 3650] clamp
    ///
    /// 동작:
    /// - 매 jsonl 파일을 읽으며 각 라인의 timestamp + category 파싱
    /// - 카테고리별 보존 기간 초과 라인 제거
    /// - 모든 라인이 제거되면 파일 삭제, 일부 남으면 rewrite
    /// - parse 실패 라인은 안전하게 유지 (silent drop 방지)
    /// </summary>
    public static class AuditCategoryRetention
    {
        private const int MinDays = 1;
        private const int MaxDays = 3650;

        /// <summary>카테고리별 기본 보존 일수 — GS 권장.</summary>
        public static readonly IReadOnlyDictionary<AuditCategory, int> Defaults =
            new Dictionary<AuditCategory, int>
            {
                [AuditCategory.Security]        = 1095,
                [AuditCategory.UserManagement]  = 1095,
                [AuditCategory.Configuration]   = 1095,
                [AuditCategory.Authentication]  = 730,
                [AuditCategory.Authorization]   = 730,
                [AuditCategory.RecipeChange]    = 730,
                [AuditCategory.SequenceControl] = 365,
                [AuditCategory.Inspection]      = 365,
                [AuditCategory.System]          = 90,
            };

        /// <summary>
        /// system_config.json 의 "auditCategoryRetentionDays" 객체에서 카테고리별
        /// override 를 로드 + 기본값에 병합. 키 누락 / 파일 없음 시 기본값 그대로.
        /// </summary>
        public static IReadOnlyDictionary<AuditCategory, int> LoadDaysFromAppData()
        {
            var result = new Dictionary<AuditCategory, int>(Defaults);
            try
            {
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var path = Path.Combine(appData, "BODA VISION AI", "system_config.json");
                if (!File.Exists(path)) return result;

                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                if (!doc.RootElement.TryGetProperty("auditCategoryRetentionDays", out var elem)
                    || elem.ValueKind != JsonValueKind.Object)
                {
                    return result;
                }

                foreach (var prop in elem.EnumerateObject())
                {
                    if (!Enum.TryParse<AuditCategory>(prop.Name, ignoreCase: true, out var cat)) continue;
                    if (!prop.Value.TryGetInt32(out var days)) continue;
                    result[cat] = Clamp(days);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AuditCategoryRetention] Load 실패 — 기본값 사용: {ex.Message}");
            }
            return result;
        }

        /// <summary>
        /// <paramref name="auditDir"/> 의 모든 *.jsonl 파일을 카테고리별 보존 기간 기준으로 필터.
        /// </summary>
        /// <returns>실제 제거된 라인 수.</returns>
        public static int FilterFilesByCategory(string auditDir, IReadOnlyDictionary<AuditCategory, int> perCategoryDays)
        {
            if (string.IsNullOrWhiteSpace(auditDir) || !Directory.Exists(auditDir)) return 0;

            int totalRemoved = 0;
            int filesRewritten = 0;
            int filesDeleted = 0;
            int filesScanned = 0;
            var nowUtc = DateTime.UtcNow;

            try
            {
                foreach (var file in Directory.EnumerateFiles(auditDir, "*.jsonl"))
                {
                    filesScanned++;
                    var (kept, removed, anyRemoved) = FilterFile(file, perCategoryDays, nowUtc);
                    totalRemoved += removed;
                    if (!anyRemoved) continue;

                    if (kept.Count == 0)
                    {
                        try { File.Delete(file); filesDeleted++; }
                        catch (Exception ex) { Debug.WriteLine($"[AuditCategoryRetention] 삭제 실패 {file}: {ex.Message}"); }
                    }
                    else
                    {
                        try
                        {
                            File.WriteAllLines(file, kept, Encoding.UTF8);
                            filesRewritten++;
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"[AuditCategoryRetention] rewrite 실패 {file}: {ex.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AuditCategoryRetention] 열거 실패: {ex.Message}");
            }

            try
            {
                AuditLogger.Instance.Log(
                    AuditCategory.System, "AuditCategoryRetention", AuditOutcome.Success,
                    source: nameof(AuditCategoryRetention),
                    details: $"FilesScanned={filesScanned}, FilesRewritten={filesRewritten}, " +
                             $"FilesDeleted={filesDeleted}, LinesRemoved={totalRemoved}");
            }
            catch { /* 감사 자체 실패는 무시 */ }

            return totalRemoved;
        }

        /// <summary>
        /// 단일 파일을 라인별로 필터. 반환: (유지 라인 / 제거 수 / 변경 여부).
        /// parse 실패 라인은 유지 — 데이터 손실 방지.
        /// </summary>
        private static (List<string> Kept, int Removed, bool AnyRemoved) FilterFile(
            string path, IReadOnlyDictionary<AuditCategory, int> perCategoryDays, DateTime nowUtc)
        {
            var kept = new List<string>();
            int removed = 0;
            bool anyRemoved = false;

            try
            {
                foreach (var line in File.ReadLines(path))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    if (ShouldKeep(line, perCategoryDays, nowUtc))
                    {
                        kept.Add(line);
                    }
                    else
                    {
                        removed++;
                        anyRemoved = true;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AuditCategoryRetention] 읽기 실패 {path}: {ex.Message}");
                anyRemoved = false;
            }

            return (kept, removed, anyRemoved);
        }

        /// <summary>
        /// JSONL 한 라인을 보존 정책에 따라 keep/drop 판정.
        /// timestamp 또는 category parse 실패 시 keep (안전 default).
        /// </summary>
        private static bool ShouldKeep(string line, IReadOnlyDictionary<AuditCategory, int> perCategoryDays, DateTime nowUtc)
        {
            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;

                if (!root.TryGetProperty("timestamp", out var tsProp) ||
                    !tsProp.TryGetDateTime(out var ts))
                {
                    return true;  // timestamp 없음 → 안전하게 keep
                }

                AuditCategory cat = AuditCategory.System;
                if (root.TryGetProperty("category", out var catProp) &&
                    catProp.ValueKind == JsonValueKind.String &&
                    Enum.TryParse<AuditCategory>(catProp.GetString(), ignoreCase: true, out var parsed))
                {
                    cat = parsed;
                }

                if (!perCategoryDays.TryGetValue(cat, out var days))
                    days = Defaults.TryGetValue(cat, out var d) ? d : 365;

                var threshold = nowUtc.AddDays(-days);
                return ts >= threshold;
            }
            catch
            {
                return true;  // 손상 라인 → keep (사용자가 직접 검토)
            }
        }

        private static int Clamp(int days)
        {
            if (days < MinDays) return MinDays;
            if (days > MaxDays) return MaxDays;
            return days;
        }
    }
}
