using System.Linq;
using OpenCvSharp;
using VMS.VisionSetup.Models;
using VMS.VisionSetup.Services;
using VMS.VisionSetup.VisionTools.DeepLearning;
using Xunit;

namespace VMS.VisionSetup.Tests
{
    /// <summary>
    /// CompositeOverlayBuilder — 한 스텝 합성 이미지에서 도구별 머리표(판정 배지·요약 글자)가
    /// 같은 자리에 포개지지 않고 아래로 쌓이는지, 판정 테두리가 스텝 판정 색 하나로 남는지.
    /// (2026-10-24 매뉴얼 캡처에서 발견: 마지막 도구 배지만 남아 스텝 NG 인데 배지는 OK 로 보임)
    /// </summary>
    public class CompositeOverlayBuilderTests
    {
        private static readonly Scalar Green = new(0, 255, 0);
        private static readonly Scalar Red = new(0, 0, 255);

        private static Mat Gray(int w = 400, int h = 300) => new Mat(h, w, MatType.CV_8UC3, new Scalar(90, 90, 90));

        /// <summary>왼쪽 위 (8,8,64,30) 에 배지를 칠한 오버레이 — Geometry 판정 배지와 같은 자리.</summary>
        private static VisionResult Badge(Mat baseImage, Scalar color, bool success)
        {
            var overlay = baseImage.Clone();
            var rect = new Rect(8, 8, 64, 30);
            Cv2.Rectangle(overlay, rect, color, -1);
            var r = new VisionResult { Success = success, OverlayImage = overlay };
            r.HeaderRegions.Add(rect);
            return r;
        }

        private static int CountColor(Mat img, Rect area, Scalar color)
        {
            using var sub = new Mat(img, area);
            using var mask = new Mat();
            var low = new Scalar(color.Val0 - 10, color.Val1 - 10, color.Val2 - 10);
            var high = new Scalar(color.Val0 + 10, color.Val1 + 10, color.Val2 + 10);
            Cv2.InRange(sub, low, high, mask);
            return Cv2.CountNonZero(mask);
        }

        [Fact]
        public void SecondHeader_IsStackedBelowFirst_NotOverwritten()
        {
            using var baseImage = Gray();
            var first = Badge(baseImage, Green, true);
            var second = Badge(baseImage, Red, false);

            using var builder = new CompositeOverlayBuilder();
            builder.Add(first, baseImage);
            builder.Add(second, baseImage);
            using var composite = builder.Build(stepPass: false)!;

            // 첫 배지는 제자리(초록)에 남고
            Assert.Equal(64 * 30, CountColor(composite, new Rect(8, 8, 64, 30), Green));
            // 두 번째 배지(빨강)는 첫 배지 아래로 간격을 두고 쌓인다
            int y = 8 + 30 + CompositeOverlayBuilder.HeaderGap;
            Assert.Equal(64 * 30, CountColor(composite, new Rect(8, y, 64, 30), Red));

            first.ReleaseMats();
            second.ReleaseMats();
        }

        [Fact]
        public void ThirdHeader_StacksUnderSecond()
        {
            using var baseImage = Gray();
            var results = new[]
            {
                Badge(baseImage, Green, true),
                Badge(baseImage, Red, false),
                Badge(baseImage, new Scalar(255, 0, 0), true),
            };

            using var builder = new CompositeOverlayBuilder();
            foreach (var r in results) builder.Add(r, baseImage);
            using var composite = builder.Build(stepPass: false)!;

            int step = 30 + CompositeOverlayBuilder.HeaderGap;
            Assert.Equal(64 * 30, CountColor(composite, new Rect(8, 8 + 2 * step, 64, 30), new Scalar(255, 0, 0)));
            foreach (var r in results) r.ReleaseMats();
        }

