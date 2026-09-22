using System;
using OpenCvSharp;
using VMS.ViewModels;
using Xunit;

namespace VMS.Tests.ViewModels
{
    /// <summary>
    /// 검사 입력 변환(BitmapSource → Mat) 검증.
    ///
    /// WPF 의 행 보폭은 4바이트 정렬, Mat 의 행 보폭은 width*3 이라 폭이 4의 배수가
    /// 아니면 두 값이 다르다. 종전 구현은 정렬된 버퍼를 통째로 평탄 복사해서 그런
    /// 폭에서는 행이 한 칸씩 밀려 영상이 사선으로 찌그러진 채 검사에 들어갔다.
    /// </summary>
    public class BitmapSourceToMatTests
    {
        // 폭 % 4 != 0 → WPF stride(= (w*3+3)&~3) ≠ Mat step(= w*3)
        [Theory]
        [InlineData(127)]
        [InlineData(126)]
        [InlineData(125)]
        [InlineData(124)]   // 대조군: 정렬이 맞아떨어지는 폭
        public void BitmapSourceToMat_PreservesPixelsAtAnyWidth(int width)
        {
            const int height = 9;

            // 행마다 다른 색 → 행이 밀리면 값이 곧바로 어긋난다.
            using var source = new Mat(height, width, MatType.CV_8UC3);
            for (int y = 0; y < height; y++)
                source.Row(y).SetTo(new Scalar(y * 10, y * 10 + 1, y * 10 + 2));

            var bitmap = OpenCvSharp.WpfExtensions.BitmapSourceConverter.ToBitmapSource(source);
            bitmap.Freeze();

            using var mat = CameraViewModel.BitmapSourceToMat(bitmap);

            Assert.NotNull(mat);
            Assert.Equal(width, mat!.Width);
            Assert.Equal(height, mat.Height);

            var indexer = mat.GetGenericIndexer<Vec3b>();
            for (int y = 0; y < height; y++)
            {
                foreach (var x in new[] { 0, width / 2, width - 1 })
                {
                    var px = indexer[y, x];
                    Assert.Equal(y * 10, px.Item0);
                    Assert.Equal(y * 10 + 1, px.Item1);
                    Assert.Equal(y * 10 + 2, px.Item2);
                }
            }
        }
    }
}
