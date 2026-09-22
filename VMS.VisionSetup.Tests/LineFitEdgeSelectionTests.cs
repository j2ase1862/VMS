using System;
using System.Collections.Generic;
using OpenCvSharp;
using VMS.VisionSetup.VisionTools.Measurement;
using Xunit;

namespace VMS.VisionSetup.Tests
{
    /// <summary>
    /// LineFitTool 의 Edge Selection 이 "같은 탐색선 안의 다른 강한 엣지" 에 흔들리지 않는지 —
    /// 현장 보고(2026-09-22): 캘리퍼 8개 중 <b>반사광이 걸린 가운데 2개만</b> 다른 경계를 잡아
    /// 맞춰진 직선이 기울었다. 원인은 선택 전에 걸려 있던 대비 하한(최대의 50%)이었다.
    /// 강한 반사 엣지가 기준을 끌어올려 정작 찾으려던 약한 경계가 탈락하고,
    /// First 가 "첫 번째 강한 엣지" 를 고르게 된다.
    /// </summary>
    public class LineFitEdgeSelectionTests
    {
        /// <summary>
        /// 왼쪽부터: 배경(밝음) → x=60 에서 <b>약한</b> 경계 → 다시 밝아짐 →
        /// x=150 에서 <b>아주 강한</b> 경계(반사광 다음의 검정).
        /// 두 행(y=45~55)에만 강한 경계를 넣어 현장 상황(일부 캘리퍼만 반사광)을 만든다.
        /// </summary>
        private static Mat WeakThenStrongEdgeImage()
        {
            const int w = 240, h = 100;
            var m = new Mat(h, w, MatType.CV_8UC3, new Scalar(220, 220, 220));

            // 모든 행: x=60 부터 살짝 어두워지는 약한 경계 (LightToDark)
            m.Rectangle(new Rect(60, 0, w - 60, h), new Scalar(170, 170, 170), -1);

            // 가운데 두 행만: x=110~150 반사광(아주 밝음) → x=150 부터 검정 (아주 강한 LightToDark)
            m.Rectangle(new Rect(110, 44, 40, 12), new Scalar(255, 255, 255), -1);
            m.Rectangle(new Rect(150, 44, w - 150, 12), new Scalar(20, 20, 20), -1);

            Cv2.GaussianBlur(m, m, new Size(7, 7), 2);
            return m;
        }

        private static LineFitTool MakeTool(LineEdgeSelectionMode mode) => new()
        {
            UseROI = true,
            ROI = new Rect(10, 10, 220, 80),
            SearchAxis = LineSearchAxis.AlongWidth,
            SelectionMode = mode,
            Polarity = EdgePolarity.LightToDark,
            NumCalipers = 9,
            MinFoundCalipers = 3,
            EdgeThreshold = 8,
            FilterHalfWidth = 2,
        };

        private static List<double> FoundX(VMS.VisionSetup.Models.VisionResult r)
        {
            var xs = new List<double>();
            foreach (var c in (List<CaliperResult>)r.Data["CaliperResults"])
                if (c.Found) xs.Add(c.EdgePoint.X);
            return xs;
        }

        [Fact]
        public void First_는_강한_엣지가_있어도_첫_경계를_잡는다()
        {
            using var img = WeakThenStrongEdgeImage();

            var result = MakeTool(LineEdgeSelectionMode.First).Execute(img);

            Assert.True(result.Success, result.Message);
            var xs = FoundX(result);
            Assert.NotEmpty(xs);

            // 모든 캘리퍼가 x≈60 의 같은 경계를 잡아야 한다 — 반사광이 걸린 행도 마찬가지.
            foreach (var x in xs)
                Assert.True(Math.Abs(x - 60) < 6, $"첫 경계(≈60)가 아니라 x={x:F1} 을 잡았다");
        }

        [Fact]
        public void Best_는_반사광_행에서_강한_엣지를_고른다()
        {
            // 대조군 — 같은 그림에서 Best 는 반사광 행의 강한 쪽(≈150)을 고르는 게 맞다.
            // (그 행만 점수가 압도적이라 직선 맞춤 자체는 실패할 수 있다 —
            //  Execute 가 최대 점수의 30% 미만 점을 버리기 때문. 여기선 선택만 본다.)
            using var img = WeakThenStrongEdgeImage();

            var result = MakeTool(LineEdgeSelectionMode.Best).Execute(img);

            Assert.Contains(FoundX(result), x => Math.Abs(x - 150) < 6);
        }
    }
}
