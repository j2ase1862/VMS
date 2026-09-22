using System;
using OpenCvSharp;
using VMS.VisionSetup.Services;
using VMS.VisionSetup.VisionTools.Measurement;
using Xunit;

namespace VMS.VisionSetup.Tests
{
    /// <summary>
    /// LineFitTool 의 탐색 방향과 엣지 선택 검증 —
    /// 현장 보고(2026-09-22): RectAffine ROI 를 그리면 화살표는 가로를 가리키는데
    /// 실제로는 세로로 훑었다. 실행이 ROI 의 <b>긴 변</b>을 기준선으로 삼았기 때문이다
    /// (가로가 긴 ROI → 세로 탐색). 이제 화살표 방향(SearchAxis)이 곧 탐색 방향이다.
    /// </summary>
    public class LineFitSearchAxisTests
    {
        /// <summary>
        /// 왼쪽이 검고 x=120 부터 흰 세로 엣지 하나. 세로선을 맞추는 그림.
        /// 살짝 흐리게 만든다 — 완벽한 계단은 미분 커널에서 평탄부를 만들어
        /// 국소 최대(&gt; 비교)에 걸리지 않는다. 실제 영상에는 없는 조건이다.
        /// </summary>
        private static Mat VerticalEdgeImage(int w = 400, int h = 300, int edgeX = 120)
        {
            var m = new Mat(h, w, MatType.CV_8UC3, new Scalar(30, 30, 30));
            m.Rectangle(new Rect(edgeX, 0, w - edgeX, h), new Scalar(230, 230, 230), -1);
            Cv2.GaussianBlur(m, m, new Size(9, 9), 3);
            return m;
        }

        private static LineFitTool MakeTool(LineSearchAxis axis)
        {
            // 가로가 긴 ROI — 구버전이라면 "긴 변 = 기준선" 이라 세로로 훑었다.
            var tool = new LineFitTool
            {
                UseROI = true,
                ROI = new Rect(40, 80, 200, 140),
                ROIAngle = 0.0001,          // 회전 ROI 경로를 타게 하는 최소 각도
                ROICenterX = 140,
                ROICenterY = 150,
                SearchAxis = axis,
                SelectionMode = LineEdgeSelectionMode.First,
                Polarity = EdgePolarity.DarkToLight,
                NumCalipers = 7,
                MinFoundCalipers = 3,
                EdgeThreshold = 20,
            };
            return tool;
        }

        [Fact]
        public void AlongWidth_는_가로로_훑어_세로_엣지를_찾는다()
        {
            using var img = VerticalEdgeImage();
            var result = MakeTool(LineSearchAxis.AlongWidth).Execute(img);

            Assert.True(result.Success, result.Message);
            // 세로선을 맞췄으면 각도가 ±90° 근처여야 한다.
            var angle = Math.Abs(Convert.ToDouble(result.Data["LineAngle"]));
            Assert.True(Math.Abs(angle - 90) < 5, $"세로선이어야 하는데 각도 {angle:F1}°");
        }

        [Fact]
        public void 구버전_LongerSide_는_긴_변을_기준선으로_삼는다()
        {
            // 가로가 긴 ROI 라 세로로 훑는다 → 세로 엣지는 못 찾는다.
            // 기존 레시피의 동작을 그대로 보존하는지 확인하는 대조군.
            using var img = VerticalEdgeImage();
            var tool = MakeTool(LineSearchAxis.LongerSide);

            var result = tool.Execute(img);

            // 찾더라도 세로선은 아니다 (균일한 영역을 세로로 훑으므로).
            if (result.Success)
            {
                var angle = Math.Abs(Convert.ToDouble(result.Data["LineAngle"]));
                Assert.False(Math.Abs(angle - 90) < 5, "구버전 경로인데 세로선을 찾았다");
            }
        }

        [Fact]
        public void 설정이_레시피_왕복에서_유지된다()
        {
            var tool = MakeTool(LineSearchAxis.AlongHeight);
            tool.SelectionMode = LineEdgeSelectionMode.Last;

            var restored = ToolSerializer.DeserializeTool(ToolSerializer.SerializeTool(tool)) as LineFitTool;

            Assert.NotNull(restored);
            Assert.Equal(LineSearchAxis.AlongHeight, restored!.SearchAxis);
            Assert.Equal(LineEdgeSelectionMode.Last, restored.SelectionMode);
        }

        [Fact]
        public void 설정이_없던_레시피는_구버전_동작으로_읽힌다()
        {
            // 새 기본값(화살표 방향 탐색·First)을 소급 적용하면 기존 레시피의
            // 측정값이 조용히 달라진다 — 키가 없으면 구버전 값으로 읽어야 한다.
            var config = ToolSerializer.SerializeTool(MakeTool(LineSearchAxis.AlongWidth));
            config.Parameters.Remove("SearchAxis");
            config.Parameters.Remove("SelectionMode");

            var restored = ToolSerializer.DeserializeTool(config) as LineFitTool;

            Assert.Equal(LineSearchAxis.LongerSide, restored!.SearchAxis);
            Assert.Equal(LineEdgeSelectionMode.ClosestToCenter, restored.SelectionMode);
        }

        [Fact]
        public void 새_도구의_기본값은_화살표_방향_탐색과_First()
        {
            var tool = new LineFitTool();

            Assert.Equal(LineSearchAxis.AlongWidth, tool.SearchAxis);
            Assert.Equal(LineEdgeSelectionMode.First, tool.SelectionMode);
        }
    }
}
