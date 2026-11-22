using CommunityToolkit.Mvvm.Messaging;
using OpenCvSharp;
using VMS.VisionSetup.Models;
using VMS.VisionSetup.Services;
using VMS.VisionSetup.ViewModels.ToolSettings;
using VMS.VisionSetup.VisionTools.Color;
using Xunit;

namespace VMS.VisionSetup.Tests
{
    /// <summary>
    /// Color Extract 의 [Pick from Image] — 이미지에서 대상 색을 클릭해 선택 모델의 색 범위를 채운다(매뉴얼 §7.4 ②).
    /// 종전에는 클릭 추출이 Color Match 에서만 됐다.
    /// </summary>
    public class ColorExtractPickFromPixelTests
    {
        [Fact]
        public void 클릭한_색의_HSV_둘레로_선택_모델_범위를_채운다()
        {
            var tool = new ColorExtractTool();

            // 순수한 빨강(BGR 0,0,255) → HSV (0, 255, 255)
            Assert.True(tool.PickFromPixel(0, 0, 255));

            // Hue 는 0 을 가로질러 감긴다(175 ~ 5)
            Assert.Equal(175, tool.HueMin);
            Assert.Equal(5, tool.HueMax);
            Assert.Equal(235, tool.SaturationMin);
            Assert.Equal(255, tool.SaturationMax);
            Assert.Equal(235, tool.ValueMin);
            Assert.Equal(255, tool.ValueMax);
        }

        [Fact]
        public void 채운_범위로_실행하면_클릭한_색이_추출된다()
        {
            // 왼쪽 절반 초록, 오른쪽 절반 파랑
            using var image = new Mat(40, 80, MatType.CV_8UC3, new Scalar(255, 0, 0));
            image[new Rect(0, 0, 40, 40)].SetTo(new Scalar(0, 200, 0));

            var tool = new ColorExtractTool { MorphKernelSize = 0 };
            var px = image.Get<Vec3b>(20, 10);
            Assert.True(tool.PickFromPixel(px.Item0, px.Item1, px.Item2));

            var result = tool.Execute(image);

            Assert.True(result.Success, result.Message);
            using var mask = result.OutputImage!.Channels() == 1
                ? result.OutputImage.Clone()
                : result.OutputImage.CvtColor(ColorConversionCodes.BGR2GRAY);
            Assert.Equal(255, mask.Get<byte>(20, 10));   // 초록 = 추출
            Assert.Equal(0, mask.Get<byte>(20, 60));     // 파랑 = 제외
        }

        [Fact]
        public void 선택한_모델만_바뀐다()
        {
            var tool = new ColorExtractTool();
            var first = tool.SelectedModel!;
            var second = new ColorModel { Name = "Model 2" };
            tool.Models.Add(second);
            tool.SelectedModel = second;

            Assert.True(tool.PickFromPixel(0, 200, 0));

            Assert.Equal(0, first.HueMin);
            Assert.Equal(179, first.HueMax);
            Assert.Equal(55, second.HueMin);   // 초록 Hue 60 ± 5
            Assert.Equal(65, second.HueMax);
        }

        [Fact]
        public void 선택한_모델이_없으면_아무것도_하지_않는다()
        {
            var tool = new ColorExtractTool { SelectedModel = null };

            Assert.False(tool.PickFromPixel(10, 20, 30));
        }

        [Fact]
        public void 설정_화면의_Pick_명령은_이미지_클릭_요청을_보낸다()
        {
            var recipient = new object();
            int requested = 0;
            WeakReferenceMessenger.Default.Register<RequestPickColorMessage>(recipient, (_, _) => requested++);
            try
            {
                var vm = new ColorExtractToolSettingsViewModel(new ColorExtractTool());

                vm.PickColorCommand.Execute(null);

                Assert.Equal(1, requested);
            }
            finally
            {
                WeakReferenceMessenger.Default.Unregister<RequestPickColorMessage>(recipient);
            }
        }

        [Fact]
        public void 클릭으로_채운_값은_설정_화면의_학습_색_표시에_반영된다()
        {
            var tool = new ColorExtractTool();
            var vm = new ColorExtractToolSettingsViewModel(tool);
            var changed = new System.Collections.Generic.List<string?>();
            vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

            tool.PickFromPixel(0, 200, 0);

            Assert.Contains(nameof(ColorExtractToolSettingsViewModel.LearnedColorText), changed);
            Assert.Equal("H=60, S=245, V=200", vm.LearnedColorText);
        }
    }
}
