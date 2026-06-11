using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using VMS.Core.Imaging;

namespace VMS.Services
{
    /// <summary>
    /// 검사 판정 결과 이미지를 <see cref="ImageSaveOptions"/> 설정에 따라 디스크에 기록.
    /// 폴더 구조: {BaseDir}\{yyyy-MM-dd}\{OK|NG}\{규칙 파일명}.{확장자}
    /// WPF <see cref="BitmapEncoder"/> 로 포맷/품질을 적용 — OpenCV 의존 없음.
    /// 인코딩/쓰기는 UI 스레드를 막지 않도록 백그라운드에서 수행하며,
    /// 실패는 검사 흐름을 중단시키지 않게 삼킨다(Debug 로그만).
    /// </summary>
    public static class InspectionImageSaver
    {
        /// <summary>
        /// 판정(ctx.Ok)에 해당하는 저장이 활성화되어 있고 BaseDir 가 지정된 경우에만
        /// 이미지를 비동기로 디스크에 저장. 그 외에는 즉시 반환.
        /// </summary>
        public static void Save(ImageSaveOptions? options, BitmapSource? image, InspectionImageContext? context)
        {
            if (options == null || image == null || context == null) return;

            var enabled = context.Ok ? options.SaveOkImages : options.SaveNgImages;
            if (!enabled) return;

            if (string.IsNullOrWhiteSpace(options.BaseDir)) return;

            // 크로스 스레드 인코딩을 위해 frozen 보장 — 가변 BitmapSource 는 클론 후 freeze.
            BitmapSource frozen = image;
            if (!frozen.IsFrozen)
            {
                frozen = image.Clone();
                frozen.Freeze();
            }

            var opts = options;
            var ctx = context;
            _ = Task.Run(() => WriteToDisk(opts, frozen, ctx));
        }

        private static void WriteToDisk(ImageSaveOptions opts, BitmapSource image, InspectionImageContext ctx)
        {
            try
            {
                // {BaseDir}\{연월일}\{OK|NG}
                var dateFolder = ctx.Timestamp.ToString("yyyy-MM-dd");
                var verdictFolder = ctx.Ok ? "OK" : "NG";
                var dir = Path.Combine(opts.BaseDir, dateFolder, verdictFolder);
                Directory.CreateDirectory(dir);

                var ext = ImageSaveOptions.ExtensionFor(opts.Format);
                var baseName = opts.BuildFileName(ctx.ToTokenValues(opts));
                var fullPath = EnsureUniquePath(Path.Combine(dir, $"{baseName}.{ext}"));

                BitmapEncoder encoder = opts.Format switch
                {
                    ImageSaveFormat.Jpeg => new JpegBitmapEncoder { QualityLevel = ImageSaveOptions.ClampQuality(opts.JpegQuality) },
                    ImageSaveFormat.Bmp => new BmpBitmapEncoder(),
                    ImageSaveFormat.Tiff => new TiffBitmapEncoder { Compression = TiffCompressOption.Zip },
                    _ => new PngBitmapEncoder(),
                };
                encoder.Frames.Add(BitmapFrame.Create(image));

                using var stream = new FileStream(fullPath, FileMode.Create, FileAccess.Write);
                encoder.Save(stream);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[InspectionImageSaver] {(ctx.Ok ? "OK" : "NG")} 이미지 저장 실패: {ex.Message}");
            }
        }

        /// <summary>
        /// 타임스탬프를 끄는 등 규칙에 따라 파일명이 충돌할 수 있으므로,
        /// 동일 이름이 있으면 " (1)", " (2)" … 를 붙여 덮어쓰기를 방지.
        /// </summary>
        private static string EnsureUniquePath(string path)
        {
            if (!File.Exists(path)) return path;
            var dir = Path.GetDirectoryName(path)!;
            var name = Path.GetFileNameWithoutExtension(path);
            var ext = Path.GetExtension(path);
            for (int i = 1; i < 10000; i++)
            {
                var candidate = Path.Combine(dir, $"{name} ({i}){ext}");
                if (!File.Exists(candidate)) return candidate;
            }
            return path;
        }
    }
}
