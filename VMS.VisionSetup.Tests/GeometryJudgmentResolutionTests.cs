using System;
using OpenCvSharp;
using VMS.VisionSetup.Models;
using VMS.VisionSetup.Services;
using VMS.VisionSetup.VisionTools.Measurement;
using Xunit;

namespace VMS.VisionSetup.Tests
{
    /// <summary>
    /// 스텝 Resolution(mm/px) 폴백 + GeometryTool 공차 판정 검증.
    /// 배경: InspectionStep.Resolution 은 그동안 어떤 측정도 쓰지 않는 죽은 속성이었고
    /// (곱하는 코드 0곳), GeometryTool 은 계산만 할 뿐 판정 개념이 없어 ResultTool 을
    /// 연결해도 "계산되면 무조건 OK"였다.
    /// VisionService 는 싱글턴이므로 각 테스트가 상태를 세팅/복원한다.
    /// </summary>
    public class GeometryJudgmentResolutionTests : IDisposable
    {
        private readonly CalibrationMetadata? _savedCal;
        private readonly double _savedRes;

        public GeometryJudgmentResolutionTests()
        {
            _savedCal = VisionService.Instance.CurrentCalibrationMetadata;
            _savedRes = VisionService.Instance.CurrentStepResolutionMmPerPx;
            VisionService.Instance.CurrentCalibrationMetadata = null;
            VisionService.Instance.CurrentStepResolutionMmPerPx = 0;
        }

        public void Dispose()
        {
            VisionService.Instance.CurrentCalibrationMetadata = _savedCal;
            VisionService.Instance.CurrentStepResolutionMmPerPx = _savedRes;
        }

        /// <summary>점 2개(100px 간격)를 물린 PointPointDistance GeometryTool 실행.</summary>
        private static VisionResult RunPointDistance(GeometryTool tool, double pxDistance = 100)
        {
            tool.SourceGeometries.Clear();
            tool.SourceGeometries.Add(new SourceGeometry { Type = GeometryType.Point, X = 10, Y = 10 });
            tool.SourceGeometries.Add(new SourceGeometry { Type = GeometryType.Point, X = 10 + pxDistance, Y = 10 });
            using var img = new Mat(200, 200, MatType.CV_8UC1, Scalar.Black);
            return tool.Execute(img);
        }

        // ── 스텝 Resolution 폴백 ──

        [Fact]
        public void EffectiveCalibration_UsesStepResolution_WhenNoCalibration()
        {
            VisionService.Instance.CurrentStepResolutionMmPerPx = 0.05;

            var tool = new GeometryTool();
            var result = RunPointDistance(tool, pxDistance: 100);

            Assert.True(result.Success);
            Assert.Equal(100.0, Convert.ToDouble(result.Data["Distance"]), 6);
            // 100px × 0.05mm/px = 5mm — Resolution 이 실제로 곱해져야 한다
            Assert.True(result.Data.ContainsKey("DistanceMm"),
                "스텝 Resolution 폴백이 DistanceMm 를 채워야 한다");
            Assert.Equal(5.0, Convert.ToDouble(result.Data["DistanceMm"]), 6);
        }

        [Fact]
        public void EffectiveCalibration_PrefersRealCalibration_OverStepResolution()
        {
            // 정식 캘리브레이션(0.1mm/px)과 스텝 Resolution(0.05mm/px)이 다르면 정식이 이겨야 한다
            VisionService.Instance.CurrentCalibrationMetadata = new CalibrationMetadata
            {
                Mode = CalibrationMode.SinglePointScale,
                PixelSizeMm = 0.1,
            };
            VisionService.Instance.CurrentStepResolutionMmPerPx = 0.05;

            var result = RunPointDistance(new GeometryTool(), pxDistance: 100);

            Assert.Equal(10.0, Convert.ToDouble(result.Data["DistanceMm"]), 6);
        }

        [Fact]
        public void EffectiveCalibration_NoMmKeys_WhenNothingConfigured()
        {
            var result = RunPointDistance(new GeometryTool(), pxDistance: 100);

            Assert.True(result.Success);
            Assert.False(result.Data.ContainsKey("DistanceMm"),
                "캘리브레이션도 Resolution 도 없으면 mm 키가 생기면 안 된다");
        }

        // ── 공차 판정 ──

