using System.Linq;
using OpenCvSharp;
using VMS.VisionSetup.Models;
using VMS.VisionSetup.Services;
using Xunit;

namespace VMS.VisionSetup.Tests
{
    /// <summary>
    /// Fixture(Coordinates) 소스가 실패했을 때 하위 도구를 <b>실행하지 않는지</b> 검증.
    ///
    /// <para>도구 인스턴스는 실행 간 재사용되므로, 기준 좌표를 못 받은 채 하위 도구를 돌리면
    /// ROI 가 직전 실행 위치에 그대로 남아 엉뚱한 자리를 측정한다. 제품이 없어도 그 자리에
    /// 엣지만 있으면 값이 나오고 공차에 들면 OK 가 된다 (2026-09-22 확인).</para>
    ///
    /// <para>VMS 메인 엔진(AUTO RUN) 쪽 대응 테스트는 VMS.Tests/Services/FixtureSourceGateTests.cs —
    /// 두 엔진의 판정이 갈리면 "VisionSetup 은 NG 인데 VMS 는 OK" 가 된다.</para>
    /// </summary>
    [Collection("VisionServiceSingleton")]
    public class FixtureSourceGateTests
    {
        /// <summary>성패를 지정할 수 있는 기준 좌표 소스 (FeatureMatch 대역).</summary>
        private sealed class FailableFixtureSource : VisionToolBase
        {
            public bool ShouldSucceed { get; set; } = true;
            public bool EmitCoordinates { get; set; } = true;

            public FailableFixtureSource(string name = "Fixture")
            {
                Name = name;
                ToolType = "FailableFixtureSource";
            }

            public override VisionResult Execute(Mat inputImage)
            {
                var result = new VisionResult
                {
                    Success = ShouldSucceed,
                    Message = ShouldSucceed ? "ok" : "패턴을 찾지 못했습니다"
                };

                // 실패해도 좌표를 내보내는 경우가 있다(자세 판정 NG 등) — 그래도 기준으로 쓰면 안 된다.
                if (EmitCoordinates)
                {
                    result.Data["CenterX"] = 200.0;
                    result.Data["CenterY"] = 200.0;
                    result.Data["Angle"] = 0.0;
                }

                return result;
            }

            public override VisionToolBase Clone() => new FailableFixtureSource(Name);
        }

        /// <summary>실행 횟수를 세는 최소 타겟.</summary>
        private sealed class CountingTargetTool : VisionToolBase
        {
            public int ExecuteCount { get; private set; }

            public CountingTargetTool(string name = "Target")
            {
                Name = name;
                ToolType = "CountingTargetTool";
            }

            public override VisionResult Execute(Mat inputImage)
            {
                ExecuteCount++;
                return new VisionResult { Success = true, Message = "측정 완료", OutputImage = inputImage.Clone() };
            }

            public override VisionToolBase Clone() => new CountingTargetTool(Name);
        }

        private static VisionService PrepareService()
        {
            var service = VisionService.Instance;
            service.ClearTools();
            using var image = new Mat(400, 400, MatType.CV_8UC3, new Scalar(64, 64, 64));
            service.SetImage(image);
            return service;
        }

        private static (VisionService service, FailableFixtureSource source, CountingTargetTool target) Build()
        {
            var service = PrepareService();
            var source = new FailableFixtureSource();
            var target = new CountingTargetTool { UseROI = true, ROI = new Rect(230, 190, 40, 20) };

            service.AddTool(source);
            service.AddTool(target);
            service.AddConnection(source, target, ConnectionType.Coordinates);
            return (service, source, target);
        }

        [Fact]
        public void 기준_소스가_성공하면_하위_도구가_실행된다()
        {
            var (service, _, target) = Build();
            try
            {
                var results = service.ExecuteAll();

                Assert.Equal(1, target.ExecuteCount);
                Assert.True(results.All(r => r.Success));
            }
            finally
            {
                service.ClearTools();
            }
        }

        [Fact]
        public void 기준_소스가_실패하면_하위_도구는_실행되지_않는다()
        {
            var (service, source, target) = Build();
            try
            {
                // ① 정상 실행 — Fixture 기준이 잡히고 ROI 가 자리를 잡는다.
                service.ExecuteAll();
                Assert.Equal(1, target.ExecuteCount);
                var roiAfterFirst = target.ROI;

                // ② 소스 실패. 좌표는 그대로 내보내지만 판정이 실패라 기준으로 쓰면 안 된다.
                source.ShouldSucceed = false;
                var results = service.ExecuteAll();

                // 하위 도구는 다시 돌지 않았다 — 돌았다면 직전 ROI 로 측정한 것이다.
                Assert.Equal(1, target.ExecuteCount);
                Assert.Equal(roiAfterFirst, target.ROI);

                var skipped = results.Single(r => !r.Success && r.Message.Contains("기준 위치를 주는 도구가 실패"));
                Assert.Contains(target.Name, skipped.Message);
            }
            finally
            {
                service.ClearTools();
            }
        }

        [Fact]
        public void 기준_좌표를_못_받으면_하위_도구는_실행되지_않는다()
        {
            var (service, source, target) = Build();
            try
            {
                service.ExecuteAll();
                Assert.Equal(1, target.ExecuteCount);

                // 소스는 성공했지만 좌표가 없는 경우 (검출은 됐으나 중심을 못 내는 도구)
                source.EmitCoordinates = false;
                var results = service.ExecuteAll();

                Assert.Equal(1, target.ExecuteCount);
                Assert.Contains(results, r => !r.Success && r.Message.Contains("기준 좌표를 받지 못해 건너뜀"));
            }
            finally
            {
                service.ClearTools();
            }
        }
    }
}
