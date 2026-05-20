using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using VMS.VisionSetup.Models;

namespace VMS.VisionSetup.Services.BatchTesting
{
    /// <summary>
    /// 배치 결과를 동적 컬럼 CSV로 작성.
    /// 컬럼: image_path, success, total_ms, failure_reason, 그리고 도구별 컬럼:
    ///   "{ToolName}.Success", "{ToolName}.{ResultKey}" (도구의 GetAvailableResultKeys 기준)
    /// Excel/Pandas 친화적인 UTF-8 BOM 출력.
    /// </summary>
    public static class CsvReportWriter
    {
        public static void Write(
            string csvPath,
            IList<BatchImageResult> results,
            ObservableCollection<VisionToolBase> tools)
        {
            // 1) 컬럼 헤더 결정 — 도구별 키 = (a) GetAvailableResultKeys 선언 키 ∪ (b) 모든 결과의 Data에 실제로 나타난 키
            //    (b)를 포함하여 도구가 선언 깜빡한 키나 동적 키(예: Gs1_{AI})도 자동 캡처
            var toolColumns = new List<(VisionToolBase Tool, List<string> Keys)>();
            foreach (var t in tools)
            {
                var keys = new List<string> { "Success" };

                // (a) 선언된 키
                try
                {
                    var resultKeys = t.GetAvailableResultKeys();
                    foreach (var k in resultKeys)
                        if (!keys.Contains(k)) keys.Add(k);
                }
                catch { /* 일부 도구는 비어있을 수 있음 */ }

                // (b) 실제 결과에서 발견된 키 union
                foreach (var r in results)
                {
                    if (!r.ToolResults.TryGetValue(t.Id, out var toolRes)) continue;
                    if (toolRes?.Data == null) continue;
                    foreach (var k in toolRes.Data.Keys)
                        if (!keys.Contains(k)) keys.Add(k);
                }

                toolColumns.Add((t, keys));
            }

            // 2) 헤더 작성
            var header = new List<string> { "image_path", "success", "total_ms", "failure_reason",
                                             "thresholds_passed", "threshold_violations" };
            foreach (var (tool, keys) in toolColumns)
                foreach (var k in keys)
                    header.Add($"{SanitizeColumn(tool.Name)}.{k}");

            // 3) UTF-8 BOM (Excel 한글 호환)
            using var sw = new StreamWriter(csvPath, false, new UTF8Encoding(true));
            sw.WriteLine(string.Join(",", header.Select(CsvEscape)));

            foreach (var r in results)
            {
                var row = new List<string>
                {
                    r.ImagePath,
                    r.Success ? "PASS" : "FAIL",
                    r.TotalMs.ToString("F2", System.Globalization.CultureInfo.InvariantCulture),
                    r.FailureReason ?? string.Empty,
                    r.ThresholdsPassed ? "true" : "false",
                    string.Join(" | ", r.ThresholdViolations ?? new List<string>())
                };

                foreach (var (tool, keys) in toolColumns)
                {
                    r.ToolResults.TryGetValue(tool.Id, out var toolRes);
                    foreach (var key in keys)
                    {
                        string cell = string.Empty;
                        if (toolRes != null)
                        {
                            if (key == "Success")
                                cell = toolRes.Success ? "true" : "false";
                            else if (toolRes.Data.TryGetValue(key, out var v) && v != null)
                                cell = FormatValue(v);
                        }
                        row.Add(cell);
                    }
                }
                sw.WriteLine(string.Join(",", row.Select(CsvEscape)));
            }
        }

        private static string SanitizeColumn(string name) =>
            string.IsNullOrEmpty(name) ? "Tool" : name.Replace(",", "_").Replace("\"", "");

        private static string FormatValue(object v) => v switch
        {
            double d => d.ToString("G6", System.Globalization.CultureInfo.InvariantCulture),
            float f => f.ToString("G6", System.Globalization.CultureInfo.InvariantCulture),
            bool b => b ? "true" : "false",
            _ => v.ToString() ?? string.Empty
        };

        private static string CsvEscape(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            bool needsQuote = s.Contains(',') || s.Contains('"') || s.Contains('\n') || s.Contains('\r');
            if (!needsQuote) return s;
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        }
    }
}
