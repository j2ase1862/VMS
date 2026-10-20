using System.Text.Json;
using OpenCvSharp;
using VMS.VisionSetup.Models;
using VMS.VisionSetup.Services;
using VMS.VisionSetup.ViewModels.ToolSettings;
using VMS.VisionSetup.VisionTools.PatternMatching;
using Xunit;

namespace VMS.VisionSetup.Tests
{
    /// <summary>
    /// 2-스텝 얼라인 검증 — StepPoseStore(스텝 간 포즈 공유) + MultiStepAlignTool
    /// (Baseline 합성 좌표계 2점 정합). 정적 저장소·VisionService 싱글턴을 쓰므로
    /// 직렬 실행 + 테스트마다 저장소 초기화.
    /// </summary>
    [Collection("VisionServiceSingleton")]
    public class MultiStepAlignToolTests
    {
        private static Mat TestImage() => new(64, 64, MatType.CV_8UC3, Scalar.Black);

        private static FeatureMatchTool RecordPose(string stepId, double cx, double cy,
            FeatureMatchTool? tool = null)
        {
            tool ??= new FeatureMatchTool();
            var r = new VisionResult { Success = true };
            r.Data["CenterX"] = cx;
            r.Data["CenterY"] = cy;
            StepPoseStore.Record(stepId, tool, r);
            return tool;
        }

        private static MultiStepAlignTool CreateAlign(
            string toolAId, string toolBId, double baselineX = 100, double baselineY = 0)
        {
            return new MultiStepAlignTool
            {
                DrawOverlay = false,
                SourceStepIdA = "step-A",
                SourceToolIdA = toolAId,
                SourceStepIdB = "step-B",
                SourceToolIdB = toolBId,
                BaselineX = baselineX,
                BaselineY = baselineY
            };
        }

        public MultiStepAlignToolTests()
        {
            StepPoseStore.ResetForTests();
            VisionService.Instance.CurrentStepResolutionMmPerPx = 0;   // px 모드 고정
        }

        [Fact]
        public void Store_RecordAndGet_RoundTrip_AndCycleInvalidation()
        {
            var tool = RecordPose("step-A", 12.5, 34.5);

            var entry = StepPoseStore.TryGet("step-A", tool.Id, requireCurrentCycle: true);
            Assert.NotNull(entry);
            Assert.Equal(12.5, entry!.CenterX);
            Assert.Equal(34.5, entry.CenterY);

            // 새 사이클 시작 → 같은 사이클 요구 시 무효, 미요구 시 잔존값 사용 가능
            StepPoseStore.BeginCycle();
            Assert.Null(StepPoseStore.TryGet("step-A", tool.Id, requireCurrentCycle: true));
            Assert.NotNull(StepPoseStore.TryGet("step-A", tool.Id, requireCurrentCycle: false));
        }

        [Fact]
        public void Execute_TranslationOnly_ComputesDelta()
        {
            // 기준: A(10,10), B로컬(20,10) + Baseline(100,0) → B합성(120,10)
            var toolA = RecordPose("step-A", 10, 10);
            var toolB = RecordPose("step-B", 20, 10);
            var align = CreateAlign(toolA.Id, toolB.Id);

            Assert.True(align.TryComputeCurrentPoints(out var ax, out var ay,
                out var bx, out var by, out var unit, out _));
            Assert.Equal("px", unit);
            Assert.Equal(120, bx, 6);
            align.RefAX = ax; align.RefAY = ay;
            align.RefBX = bx; align.RefBY = by;
            align.HasReference = true;

            // 현재: 두 점 모두 +5 Y 이동
            RecordPose("step-A", 10, 15, toolA);
            RecordPose("step-B", 20, 15, toolB);

            using var img = TestImage();
            var result = align.Execute(img);

            Assert.True(result.Success);
            Assert.Equal(0.0, (double)result.Data["DeltaX"], 6);
            Assert.Equal(5.0, (double)result.Data["DeltaY"], 6);
            Assert.Equal(0.0, (double)result.Data["DeltaTheta"], 6);
            Assert.Equal(1.0, (double)result.Data["ScaleRatio"], 6);
            result.ReleaseMats();
        }

