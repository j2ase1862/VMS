using System.Text.Json;
using OpenCvSharp;
using VMS.VisionSetup.Models;
using VMS.VisionSetup.Services;
using VMS.VisionSetup.ViewModels.ToolSettings;
using VMS.VisionSetup.VisionTools.Measurement;
using VMS.VisionSetup.VisionTools.PatternMatching;
using Xunit;

namespace VMS.VisionSetup.Tests
{
    /// <summary>
    /// MatchAlignTool — 표준 2D 매치 얼라인 (기준 대비 ΔX/ΔY/Δθ) 검증.
    /// Execute 가 VisionService.Instance(EffectiveCalibration/CurrentImage)를 참조하므로
    /// 싱글턴 컬렉션으로 직렬 실행.
    /// </summary>
    [Collection("VisionServiceSingleton")]
    public class MatchAlignToolTests
    {
        private static VisionResult MatchResult(
            double cx, double cy, double angle, double trainedX = 0, double trainedY = 0)
        {
            var r = new VisionResult { Success = true };
            r.Data["CenterX"] = cx;
            r.Data["CenterY"] = cy;
            r.Data["Angle"] = angle;
            r.Data["TrainedCenterX"] = trainedX;
            r.Data["TrainedCenterY"] = trainedY;
            return r;
        }

        private static Mat TestImage() => new(64, 64, MatType.CV_8UC3, Scalar.Black);

        [Fact]
        public void Execute_TrainedReference_ComputesDelta()
        {
            var tool = new MatchAlignTool { DrawOverlay = false };
            tool.SourceMatchResult = MatchResult(110, 95, 3.5, trainedX: 100, trainedY: 100);

            using var img = TestImage();
            var result = tool.Execute(img);

            Assert.True(result.Success);
            Assert.Equal(10.0, (double)result.Data["DeltaX"], 6);
            Assert.Equal(-5.0, (double)result.Data["DeltaY"], 6);
            Assert.Equal(3.5, (double)result.Data["DeltaTheta"], 6);
            result.ReleaseMats();
        }

        [Fact]
        public void Execute_ManualReference_ComputesDelta_AndNormalizesAngle()
        {
            var tool = new MatchAlignTool
            {
                DrawOverlay = false,
                UseTrainedReference = false,
                RefX = 50,
                RefY = 60,
                RefTheta = 170
            };
            // 현재 -175° — 기준 170° 대비 실제 변위는 +15° (360° 주기 정규화)
            tool.SourceMatchResult = MatchResult(53, 64, -175);

            using var img = TestImage();
            var result = tool.Execute(img);

            Assert.True(result.Success);
            Assert.Equal(3.0, (double)result.Data["DeltaX"], 6);
            Assert.Equal(4.0, (double)result.Data["DeltaY"], 6);
            Assert.Equal(15.0, (double)result.Data["DeltaTheta"], 6);
            result.ReleaseMats();
        }

        [Fact]
        public void Execute_RobotTransform_AppliesLinearMatrixAndThetaSign()
        {
            // 90° 회전 행렬 (x'=−y, y'=x) + 회전 부호 반전
            var tool = new MatchAlignTool
            {
                DrawOverlay = false,
                UseTrainedReference = false,
                RefX = 0,
                RefY = 0,
                RefTheta = 0,
                EnableRobotTransform = true,
                RobotM11 = 0,
                RobotM12 = -1,
                RobotM21 = 1,
                RobotM22 = 0,
                RobotThetaSign = -1
            };
            tool.SourceMatchResult = MatchResult(10, 4, 2);

            using var img = TestImage();
            var result = tool.Execute(img);

            Assert.True(result.Success);
            Assert.Equal(-4.0, (double)result.Data["RobotDX"], 6);
            Assert.Equal(10.0, (double)result.Data["RobotDY"], 6);
            Assert.Equal(-2.0, (double)result.Data["RobotDTheta"], 6);
            result.ReleaseMats();
        }

