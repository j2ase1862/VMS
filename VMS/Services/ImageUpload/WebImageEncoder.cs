using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using VMS.Core.Imaging;

namespace VMS.Services.ImageUpload
{
    /// <summary>
    /// Web 전송용 이미지 바이트 인코딩 — 풀(원본 포맷) 또는 썸네일(JPEG 리사이즈).
    /// WPF <see cref="BitmapEncoder"/> 사용. 입력 BitmapSource 는 frozen 이어야 안전(크로스 스레드).
    /// </summary>
    public static class WebImageEncoder
    {
        public const int ThumbnailJpegQuality = 80;

        /// <summary>옵션의 화질 설정에 따라 전송 바이트와 확장자를 생성.</summary>
        public static (byte[] bytes, string ext) Encode(BitmapSource image, ImageSaveOptions opts)
        {
            if (opts.WebImageVariant == WebImageVariant.Thumbnail)
            {
                var thumb = Resize(image, opts.ThumbnailMaxEdge);
                return (EncodeWith(new JpegBitmapEncoder { QualityLevel = ThumbnailJpegQuality }, thumb), "jpg");
            }

            // 풀(원본) — 로컬 저장과 동일 포맷/품질.
            return EncodeFull(image, opts.Format, opts.JpegQuality);
        }

        /// <summary>원본 해상도 그대로 지정 포맷으로 인코딩 — Web 풀 전송과 MLOps 학습 데이터 전송이 공유.</summary>
        public static (byte[] bytes, string ext) EncodeFull(BitmapSource image, ImageSaveFormat format, int jpegQuality)
        {
            var ext = ImageSaveOptions.ExtensionFor(format);
            BitmapEncoder encoder = format switch
            {
                ImageSaveFormat.Jpeg => new JpegBitmapEncoder { QualityLevel = ImageSaveOptions.ClampQuality(jpegQuality) },
                ImageSaveFormat.Bmp => new BmpBitmapEncoder(),
                ImageSaveFormat.Tiff => new TiffBitmapEncoder { Compression = TiffCompressOption.Zip },
                _ => new PngBitmapEncoder(),
            };
            return (EncodeWith(encoder, image), ext);
        }

        /// <summary>장변이 maxEdge 보다 크면 비율 유지 축소. 작으면 원본 그대로.</summary>
        private static BitmapSource Resize(BitmapSource src, int maxEdge)
        {
            int w = src.PixelWidth, h = src.PixelHeight;
            int longEdge = w > h ? w : h;
            if (longEdge <= maxEdge || longEdge == 0) return src;

            double scale = (double)maxEdge / longEdge;
            var scaled = new TransformedBitmap(src, new ScaleTransform(scale, scale));
            scaled.Freeze();
            return scaled;
        }

        private static byte[] EncodeWith(BitmapEncoder encoder, BitmapSource image)
        {
            encoder.Frames.Add(BitmapFrame.Create(image));
            using var ms = new MemoryStream();
            encoder.Save(ms);
            return ms.ToArray();
        }
    }
}
