using System.Linq;
using OpenCvSharp;
using VMS.VisionSetup.Services;
using VMS.VisionSetup.VisionTools.PatternMatching;
using Xunit;

namespace VMS.VisionSetup.Tests
{
    /// <summary>
    /// FeatureMatch 기준(원본) 이미지 경로 테스트 — 레시피에는 ROI 크롭 템플릿만
    /// 남으므로, Train 당시 전체 장면 PNG 경로(ReferenceImagePath)가 레시피
    /// 저장/복원과 Clone 에서 유지되어야 ROI 조정·재학습 시 장면을 되불러올 수 있다.
    /// </summary>
    public class FeatureMatchReferenceImageTests
    {
        private static Mat CreateTemplate()
        {
            var image = new Mat(100, 100, MatType.CV_8UC1, Scalar.Black);
            Cv2.Rectangle(image, new Rect(25, 25, 50, 50), Scalar.White, -1);
            return image;
        }

        [Fact]
        public void SerializeRoundTrip_PreservesReferenceImagePath()
        {
            using var template = CreateTemplate();
            var tool = new FeatureMatchTool();
            Assert.True(tool.TrainPattern(template));
            tool.ReferenceImagePath = @"C:\VMS\Recipes\RefImages\featurematch_abc.png";

            var config = ToolSerializer.SerializeTool(tool);
            var restored = ToolSerializer.DeserializeTool(config) as FeatureMatchTool;

            Assert.NotNull(restored);
            Assert.Equal(tool.ReferenceImagePath, restored!.ReferenceImagePath);
        }

        [Fact]
        public void SerializeRoundTrip_NoReferenceImage_StaysCompatible()
        {
            using var template = CreateTemplate();
            var tool = new FeatureMatchTool();
            Assert.True(tool.TrainPattern(template));

            var config = ToolSerializer.SerializeTool(tool);
            Assert.False(config.Parameters.ContainsKey("ReferenceImagePath"));   // 기존 레시피 호환

            var restored = ToolSerializer.DeserializeTool(config) as FeatureMatchTool;
            Assert.NotNull(restored);
            Assert.Null(restored!.ReferenceImagePath);
        }

        [Fact]
        public void Clone_PreservesReferenceImagePath()
        {
            using var template = CreateTemplate();
            var tool = new FeatureMatchTool();
            Assert.True(tool.TrainPattern(template));
            tool.ReferenceImagePath = @"C:\VMS\Recipes\RefImages\featurematch_abc.png";

            var clone = (FeatureMatchTool)tool.Clone();
            Assert.Equal(tool.ReferenceImagePath, clone.ReferenceImagePath);
        }
    }
}
