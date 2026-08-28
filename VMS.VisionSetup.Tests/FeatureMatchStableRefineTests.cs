using System.Collections.Generic;
using System.Linq;
using OpenCvSharp;
using VMS.VisionSetup.VisionTools.PatternMatching;
using Xunit;

namespace VMS.VisionSetup.Tests
{
    /// <summary>
    /// FeatureMatch 안정 특징 정제(다중 이미지 자동 마스크) + Top-K 투표 후보 NMS 테스트.
    /// 정제 원리: 샘플 이미지들에서 패턴 자세를 찾아 템플릿 엣지별 그래디언트 일치를
    /// 검증 — 이미지마다 흔들리는 엣지(그림자 외곽·반사)는 TrainMask 에 자동 반영되어
    /// 재학습에서 탈락하고, 객체 자신의 윤곽은 자세와 함께 움직여 유지된다.
    /// </summary>
    public class FeatureMatchStableRefineTests
    {
        // ── Top-K NMS 후보 선별 (순수 함수) ──

        [Fact]
        public void SelectTopKCandidates_PicksSpatiallySeparatedPeaks_VoteDescending()
        {
            var all = new List<(double cx, double cy, double angle, int votes)>
            {
                (10, 10, 0, 50),
                (100, 100, 5, 90),
                (200, 30, -3, 70),
                (300, 300, 1, 10),
            };

            var top = FeatureMatchTool.SelectTopKCandidates(all, 3, 20);

            Assert.Equal(3, top.Count);
            Assert.Equal(90, top[0].votes);
            Assert.Equal(70, top[1].votes);
            Assert.Equal(50, top[2].votes);
        }

        [Fact]
        public void SelectTopKCandidates_MergesNearbyPeaks_KeepsBestOnly()
        {
            // 같은 위치 주변의 다른 각도 응답들은 하나로 병합되어야 함
            var all = new List<(double cx, double cy, double angle, int votes)>
            {
                (100, 100, 0, 90),
                (102, 101, 4, 85),   // 90짜리와 3px 거리 — 같은 인스턴스
                (104, 99, -4, 80),   // 역시 근접
                (250, 250, 0, 40),
            };

            var top = FeatureMatchTool.SelectTopKCandidates(all, 3, 20);

            Assert.Equal(2, top.Count);
            Assert.Equal(90, top[0].votes);
            Assert.Equal(40, top[1].votes);
        }

        [Fact]
        public void SelectTopKCandidates_IgnoresZeroVotes()
        {
            var all = new List<(double cx, double cy, double angle, int votes)>
            {
                (10, 10, 0, 0),
                (50, 50, 0, -1),
            };

            Assert.Empty(FeatureMatchTool.SelectTopKCandidates(all, 3, 10));
        }

        // ── 안정 특징 정제 (합성 이미지) ──

        /// <summary>본체(흰 사각형) 중앙 + 그림자 역할(회색 사각형) 우측의 템플릿.</summary>
        private static readonly Rect TplBody = new(30, 30, 60, 60);
        private static readonly Rect TplShadow = new(95, 45, 20, 20);

        private static Mat CreateTemplate()
        {
            var image = new Mat(120, 120, MatType.CV_8UC1, Scalar.Black);
            Cv2.Rectangle(image, TplBody, Scalar.White, -1);
            Cv2.Rectangle(image, TplShadow, new Scalar(120), -1);
            return image;
        }

        /// <summary>240x240 샘플 — 본체는 (90,90) 고정, 그림자는 위치 지정(없으면 생략).</summary>
        private static Mat CreateSample(Rect? shadow)
        {
            var image = new Mat(240, 240, MatType.CV_8UC1, Scalar.Black);
            Cv2.Rectangle(image, new Rect(90, 90, 60, 60), Scalar.White, -1);
            if (shadow is { } s)
                Cv2.Rectangle(image, s, new Scalar(120), -1);
            return image;
        }

