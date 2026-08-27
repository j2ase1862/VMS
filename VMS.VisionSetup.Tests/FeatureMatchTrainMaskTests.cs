using System.Linq;
using OpenCvSharp;
using VMS.VisionSetup.Services;
using VMS.VisionSetup.VisionTools.PatternMatching;
using Xunit;

namespace VMS.VisionSetup.Tests
{
    /// <summary>
    /// FeatureMatch 학습 마스크(don't-care 비트맵) 테스트 — VisionPro PatMax
    /// train-time mask 페인팅 대응. 그림자·가변 각인·정반사처럼 이미지마다 달라지는
    /// 요소가 모델에 포함되면 객체 이동/회전 시 중심이 끌려간다 (세연공장 실증
    /// 2026-08-26). 마스크(8UC1, 255=제외) 영역의 에지는 모델에서 제외되어야 하고,
    /// 모델별 저장/복원(재학습)·Clone 에서도 유지되어야 한다.
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

        private static readonly Rect ShadowArea = new(70, 70, 45, 45);

        /// <summary>그림자 영역만 255 로 칠한 마스크 (편집기 브러시/사각형 결과에 해당).</summary>
        private static Mat CreateShadowMask()
        {
            var mask = new Mat(120, 120, MatType.CV_8UC1, Scalar.Black);
            Cv2.Rectangle(mask, ShadowArea, Scalar.White, -1);
            return mask;
        }

        private static bool HasEdgeInside(FeatureMatchModel model, Rect templateRect)
        {
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
            using var mask = CreateShadowMask();

            var noMask = new FeatureMatchTool();
            Assert.True(noMask.TrainPattern(template));
            Assert.True(HasEdgeInside(noMask.Models.Last(), ShadowArea), "마스크 없으면 그림자 에지가 모델에 포함 (전제)");

            var masked = new FeatureMatchTool();
            Assert.True(masked.TrainPattern(template, null, mask));
            var model = masked.Models.Last();
            Assert.True(model.HasTrainMask);
            Assert.False(HasEdgeInside(model, ShadowArea), "마스크 영역 에지는 모델에서 제외되어야 함");
            Assert.True(HasEdgeInside(model, new Rect(15, 15, 50, 50)), "본체 에지는 유지되어야 함");
        }

        [Fact]
        public void Retrain_KeepsModelMask_AndClearRestores()
        {
            using var template = CreateTwoSquareTemplate();
            using var mask = CreateShadowMask();

            var tool = new FeatureMatchTool();
            Assert.True(tool.TrainPattern(template, null, mask));
            var model = tool.Models.Last();

            // 마스크 인자 없이 재학습해도 모델에 저장된 마스크가 유지 적용
            Assert.True(tool.TrainPattern(template, model));
            Assert.True(model.HasTrainMask);
            Assert.False(HasEdgeInside(model, ShadowArea), "재학습에도 모델 마스크 유지 적용");

            // 마스크 해제 후 재학습이면 다시 포함
            model.TrainMask = null;
            Assert.True(tool.TrainPattern(template, model));
            Assert.True(HasEdgeInside(model, ShadowArea), "마스크 해제 후 재학습이면 다시 포함");
        }

        [Fact]
        public void TrainPattern_SizeMismatchedMask_IsIgnored()
        {
            using var template = CreateTwoSquareTemplate();
            using var wrongSize = new Mat(60, 60, MatType.CV_8UC1, Scalar.White);   // 전부 제외 크기 불일치

            var tool = new FeatureMatchTool();
            Assert.True(tool.TrainPattern(template, null, wrongSize), "크기 불일치 마스크는 무시하고 정상 학습");
            Assert.True(HasEdgeInside(tool.Models.Last(), ShadowArea), "무시된 마스크는 에지에 영향 없음");
        }

        [Fact]
        public void Clone_CopiesPerModelTrainMask()
        {
            using var template = CreateTwoSquareTemplate();
            using var mask = CreateShadowMask();
            var tool = new FeatureMatchTool();
            Assert.True(tool.TrainPattern(template, null, mask));

            var clone = (FeatureMatchTool)tool.Clone();

            var clonedModel = clone.Models.Last();
            Assert.True(clonedModel.HasTrainMask);
            Assert.NotSame(tool.Models.Last().TrainMask, clonedModel.TrainMask);   // 깊은 복사
            Assert.Equal(255, clonedModel.TrainMask!.At<byte>(80, 80));            // 그림자 영역 유지
        }

        [Fact]
        public void SerializeRoundTrip_RestoresMaskAppliedModel()
        {
            using var template = CreateTwoSquareTemplate();
            using var mask = CreateShadowMask();
            var tool = new FeatureMatchTool();
            Assert.True(tool.TrainPattern(template, null, mask));

            var config = ToolSerializer.SerializeTool(tool);
            var restored = ToolSerializer.DeserializeTool(config) as FeatureMatchTool;

            Assert.NotNull(restored);
            var model = restored!.Models.LastOrDefault();
            Assert.NotNull(model);
            Assert.True(model!.HasTrainMask, "마스크 비트맵이 함께 복원되어야 함");

            // 복원은 템플릿 재학습 경로 — 마스크가 재학습에 함께 적용되어 있어야 한다
            Assert.False(HasEdgeInside(model, ShadowArea), "복원 재학습에도 마스크 적용");
        }

        [Fact]
        public void SerializeRoundTrip_NoMask_StaysCompatible()
        {
            using var template = CreateTwoSquareTemplate();
            var tool = new FeatureMatchTool();
            Assert.True(tool.TrainPattern(template));

            var config = ToolSerializer.SerializeTool(tool);
            var restored = ToolSerializer.DeserializeTool(config) as FeatureMatchTool;

            Assert.NotNull(restored);
            Assert.False(restored!.Models.Last().HasTrainMask);   // 기존 레시피 호환 (마스크 없음)
        }
    }
}
