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

        /// <summary>
        /// Phase 3: SLM이 ImageDependent 파라미터 결정을 위해 분석을 요청.
        /// 값이 있으면 클라이언트가 분석 실행 후 결과를 컨텍스트로 재호출.
        /// 예: ["histogram", "edges"].
        /// </summary>
        [JsonPropertyName("AnalysisRequests")]
        public List<string>? AnalysisRequests { get; set; }

        /// <summary>
        /// AnalysisRequests를 낸 사유(작업자에게 보여줄 한국어 한 줄).
        /// </summary>
        [JsonPropertyName("Reason")]
        public string? Reason { get; set; }

        /// <summary>
        /// Phase 4a: AnalysisRequests와 함께 분석할 영역을 지정.
        /// null이면 전체 이미지 분석.
        /// </summary>
        [JsonPropertyName("AnalysisRoi")]
        public RoiHint? AnalysisRoi { get; set; }
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

        /// <summary>
        /// SLM이 추정한 파라미터(PropertyName -&gt; value). 값은 JsonElement 또는 primitive.
        /// </summary>
        [JsonPropertyName("Parameters")]
        public Dictionary<string, object?>? Parameters { get; set; }

        /// <summary>
        /// Phase 4a: 사용자 의도에 따라 SLM이 지정한 ROI 힌트.
        /// 없으면 ROI 미설정(전체 이미지).
        /// </summary>
        [JsonPropertyName("RoiHint")]
        public RoiHint? RoiHint { get; set; }

        /// <summary>
        /// 단계 1 (Confidence): 파라미터별 신뢰도 레이블.
        /// 키는 PropertyName, 값은 "high" / "medium" / "low".
        /// Parameters에 있는 키만 의미가 있음. 모든 파라미터에 대해 채울 필요 없음.
        /// </summary>
        [JsonPropertyName("ParameterConfidence")]
        public Dictionary<string, string>? ParameterConfidence { get; set; }
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

        /// <summary>
        /// AddTool 또는 SetParameters 연산 시 적용할 파라미터.
        /// </summary>
        [JsonPropertyName("Parameters")]
        public Dictionary<string, object?>? Parameters { get; set; }

        /// <summary>
        /// AddTool 시 적용할 ROI 힌트.
        /// </summary>
        [JsonPropertyName("RoiHint")]
        public RoiHint? RoiHint { get; set; }

        /// <summary>
        /// AddTool / SetParameters 시 파라미터 신뢰도.
        /// </summary>
        [JsonPropertyName("ParameterConfidence")]
        public Dictionary<string, string>? ParameterConfidence { get; set; }
    }

    /// <summary>
    /// SLM이 도구의 검사 영역을 지정하는 힌트.
    /// Strategy에 따라 추가 필드가 의미를 가짐.
    /// </summary>
    public class RoiHint
    {
        /// <summary>
        /// FullImage: 전체 이미지(UseROI=false 또는 ROI=전체).
        /// CenterRect: 이미지 중앙 사각형. MarginPercent로 가장자리 여백 비율 지정.
        /// Custom: X/Y/Width/Height 명시.
        /// Inherit: 이전 도구의 ROI를 그대로 사용(현재 미지원 → FullImage로 폴백).
        /// </summary>
        [JsonPropertyName("Strategy")]
        public string Strategy { get; set; } = "FullImage";

        [JsonPropertyName("X"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? X { get; set; }

        [JsonPropertyName("Y"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? Y { get; set; }

        [JsonPropertyName("Width"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? Width { get; set; }

        [JsonPropertyName("Height"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? Height { get; set; }

        /// <summary>
        /// CenterRect 전략에서 가장자리 여백 비율(%). 20이면 좌우상하 20%씩 잘라낸 중앙 60% 영역.
        /// </summary>
        [JsonPropertyName("MarginPercent"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? MarginPercent { get; set; }
    }
}
