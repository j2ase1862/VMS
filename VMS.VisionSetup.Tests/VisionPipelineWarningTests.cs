using OpenCvSharp;
using VMS.VisionSetup.Models;
using VMS.VisionSetup.Services;
using Xunit;

namespace VMS.VisionSetup.Tests
{
    /// <summary>
    /// 파이프라인 침묵 폴백 표면화 회귀 테스트.
    /// ① 연결 사이클 시 무경고 부분 정렬 → LastPipelineWarning으로 표면화
    /// ② Image 연결 Source의 OutputImage 부재 시 원본 폴백 → 경고 표면화
    /// ③ ApplyCoordinatesConnection: CenterX/CenterY만 가진 비-Fixture 소스도
    ///    첫 분기(Fixture 스냅샷)가 처리 — 죽은 폴백 분기 제거의 동작 보존 검증
    /// VisionService.Instance 싱글턴을 사용하므로 동일 Collection으로 직렬 실행.
    /// </summary>
    [Collection("VisionServiceSingleton")]
    public class VisionPipelineWarningTests
    {
        /// <summary>입력을 복사해 OutputImage로 반환하는 최소 도구</summary>
        private sealed class PassthroughTool : VisionToolBase
        {
            public PassthroughTool(string name = "Passthrough")
            {
                Name = name;
                ToolType = "PassthroughTool";
            }

            public override VisionResult Execute(Mat inputImage)
            {
                return new VisionResult
                {
                    Success = true,
                    Message = "ok",
                    OutputImage = inputImage.Clone()
                };
            }

            public override VisionToolBase Clone() => new PassthroughTool(Name);
        }

        /// <summary>OutputImage를 설정하지 않는 도구 (수정 전 FeatureMatchTool과 동일 패턴)</summary>
        private sealed class NoOutputImageTool : VisionToolBase
        {
            public NoOutputImageTool(string name = "NoOutput")
            {
                Name = name;
                ToolType = "NoOutputImageTool";
            }

            public override VisionResult Execute(Mat inputImage)
            {
                var result = new VisionResult { Success = true, Message = "ok" };
                result.Data["CenterX"] = 120.0;
                result.Data["CenterY"] = 110.0;
                return result;
            }

            public override VisionToolBase Clone() => new NoOutputImageTool(Name);
        }

        private static VisionService PrepareService(int width = 64, int height = 64)
        {
            var service = VisionService.Instance;
            service.ClearTools();
            using var image = new Mat(height, width, MatType.CV_8UC3, new Scalar(64, 64, 64));
            service.SetImage(image);
            return service;
        }

        [Fact]
        public void ExecuteAll_ConnectionCycle_SurfacesWarning_AndStillRunsAllTools()
        {
            var service = PrepareService();
            var toolA = new PassthroughTool("A");
            var toolB = new PassthroughTool("B");
            service.AddTool(toolA);
            service.AddTool(toolB);
            service.AddConnection(toolA, toolB, ConnectionType.Image);
            service.AddConnection(toolB, toolA, ConnectionType.Image); // 사이클

            try
            {
                var results = service.ExecuteAll();

                // 사이클이어도 실행 자체는 계속 (기존 동작 보존)
                Assert.Equal(2, results.Count);
                Assert.All(results, r => Assert.True(r.Success));

                // 더 이상 침묵하지 않음 — 사이클 도구 이름이 경고에 포함
                Assert.NotNull(service.LastPipelineWarning);
                Assert.Contains("순환", service.LastPipelineWarning);
                Assert.Contains("A", service.LastPipelineWarning);
                Assert.Contains("B", service.LastPipelineWarning);
            }
            finally
            {
                service.ClearTools();
            }
        }

