using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Color = System.Drawing.Color;

namespace VMS.VisionSetup.Services.SynthData
{
    public enum BackgroundMode
    {
        Solid,        // 단색 (cfg.Foreground 대비)
        Gradient,     // 무작위 grayscale gradient
        FromFolder    // 사용자 제공 폴더의 텍스트 없는 라벨 crop을 임의 샘플
    }

    public class BackgroundConfig
    {
        public BackgroundMode Mode { get; set; } = BackgroundMode.Solid;
        /// <summary>Solid 모드의 배경색.</summary>
        public Color SolidColor { get; set; } = Color.White;
        /// <summary>FromFolder 모드: 배경 이미지 폴더 경로 (jpg/png).</summary>
        public string FolderPath { get; set; } = string.Empty;
        /// <summary>흰배경/검정배경 무작위 혼합 (Solid + Gradient 모드).</summary>
        public bool RandomizeInvert { get; set; } = true;
    }

    /// <summary>
    /// 텍스트 렌더 결과 위에 합성될 배경 이미지를 공급.
    /// FromFolder 모드는 실제 라벨 배경(텍스트 영역 외)의 noise/조명 분포를 보존.
    /// </summary>
    public class BackgroundProvider
    {
        private readonly BackgroundConfig _cfg;
        private readonly List<string> _folderFiles = new();

        public BackgroundProvider(BackgroundConfig cfg)
        {
            _cfg = cfg;
            if (cfg.Mode == BackgroundMode.FromFolder && Directory.Exists(cfg.FolderPath))
            {
                _folderFiles.AddRange(
                    Directory.GetFiles(cfg.FolderPath, "*.jpg")
                    .Concat(Directory.GetFiles(cfg.FolderPath, "*.png"))
                    .Concat(Directory.GetFiles(cfg.FolderPath, "*.bmp")));
            }
        }

        /// <summary>
        /// 텍스트 이미지 크기에 맞춰 배경 생성/샘플 후 텍스트를 합성.
        /// textBgColor: 텍스트 렌더 시 사용된 배경색 → 이 색을 투명으로 간주하고 새 배경으로 교체.
        /// </summary>
        public Mat Compose(Mat textImage, Color textBgColor, Color textFgColor, Random rng)
        {
            int w = textImage.Width, h = textImage.Height;
            using Mat background = GenerateBackground(w, h, rng);

            // 텍스트 마스크: textBgColor와 다른 픽셀 = 글자
            using var textGray = textImage.CvtColor(ColorConversionCodes.BGR2GRAY);
            using var bgRefMat = new Mat(textImage.Size(), MatType.CV_8UC3,
                new Scalar(textBgColor.B, textBgColor.G, textBgColor.R));
            using var diff = new Mat();
            Cv2.Absdiff(textImage, bgRefMat, diff);
            using var diffGray = diff.CvtColor(ColorConversionCodes.BGR2GRAY);
            using var charMask = new Mat();
            Cv2.Threshold(diffGray, charMask, 30, 255, ThresholdTypes.Binary);

            // 글자색 결정: invert 옵션 + 배경 평균 밝기로 자동 반전
            Scalar fgScalar = new Scalar(textFgColor.B, textFgColor.G, textFgColor.R);
            if (_cfg.RandomizeInvert && rng.NextDouble() < 0.5)
            {
                double bgMean = background.Mean().Val0;
                fgScalar = bgMean > 127 ? Scalar.All(20) : Scalar.All(235); // 대비 보장
            }

            var result = background.Clone();
            result.SetTo(fgScalar, charMask);
            return result;
        }

        private Mat GenerateBackground(int w, int h, Random rng)
        {
            switch (_cfg.Mode)
            {
                case BackgroundMode.FromFolder when _folderFiles.Count > 0:
                    var file = _folderFiles[rng.Next(_folderFiles.Count)];
                    using (var loaded = Cv2.ImRead(file, ImreadModes.Color))
                    {
                        if (!loaded.Empty())
                        {
                            var resized = new Mat();
                            int cropW = Math.Min(loaded.Width, Math.Max(w, w * 2));
                            int cropH = Math.Min(loaded.Height, Math.Max(h, h * 2));
                            int cx = rng.Next(Math.Max(1, loaded.Width - cropW));
                            int cy = rng.Next(Math.Max(1, loaded.Height - cropH));
                            using (var crop = new Mat(loaded, new Rect(cx, cy, cropW, cropH)))
                                Cv2.Resize(crop, resized, new OpenCvSharp.Size(w, h));
                            return resized;
                        }
                    }
                    return new Mat(h, w, MatType.CV_8UC3,
                        new Scalar(_cfg.SolidColor.B, _cfg.SolidColor.G, _cfg.SolidColor.R));

                case BackgroundMode.Gradient:
                    int v1 = rng.Next(80, 200);
                    int v2 = rng.Next(80, 200);
                    var grad = new Mat(h, w, MatType.CV_8UC3);
                    for (int y = 0; y < h; y++)
                    {
                        double t = (double)y / Math.Max(1, h - 1);
                        int v = (int)(v1 * (1 - t) + v2 * t);
                        Cv2.Line(grad, new OpenCvSharp.Point(0, y), new OpenCvSharp.Point(w - 1, y),
                            new Scalar(v, v, v), 1);
                    }
                    return grad;

                default: // Solid
                    return new Mat(h, w, MatType.CV_8UC3,
                        new Scalar(_cfg.SolidColor.B, _cfg.SolidColor.G, _cfg.SolidColor.R));
            }
        }
    }
}
