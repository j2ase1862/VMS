using OpenCvSharp;
using VMS.VisionSetup.Models;
using VMS.VisionSetup.Services;
using Xunit;

namespace VMS.VisionSetup.Tests
{
    /// <summary>
    /// VisionResult Mat 해제 정책 회귀 테스트.
    /// 연속 검사(AUTO RUN)에서 이전 실행 결과의 OutputImage/OverlayImage가
    /// GC 파이널라이저에 의존하지 않고 다음 실행 시점에 결정적으로 해제되는지 검증.
    /// VisionService.Instance 싱글턴을 사용하므로 동일 Collection으로 직렬 실행.
    /// </summary>
    [Collection("VisionServiceSingleton")]
    public class VisionResultMatReleaseTests
    {
        /// <summary>테스트용 최소 도구 — 입력을 복사해 OutputImage/OverlayImage로 반환</summary>
        private sealed class PassthroughTool : VisionToolBase
        {
            public PassthroughTool()
            {
                Name = "Passthrough";
                ToolType = "PassthroughTool";
            }

            public override VisionResult Execute(Mat inputImage)
            {
                var result = new VisionResult
                {
                    Success = true,
                    Message = "ok",
                    OutputImage = inputImage.Clone(),
                    OverlayImage = inputImage.Clone()
                };
                // 실제 도구들과 동일하게 도구 내부에서 LastResult를 먼저 set
                // (VisionService가 같은 인스턴스를 다시 set — 해제되면 안 됨)
                LastResult = result;
                return result;
            }

            public override VisionToolBase Clone() => new PassthroughTool();
        }

        [Fact]
        public void ReleaseMats_DisposesAndNulls_AndIsIdempotent()
        {
            var output = new Mat(8, 8, MatType.CV_8UC1);
            var overlay = new Mat(8, 8, MatType.CV_8UC1);
            var result = new VisionResult { OutputImage = output, OverlayImage = overlay };

            result.ReleaseMats();

            Assert.Null(result.OutputImage);
            Assert.Null(result.OverlayImage);
            Assert.True(output.IsDisposed);
            Assert.True(overlay.IsDisposed);

            // 여러 컬렉션이 동일 인스턴스를 공유하므로 중복 호출이 안전해야 함
            result.ReleaseMats();
        }

        [Fact]
        public void LastResult_SameInstanceReassign_DoesNotRelease()
        {
            var tool = new PassthroughTool();
            var result = new VisionResult { OutputImage = new Mat(8, 8, MatType.CV_8UC1) };

            // 도구 내부 set 후 VisionService가 동일 인스턴스를 재-set하는 시나리오
            tool.LastResult = result;
            tool.LastResult = result;

            Assert.NotNull(result.OutputImage);
            Assert.False(result.OutputImage!.IsDisposed);
        }

        [Fact]
        public void LastResult_Replace_ReleasesPreviousMats()
        {
            var tool = new PassthroughTool();
            var previous = new VisionResult
            {
                OutputImage = new Mat(8, 8, MatType.CV_8UC1),
                OverlayImage = new Mat(8, 8, MatType.CV_8UC1)
            };
            var previousOutput = previous.OutputImage;

            tool.LastResult = previous;
            tool.LastResult = new VisionResult();

            Assert.Null(previous.OutputImage);
            Assert.Null(previous.OverlayImage);
            Assert.True(previousOutput!.IsDisposed);
        }

        [Fact]
        public void ExecuteAll_SecondRun_ReleasesPreviousRunMats()
        {
            var service = VisionService.Instance;
            service.ClearTools();

            using (var image = new Mat(32, 32, MatType.CV_8UC3, new Scalar(128, 128, 128)))
            {
                service.SetImage(image);    // 내부에서 Clone — 원본은 여기서 해제
            }

            var tool = new PassthroughTool();
            service.AddTool(tool);

            try
            {
                var firstRun = service.ExecuteAll();
                var firstResult = Assert.Single(firstRun);
                var firstOutput = firstResult.OutputImage;
                var firstOverlay = firstResult.OverlayImage;

                // 도구 내부 set + VisionService 재-set(동일 인스턴스)이 현재 결과를 해제하지 않아야 함
                Assert.NotNull(firstOutput);
                Assert.NotNull(firstOverlay);
                Assert.False(firstOutput!.IsDisposed);

                var secondRun = service.ExecuteAll();

                // 이전 실행 결과의 Mat은 다음 실행 시작 시점에 결정적으로 해제됨
                Assert.Null(firstResult.OutputImage);
                Assert.Null(firstResult.OverlayImage);
                Assert.True(firstOutput.IsDisposed);
                Assert.True(firstOverlay!.IsDisposed);

                // 새 실행 결과는 살아있어야 함 (표시 경로가 복사본을 만들 수 있도록)
                var secondResult = Assert.Single(secondRun);
                Assert.NotNull(secondResult.OutputImage);
                Assert.False(secondResult.OutputImage!.IsDisposed);
            }
            finally
            {
                service.ClearTools();
            }
        }
    }
}