        [Fact]
        public void Execute_Rotation90_ComputesThetaFromCombinedFrame()
        {
            // 기준: A합성(0,0), B합성(100,0) — 기저선 = +X
            var toolA = RecordPose("step-A", 0, 0);
            var toolB = RecordPose("step-B", 0, 0);   // B로컬(0,0)+Baseline(100,0)=(100,0)
            var align = CreateAlign(toolA.Id, toolB.Id);
            align.RefAX = 0; align.RefAY = 0;
            align.RefBX = 100; align.RefBY = 0;
            align.HasReference = true;

            // 현재: A합성(0,0), B합성(0,100) → 기저선이 +Y 로 90° 회전
            RecordPose("step-A", 0, 0, toolA);
            RecordPose("step-B", -100, 100, toolB);   // (-100,100)+(100,0)=(0,100)

            using var img = TestImage();
            var result = align.Execute(img);

            Assert.True(result.Success);
            Assert.Equal(90.0, (double)result.Data["DeltaTheta"], 6);
            // 중심: 기준(50,0) → 현재(0,50)
            Assert.Equal(-50.0, (double)result.Data["DeltaX"], 6);
            Assert.Equal(50.0, (double)result.Data["DeltaY"], 6);
            result.ReleaseMats();
        }

        [Fact]
        public void Execute_RequireSameCycle_RejectsStaleEntries()
        {
            var toolA = RecordPose("step-A", 0, 0);
            var toolB = RecordPose("step-B", 0, 0);
            var align = CreateAlign(toolA.Id, toolB.Id);
            align.RefAX = 0; align.RefAY = 0; align.RefBX = 100; align.RefBY = 0;
            align.HasReference = true;

            // 새 사이클 시작 — A/B 미실행 상태
            StepPoseStore.BeginCycle();

            using (var img = TestImage())
            {
                var stale = align.Execute(img);
                Assert.False(stale.Success);
                Assert.Contains("사이클", stale.Message);
                stale.ReleaseMats();
            }

            // RequireSameCycle 해제 시 잔존값으로 계산 가능 (편집/튜닝 모드)
            align.RequireSameCycle = false;
            using (var img = TestImage())
            {
                var ok = align.Execute(img);
                Assert.True(ok.Success);
                ok.ReleaseMats();
            }
        }

        [Fact]
        public void Execute_WithoutReference_FailsWithGuide()
        {
            var toolA = RecordPose("step-A", 0, 0);
            var toolB = RecordPose("step-B", 0, 0);
            var align = CreateAlign(toolA.Id, toolB.Id);

            using var img = TestImage();
            var result = align.Execute(img);

            Assert.False(result.Success);
            Assert.Contains("기준", result.Message);
        }

        [Fact]
        public void SerializerRoundTrip_ThroughSettingsViewModel_PreservesParameters()
        {
            var tool = new MultiStepAlignTool
            {
                SourceStepIdA = "sA", SourceToolIdA = "tA",
                SourceStepIdB = "sB", SourceToolIdB = "tB"
            };
            var vm = new MultiStepAlignToolSettingsViewModel(tool)
            {
                BaselineX = 150.5,
                BaselineY = -20.25,
                RefAX = 1.5, RefAY = 2.5, RefBX = 151.5, RefBY = 3.25,
                HasReference = true,
                RequireSameCycle = false,
                EnableRobotTransform = true,
                RobotM11 = 0, RobotM12 = -1, RobotM21 = 1, RobotM22 = 0,
                RobotThetaSign = -1,
                EnableJudgment = true,
                MaxDeltaXY = 0.5,
                MaxDeltaTheta = 0.25,
                DrawOverlay = false
            };
            Assert.NotNull(vm);
            tool.ReferenceUnit = "mm";

            var config = ToolSerializer.SerializeTool(tool);
            var jsonOpts = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
            var json = JsonSerializer.Serialize(config, jsonOpts);
            var restored = Assert.IsType<MultiStepAlignTool>(
                ToolSerializer.DeserializeTool(JsonSerializer.Deserialize<ToolConfig>(json, jsonOpts)!));

            Assert.Equal("sA", restored.SourceStepIdA);
            Assert.Equal("tA", restored.SourceToolIdA);
            Assert.Equal("sB", restored.SourceStepIdB);
            Assert.Equal("tB", restored.SourceToolIdB);
            Assert.Equal(150.5, restored.BaselineX);
            Assert.Equal(-20.25, restored.BaselineY);
            Assert.Equal(1.5, restored.RefAX);
            Assert.Equal(2.5, restored.RefAY);
            Assert.Equal(151.5, restored.RefBX);
            Assert.Equal(3.25, restored.RefBY);
            Assert.True(restored.HasReference);
            Assert.False(restored.RequireSameCycle);
            Assert.True(restored.EnableRobotTransform);
            Assert.Equal(-1, restored.RobotM12);
            Assert.Equal(1, restored.RobotM21);
            Assert.Equal(-1, restored.RobotThetaSign);
            Assert.True(restored.EnableJudgment);
            Assert.Equal(0.5, restored.MaxDeltaXY);
            Assert.Equal(0.25, restored.MaxDeltaTheta);
            Assert.False(restored.DrawOverlay);
            Assert.Equal("mm", restored.ReferenceUnit);
        }