        [Fact]
        public void Execute_Judgment_Px_FailsOutsideTolerance_PassesInside()
        {
            var tool = new MatchAlignTool
            {
                DrawOverlay = false,
                UseTrainedReference = false,
                EnableJudgment = true,
                JudgmentUnit = GeometryJudgmentUnit.Px,
                MaxDeltaXY = 5.0,
                MaxDeltaTheta = 1.0
            };

            // 변위 (3,4) = 반경 5.0 → 경계 합격, 각도 0.5° 합격
            tool.SourceMatchResult = MatchResult(3, 4, 0.5);
            using (var img = TestImage())
            {
                var ok = tool.Execute(img);
                Assert.True(ok.Success);
                Assert.True((bool)ok.Data["JudgmentPass"]);
                ok.ReleaseMats();
            }

            // 각도 초과 → NG (매칭 데이터는 유지)
            tool.SourceMatchResult = MatchResult(0, 0, 2.0);
            using (var img = TestImage())
            {
                var ng = tool.Execute(img);
                Assert.False(ng.Success);
                Assert.False((bool)ng.Data["JudgmentPass"]);
                Assert.Equal(2.0, (double)ng.Data["DeltaTheta"], 6);
                ng.ReleaseMats();
            }
        }

        [Fact]
        public void Execute_Judgment_Mm_WithoutCalibration_FailsExplicitly()
        {
            var tool = new MatchAlignTool
            {
                DrawOverlay = false,
                UseTrainedReference = false,
                EnableJudgment = true,
                JudgmentUnit = GeometryJudgmentUnit.Mm
            };
            tool.SourceMatchResult = MatchResult(1, 1, 0);

            using var img = TestImage();
            var result = tool.Execute(img);

            // 캘리브레이션 없음 → 픽셀 값으로 오판하는 대신 명확히 실패 (GeometryTool 컨벤션)
            Assert.False(result.Success);
            Assert.Contains("mm 변환 불가", result.Message);
            result.ReleaseMats();
        }

        [Fact]
        public void Execute_WithoutSource_FailsWithGuide()
        {
            var tool = new MatchAlignTool { DrawOverlay = false };

            using var img = TestImage();
            var result = tool.Execute(img);

            Assert.False(result.Success);
            Assert.Contains("Result", result.Message);
        }

        [Fact]
        public void SerializerRoundTrip_ThroughSettingsViewModel_PreservesParameters()
        {
            // 설정 VM 래퍼 경유로 값 주입 (래퍼 누락 시 바인딩이 허공 — 5종 세트 회귀 방어)
            var tool = new MatchAlignTool();
            var vm = new MatchAlignToolSettingsViewModel(tool)
            {
                UseTrainedReference = false,
                RefX = 12.5,
                RefY = 34.5,
                RefTheta = -7.25,
                EnableRobotTransform = true,
                RobotM11 = 0.5,
                RobotM12 = -0.5,
                RobotM21 = 0.25,
                RobotM22 = 0.75,
                RobotThetaSign = -1,
                EnableJudgment = true,
                JudgmentUnit = GeometryJudgmentUnit.Px,
                MaxDeltaXY = 2.5,
                MaxDeltaTheta = 0.75,
                DrawOverlay = false
            };
            Assert.NotNull(vm);

            var config = ToolSerializer.SerializeTool(tool);
            var jsonOpts = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
            var json = JsonSerializer.Serialize(config, jsonOpts);
            var configFromJson = JsonSerializer.Deserialize<ToolConfig>(json, jsonOpts)!;

            var restored = Assert.IsType<MatchAlignTool>(ToolSerializer.DeserializeTool(configFromJson));
            Assert.False(restored.UseTrainedReference);
            Assert.Equal(12.5, restored.RefX);
            Assert.Equal(34.5, restored.RefY);
            Assert.Equal(-7.25, restored.RefTheta);
            Assert.True(restored.EnableRobotTransform);
            Assert.Equal(0.5, restored.RobotM11);
            Assert.Equal(-0.5, restored.RobotM12);
            Assert.Equal(0.25, restored.RobotM21);
            Assert.Equal(0.75, restored.RobotM22);
            Assert.Equal(-1, restored.RobotThetaSign);
            Assert.True(restored.EnableJudgment);
            Assert.Equal(GeometryJudgmentUnit.Px, restored.JudgmentUnit);
            Assert.Equal(2.5, restored.MaxDeltaXY);
            Assert.Equal(0.75, restored.MaxDeltaTheta);
            Assert.False(restored.DrawOverlay);
        }

        [Fact]
        public void SettingsViewModel_CaptureReference_CopiesCurrentPose()
        {
            var tool = new MatchAlignTool();
            tool.SourceMatchResult = MatchResult(200.5, 300.25, 12.75);
            var vm = new MatchAlignToolSettingsViewModel(tool);

            vm.CaptureReferenceCommand.Execute(null);

            Assert.False(tool.UseTrainedReference);
            Assert.Equal(200.5, tool.RefX);
            Assert.Equal(300.25, tool.RefY);
            Assert.Equal(12.75, tool.RefTheta);
        }
    }
}
