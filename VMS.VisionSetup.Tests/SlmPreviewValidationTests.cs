using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using VMS.VisionSetup.Services;
using Xunit;

namespace VMS.VisionSetup.Tests
{
    /// <summary>
    /// VS-36: 레시피 봇 미리보기가 모델 답을 그대로 쓰던 문제 —
    /// ① Parameters 안의 신뢰도 표를 파라미터처럼 보이지 않는다
    /// ② 도구에 없는 파라미터는 "적용하지 않음" 으로 표시한다.
    /// </summary>
    public class SlmPreviewValidationTests
    {
        private static string Preview(Dictionary<string, object?> parameters, string? toolType)
        {
            var sb = new StringBuilder();
            SLMToolGeneratorService.AppendParametersPreview(sb, parameters, indent: "", toolType: toolType);
            return sb.ToString();
        }

        [Fact]
        public void NestedConfidence_IsNotListedAsParameter_ButUsedAsConfidence()
        {
            using var doc = JsonDocument.Parse("""{ "MinArea": "medium" }""");
            var text = Preview(new Dictionary<string, object?>
            {
                ["MinArea"] = 50,
                ["ParameterConfidence"] = doc.RootElement.Clone(),
            }, "BlobTool");

            Assert.DoesNotContain("ParameterConfidence", text);
            Assert.Contains("MinArea = 50  · 보통", text);
        }

        [Fact]
        public void UnknownParameter_IsMarkedNotApplied()
        {
            var text = Preview(new Dictionary<string, object?>
            {
                ["MinArea"] = 50,
                ["MinDiameter"] = 3,
            }, "BlobTool");

            Assert.Contains("MinDiameter = 3  ✕ 이 도구에 없는 파라미터", text);
            Assert.DoesNotContain("MinArea = 50  ✕", text);
        }

        [Fact]
        public void UnknownToolType_SkipsCheck()
        {
            var text = Preview(new Dictionary<string, object?> { ["Anything"] = 1 }, null);
            Assert.DoesNotContain("✕", text);
        }
    }
}