        // ───────────── 기준 단위 기록 (캘리브레이션이 바뀌면 알린다) ─────────────

        /// <summary>[현재 두 점을 기준으로 등록] 이 그때의 단위를 기록하고, 같은 단위면 예전처럼 Δ 를 낸다.</summary>
        [Fact]
        public void CaptureReference_records_the_unit_and_same_unit_runs()
        {
            var toolA = RecordPose("step-A", 10, 10);
            var toolB = RecordPose("step-B", 20, 10);
            var align = CreateAlign(toolA.Id, toolB.Id);
            var vm = new MultiStepAlignToolSettingsViewModel(align);

            vm.CaptureReferenceCommand.Execute(null);

            Assert.True(align.HasReference);
            Assert.Equal("px", align.ReferenceUnit);
            Assert.Contains("px", vm.ReferenceUnitText);
            using var img = TestImage();
            var r = align.Execute(img);
            Assert.True(r.Success, r.Message);
            Assert.Equal("px", r.Data["ReferenceUnit"]);
        }

        /// <summary>핵심 — 기준을 px 로 잡은 뒤 캘리브레이션이 생겨 mm 로 계산되면 Δ 를 내지 않고 실패로 알린다.</summary>
        [Fact]
        public void Unit_change_after_capture_fails_instead_of_mixing_units()
        {
            var toolA = RecordPose("step-A", 10, 10);
            var toolB = RecordPose("step-B", 20, 10);
            var align = CreateAlign(toolA.Id, toolB.Id);
            new MultiStepAlignToolSettingsViewModel(align).CaptureReferenceCommand.Execute(null);
            Assert.Equal("px", align.ReferenceUnit);

            try
            {
                VisionService.Instance.CurrentStepResolutionMmPerPx = 0.1;   // 캘리브레이션(스텝 Resolution) 추가
                RecordPose("step-A", 10, 10, toolA);
                RecordPose("step-B", 20, 10, toolB);

                using var img = TestImage();
                var r = align.Execute(img);

                Assert.False(r.Success);
                Assert.Contains("단위 불일치", r.Message);
                Assert.Equal("mm", r.Data["Unit"]);
                Assert.Equal("px", r.Data["ReferenceUnit"]);
                Assert.False(r.Data.ContainsKey("DeltaX"));
            }
            finally
            {
                VisionService.Instance.CurrentStepResolutionMmPerPx = 0;
            }
        }

        /// <summary>단위 기록이 없는 옛 레시피(빈 값)는 예전처럼 단위를 따지지 않는다 — 업데이트로 판정이 갑자기 바뀌지 않게.</summary>
        [Fact]
        public void Legacy_reference_without_unit_keeps_old_behavior()
        {
            var toolA = RecordPose("step-A", 10, 10);
            var toolB = RecordPose("step-B", 20, 10);
            var align = CreateAlign(toolA.Id, toolB.Id);
            align.RefAX = 10; align.RefAY = 10; align.RefBX = 120; align.RefBY = 10;
            align.HasReference = true;   // ReferenceUnit 은 비어 있음

            try
            {
                VisionService.Instance.CurrentStepResolutionMmPerPx = 0.1;
                RecordPose("step-A", 10, 10, toolA);
                RecordPose("step-B", 20, 10, toolB);
                using var img = TestImage();
                var r = align.Execute(img);
                Assert.True(r.Success, r.Message);
                Assert.Equal("mm", r.Data["Unit"]);
            }
            finally
            {
                VisionService.Instance.CurrentStepResolutionMmPerPx = 0;
            }

            // 옛 레시피 JSON 에는 키가 없다 → 빈 값으로 읽힌다
            var config = ToolSerializer.SerializeTool(align);
            config.Parameters.Remove("ReferenceUnit");
            var restored = Assert.IsType<MultiStepAlignTool>(ToolSerializer.DeserializeTool(config));
            Assert.Equal(string.Empty, restored.ReferenceUnit);
            Assert.Contains("기록 없음", new MultiStepAlignToolSettingsViewModel(restored).ReferenceUnitText);
        }
    }
}
