using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using VMS.VisionSetup.Models;

namespace VMS.VisionSetup.Services.BatchTesting
{
    /// <summary>한 이미지에 대한 임계치 평가 결과.</summary>
    public class ThresholdEvaluation
    {
        public bool Pass { get; set; } = true;
        public List<string> Violations { get; } = new();
    }

    /// <summary>
    /// Recipe.Criteria + 도구별 VisionResult 를 입력받아 임계치 위반을 감지.
    /// MustSucceed + 결과 키별 Range(Min/Max) 평가.
    /// </summary>
    public static class PassFailEvaluator
    {
        /// <summary>한 도구의 결과를 PassFailCriteria로 평가.</summary>
        public static void EvaluateTool(
            VisionToolBase tool,
            VisionResult result,
            PassFailCriteria? criteria,
            ThresholdEvaluation accumulator)
        {
            if (criteria?.ToolCriteria == null) return;
            if (!criteria.ToolCriteria.TryGetValue(tool.Id, out var toolCrit) || toolCrit == null) return;

            // 1. MustSucceed
            if (toolCrit.MustSucceed && !result.Success)
            {
                accumulator.Pass = false;
                accumulator.Violations.Add($"{tool.Name}: 도구 실행 실패 (Must Succeed)");
            }

            // 2. Range 평가
            if (toolCrit.Ranges == null) return;
            foreach (var kv in toolCrit.Ranges)
            {
                var key = kv.Key;
                var range = kv.Value;
                if (range == null) continue;

                if (!result.Data.TryGetValue(key, out var rawVal) || rawVal == null)
                {
                    // 키가 없으면 검사 불가 — 위반으로 보고 (의도된 키가 출력 안 됨)
                    accumulator.Pass = false;
                    accumulator.Violations.Add($"{tool.Name}.{key}: 결과 데이터 없음");
                    continue;
                }

                if (!TryToDouble(rawVal, out var v))
                {
                    accumulator.Pass = false;
                    accumulator.Violations.Add($"{tool.Name}.{key}: 숫자가 아님 ({rawVal})");
                    continue;
                }

                if (!range.IsInRange(v))
                {
                    accumulator.Pass = false;
                    accumulator.Violations.Add(BuildRangeViolationMessage(tool.Name, key, v, range));
                }
            }
        }

        /// <summary>여러 도구 결과를 한 번에 평가.</summary>
        public static ThresholdEvaluation EvaluateAll(
            IEnumerable<(VisionToolBase Tool, VisionResult Result)> pairs,
            PassFailCriteria? criteria)
        {
            var eval = new ThresholdEvaluation();
            if (criteria == null) return eval;
            foreach (var (tool, res) in pairs)
                EvaluateTool(tool, res, criteria, eval);
            return eval;
        }

        private static bool TryToDouble(object v, out double result)
        {
            switch (v)
            {
                case double d: result = d; return true;
                case float f: result = f; return true;
                case int i: result = i; return true;
                case long l: result = l; return true;
                case decimal dec: result = (double)dec; return true;
                case bool b: result = b ? 1.0 : 0.0; return true;
                case string s when double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed):
                    result = parsed; return true;
                default:
                    result = 0;
                    return false;
            }
        }

        private static string BuildRangeViolationMessage(string toolName, string key, double value, RangeCriteria range)
        {
            string bounds;
            if (range.Min.HasValue && range.Max.HasValue)
                bounds = $"{range.Min.Value:G6}..{range.Max.Value:G6}";
            else if (range.Min.HasValue)
                bounds = $"≥ {range.Min.Value:G6}";
            else if (range.Max.HasValue)
                bounds = $"≤ {range.Max.Value:G6}";
            else
                bounds = "(범위 없음)";

            return $"{toolName}.{key}: {value:G6} ∉ {bounds}";
        }
    }
}
