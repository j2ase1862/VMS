using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using VMS.VisionSetup.Models;

namespace VMS.VisionSetup.Services.BatchTesting
{
    /// <summary>한 이미지의 기대 출력 — 도구 ID/이름 → 결과 키 → 기대 값.</summary>
    public class GoldenSetEntry
    {
        /// <summary>이미지 파일 식별자 — 파일명(경로 제외).</summary>
        public string FileName { get; set; } = string.Empty;

        /// <summary>이 이미지가 전체적으로 PASS여야 하는지 (true면 모든 도구 성공 기대).</summary>
        public bool ExpectedSuccess { get; set; } = true;

        /// <summary>도구 ID → (결과 키 → 기대 값 문자열).</summary>
        public Dictionary<string, Dictionary<string, string>> ToolExpectations { get; set; } = new();
    }

    /// <summary>
    /// 골든셋 — 폴더 이미지에 대한 정답 등록. BatchTest 실행 시 결과와 비교해 정확도 측정.
    /// Recipe와 독립적으로 JSON 파일에 저장.
    /// </summary>
    public class GoldenSet
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime ModifiedAt { get; set; } = DateTime.UtcNow;

        /// <summary>관련 레시피 이름 (참고용, 강제 매칭 아님).</summary>
        public string RecipeName { get; set; } = string.Empty;

        public List<GoldenSetEntry> Entries { get; set; } = new();

        /// <summary>파일명 기반 lookup index — Load 후 빌드.</summary>
        [JsonIgnore]
        private Dictionary<string, GoldenSetEntry>? _byFileName;

        public GoldenSetEntry? Lookup(string imagePath)
        {
            if (_byFileName == null) BuildIndex();
            var fn = Path.GetFileName(imagePath);
            return _byFileName!.TryGetValue(fn, out var e) ? e : null;
        }

        public void BuildIndex()
        {
            _byFileName = Entries
                .GroupBy(e => e.FileName, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        }

        public int Count => Entries.Count;

        // ─── JSON IO ───
        public static GoldenSet? Load(string filePath)
        {
            try
            {
                if (!File.Exists(filePath)) return null;
                var json = File.ReadAllText(filePath);
                var gs = JsonSerializer.Deserialize<GoldenSet>(json, JsonOptions);
                gs?.BuildIndex();
                return gs;
            }
            catch (Exception)
            {
                return null;
            }
        }

        public bool Save(string filePath)
        {
            try
            {
                ModifiedAt = DateTime.UtcNow;
                var json = JsonSerializer.Serialize(this, JsonOptions);
                File.WriteAllText(filePath, json);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>BatchTest 결과 리스트로부터 골든셋 생성 — "현재 결과를 정답으로".</summary>
        public static GoldenSet FromBatchResults(
            string name,
            string recipeName,
            IList<BatchImageResult> results,
            IDictionary<string, string> toolIdToName)
        {
            var gs = new GoldenSet
            {
                Name = name,
                RecipeName = recipeName,
                Description = $"Saved from batch run · {results.Count} images"
            };

            foreach (var r in results)
            {
                var entry = new GoldenSetEntry
                {
                    FileName = Path.GetFileName(r.ImagePath),
                    ExpectedSuccess = r.Success
                };

                foreach (var kv in r.ToolResults)
                {
                    var toolId = kv.Key;
                    var res = kv.Value;
                    if (res?.Data == null) continue;
                    var expected = new Dictionary<string, string>();
                    foreach (var d in res.Data)
                    {
                        if (d.Value == null) continue;
                        expected[d.Key] = FormatValue(d.Value);
                    }
                    if (expected.Count > 0)
                        entry.ToolExpectations[toolId] = expected;
                }

                gs.Entries.Add(entry);
            }

            gs.BuildIndex();
            return gs;
        }

        private static string FormatValue(object v) => v switch
        {
            double d => d.ToString("G6", System.Globalization.CultureInfo.InvariantCulture),
            float f => f.ToString("G6", System.Globalization.CultureInfo.InvariantCulture),
            bool b => b ? "true" : "false",
            _ => v.ToString() ?? string.Empty
        };

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
    }
}
