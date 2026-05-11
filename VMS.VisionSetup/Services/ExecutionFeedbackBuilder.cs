using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using VMS.VisionSetup.Models;

namespace VMS.VisionSetup.Services
{
    /// <summary>
    /// Phase 5: 도구들의 최근 실행 결과(VisionResult)를 SLM 컨텍스트용 JSON으로 직렬화.
    /// 사용자가 후속 질문을 보낼 때 LLM이 결과를 보고 진단/조정 제안할 수 있게 해줌.
    /// </summary>
    public static class ExecutionFeedbackBuilder
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        /// <summary>
        /// 결과가 하나라도 있으면 LLM에 줄 텍스트 블록 반환, 없으면 null.
        /// </summary>
        public static string? Build(IEnumerable<VisionToolBase> tools)
        {
            var items = new List<ToolFeedback>();
            foreach (var t in tools)
            {
                if (t.LastResult == null) continue;
                items.Add(new ToolFeedback
                {
                    Name = t.Name,
                    Type = t.ToolType,
                    Success = t.LastResult.Success,
                    Message = string.IsNullOrEmpty(t.LastResult.Message) ? null : t.LastResult.Message,
                    ExecutionTimeMs = t.ExecutionTime > 0 ? System.Math.Round(t.ExecutionTime, 2) : null,
                    Data = ScrubData(t.LastResult.Data),
                });
            }

            if (items.Count == 0) return null;

            string json = JsonSerializer.Serialize(new { tools = items }, JsonOptions);
            return "[Execution Feedback]\n" + json;
        }

        /// <summary>
        /// VisionResult.Data dictionary에서 LLM 컨텍스트로 안전한 항목만 추출.
        /// Mat / GraphicOverlay / 매우 큰 컬렉션은 제외.
        /// </summary>
        private static Dictionary<string, object>? ScrubData(IDictionary<string, object>? data)
        {
            if (data == null || data.Count == 0) return null;

            var filtered = new Dictionary<string, object>();
            foreach (var (k, v) in data)
            {
                if (v == null) continue;

                // OpenCvSharp.Mat 같은 무거운 객체 제외
                var typeName = v.GetType().FullName ?? string.Empty;
                if (typeName.Contains("OpenCvSharp.Mat", System.StringComparison.OrdinalIgnoreCase))
                    continue;

                // primitive / string / enum은 그대로
                if (v is string || v is bool || v.GetType().IsPrimitive || v.GetType().IsEnum)
                {
                    filtered[k] = v;
                    continue;
                }

                // 컬렉션은 길이만 노출(점/좌표 다량 시 LLM 토큰 절약)
                if (v is System.Collections.ICollection coll)
                {
                    filtered[k + "_count"] = coll.Count;
                    continue;
                }

                // 그 외 객체는 ToString
                filtered[k] = v.ToString() ?? string.Empty;
            }

            return filtered.Count > 0 ? filtered : null;
        }

        /// <summary>
        /// LLM에 전달되는 도구별 결과 1건.
        /// </summary>
        private class ToolFeedback
        {
            [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
            [JsonPropertyName("type")] public string Type { get; set; } = string.Empty;
            [JsonPropertyName("success")] public bool Success { get; set; }
            [JsonPropertyName("message")] public string? Message { get; set; }
            [JsonPropertyName("executionTimeMs")] public double? ExecutionTimeMs { get; set; }
            [JsonPropertyName("data")] public Dictionary<string, object>? Data { get; set; }
        }
    }
}
