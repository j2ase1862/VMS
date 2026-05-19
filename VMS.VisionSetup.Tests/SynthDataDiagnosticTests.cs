using OpenCvSharp;
using System.Drawing;
using System.IO;
using System.Linq;
using VMS.VisionSetup.Services.SynthData;
using VMS.VisionSetup.VisionTools.Identification;
using Xunit;
using Xunit.Abstractions;

namespace VMS.VisionSetup.Tests
{
    /// <summary>
    /// 합성 데이터 생성기 진단.
    /// 50샘플 생성 → PaddleOCR rec 포맷 검증 → 생성 데이터에 PP-OCR 돌려 인식률 확인.
    /// </summary>
    public class SynthDataDiagnosticTests
    {
        private readonly ITestOutputHelper _out;
        public SynthDataDiagnosticTests(ITestOutputHelper output) { _out = output; }

        [Fact]
        public void Generate_50Samples_PaddleRecFormat()
        {
            var outputDir = Path.Combine(Path.GetTempPath(), "vms_synth_test_" + System.Guid.NewGuid().ToString("N").Substring(0, 8));
            try
            {
                var cfg = new SynthDataConfig
                {
                    Patterns = new() { "DD/MM/YYYY", "DDDDDDD" },
                    FontFamilies = new() { "Arial", "Consolas" },
                    FontSizeMin = 28,
                    FontSizeMax = 56,
                    RandomBoldItalic = false,
                    SampleCount = 50,
                    ValRatio = 0.2,
                    OutputFormat = DatasetFormat.PaddleOcrRec,
                    OutputDir = outputDir,
                    RandomSeed = 42,
                    Background = new BackgroundConfig
                    {
                        Mode = BackgroundMode.Solid,
                        SolidColor = Color.White,
                        RandomizeInvert = true
                    },
                    Augmentation = new AugmentationConfig
                    {
                        RotationDegMax = 3,
                        PerspectiveJitter = 0.01,
                        BlurMaxKernel = 3,
                        NoiseStdMax = 3,
                        BrightnessJitter = 0.1,
                        ContrastJitter = 0.1
                    }
                };

                var gen = new SyntheticOcrDataGenerator();
                var (train, val) = gen.Generate(cfg);
                _out.WriteLine($"Generated: train={train}, val={val}, total={train + val}");

                Assert.Equal(50, train + val);
                Assert.True(File.Exists(Path.Combine(outputDir, "train_rec.txt")));
                Assert.True(File.Exists(Path.Combine(outputDir, "val_rec.txt")));
                Assert.True(File.Exists(Path.Combine(outputDir, "dict.txt")));
                Assert.True(Directory.Exists(Path.Combine(outputDir, "train_crops")));

                var trainLines = File.ReadAllLines(Path.Combine(outputDir, "train_rec.txt"));
                Assert.Equal(train, trainLines.Length);

                // 라인 포맷: image_path\tlabel
                foreach (var line in trainLines.Take(3))
                {
                    var parts = line.Split('\t');
                    Assert.Equal(2, parts.Length);
                    Assert.True(File.Exists(Path.Combine(outputDir, parts[0])));
                    _out.WriteLine($"  sample: {parts[0]}  →  \"{parts[1]}\"");
                }

                // dict.txt — unique char 목록
                var dictLines = File.ReadAllLines(Path.Combine(outputDir, "dict.txt"));
                _out.WriteLine($"Dict chars ({dictLines.Length}): {string.Join("", dictLines)}");
                Assert.Contains("/", dictLines);
                Assert.Contains("0", dictLines);
                Assert.Contains("9", dictLines);
            }
            finally
            {
                if (Directory.Exists(outputDir))
                    try { Directory.Delete(outputDir, true); } catch { }
            }
        }

        /// <summary>
        /// 생성된 합성 샘플 일부를 OCRTool(PP-OCR)로 인식 → 라벨과 일치 여부 측정.
        /// 합성 품질이 학습 가치 있는 수준인지 sanity check.
        /// </summary>
        [Fact]
        public void GeneratedSamples_PaddleOcr_RecognitionRate()
        {
            var outputDir = Path.Combine(Path.GetTempPath(), "vms_synth_pp_" + System.Guid.NewGuid().ToString("N").Substring(0, 8));
            try
            {
                var cfg = new SynthDataConfig
                {
                    Patterns = new() { "DD/MM/YYYY" },
                    FontFamilies = new() { "Arial" },
                    FontSizeMin = 48, FontSizeMax = 56,
                    SampleCount = 20,
                    ValRatio = 0,
                    OutputFormat = DatasetFormat.PaddleOcrRec,
                    OutputDir = outputDir,
                    RandomSeed = 7,
                    Background = new BackgroundConfig { Mode = BackgroundMode.Solid, SolidColor = Color.White, RandomizeInvert = false },
                    Augmentation = new AugmentationConfig
                    {
                        RotationDegMax = 0,
                        PerspectiveJitter = 0,
                        BlurMaxKernel = 0,
                        NoiseStdMax = 0,
                        BrightnessJitter = 0,
                        ContrastJitter = 0
                    }
                };

                new SyntheticOcrDataGenerator().Generate(cfg);
                var trainLines = File.ReadAllLines(Path.Combine(outputDir, "train_rec.txt"));

                var tool = new OCRTool
                {
                    OcrEngine = OcrEngineType.PPOcrOnnx,
                    MaxSideLen = 960,
                    ConfidenceThreshold = 30,
                    FormatPreset = OcrOutputFormatPreset.DateDmy,
                    DrawOverlay = false
                };

                int matched = 0;
                foreach (var line in trainLines)
                {
                    var parts = line.Split('\t');
                    using var img = Cv2.ImRead(Path.Combine(outputDir, parts[0]), ImreadModes.Color);
                    var result = tool.Execute(img);
                    string rec = result.Data.TryGetValue("RecognizedText", out var t) ? t?.ToString() ?? "" : "";
                    bool ok = rec.Contains(parts[1]);
                    if (ok) matched++;
                    _out.WriteLine($"  expected=\"{parts[1]}\" recognized=\"{rec}\" {(ok ? "OK" : "FAIL")}");
                }
                _out.WriteLine($"\nRecognition: {matched}/{trainLines.Length}");
                Assert.True(matched >= trainLines.Length * 8 / 10,
                    $"합성 데이터의 PP-OCR 인식률이 80% 미만: {matched}/{trainLines.Length}");
            }
            finally
            {
                if (Directory.Exists(outputDir))
                    try { Directory.Delete(outputDir, true); } catch { }
            }
        }
    }
}
