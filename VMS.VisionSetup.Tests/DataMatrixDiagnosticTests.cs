using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using VMS.VisionSetup.VisionTools.CodeReading;
using Xunit;
using Xunit.Abstractions;

namespace VMS.VisionSetup.Tests
{
    /// <summary>
    /// 실제 라벨 이미지로 CodeReaderTool/DataMatrixLocator 진단.
    /// dotnet test --filter "FullyQualifiedName~DataMatrixDiagnosticTests" 로 실행.
    /// </summary>
    public class DataMatrixDiagnosticTests
    {
        private const string ImageFolder = @"D:\참고 이미지\딥러닝 이미지\라벨 ocr";

        private readonly ITestOutputHelper _out;
        public DataMatrixDiagnosticTests(ITestOutputHelper output) { _out = output; }

        [Fact]
        public void DiagnoseAllImages()
        {
            if (!Directory.Exists(ImageFolder))
            {
                _out.WriteLine($"SKIP — folder not found: {ImageFolder}");
                return;
            }

            var files = Directory.GetFiles(ImageFolder, "*.jpg")
                .Concat(Directory.GetFiles(ImageFolder, "*.png"))
                .OrderBy(f => f).ToArray();
            _out.WriteLine($"Found {files.Length} images");
            _out.WriteLine("");

            int totalCandidates = 0, totalDecoded = 0, totalDirect = 0;

            foreach (var path in files)
            {
                using var img = Cv2.ImRead(path, ImreadModes.Color);
                if (img.Empty()) { _out.WriteLine($"{Path.GetFileName(path)}: FAIL load"); continue; }

                var reader = new ZXingCodeReader();
                _out.WriteLine($"== {Path.GetFileName(path)} ({img.Width}x{img.Height}) ==");

                // 1) 전체 ROI 직접 디코딩 (Auto)
                var directAuto = reader.Read(img, CodeReaderMode.Auto, tryHarder: true);
                _out.WriteLine($"  Direct (Auto, full): {directAuto.Count} codes");
                foreach (var c in directAuto)
                    _out.WriteLine($"    [{c.Format}] {c.Text}");
                totalDirect += directAuto.Count(c => c.Format.Equals("DATA_MATRIX", StringComparison.OrdinalIgnoreCase));

                // 2) 전체 ROI 직접 디코딩 (DataMatrix only)
                var directDm = reader.Read(img, CodeReaderMode.DataMatrix, tryHarder: true);
                _out.WriteLine($"  Direct (DM, full): {directDm.Count} codes");
                foreach (var c in directDm)
                    _out.WriteLine($"    [{c.Format}] {c.Text}");

                // 3) Localization → 영역별 디코딩
                var candidates = DataMatrixLocator.FindCandidates(img);
                _out.WriteLine($"  Locator candidates: {candidates.Count}");
                foreach (var rect in candidates)
                    _out.WriteLine($"    bbox=({rect.X},{rect.Y},{rect.Width}x{rect.Height})");
                totalCandidates += candidates.Count;

                int localizedHits = 0;
                foreach (var rect in candidates)
                {
                    using var sub = new Mat(img, rect);
                    var subCodes = reader.Read(sub, CodeReaderMode.DataMatrix, tryHarder: true);
                    string strategy = "orig";
                    if (subCodes.Count == 0)
                    {
                        // 2x upscale
                        using var sub2x = new Mat();
                        Cv2.Resize(sub, sub2x, new Size(sub.Width * 2, sub.Height * 2), 0, 0, InterpolationFlags.Cubic);
                        subCodes = reader.Read(sub2x, CodeReaderMode.DataMatrix, true);
                        if (subCodes.Count > 0) strategy = "2x";
                    }
                    if (subCodes.Count == 0)
                    {
                        // CLAHE
                        using var subGray = sub.Channels() > 1 ? sub.CvtColor(ColorConversionCodes.BGR2GRAY) : sub.Clone();
                        using var clahe = Cv2.CreateCLAHE(4.0, new Size(8, 8));
                        using var enhanced = new Mat();
                        clahe.Apply(subGray, enhanced);
                        subCodes = reader.Read(enhanced, CodeReaderMode.DataMatrix, true);
                        if (subCodes.Count > 0) strategy = "clahe";
                    }
                    if (subCodes.Count == 0)
                    {
                        // 2x + CLAHE
                        using var subGray = sub.Channels() > 1 ? sub.CvtColor(ColorConversionCodes.BGR2GRAY) : sub.Clone();
                        using var clahe = Cv2.CreateCLAHE(4.0, new Size(8, 8));
                        using var enhanced = new Mat();
                        clahe.Apply(subGray, enhanced);
                        using var enh2x = new Mat();
                        Cv2.Resize(enhanced, enh2x, new Size(enhanced.Width * 2, enhanced.Height * 2), 0, 0, InterpolationFlags.Cubic);
                        subCodes = reader.Read(enh2x, CodeReaderMode.DataMatrix, true);
                        if (subCodes.Count > 0) strategy = "clahe+2x";
                    }
                    if (subCodes.Count == 0)
                    {
                        // 4 rotations at 2x
                        for (int r = 1; r <= 3; r++)
                        {
                            using var sub2x = new Mat();
                            Cv2.Resize(sub, sub2x, new Size(sub.Width * 2, sub.Height * 2), 0, 0, InterpolationFlags.Cubic);
                            using var rot = new Mat();
                            Cv2.Rotate(sub2x, rot, (RotateFlags)(r - 1));
                            subCodes = reader.Read(rot, CodeReaderMode.DataMatrix, true);
                            if (subCodes.Count > 0) { strategy = $"rot{r * 90}+2x"; break; }
                        }
                    }
                    if (subCodes.Count > 0)
                    {
                        localizedHits++;
                        foreach (var c in subCodes)
                            _out.WriteLine($"      candidate ({rect.X},{rect.Y},{rect.Width}x{rect.Height}) [{strategy}] → [{c.Format}] {c.Text}");
                    }
                }
                _out.WriteLine($"  Localized decoded: {localizedHits}/{candidates.Count}");
                totalDecoded += localizedHits;
                _out.WriteLine("");
            }

            _out.WriteLine("============================================");
            _out.WriteLine($"Files: {files.Length}");
            _out.WriteLine($"Direct DM hits: {totalDirect}");
            _out.WriteLine($"Locator total candidates: {totalCandidates}");
            _out.WriteLine($"Localized DM decodes: {totalDecoded}");
        }