        [Fact]
        public void Judgment_Mm_PassAndFail()
        {
            VisionService.Instance.CurrentStepResolutionMmPerPx = 0.05;   // 100px = 5mm

            var tool = new GeometryTool
            {
                EnableJudgment = true,
                JudgmentUnit = GeometryJudgmentUnit.Mm,
                ExpectedValue = 5.0,
                ToleranceMinus = 0.1,
                TolerancePlus = 0.1,
            };

            var pass = RunPointDistance(tool, pxDistance: 100);   // 5.00mm ∈ 4.9~5.1
            Assert.True(pass.Success);
            Assert.True((bool)pass.Data["JudgmentPass"]);
            Assert.Contains("판정 OK", pass.Message);

            var fail = RunPointDistance(tool, pxDistance: 110);   // 5.50mm ∉ 4.9~5.1
            Assert.False(fail.Success);
            Assert.False((bool)fail.Data["JudgmentPass"]);
            Assert.Contains("판정 NG", fail.Message);
        }

        [Fact]
        public void Judgment_Px_WorksWithoutCalibration()
        {
            var tool = new GeometryTool
            {
                EnableJudgment = true,
                JudgmentUnit = GeometryJudgmentUnit.Px,
                ExpectedValue = 100,
                ToleranceMinus = 2,
                TolerancePlus = 2,
            };

            Assert.True(RunPointDistance(tool, pxDistance: 101).Success);
            Assert.False(RunPointDistance(tool, pxDistance: 105).Success);
        }

        [Fact]
        public void Judgment_Mm_FailsExplicitly_WhenNoConversionAvailable()
        {
            // mm 판정인데 변환 수단이 없으면 — px 값을 mm 기준과 비교해 오판하는 대신 명확히 실패
            var tool = new GeometryTool
            {
                EnableJudgment = true,
                JudgmentUnit = GeometryJudgmentUnit.Mm,
                ExpectedValue = 100,
            };

            var result = RunPointDistance(tool, pxDistance: 100);

            Assert.False(result.Success);
            Assert.Contains("mm 변환 불가", result.Message);
            Assert.False((bool)result.Data["JudgmentPass"]);
        }

        [Fact]
        public void Judgment_Disabled_KeepsLegacyBehavior()
        {
            var result = RunPointDistance(new GeometryTool(), pxDistance: 100);

            Assert.True(result.Success);
            Assert.False(result.Data.ContainsKey("JudgmentPass"));
        }

        // ── 설정 ViewModel 래퍼 — 설정 UI 는 툴이 아니라 VM 에 바인딩된다.
        // 래퍼가 빠지면 바인딩이 허공이라 화면은 공백, 입력은 저장 안 됨 (실사용 보고 결함).

        [Fact]
        public void SettingsViewModel_ExposesJudgmentProperties_WiredToTool()
        {
            var tool = new GeometryTool();
            var vm = new ViewModels.ToolSettings.GeometryToolSettingsViewModel(tool);

            vm.EnableJudgment = true;
            vm.JudgmentUnit = GeometryJudgmentUnit.Px;
            vm.ExpectedValue = 12.5;
            vm.ToleranceMinus = 0.25;
            vm.TolerancePlus = 0.75;

            // VM 입력이 툴까지 도달해야 저장(SerializeTool)에 반영된다
            Assert.True(tool.EnableJudgment);
            Assert.Equal(GeometryJudgmentUnit.Px, tool.JudgmentUnit);
            Assert.Equal(12.5, tool.ExpectedValue, 6);
            Assert.Equal(0.25, tool.ToleranceMinus, 6);
            Assert.Equal(0.75, tool.TolerancePlus, 6);

            // 역방향 — 로드된 툴 값이 VM(화면)에 보여야 한다
            tool.ExpectedValue = 99;
            Assert.Equal(99, vm.ExpectedValue, 6);
        }

        // ── 직렬화 왕복 ──

        [Fact]
        public void JudgmentProperties_SurviveSerializationRoundTrip()
        {
            var tool = new GeometryTool
            {
                Operation = GeometryOperation.LineLineDistance,
                EnableJudgment = true,
                JudgmentUnit = GeometryJudgmentUnit.Px,
                ExpectedValue = 42.5,
                ToleranceMinus = 1.5,
                TolerancePlus = 2.5,
            };

            var config = ToolSerializer.SerializeTool(tool);
            var restored = Assert.IsType<GeometryTool>(ToolSerializer.DeserializeTool(config));

            Assert.Equal(GeometryOperation.LineLineDistance, restored.Operation);
            Assert.True(restored.EnableJudgment);
            Assert.Equal(GeometryJudgmentUnit.Px, restored.JudgmentUnit);
            Assert.Equal(42.5, restored.ExpectedValue, 6);
            Assert.Equal(1.5, restored.ToleranceMinus, 6);
            Assert.Equal(2.5, restored.TolerancePlus, 6);
        }
    }
}
