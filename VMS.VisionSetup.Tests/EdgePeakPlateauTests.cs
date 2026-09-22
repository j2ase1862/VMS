using System;
using OpenCvSharp;
using VMS.VisionSetup.VisionTools.Measurement;
using Xunit;

namespace VMS.VisionSetup.Tests
{
    /// <summary>
    /// 그래디언트 봉우리가 두 표본에 걸쳐 같은 값일 때도 엣지를 찾는지 —
    /// 2026-09-22 발견: 봉우리 판정이 양쪽 모두 엄격(&gt;)이라
    /// <c>… 51.8, 57.8, 57.8, 52.2 …</c> 같은 평탄부에서 두 표본 다 탈락해
    /// <b>엣지를 통째로 놓쳤다</b>. 대칭 엣지가 두 화소 사이에 놓이면 생기는,
    /// 드물지 않은 조건이다. 검출 실패가 조용해서 "측정이 안 된다"로만 보인다.
    /// </summary>
    public class EdgePeakPlateauTests
    {
        /// <summary>
        /// 좌우 대칭으로 흐려진 세로 엣지. 엣지 중심을 화소 경계에 두어
        /// 그래디언트 봉우리가 두 표본에 걸치게 만든다.
        /// </summary>
        private static Mat SymmetricEdgeImage(int w = 240, int h = 120, int edgeX = 120)
        {
            var m = new Mat(h, w, MatType.CV_8UC3, new Scalar(30, 30, 30));
            m.Rectangle(new Rect(edgeX, 0, w - edgeX, h), new Scalar(230, 230, 230), -1);
            Cv2.GaussianBlur(m, m, new Size(9, 9), 3);
            return m;
        }

        [Fact]
        public void CaliperTool_대칭_엣지를_놓치지_않는다()
        {
            using var img = SymmetricEdgeImage();
            var tool = new CaliperTool
            {
                UseROI = true,
                ROI = new Rect(20, 40, 200, 40),
                SearchAxis = CaliperSearchAxis.AlongWidth,
                Polarity = EdgePolarity.DarkToLight,
                Mode = CaliperMode.SingleEdge,
                EdgeThreshold = 20,
            };

            var result = tool.Execute(img);

            Assert.True(result.Success, result.Message);
            var edgeX = Convert.ToDouble(result.Data["EdgeX"]);
            Assert.True(Math.Abs(edgeX - 120) < 3, $"엣지 X={edgeX:F1} (기대 120 근처)");
        }

        [Fact]
        public void LineFitTool_대칭_엣지를_놓치지_않는다()
        {
            using var img = SymmetricEdgeImage();
            var tool = new LineFitTool
            {
                UseROI = true,
                ROI = new Rect(20, 20, 200, 80),
                SearchAxis = LineSearchAxis.AlongWidth,
                SelectionMode = LineEdgeSelectionMode.First,
                Polarity = EdgePolarity.DarkToLight,
                NumCalipers = 7,
                MinFoundCalipers = 3,
                EdgeThreshold = 20,
            };

            var result = tool.Execute(img);

            Assert.True(result.Success, result.Message);
            Assert.Equal(7, Convert.ToInt32(result.Data["FoundCount"]));
        }
    }
}
