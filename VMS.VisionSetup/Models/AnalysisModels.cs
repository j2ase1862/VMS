using System.Text.Json.Serialization;

namespace VMS.VisionSetup.Models
{
    /// <summary>
    /// ROI(또는 전체 이미지) 픽셀 강도 분포 분석 결과.
    /// SLM에게 JSON으로 직렬화되어 컨텍스트로 전달됨.
    /// </summary>
    public class HistogramAnalysis
    {
        [JsonPropertyName("mean")]
        public double Mean { get; set; }

        [JsonPropertyName("stdDev")]
        public double StdDev { get; set; }

        [JsonPropertyName("min")]
        public int Min { get; set; }

        [JsonPropertyName("max")]
        public int Max { get; set; }

        [JsonPropertyName("p10")]
        public int P10 { get; set; }

        [JsonPropertyName("p50")]
        public int P50 { get; set; }

        [JsonPropertyName("p90")]
        public int P90 { get; set; }

        /// <summary>
        /// Otsu 알고리즘이 자동 산출한 최적 임계값.
        /// </summary>
        [JsonPropertyName("otsuThreshold")]
        public int OtsuThreshold { get; set; }

        /// <summary>
        /// 히스토그램이 명확한 이중 봉우리(bimodal)를 가지면 true.
        /// true일 때 Otsu / 단순 Threshold가 잘 작동.
        /// </summary>
        [JsonPropertyName("hasBimodal")]
        public bool HasBimodal { get; set; }

        [JsonPropertyName("darkPeak"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? DarkPeak { get; set; }

        [JsonPropertyName("lightPeak"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? LightPeak { get; set; }

        /// <summary>
        /// ROI 픽셀 수(분석 신뢰도 가늠용).
        /// </summary>
        [JsonPropertyName("pixelCount")]
        public long PixelCount { get; set; }
    }

    /// <summary>
    /// ROI(또는 전체 이미지) 엣지 분포 분석 결과.
    /// </summary>
    public class EdgeAnalysis
    {
        /// <summary>
        /// 엣지 픽셀 비율 (0~1). Canny 결과 기준.
        /// </summary>
        [JsonPropertyName("edgeDensity")]
        public double EdgeDensity { get; set; }

        /// <summary>
        /// 그래디언트 크기의 평균. EdgeThreshold 추정에 사용.
        /// </summary>
        [JsonPropertyName("meanGradientMagnitude")]
        public double MeanGradientMagnitude { get; set; }

        /// <summary>
        /// 그래디언트 크기의 표준편차. ImageDependent 노이즈 가늠.
        /// </summary>
        [JsonPropertyName("gradientStdDev")]
        public double GradientStdDev { get; set; }

        /// <summary>
        /// 가장 두드러진 엣지 방향(도, 0~180). 직선/라인 검출 힌트.
        /// </summary>
        [JsonPropertyName("dominantAngleDeg")]
        public double DominantAngleDeg { get; set; }

        /// <summary>
        /// 권장 Canny 하한(P70 의 그래디언트 크기 기준).
        /// </summary>
        [JsonPropertyName("suggestedCannyLow")]
        public int SuggestedCannyLow { get; set; }

        /// <summary>
        /// 권장 Canny 상한(SuggestedCannyLow * 3).
        /// </summary>
        [JsonPropertyName("suggestedCannyHigh")]
        public int SuggestedCannyHigh { get; set; }
    }

    /// <summary>
    /// 분석 함수의 묶음 결과. SLM에 컨텍스트로 줄 때 비어 있지 않은 필드만 직렬화.
    /// </summary>
    public class ImageAnalysisBundle
    {
        [JsonPropertyName("histogram"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public HistogramAnalysis? Histogram { get; set; }

        [JsonPropertyName("edges"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public EdgeAnalysis? Edges { get; set; }

        /// <summary>
        /// 어떤 ROI에서 분석했는지(컨텍스트용). null이면 전체 이미지.
        /// </summary>
        [JsonPropertyName("roi"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? RoiSummary { get; set; }
    }
}
