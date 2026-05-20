using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.Versioning;

namespace VMS.VisionSetup.Services.SynthData
{
    /// <summary>
    /// 합성 OCR 데이터셋 생성 전체 설정.
    /// </summary>
    public class SynthDataConfig
    {
        public List<string> Patterns { get; set; } = new() { "DD/MM/YYYY", "DDDDDDD" };
        public List<string> FontFamilies { get; set; } = new() { "Arial", "Consolas" };
        public float FontSizeMin { get; set; } = 28;
        public float FontSizeMax { get; set; } = 64;
        public bool RandomBoldItalic { get; set; } = true;

        public int SampleCount { get; set; } = 200;
        public double ValRatio { get; set; } = 0.2;

        public BackgroundConfig Background { get; set; } = new();
        public AugmentationConfig Augmentation { get; set; } = new();
        public DatasetFormat OutputFormat { get; set; } = DatasetFormat.PaddleOcrRec;
        public string OutputDir { get; set; } = string.Empty;

        public int? RandomSeed { get; set; }
    }

    /// <summary>
    /// 합성 OCR 데이터셋 오케스트레이터.
    /// 패턴 → 무작위 문자열 → 폰트 렌더 → 배경 합성 → 증강 → 데이터셋 저장.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public class SyntheticOcrDataGenerator
    {
        public event Action<int, int>? Progress; // (current, total)

        public (int Train, int Val) Generate(SynthDataConfig cfg)
        {
            if (string.IsNullOrWhiteSpace(cfg.OutputDir))
                throw new ArgumentException("OutputDir is required");
            if (cfg.Patterns.Count == 0)
                throw new ArgumentException("At least one pattern required");
            if (cfg.FontFamilies.Count == 0)
                throw new ArgumentException("At least one font required");

            var rng = cfg.RandomSeed.HasValue ? new Random(cfg.RandomSeed.Value) : new Random();
            var bgProvider = new BackgroundProvider(cfg.Background);
            var writer = new DatasetWriter(cfg.OutputDir, cfg.OutputFormat, cfg.ValRatio);

            for (int i = 0; i < cfg.SampleCount; i++)
            {
                string pattern = cfg.Patterns[rng.Next(cfg.Patterns.Count)];
                string label = PatternStringGenerator.Generate(pattern, rng);

                var fontCfg = new TextRenderConfig
                {
                    FontFamily = cfg.FontFamilies[rng.Next(cfg.FontFamilies.Count)],
                    FontSize = (float)(cfg.FontSizeMin
                        + rng.NextDouble() * (cfg.FontSizeMax - cfg.FontSizeMin)),
                    Style = cfg.RandomBoldItalic ? RandomStyle(rng) : System.Drawing.FontStyle.Regular,
                    Foreground = Color.Black,
                    Background = Color.White,
                    PaddingPx = 10
                };

                using var rendered = TextRenderer.Render(label, fontCfg);
                using var composed = bgProvider.Compose(rendered,
                    fontCfg.Background, fontCfg.Foreground, rng);
                using var augmented = Augmentation.Apply(composed, cfg.Augmentation, rng);

                writer.AddSample(augmented, label, rng);

                if ((i + 1) % Math.Max(1, cfg.SampleCount / 20) == 0 || i == cfg.SampleCount - 1)
                    Progress?.Invoke(i + 1, cfg.SampleCount);
            }

            writer.FinalizeWriter();
            return writer.Counts;
        }

        private static System.Drawing.FontStyle RandomStyle(Random rng)
        {
            int r = rng.Next(4);
            return r switch
            {
                1 => System.Drawing.FontStyle.Bold,
                2 => System.Drawing.FontStyle.Italic,
                3 => System.Drawing.FontStyle.Bold | System.Drawing.FontStyle.Italic,
                _ => System.Drawing.FontStyle.Regular
            };
        }
    }
}
