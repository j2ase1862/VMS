using OpenCvSharp;
using VMS.VisionSetup.VisionTools.PatternMatching;
using Xunit;

namespace VMS.VisionSetup.Tests
{
    /// <summary>
    /// FeatureMatchTool OutputImage 회귀 테스트.
    /// 위치 검출 도구는 이미지를 변형하지 않지만, Image 연결 소스로 쓰일 때
    /// 하류가 조용히 원본으로 폴백하지 않도록 입력을 passthrough로 OutputImage에 설정해야 함.
    /// </summary>
    public class FeatureMatchOutputImageTests
    {
        private static Mat CreateSquareImage(int size, Rect square)
        {
            var image = new Mat(size, size, MatType.CV_8UC1, Scalar.Black);
            Cv2.Rectangle(image, square, Scalar.White, -1);
            return image;
        }

        [Fact]
        public void Execute_TrainedModel_SetsPassthroughOutputImage()
        {
            var tool = new FeatureMatchTool();
            using var pattern = CreateSquareImage(64, new Rect(16, 16, 32, 32));
            Assert.True(tool.TrainPattern(pattern), "합성 패턴 학습이 실패하면 테스트 전제가 깨짐");

            using var search = CreateSquareImage(200, new Rect(80, 80, 32, 32));
            var result = tool.Execute(search);

            // 매칭 성패와 무관하게 (학습 모델로 실행된 경우) 입력 passthrough가 설정되어야 함
            Assert.NotNull(result.OutputImage);
            Assert.False(result.OutputImage!.Empty());
            Assert.Equal(search.Width, result.OutputImage.Width);
            Assert.Equal(search.Height, result.OutputImage.Height);
            Assert.Equal(1, result.OutputImage.Channels());

            // 소유권 규칙: OutputImage는 입력의 복사본 (공유 참조가 아님) —
            // 재실행 시 ReleaseMats가 Dispose해도 입력/업스트림 Mat에 영향 없어야 함
            Assert.NotSame(search, result.OutputImage);
            result.ReleaseMats();
            Assert.False(search.IsDisposed);

            result.OverlayImage?.Dispose();
        }

        [Fact]
        public void Execute_NonGrayInput_FailsWithoutOutputImage()
        {
            // 입력 계약 위반(컬러 입력) 시에는 실행 자체가 거부되므로 OutputImage 없음 —
            // 이 경우 하류 폴백은 VisionService의 파이프라인 경고로 표면화됨
            var tool = new FeatureMatchTool();
            using var color = new Mat(64, 64, MatType.CV_8UC3, Scalar.Black);

            var result = tool.Execute(color);

            Assert.False(result.Success);
            Assert.Null(result.OutputImage);
        }
    }
}