        /// <summary>
        /// CodeReaderTool 통합 흐름 (UseLocalization + fallback chain) end-to-end 검증.
        /// </summary>
        [Fact]
        public void EndToEnd_CodeReaderTool_AllImages()
        {
            if (!Directory.Exists(ImageFolder))
            {
                _out.WriteLine($"SKIP — folder not found: {ImageFolder}");
                return;
            }

            var files = Directory.GetFiles(ImageFolder, "*.jpg")
                .Concat(Directory.GetFiles(ImageFolder, "*.png"))
                .OrderBy(f => f).ToArray();

            var tool = new CodeReaderTool
            {
                CodeReaderMode = CodeReaderMode.DataMatrix,
                TryHarder = true,
                UseLocalization = true,
                EnableQualityGrading = true,
                ParseGs1 = false,
                DrawOverlay = false
            };

            int success = 0;
            foreach (var path in files)
            {
                using var img = Cv2.ImRead(path, ImreadModes.Color);
                if (img.Empty()) continue;
                var result = tool.Execute(img);
                bool ok = result.Success
                    && result.Data.TryGetValue("DecodedText", out var t)
                    && !string.IsNullOrEmpty(t?.ToString());
                if (ok) success++;
                _out.WriteLine($"{Path.GetFileName(path)}: {(ok ? "PASS" : "FAIL")} | " +
                    $"{GetVal(result.Data, "DecodedText")} | " +
                    $"Grade={GetVal(result.Data, "OverallGrade")} | " +
                    $"N={GetVal(result.Data, "SymbolSize")} | " +
                    $"PPM={GetVal(result.Data, "PixelsPerModule")} | " +
                    $"SC={GetVal(result.Data, "SymbolContrast")} | " +
                    $"MOD={GetVal(result.Data, "Modulation")} | " +
                    $"FPD={GetVal(result.Data, "FixedPatternDamage")} | " +
                    $"AN={GetVal(result.Data, "AxialNonuniformity")}");
            }
            _out.WriteLine($"\n{success}/{files.Length} PASS");
            Assert.True(success >= files.Length * 9 / 10,
                $"기대 PASS율 90% 이상, 실제 {success}/{files.Length}");
        }

        private static string GetVal(Dictionary<string, object> data, string key)
            => data.TryGetValue(key, out var v) && v != null ? v.ToString() ?? "" : "";
    }
}
