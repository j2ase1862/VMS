using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using VMS.Interfaces;
using VMS.Services;
using VMS.VisionSetup.Services;
using VMS.VisionSetup.VisionTools.BlobAnalysis;
using VMS.VisionSetup.VisionTools.Measurement;
using Xunit;

namespace VMS.Tests.Services
{
    /// <summary>
    /// Fixture(Coordinates) 소스가 실패했을 때 하위 도구가 <b>실행되지 않는지</b> 검증.
    ///
    /// <para>2026-09-22 확인: 도구 인스턴스는 사이클 간 재사용되므로, 기준 위치를 주는
    /// 도구가 실패해도 하위 측정 도구를 그냥 실행하면 ROI 가 <b>직전 사이클 위치에 그대로</b>
    /// 남는다. 제품이 없는 화면이라도 직전 자리에 엣지가 있으면 값이 나오고, 그 값이 공차에
    /// 들면 AUTO RUN 이 OK 를 내보낸다 — 무인 운전에서 불량이 양품으로 흘러간다.</para>
    ///
    /// <para>실패 판정은 두 실행 엔진이 같아야 한다 — VisionSetup 쪽 대응 테스트는
    /// VMS.VisionSetup.Tests/FixtureSourceGateTests.cs.</para>
    /// </summary>
    [Collection(InspectionServiceStaticsCollection.Name)]
    public class FixtureSourceGateTests : IDisposable
    {
        private readonly InspectionService _service = new();
        private readonly List<Mat> _mats = new();

        // 기준 표식이 놓이는 자리 — BlobTool ROI 가 이 영역만 본다.
        private static readonly Rect MarkRoi = new(60, 180, 100, 100);
        private static readonly Rect Mark = new(80, 200, 60, 60);

        public void Dispose()
        {
            foreach (var m in _mats)
            {
                try { m.Dispose(); } catch { /* 이미 해제됨 무시 */ }
            }
            _service.ClearCache();
        }

        /// <summary>
        /// 오른쪽 절반이 밝은 세로 엣지(측정 대상)는 <b>항상</b> 그려 둔다 —
        /// 기준 표식만 사라져도 하위 도구가 돌면 "측정에 성공" 해 버린다는 것이 이 회귀의 핵심.
        /// </summary>
        private Mat MakeImage(bool withFixtureMark)
        {
            var img = new Mat(300, 400, MatType.CV_8UC3, new Scalar(30, 30, 30));
            img.Rectangle(new Rect(200, 0, 200, 300), new Scalar(230, 230, 230), -1);
            if (withFixtureMark)
                img.Rectangle(Mark, new Scalar(230, 230, 230), -1);
            Cv2.GaussianBlur(img, img, new Size(9, 9), 3);
            _mats.Add(img);
            return img;
        }

        private static InspectionStep MakeStep()
        {
            var source = new BlobTool
            {
                Name = "Fixture",
                UseROI = true,
                ROI = MarkRoi,
                UseInternalThreshold = true,
                ThresholdValue = 128,
                MinArea = 50
            };

            var target = new LineFitTool
            {
                Name = "Line",
                UseROI = true,
                ROI = new Rect(120, 80, 200, 140),
                ROIAngle = 0.0001,
                ROICenterX = 220,
                ROICenterY = 150,
                SearchAxis = LineSearchAxis.AlongWidth,
                SelectionMode = LineEdgeSelectionMode.First,
                Polarity = EdgePolarity.DarkToLight,
                NumCalipers = 7,
                MinFoundCalipers = 3,
                EdgeThreshold = 20
            };

            var sourceConfig = ToolSerializer.SerializeTool(source);
            var targetConfig = ToolSerializer.SerializeTool(target);
            targetConfig.Connections.Add(new ToolConnectionConfig
            {
                SourceToolId = sourceConfig.Id,
                ConnectionType = "Coordinates"
            });

            return new InspectionStep
            {
                Id = "fixture_gate_" + Guid.NewGuid().ToString("N"),
                Name = "FixtureGateStep",
                Tools = new List<ToolConfig> { sourceConfig, targetConfig }
            };
        }

        private static ToolInspectionResult ToolResult(StepInspectionResult result, string name) =>
            result.ToolResults.First(t => t.ToolName == name);

        [Fact]
        public async Task 기준_소스가_성공하면_하위_도구는_정상_실행된다()
        {
            var step = MakeStep();
            var result = await _service.ExecuteStepAsync(step, MakeImage(withFixtureMark: true));

            var fixtureResult = ToolResult(result, "Fixture");
            var lineResult = ToolResult(result, "Line");

            Assert.True(fixtureResult.Success, fixtureResult.Message);
            Assert.True(lineResult.Success, lineResult.Message);
            Assert.True(lineResult.Data.ContainsKey("LineAngle"), "정상 경로인데 측정값이 없다");
        }

        [Fact]
        public async Task 기준_소스가_실패하면_하위_도구는_직전_ROI_로_측정하지_않는다()
        {
            var step = MakeStep();

            // ① 기준 표식이 있는 상태로 한 번 — Fixture 기준이 잡히고 ROI 가 자리를 잡는다.
            var first = await _service.ExecuteStepAsync(step, MakeImage(withFixtureMark: true));
            Assert.True(ToolResult(first, "Line").Success, "선행 실행이 실패하면 회귀 조건이 성립하지 않는다");

            // ② 기준 표식만 사라진다. 측정 대상 엣지는 그대로라 — 도구가 돌기만 하면 "성공" 한다.
            var second = await _service.ExecuteStepAsync(step, MakeImage(withFixtureMark: false));

            var fixtureResult = ToolResult(second, "Fixture");
            var lineResult = ToolResult(second, "Line");

            Assert.False(fixtureResult.Success);

            // 하위 도구는 실행되지 않아야 한다 — 돌았다면 직전 ROI 로 엉뚱한 값을 만든 것이다.
            Assert.False(lineResult.Success);
            Assert.Contains("기준 위치를 주는 도구가 실패하여 건너뜀", lineResult.Message);
            Assert.False(lineResult.Data.ContainsKey("LineAngle"),
                "기준 좌표가 없는데 측정값이 나왔다 — 직전 사이클 ROI 를 그대로 쓴 것이다");

            // 스텝 판정도 NG 여야 한다 (AUTO RUN 에서 양품으로 흘러가면 안 된다).
            Assert.False(second.Success);
        }
    }
}