        [Fact]
        public void NonHeaderGraphics_StayInPlace()
        {
            using var baseImage = Gray();
            var first = Badge(baseImage, Green, true);
            var second = Badge(baseImage, Red, false);
            Cv2.Circle(second.OverlayImage!, new Point(300, 200), 20, new Scalar(255, 255, 0), -1);

            using var builder = new CompositeOverlayBuilder();
            builder.Add(first, baseImage);
            builder.Add(second, baseImage);
            using var composite = builder.Build(stepPass: false)!;

            Assert.True(CountColor(composite, new Rect(290, 190, 20, 20), new Scalar(255, 255, 0)) > 300);
            first.ReleaseMats();
            second.ReleaseMats();
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void VerdictFrame_UsesStepVerdict_NotLastTool(bool stepPass)
        {
            using var baseImage = Gray();
            var ensembleOr = RunEnsemble(baseImage, EnsembleMode.Or);   // OK — 초록 테두리
            var ensembleAnd = RunEnsemble(baseImage, EnsembleMode.And); // NG — 빨강 테두리
            Assert.True(ensembleOr.Success);
            Assert.False(ensembleAnd.Success);
            Assert.Equal(4, ensembleAnd.VerdictFrameThickness);
            Assert.Single(ensembleAnd.HeaderRegions);

            using var builder = new CompositeOverlayBuilder();
            builder.Add(ensembleAnd, baseImage);
            builder.Add(ensembleOr, baseImage); // 마지막 도구는 OK
            using var composite = builder.Build(stepPass)!;

            var expected = stepPass ? Green : Red;
            var edge = new Rect(0, 150, 2, 20);
            Assert.Equal(edge.Width * edge.Height, CountColor(composite, edge, expected));

            // 두 앙상블 요약 글자가 겹치지 않고 각각 남는다 — 빨강(첫째) 위, 초록(둘째) 아래
            var firstHeader = ensembleAnd.HeaderRegions.Single();
            Assert.True(CountColor(composite, firstHeader, Red) > 50);
            var below = new Rect(firstHeader.X, firstHeader.Bottom + CompositeOverlayBuilder.HeaderGap,
                firstHeader.Width, firstHeader.Height);
            Assert.True(CountColor(composite, below, Green) > 50);
            Assert.Equal(0, CountColor(composite, new Rect(firstHeader.X + 4, firstHeader.Y + 2,
                firstHeader.Width - 8, firstHeader.Height - 4), Green));

            ensembleOr.ReleaseMats();
            ensembleAnd.ReleaseMats();
        }

        [Fact]
        public void HeaderText_ReplacesSymbolsTheImageFontCannotDraw()
        {
            Assert.Equal("score=0.682 (det_fail=0.00x0.50) - th >= 0.50",
                VisionToolBase.ToOverlayAscii("score=0.682 (det_fail=0.00×0.50) — th ≥ 0.50"));
            Assert.Equal("plain ASCII", VisionToolBase.ToOverlayAscii("plain ASCII"));
        }

        [Theory]
        [InlineData(true, false, "CONSENSUS: disagree 1/2 passed - review")]
        [InlineData(true, true, "CONSENSUS: all passed")]
        [InlineData(false, false, "CONSENSUS: all failed")]
        public void EnsembleConsensus_OverlayTextIsAscii_MessageUnchanged(bool a, bool b, string overlayText)
        {
            using var baseImage = Gray(1400, 200);
            var tool = new EnsembleTool { Mode = EnsembleMode.Consensus, DrawOverlay = true, OverlayBaseImage = baseImage };
            tool.SourceResults.Add(new SourceToolResultEx { ToolName = "A", ToolType = "DetectionTool", Success = a });
            tool.SourceResults.Add(new SourceToolResultEx { ToolName = "B", ToolType = "AnomalyTool", Success = b });
            var result = tool.Execute(baseImage);

            // 메시지(한글)는 그대로 — 기대 결과표 · Run Results 가 이 문구를 쓴다
            Assert.Matches("CONSENSUS: (모델 간 판정 불일치 — 검토 필요|전원 정상|전원 이상)", result.Message);
            // 이미지에 그린 글자 폭 = 영문 문구의 폭
            int expectedWidth = Cv2.GetTextSize($"{(a && b ? "OK" : "NG")}: {overlayText}",
                HersheyFonts.HersheySimplex, 0.8, 2, out _).Width + 4;
            Assert.Equal(expectedWidth, Assert.Single(result.HeaderRegions).Width);
            result.ReleaseMats();
        }

        [Fact]
        public void NoOverlay_BuildsNull()
        {
            using var builder = new CompositeOverlayBuilder();
            builder.Add(new VisionResult { Success = true }, null);
            Assert.Null(builder.Build(stepPass: true));
        }

        private static VisionResult RunEnsemble(Mat baseImage, EnsembleMode mode)
        {
            var tool = new EnsembleTool { Mode = mode, DrawOverlay = true, OverlayBaseImage = baseImage };
            tool.SourceResults.Add(new SourceToolResultEx { ToolName = "Det", ToolType = "DetectionTool", Success = true });
            tool.SourceResults.Add(new SourceToolResultEx { ToolName = "Ano", ToolType = "AnomalyTool", Success = false });
            return tool.Execute(baseImage);
        }
    }
}
