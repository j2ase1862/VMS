using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using VMS.VisionSetup.Models;

namespace VMS.VisionSetup.Services.BatchTesting
{
    public class GoldenSetEvaluation
    {
        public bool HasEntry { get; set; }   // 골든셋에 이 이미지의 entry가 있는지
        public bool Pass { get; set; } = true;
        public List<string> Mismatches { get; } = new();
    }

    /// <summary>
    /// 한 이미지의 결과를 골든셋과 비교 — ExpectedSuccess + 도구별 결과 키별 값 비교.
    /// 숫자 키는 상대 오차 1% 허용, 문자열은 정확 일치.
    /// </summary>
    public static class GoldenSetEvaluator
    {
        public static GoldenSetEvaluation Evaluate(
            string imagePath,
            BatchImageResult result,
            GoldenSet? golden,
            IDictionary<string, string>? toolIdToName = null)
        {
            var eval = new GoldenSetEvaluation();
            if (golden == null) return eval;

            var entry = golden.Lookup(imagePath);
            if (entry == null) return eval;
            eval.HasEntry = true;

            // 1. ExpectedSuccess 비교
            if (entry.ExpectedSuccess != result.Success)
            {
                eval.Pass = false;
                eval.Mismatches.Add($"Success: expected {entry.ExpectedSuccess}, got {result.Success}");
            }

            // 2. 도구별 결과 비교
            foreach (var toolKv in entry.ToolExpectations)
            {
                var toolId = toolKv.Key;
                var expectedMap = toolKv.Value;
                if (!result.ToolResults.TryGetValue(toolId, out var actualRes) || actualRes?.Data == null)
                {
                    // 결과 자체가 없음 — 매핑 깨졌거나 도구 비활성
                    var toolLabel = toolIdToName != null && toolIdToName.TryGetValue(toolId, out var n) ? n : toolId;
                    eval.Pass = false;
                    eval.Mismatches.Add($"{toolLabel}: 도구 결과 없음");
                    continue;
                }

                foreach (var kv in expectedMap)
                {
                    var key = kv.Key;
                    var expectedStr = kv.Value;

                    if (!actualRes.Data.TryGetValue(key, out var actualVal) || actualVal == null)
                    {
                        var toolLabel = toolIdToName != null && toolIdToName.TryGetValue(toolId, out var n) ? n : toolId;
                        eval.Pass = false;
                        eval.Mismatches.Add($"{toolLabel}.{key}: 값 없음 (expected {expectedStr})");
                        continue;
                    }

                    var actualStr = FormatValue(actualVal);
                    if (!ValuesMatch(expectedStr, actualStr))
                    {
                        var toolLabel = toolIdToName != null && toolIdToName.TryGetValue(toolId, out var n) ? n : toolId;
                        eval.Pass = false;
                        eval.Mismatches.Add($"{toolLabel}.{key}: expected '{expectedStr}', got '{actualStr}'");
                    }
                }
            }

            return eval;
        }

        /// <summary>
        /// 값 매칭 — 양쪽 모두 숫자로 파싱되면 상대 오차 1% 허용,
        /// 아니면 case-sensitive 정확 일치.
        /// </summary>
        private static bool ValuesMatch(string expected, string actual)
        {
            if (string.Equals(expected, actual, System.StringComparison.Ordinal)) return true;

            if (double.TryParse(expected, NumberStyles.Any, CultureInfo.InvariantCulture, out var e) &&
                double.TryParse(actual, NumberStyles.Any, CultureInfo.InvariantCulture, out var a))
            {
                if (e == 0 && a == 0) return true;
                var denom = System.Math.Max(System.Math.Abs(e), 1e-9);
                var relErr = System.Math.Abs(e - a) / denom;
                return relErr < 0.01;  // 1% 허용
            }

            return false;
        }

        private static string FormatValue(object v) => v switch
        {
            double d => d.ToString("G6", CultureInfo.InvariantCulture),
            float f => f.ToString("G6", CultureInfo.InvariantCulture),
            bool b => b ? "true" : "false",
            _ => v.ToString() ?? string.Empty
        };
    }
}
