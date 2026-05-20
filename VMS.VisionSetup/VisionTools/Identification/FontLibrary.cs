using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VMS.VisionSetup.VisionTools.Identification
{
    /// <summary>
    /// OCV 폰트 라이브러리.
    /// 학습된 문자 템플릿 컬렉션 + JSON 직렬화.
    /// 동일 문자에 여러 폰트/스타일 템플릿을 등록 가능 (매칭은 최댓값 사용).
    /// </summary>
    public class FontLibrary
    {
        /// <summary>모든 템플릿이 정규화될 정사각 패치 크기 (픽셀).</summary>
        public const int CanonicalSize = 32;

        public ObservableCollection<CharTemplate> Templates { get; } = new();

        public int Count => Templates.Count;

        public IEnumerable<string> UniqueChars =>
            Templates.Select(t => t.Char).Distinct(StringComparer.Ordinal).OrderBy(c => c, StringComparer.Ordinal);

        public void Add(string ch, byte[] templatePng)
        {
            Templates.Add(new CharTemplate { Char = ch, TemplatePng = templatePng });
        }

        public void RemoveByChar(string ch)
        {
            for (int i = Templates.Count - 1; i >= 0; i--)
                if (string.Equals(Templates[i].Char, ch, StringComparison.Ordinal))
                {
                    Templates[i].InvalidateCache();
                    Templates.RemoveAt(i);
                }
        }

        public void Clear()
        {
            foreach (var t in Templates) t.InvalidateCache();
            Templates.Clear();
        }

        /// <summary>
        /// 입력 패치(임의 크기)를 정규화 → 라이브러리 전체와 매칭 → 최고 점수의 (문자, NCC) 반환.
        /// 라이브러리가 비어 있으면 (null, 0).
        /// </summary>
        public (string? Char, double Score) MatchBest(Mat patch)
        {
            if (Templates.Count == 0 || patch == null || patch.Empty()) return (null, 0);

            using var norm = Normalize(patch);
            string? bestChar = null;
            double bestScore = -1.0;
            foreach (var t in Templates)
            {
                var tplMat = t.GetMat();
                if (tplMat == null) continue;
                using var tpl = tplMat.Size() == norm.Size() ? tplMat.Clone() : tplMat.Resize(norm.Size());
                using var result = new Mat();
                Cv2.MatchTemplate(norm, tpl, result, TemplateMatchModes.CCoeffNormed);
                Cv2.MinMaxLoc(result, out _, out double maxVal);
                if (maxVal > bestScore)
                {
                    bestScore = maxVal;
                    bestChar = t.Char;
                }
            }
            return (bestChar, Math.Max(0, bestScore));
        }

        /// <summary>
        /// 임의 크기 그레이/이진 패치를 CanonicalSize x CanonicalSize 정규화 (비율 무시 스트레치).
        /// 매칭 안정성을 위해 Otsu 이진화 후 리사이즈.
        /// </summary>
        public static Mat Normalize(Mat patch)
        {
            Mat gray = patch.Channels() > 1
                ? patch.CvtColor(ColorConversionCodes.BGR2GRAY)
                : patch.Clone();
            try
            {
                using var bin = new Mat();
                Cv2.Threshold(gray, bin, 0, 255, ThresholdTypes.Binary | ThresholdTypes.Otsu);
                // 문자가 어두운지 밝은지 통계로 판정 → 항상 white-on-black로 통일
                double mean = bin.Mean().Val0;
                if (mean > 127) Cv2.BitwiseNot(bin, bin);
                var resized = new Mat();
                Cv2.Resize(bin, resized, new Size(CanonicalSize, CanonicalSize), 0, 0, InterpolationFlags.Area);
                return resized;
            }
            finally { gray.Dispose(); }
        }

        // ── JSON 직렬화 ──

        public string ToJson()
        {
            var dto = new FontLibraryDto
            {
                Templates = Templates.Select(t => new CharTemplateDto
                {
                    Char = t.Char,
                    TemplatePngBase64 = t.TemplatePng == null ? string.Empty : Convert.ToBase64String(t.TemplatePng)
                }).ToList()
            };
            return JsonSerializer.Serialize(dto, JsonOpts);
        }

        public static FontLibrary FromJson(string json)
        {
            var lib = new FontLibrary();
            if (string.IsNullOrWhiteSpace(json)) return lib;
            try
            {
                var dto = JsonSerializer.Deserialize<FontLibraryDto>(json, JsonOpts);
                if (dto?.Templates == null) return lib;
                foreach (var t in dto.Templates)
                {
                    byte[]? bytes = null;
                    if (!string.IsNullOrEmpty(t.TemplatePngBase64))
                    {
                        try { bytes = Convert.FromBase64String(t.TemplatePngBase64); }
                        catch { continue; }
                    }
                    if (bytes != null && bytes.Length > 0)
                        lib.Add(t.Char ?? string.Empty, bytes);
                }
            }
            catch { /* corrupt JSON — return empty */ }
            return lib;
        }

        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            WriteIndented = false,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        private class FontLibraryDto
        {
            [JsonPropertyName("templates")]
            public List<CharTemplateDto> Templates { get; set; } = new();
        }

        private class CharTemplateDto
        {
            [JsonPropertyName("char")]
            public string Char { get; set; } = string.Empty;

            [JsonPropertyName("templatePngBase64")]
            public string TemplatePngBase64 { get; set; } = string.Empty;
        }
    }
}
