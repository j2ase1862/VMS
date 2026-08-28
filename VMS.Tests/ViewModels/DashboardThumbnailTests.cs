using System.Windows.Media;
using System.Windows.Media.Imaging;
using VMS.ViewModels;
using Xunit;

namespace VMS.Tests.ViewModels
{
    /// <summary>
    /// NG 이력 썸네일 축소 회귀 (실증 PC 2026-08-29): 원본 5MP BitmapSource 를
    /// 그대로 보관하면 이력 10장이 최대 150MB 를 상시 점유했다.
    /// </summary>
    public class DashboardThumbnailTests
    {
        private static BitmapSource MakeImage(int width, int height)
        {
            var stride = width * 3;
            var pixels = new byte[stride * height];
            var bmp = BitmapSource.Create(
                width, height, 96, 96, PixelFormats.Bgr24, null, pixels, stride);
            bmp.Freeze();
            return bmp;
        }

        [Fact]
        public void CreateThumbnail_LargeImage_DownscalesToMaxEdge()
        {
            var src = MakeImage(2448, 2048);

            var thumb = DashboardViewModel.CreateThumbnail(src);

            Assert.NotSame(src, thumb);
            Assert.Equal(960, thumb.PixelWidth);
            Assert.True(thumb.PixelHeight < 2048);
            Assert.True(thumb.IsFrozen);
        }

        [Fact]
        public void CreateThumbnail_SmallImage_ReturnsOriginal()
        {
            var src = MakeImage(640, 480);

            var thumb = DashboardViewModel.CreateThumbnail(src);

            Assert.Same(src, thumb);
        }

        [Fact]
        public void RecordInspectionResult_Ng_StoresDownscaledThumbnail()
        {
            var vm = new DashboardViewModel();
            var src = MakeImage(2448, 2048);

            vm.RecordInspectionResult(ok: false, cameraName: "Cam1", image: src);

            var item = Assert.Single(vm.NgImageHistory);
            Assert.NotNull(item.Thumbnail);
            Assert.True(item.Thumbnail!.PixelWidth <= 960);
        }
    }
}
