using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace VMS.VisionSetup.Models
{
    public class SLMActionModel
    {
        [JsonPropertyName("Action")]
        public string Action { get; set; } = "Create";

        [JsonPropertyName("RecipeName")]
        public string? RecipeName { get; set; }

        [JsonPropertyName("Sequence")]
        public List<SLMToolItem>? Sequence { get; set; }

        [JsonPropertyName("Changes")]
        public List<SLMChangeItem>? Changes { get; set; }
    }

    public class SLMToolItem
    {
        [JsonPropertyName("ToolType")]
        public string ToolType { get; set; } = string.Empty;

        [JsonPropertyName("ToolName")]
        public string ToolName { get; set; } = string.Empty;

        [JsonPropertyName("InputSource")]
        public string InputSource { get; set; } = "Camera";

        [JsonPropertyName("Description")]
        public string? Description { get; set; }
    }

    public class SLMChangeItem
    {
        [JsonPropertyName("Operation")]
        public string Operation { get; set; } = string.Empty;

        [JsonPropertyName("SourceTool")]
        public string? SourceTool { get; set; }

        [JsonPropertyName("TargetTool")]
        public string? TargetTool { get; set; }

        [JsonPropertyName("ConnectionType")]
        public string? ConnectionType { get; set; }

        [JsonPropertyName("ToolType")]
        public string? ToolType { get; set; }

        [JsonPropertyName("ToolName")]
        public string? ToolName { get; set; }

        [JsonPropertyName("InputSource")]
        public string? InputSource { get; set; }

        [JsonPropertyName("Description")]
        public string? Description { get; set; }
    }
}