        private static bool HasEdgeInside(FeatureMatchModel model, Rect templateRect)
        {
            double cx = model.TemplateWidth / 2.0;
            double cy = model.TemplateHeight / 2.0;
            return model.ModelEdges.Any(e =>
            {
                double px = e.X + cx, py = e.Y + cy;
                return px >= templateRect.X && px < templateRect.X + templateRect.Width
                    && py >= templateRect.Y && py < templateRect.Y + templateRect.Height;
            });
        }

        /// <summary>그림자 사각형 주변(엣지 포함) 검사 영역.</summary>
        private static readonly Rect ShadowArea = new(92, 42, 26, 26);

        [Fact]
        public void RefineStableFeatures_MasksMovingShadow_KeepsBody()
        {
            var tool = new FeatureMatchTool();
            using var template = CreateTemplate();
            Assert.True(tool.TrainPattern(template), "합성 템플릿 학습 실패 — 테스트 전제");
            var model = tool.Models.Last();
            Assert.True(HasEdgeInside(model, ShadowArea), "정제 전에는 그림자 엣지가 모델에 포함 (전제)");

            // 그림자가 이미지마다 다른 곳에 있는 샘플 3장 — 불안정 특징
            using var s1 = CreateSample(new Rect(30, 30, 20, 20));
            using var s2 = CreateSample(new Rect(190, 190, 20, 20));
            using var s3 = CreateSample(null);

            var result = tool.RefineStableFeatures(model, new[] { s1, s2, s3 });

            Assert.True(result.Success, result.Message);
            Assert.Equal(3, result.ImagesMatched);
            Assert.True(result.EdgesMasked > 0, "흔들리는 그림자 엣지가 마스크되어야 함");
            Assert.True(model.HasTrainMask);
            Assert.False(HasEdgeInside(model, ShadowArea), "정제 후 그림자 엣지는 모델에서 제외");
            Assert.True(HasEdgeInside(model, TplBody), "본체 윤곽 엣지는 유지");
        }

        [Fact]
        public void RefineStableFeatures_StableScene_NoChange()
        {
            var tool = new FeatureMatchTool();
            using var template = CreateTemplate();
            Assert.True(tool.TrainPattern(template));
            var model = tool.Models.Last();

            // 그림자가 템플릿과 같은 상대 위치(중심 +35,-15)에 있는 샘플 — 안정 특징.
            // 본체 중심 (120,120) 기준 그림자 좌상단 = (120+35, 120-15) = (155, 105)
            using var s1 = CreateSample(new Rect(155, 105, 20, 20));
            using var s2 = CreateSample(new Rect(155, 105, 20, 20));

            var result = tool.RefineStableFeatures(model, new[] { s1, s2 });

            Assert.True(result.Success, result.Message);
            Assert.Equal(0, result.EdgesMasked);
            Assert.False(model.HasTrainMask);
            Assert.True(HasEdgeInside(model, ShadowArea), "안정적인 그림자 엣지는 유지");
        }

        [Fact]
        public void RefineStableFeatures_NoMatchInSamples_FailsWithoutChange()
        {
            var tool = new FeatureMatchTool();
            using var template = CreateTemplate();
            Assert.True(tool.TrainPattern(template));
            var model = tool.Models.Last();
            int edgesBefore = model.ModelEdges.Count;

            using var empty1 = new Mat(240, 240, MatType.CV_8UC1, Scalar.Black);
            using var empty2 = new Mat(240, 240, MatType.CV_8UC1, Scalar.Black);

            var result = tool.RefineStableFeatures(model, new[] { empty1, empty2 });

            Assert.False(result.Success);
            Assert.Equal(0, result.ImagesMatched);
            Assert.False(model.HasTrainMask);
            Assert.Equal(edgesBefore, model.ModelEdges.Count);
        }

        [Fact]
        public void RefineStableFeatures_UntrainedModel_Fails()
        {
            var tool = new FeatureMatchTool();
            var model = new FeatureMatchModel();
            using var img = new Mat(64, 64, MatType.CV_8UC1, Scalar.Black);

            var result = tool.RefineStableFeatures(model, new[] { img });

            Assert.False(result.Success);
        }
    }
}
