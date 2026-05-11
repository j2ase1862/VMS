using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json.Serialization;
using VMS.VisionSetup.Attributes;
using VMS.VisionSetup.Models;
using VMS.VisionSetup.Services;

namespace VMS.VisionSetup.Services
{
    /// <summary>
    /// VisionToolBase 파생 클래스의 [TunableParam] 어트리뷰트를 리플렉션으로 수집해
    /// SLM 컨텍스트 주입용 JSON-friendly 스키마로 변환.
    /// </summary>
    public static class ParameterSchemaExtractor
    {
        /// <summary>
        /// 단일 도구 인스턴스의 파라미터 스키마 추출.
        /// </summary>
        public static ToolParameterSchema Extract(VisionToolBase tool)
        {
            return Extract(tool.GetType(), tool.ToolType);
        }

        /// <summary>
        /// 도구 타입의 파라미터 스키마 추출. ToolType 명시 가능.
        /// </summary>
        public static ToolParameterSchema Extract(Type toolType, string? toolTypeName = null)
        {
            var parameters = new List<ParameterDescriptor>();

            var properties = toolType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.GetCustomAttribute<TunableParamAttribute>() != null);

            foreach (var prop in properties)
            {
                var attr = prop.GetCustomAttribute<TunableParamAttribute>()!;
                parameters.Add(BuildDescriptor(prop, attr));
            }

            return new ToolParameterSchema
            {
                ToolType = toolTypeName ?? toolType.Name,
                Parameters = parameters
            };
        }

        /// <summary>
        /// 여러 도구 타입의 스키마를 한 번에 추출 (SLM 시스템 프롬프트용).
        /// </summary>
        public static List<ToolParameterSchema> ExtractMany(IEnumerable<Type> toolTypes)
        {
            return toolTypes.Select(t => Extract(t)).ToList();
        }

        /// <summary>
        /// SLM 시스템 프롬프트에 삽입할 컴팩트한 파라미터 설명 섹션 생성.
        /// 어트리뷰트가 없는 도구는 자동 제외.
        /// </summary>
        public static string BuildPromptSection(IEnumerable<Type> toolTypes)
        {
            var schemas = ExtractMany(toolTypes).Where(s => s.Parameters.Count > 0).ToList();
            if (schemas.Count == 0)
                return string.Empty;

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("[튜닝 가능 파라미터 / Tunable Parameters]");
            sb.AppendLine("아래 도구는 \"Parameters\" 필드로 값을 함께 지정할 수 있습니다.");
            sb.AppendLine("Tier가 Semantic/DomainCommon인 항목은 사용자 의도로 적극 추정하고, ImageDependent는 명확한 단서가 있을 때만 지정하십시오.");
            sb.AppendLine();

            foreach (var s in schemas)
            {
                sb.AppendLine($"{s.ToolType}:");
                foreach (var p in s.Parameters)
                {
                    var typeStr = p.EnumValues != null
                        ? $"enum [{string.Join("|", p.EnumValues)}]"
                        : p.Type;

                    var rangeStr = "";
                    if (p.Min.HasValue && p.Max.HasValue)
                        rangeStr = $", {p.Min.Value:0.##}~{p.Max.Value:0.##}";
                    else if (p.Min.HasValue)
                        rangeStr = $", ≥{p.Min.Value:0.##}";
                    else if (p.Max.HasValue)
                        rangeStr = $", ≤{p.Max.Value:0.##}";

                    var dependsStr = string.IsNullOrEmpty(p.DependsOn) ? "" : $" [if {p.DependsOn}]";

                    sb.AppendLine($"  - {p.Name} ({typeStr}{rangeStr}, {p.Tier}{dependsStr}): {p.Description}");
                }
                sb.AppendLine();
            }

            return sb.ToString();
        }

        private static ParameterDescriptor BuildDescriptor(PropertyInfo prop, TunableParamAttribute attr)
        {
            var descriptor = new ParameterDescriptor
            {
                Name = prop.Name,
                Description = attr.Description,
                Tier = attr.Tier.ToString(),
                Type = ResolveTypeName(prop.PropertyType),
            };

            if (!double.IsNaN(attr.Min))
                descriptor.Min = attr.Min;

            if (!double.IsNaN(attr.Max))
                descriptor.Max = attr.Max;

            if (!string.IsNullOrEmpty(attr.DefaultHint))
                descriptor.DefaultHint = attr.DefaultHint;

            if (!string.IsNullOrEmpty(attr.DependsOn))
                descriptor.DependsOn = attr.DependsOn;

            if (prop.PropertyType.IsEnum)
                descriptor.EnumValues = Enum.GetNames(prop.PropertyType).ToList();

            return descriptor;
        }

        private static string ResolveTypeName(Type t)
        {
            if (t.IsEnum) return "enum";
            if (t == typeof(bool)) return "bool";
            if (t == typeof(int) || t == typeof(long)) return "int";
            if (t == typeof(double) || t == typeof(float)) return "double";
            if (t == typeof(string)) return "string";
            return t.Name;
        }
    }

    /// <summary>
    /// 단일 도구의 튜닝 가능 파라미터 스키마.
    /// </summary>
    public class ToolParameterSchema
    {
        [JsonPropertyName("toolType")]
        public string ToolType { get; set; } = string.Empty;

        [JsonPropertyName("parameters")]
        public List<ParameterDescriptor> Parameters { get; set; } = new();
    }

    /// <summary>
    /// 개별 파라미터 설명자. LLM 친화적 필드 구성.
    /// </summary>
    public class ParameterDescriptor
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;

        [JsonPropertyName("description")]
        public string Description { get; set; } = string.Empty;

        [JsonPropertyName("tier")]
        public string Tier { get; set; } = string.Empty;

        [JsonPropertyName("min"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public double? Min { get; set; }

        [JsonPropertyName("max"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public double? Max { get; set; }

        [JsonPropertyName("defaultHint"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? DefaultHint { get; set; }

        [JsonPropertyName("dependsOn"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? DependsOn { get; set; }

        [JsonPropertyName("enumValues"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<string>? EnumValues { get; set; }
    }
}
