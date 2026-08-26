using System.Linq;
using OpenCvSharp;
using VMS.VisionSetup.Services;
using VMS.VisionSetup.VisionTools.PatternMatching;
using Xunit;

namespace VMS.VisionSetup.Tests
{
    /// <summary>
    /// FeatureMatch 학습 마스크(don't-care) 테스트 — VisionPro PatMax train-time mask 대응.
    /// 객체와 함께 학습된 그림자 윤곽이 모델에 포함되면 객체 이동/회전 시 중심이
    /// 그림자 쪽으로 끌려간다 (세연공장 실증 2026-08-26). 마스크 영역의 에지는
    /// 모델에서 제외되어야 하고, 저장/복원(재학습)·Clone 에서도 유지되어야 한다.
    /// </summary>
    public class FeatureMatchTrainMaskTests
    {
        /// <summary>본체 사각형 + (그림자 역할) 보조 사각형이 함께 있는 합성 템플릿.</summary>
        private static Mat CreateTwoSquareTemplate()
        {
            var image = new Mat(120, 120, MatType.CV_8UC1, Scalar.Black);
            Cv2.Rectangle(image, new Rect(20, 20, 40, 40), Scalar.White, -1);   // 본체
            Cv2.Rectangle(image, new Rect(75, 75, 30, 30), new Scalar(90), -1); // 그림자 역할
            return image;
        }

        private static readonly Rect ShadowMask = new(70, 70, 45, 45);

        private static bool HasEdgeInside(FeatureMatchTool tool, Rect templateRect)
        {
            var model = tool.Models.Last();
            double cx = model.TemplateWidth / 2.0;
            double cy = model.TemplateHeight / 2.0;
            return model.ModelEdges.Any(e =>
            {
                double px = e.X + cx, py = e.Y + cy;   // 중심 상대 → 템플릿 좌표
                return px >= templateRect.X && px < templateRect.X + templateRect.Width
                    && py >= templateRect.Y && py < templateRect.Y + templateRect.Height;
            });
        }

        [Fact]
        public void TrainPattern_WithMask_ExcludesMaskedEdges()
        {
            using var template = CreateTwoSquareTemplate();

            var noMask = new FeatureMatchTool();
            Assert.True(noMask.TrainPattern(template));
            Assert.True(HasEdgeInside(noMask, ShadowMask), "마스크 없으면 그림자 에지가 모델에 포함 (전제)");

            var masked = new FeatureMatchTool();
            masked.AddTrainMaskRegion(ShadowMask);
            Assert.True(masked.TrainPattern(template));
            Assert.False(HasEdgeInside(masked, ShadowMask), "마스크 영역 에지는 모델에서 제외되어야 함");
            Assert.True(HasEdgeInside(masked, new Rect(15, 15, 50, 50)), "본체 에지는 유지되어야 함");
        }

        [Fact]
        public void ClearTrainMaskRegions_RestoresUnmaskedTraining()
        {
            using var template = CreateTwoSquareTemplate();
            var tool = new FeatureMatchTool();
            tool.AddTrainMaskRegion(ShadowMask);
            Assert.True(tool.TrainPattern(template));
            Assert.False(HasEdgeInside(tool, ShadowMask));

            tool.ClearTrainMaskRegions();
            Assert.True(tool.TrainPattern(template, tool.SelectedModel));   // 재학습
            Assert.True(HasEdgeInside(tool, ShadowMask), "마스크 해제 후 재학습이면 다시 포함");
        }

        [Fact]
        public void Clone_CopiesTrainMaskRegions()
        {
            var tool = new FeatureMatchTool();
            tool.AddTrainMaskRegion(new Rect(1, 2, 3, 4));
            tool.AddTrainMaskRegion(new Rect(5, 6, 7, 8));

            var clone = (FeatureMatchTool)tool.Clone();

            Assert.Equal(2, clone.TrainMaskCount);
            Assert.Equal(new Rect(1, 2, 3, 4), clone.TrainMaskRegions[0]);
            Assert.Equal(new Rect(5, 6, 7, 8), clone.TrainMaskRegions[1]);
        }

        [Fact]
        public void SerializeRoundTrip_RestoresMaskBeforeModelRetrain()
        {
            using var template = CreateTwoSquareTemplate();
            var tool = new FeatureMatchTool();
            tool.AddTrainMaskRegion(ShadowMask);
            Assert.True(tool.TrainPattern(template));

            var config = ToolSerializer.SerializeTool(tool);
            var restored = ToolSerializer.DeserializeTool(config) as FeatureMatchTool;

            Assert.NotNull(restored);
            Assert.Equal(1, restored!.TrainMaskCount);
            Assert.Equal(ShadowMask, restored.TrainMaskRegions[0]);

            // 복원은 템플릿 이미지 재학습 경로 — 마스크가 모델 복원보다 먼저 세팅되어
            // 재학습된 모델에도 마스크가 적용되어 있어야 한다 (순서 회귀 방지)
            Assert.True(restored.Models.Count > 0, "모델이 템플릿에서 재학습되어야 함");
            Assert.False(HasEdgeInside(restored, ShadowMask), "복원 재학습에도 마스크 적용");
        }

        [Fact]
        public void SerializeRoundTrip_NoMask_HasNoMaskParameter()
        {
            using var template = CreateTwoSquareTemplate();
            var tool = new FeatureMatchTool();
            Assert.True(tool.TrainPattern(template));

            var config = ToolSerializer.SerializeTool(tool);
            Assert.False(config.Parameters.ContainsKey("TrainMaskRegions"));

            var restored = ToolSerializer.DeserializeTool(config) as FeatureMatchTool;
            Assert.NotNull(restored);
            Assert.Equal(0, restored!.TrainMaskCount);   // 기존 레시피 호환 (마스크 없음)
        }
    }
}
