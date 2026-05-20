using OpenCvSharp;
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.Versioning;

namespace VMS.VisionSetup.Services.SynthData
{
    /// <summary>
    /// 텍스트 렌더 설정. 폰트/크기/스타일/색상/패딩.
    /// </summary>
    public class TextRenderConfig
    {
        public string FontFamily { get; set; } = "Arial";
        public float FontSize { get; set; } = 40;
        public FontStyle Style { get; set; } = FontStyle.Regular;
        public Color Foreground { get; set; } = Color.Black;
        public Color Background { get; set; } = Color.White;
        /// <summary>텍스트 주변 quiet zone(px). 글자 잘림 방지 + 폰트 디센더 확보용.</summary>
        public int PaddingPx { get; set; } = 8;
    }

    /// <summary>
    /// System.Drawing(GDI+) 기반 텍스트 렌더러. WPF 의존 없이 free-threaded.
    /// 결과는 OpenCV Mat(BGR 3채널)로 반환 — 이후 증강 파이프라인에 그대로 투입.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public static class TextRenderer
    {
        public static Mat Render(string text, TextRenderConfig cfg)
        {
            if (string.IsNullOrEmpty(text)) text = " ";

            // 1) 텍스트 측정 — 캔버스 크기 결정
            using var measureBmp = new Bitmap(1, 1, PixelFormat.Format24bppRgb);
            using var measureGfx = Graphics.FromImage(measureBmp);
            using var font = new Font(cfg.FontFamily, cfg.FontSize, cfg.Style, GraphicsUnit.Pixel);
            var size = measureGfx.MeasureString(text, font);

            int w = Math.Max(1, (int)Math.Ceiling(size.Width)) + 2 * cfg.PaddingPx;
            int h = Math.Max(1, (int)Math.Ceiling(size.Height)) + 2 * cfg.PaddingPx;

            // 2) 캔버스 + 렌더링
            using var bitmap = new Bitmap(w, h, PixelFormat.Format24bppRgb);
            using (var gfx = Graphics.FromImage(bitmap))
            {
                gfx.Clear(cfg.Background);
                gfx.TextRenderingHint = TextRenderingHint.AntiAlias;
                gfx.SmoothingMode = SmoothingMode.AntiAlias;
                gfx.InterpolationMode = InterpolationMode.HighQualityBicubic;
                using var brush = new SolidBrush(cfg.Foreground);
                gfx.DrawString(text, font, brush, cfg.PaddingPx, cfg.PaddingPx);
            }

            return BitmapToMat(bitmap);
        }

        /// <summary>
        /// System.Drawing Bitmap(24bpp RGB, BGR 메모리 순서) → OpenCV Mat(CV_8UC3, BGR).
        /// LockBits 직접 메모리 복사 — PNG 인코딩 우회.
        /// </summary>
        public static Mat BitmapToMat(Bitmap bmp)
        {
            var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
            BitmapData data = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
            try
            {
                using var view = Mat.FromPixelData(bmp.Height, bmp.Width,
                    MatType.CV_8UC3, data.Scan0, data.Stride);
                return view.Clone(); // LockBits 버퍼에서 분리
            }
            finally { bmp.UnlockBits(data); }
        }
    }
}