        [Fact]
        public void ExecuteAll_NoCycle_NoWarning()
        {
            var service = PrepareService();
            var toolA = new PassthroughTool("A");
            var toolB = new PassthroughTool("B");
            service.AddTool(toolA);
            service.AddTool(toolB);
            service.AddConnection(toolA, toolB, ConnectionType.Image);

            try
            {
                var results = service.ExecuteAll();

                Assert.Equal(2, results.Count);
                Assert.Null(service.LastPipelineWarning);
            }
            finally
            {
                service.ClearTools();
            }
        }

        [Fact]
        public void ExecuteAll_ImageSourceWithoutOutputImage_FallsBackWithWarning()
        {
            var service = PrepareService(width: 80, height: 48);
            var source = new NoOutputImageTool("Source");
            var target = new PassthroughTool("Target");
            service.AddTool(source);
            service.AddTool(target);
            service.AddConnection(source, target, ConnectionType.Image);

            try
            {
                var results = service.ExecuteAll();

                Assert.Equal(2, results.Count);

                // 폴백 동작 자체는 유지 — Target은 원본 이미지를 입력으로 받음
                var targetResult = results[1];
                Assert.NotNull(targetResult.OutputImage);
                Assert.Equal(80, targetResult.OutputImage!.Width);
                Assert.Equal(48, targetResult.OutputImage.Height);

                // 침묵하지 않고 경고로 표면화
                Assert.NotNull(service.LastPipelineWarning);
                Assert.Contains("Source", service.LastPipelineWarning);
                Assert.Contains("원본", service.LastPipelineWarning);
            }
            finally
            {
                service.ClearTools();
            }
        }

        [Fact]
        public void ExecuteAll_WarningIsResetBetweenRuns()
        {
            var service = PrepareService();
            var toolA = new PassthroughTool("A");
            var toolB = new PassthroughTool("B");
            service.AddTool(toolA);
            service.AddTool(toolB);
            service.AddConnection(toolA, toolB, ConnectionType.Image);
            service.AddConnection(toolB, toolA, ConnectionType.Image); // 사이클

            try
            {
                service.ExecuteAll();
                Assert.NotNull(service.LastPipelineWarning);

                // 사이클 제거 후 재실행 → 경고도 사라져야 함
                service.RemoveConnection(toolB, toolA, ConnectionType.Image);
                service.ExecuteAll();
                Assert.Null(service.LastPipelineWarning);
            }
            finally
            {
                service.ClearTools();
            }
        }

        [Fact]
        public void CoordinatesConnection_CenterOnlySource_PositionsTargetROI()
        {
            // 죽은 "center-based" 폴백 분기 제거의 동작 보존 검증:
            // CenterX/CenterY만 가진 비-Fixture 소스(사용자 ROI 미지정 Target)는
            // 첫 분기(Fixture 스냅샷)가 기준 좌표 중심 기본 ROI(200x200)를 생성한다.
            var service = PrepareService(width: 400, height: 400);
            var source = new NoOutputImageTool("CenterSource"); // CenterX=120, CenterY=110
            var target = new PassthroughTool("RoiTarget");
            service.AddTool(source);
            service.AddTool(target);
            service.AddConnection(source, target, ConnectionType.Coordinates);

            try
            {
                service.ExecuteAll();

                Assert.True(target.UseROI);
                Assert.Equal(new Rect(20, 10, 200, 200), target.ROI);
                Assert.True(target.HasFixtureBaseROI);
            }
            finally
            {
                service.ClearTools();
            }
        }

        [Fact]
        public async System.Threading.Tasks.Task ExecuteAllAsync_ResultsCollectionMatchesReturnedList()
        {
            // ExecuteAllAsync(Task.Run 경유)에서도 반환 시점에 Results가 일괄 반영되어 있어야 함
            var service = PrepareService();
            var tool = new PassthroughTool("Async");
            service.AddTool(tool);

            try
            {
                var results = await service.ExecuteAllAsync();

                Assert.Single(results);
                Assert.Equal(results.Count, service.Results.Count);
                Assert.Same(results[0], service.Results[0]);
            }
            finally
            {
                service.ClearTools();
            }
        }
    }
}
