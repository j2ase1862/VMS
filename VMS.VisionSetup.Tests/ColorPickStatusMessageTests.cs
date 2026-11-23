using VMS.VisionSetup.ViewModels;
using VMS.VisionSetup.VisionTools.Color;
using VMS.VisionSetup.VisionTools.ImageProcessing;
using Xunit;

namespace VMS.VisionSetup.Tests
{
    /// <summary>
    /// [Pick from Image] 뒤 이미지를 클릭했을 때 상태 표시줄 문구 — 선택 모델에 값을 채웠을 때만 "Picked …",
    /// 선택한 모델이 없어 못 채웠으면 무엇을 해야 하는지 알린다(Color Extract · Color Match 공통).
    /// </summary>
    public class ColorPickStatusMessageTests
    {
        private const string NeedModel = "색을 채우지 못했습니다. Models 에서 모델을 먼저 선택한 뒤 [Pick from Image] 를 다시 누르세요.";

        [Fact]
        public void ColorExtract_모델을_채우면_Picked_문구()
        {
            var tool = new ColorExtractTool();

            var status = MainViewModel.ApplyColorPick(tool, 12, 34, 0, 200, 0, isGray: false);

            Assert.Equal("Picked @(12,34) BGR=(0,200,0)", status);
            Assert.Equal(55, tool.HueMin);
        }

        [Fact]
        public void ColorExtract_선택_모델이_없으면_모델_선택_안내()
        {
            var tool = new ColorExtractTool { SelectedModel = null };

            var status = MainViewModel.ApplyColorPick(tool, 12, 34, 0, 200, 0, isGray: false);

            Assert.Equal(NeedModel, status);
            Assert.DoesNotContain("Picked", status);
        }

        [Fact]
        public void ColorMatch_모델을_채우면_Picked_문구()
        {
            var tool = new ColorMatchTool();

            var status = MainViewModel.ApplyColorPick(tool, 5, 6, 0, 0, 255, isGray: false);

            Assert.Equal("Picked @(5,6) BGR=(0,0,255)", status);
            Assert.NotEqual(0, tool.SelectedModel!.MeanL);
        }

        [Fact]
        public void ColorMatch_선택_모델이_없으면_모델_선택_안내_값은_그대로()
        {
            var tool = new ColorMatchTool();
            var model = tool.SelectedModel!;
            int before = model.MeanL;
            tool.SelectedModel = null;

            var status = MainViewModel.ApplyColorPick(tool, 5, 6, 0, 0, 255, isGray: false);

            Assert.Equal(NeedModel, status);
            Assert.Equal(before, model.MeanL);
        }

        [Fact]
        public void 흑백_이미지는_Gray_값으로_표시()
        {
            var tool = new ColorMatchTool();

            var status = MainViewModel.ApplyColorPick(tool, 1, 2, 128, 128, 128, isGray: true);

            Assert.Equal("Picked @(1,2) Gray=128", status);
        }

        [Fact]
        public void 색_도구가_아니면_문구를_바꾸지_않는다()
        {
            Assert.Null(MainViewModel.ApplyColorPick(new ThresholdTool(), 1, 2, 3, 4, 5, isGray: false));
            Assert.Null(MainViewModel.ApplyColorPick(null, 1, 2, 3, 4, 5, isGray: false));
        }
    }
}
