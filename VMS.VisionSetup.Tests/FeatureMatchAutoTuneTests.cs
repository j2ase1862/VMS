using OpenCvSharp;
using System;
using VMS.VisionSetup.VisionTools.PatternMatching;
using Xunit;

namespace VMS.VisionSetup.Tests
{
    /// <summary>
    /// Auto Tune 의 MaxModelPoints 산정 규칙 회귀 테스트.
    /// 종전 밀도(엣지/면적) 기준은 대형 템플릿 + 가는 윤곽에서 항상 최저 등급(100점)이
    /// 나와 수천 픽셀 윤곽을 100점으로 솎아냈다 (실증 T-피팅 483×355 케이스).
    /// 현재 규칙: 검출 엣지 8px당 1점, 100~500 클램프.
    /// </summary>
    public class FeatureMatchAutoTuneTests
    {
        /// <summary>AutoTuneParameters 와 동일한 절차로 기대 포인트 수를 재계산.</summary>
        private static int ExpectedPoints(Mat template, double cannyLow, double cannyHigh)
        {
            using var gray = template.Channels() > 1
                ? template.CvtColor(ColorConversionCodes.BGR2GRAY)
                : template.Clone();
            using var edges = gray.Canny(cannyLow, cannyHigh);
            return Math.Clamp(Cv2.CountNonZero(edges) / 8, 100, 500);
        }

        [Fact]
        public void AutoTune_LargeSparseSilhouette_AssignsMoreThanFloor()
        {
            // 대형 템플릿 + 긴 윤곽 (면적 대비 밀도는 낮음) — 종전 규칙이면 100점으로
            // 떨어지던 형태. 총 윤곽 길이를 충분히 크게 만든다.
            using var tpl = new Mat(360, 480, MatType.CV_8UC1, Scalar.All(64));
            Cv2.Rectangle(tpl, new Rect(30, 30, 420, 120), Scalar.All(200), -1);
            Cv2.Rectangle(tpl, new Rect(60, 190, 150, 130), Scalar.All(200), -1);
            Cv2.Rectangle(tpl, new Rect(270, 190, 150, 130), Scalar.All(200), -1);
            Cv2.Circle(tpl, new Point(240, 255), 50, Scalar.All(160), -1);

            var tool = new FeatureMatchTool { IsAutoTuneEnabled = true };
            tool.AutoTuneParameters(tpl);

            int expected = ExpectedPoints(tpl, tool.CannyLow, tool.CannyHigh);
            Assert.True(expected > 100, $"테스트 전제: 윤곽이 100점 하한을 넘어야 함 (expected={expected})");
            Assert.Equal(expected, tool.MaxModelPoints);
        }

        [Fact]
        public void AutoTune_TinyTemplate_ClampsToFloor()
        {
            using var tpl = new Mat(64, 64, MatType.CV_8UC1, Scalar.All(64));
            Cv2.Rectangle(tpl, new Rect(16, 16, 32, 32), Scalar.All(200), -1);

            var tool = new FeatureMatchTool { IsAutoTuneEnabled = true };
            tool.AutoTuneParameters(tpl);

            Assert.Equal(100, tool.MaxModelPoints);
        }

        [Fact]
        public void AutoTune_DenseTexture_ClampsToCeiling()
        {
            // 고밀도 텍스처 — 엣지가 매우 많아도 상한 500 에서 캡
            using var tpl = new Mat(300, 300, MatType.CV_8UC1);
            var rnd = new System.Random(42);
            var data = new byte[300 * 300];
            rnd.NextBytes(data);
            System.Runtime.InteropServices.Marshal.Copy(data, 0, tpl.Data, data.Length);

            var tool = new FeatureMatchTool { IsAutoTuneEnabled = true };
            tool.AutoTuneParameters(tpl);

            Assert.Equal(500, tool.MaxModelPoints);
        }

        [Fact]
        public void AutoTune_SuggestionMode_UsesSameRule()
        {
            using var tpl = new Mat(360, 480, MatType.CV_8UC1, Scalar.All(64));
            Cv2.Rectangle(tpl, new Rect(30, 30, 420, 120), Scalar.All(200), -1);
            Cv2.Rectangle(tpl, new Rect(60, 190, 150, 130), Scalar.All(200), -1);
            Cv2.Rectangle(tpl, new Rect(270, 190, 150, 130), Scalar.All(200), -1);

            var tool = new FeatureMatchTool { IsAutoTuneEnabled = false, MaxModelPoints = 200 };
            tool.AutoTuneParameters(tpl);

            // 자동 적용 꺼짐 → 값은 그대로, 제안만 채워짐
            Assert.Equal(200, tool.MaxModelPoints);
            Assert.True(tool.HasSuggestions);
            int expected = ExpectedPoints(tpl, tool.SuggestedCannyLow, tool.SuggestedCannyHigh);
            Assert.Equal(expected, tool.SuggestedMaxModelPoints);
        }
    }
}
