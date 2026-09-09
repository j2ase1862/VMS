using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace VMS.Core.Services
{
    /// <summary>
    /// 학습에 실제로 들어간 클래스 이름을, 학습이 읽은 내보내기 폴더에서 되읽는다.
    ///
    /// <para>
    /// 왜 되읽는가. 학습 도구의 데이터셋과 학습에 쓴 데이터가 늘 같지는 않다 —
    /// 웹에서 받은 데이터셋으로 학습하면 클래스는 그 내보내기가 정본이고, 로컬 데이터셋의
    /// 클래스 목록과 다를 수 있다. 그 상태로 모델을 레지스트리에 올리면서 로컬 목록을 보내면,
    /// 서버가 ONNX 안의 이름과 대조할 수 있는 규약(yolo 등)에서는 400 으로 막히지만
    /// 그러지 못하는 규약에서는 <b>이름이 밀린 모델이 조용히 등록된다</b>.
    /// 그러면 라인의 검사 결과 이름이 통째로 틀린다.
    /// </para>
    /// <para>
    /// 순서까지 그대로 돌려준다 — 클래스 이름은 인덱스와 짝이라 순서가 곧 뜻이다.
    /// 읽지 못하면 빈 목록을 준다. 부르는 쪽이 그때 원래 쓰던 값으로 되돌아가면 된다.
    /// </para>
    /// </summary>
    public static class TrainingExportClasses
    {
        /// <summary>
        /// 내보내기 폴더에서 클래스 이름을 읽는다. 형식은 폴더 모양으로 가린다.
        /// <list type="bullet">
        /// <item>yolo·yolo-seg — <c>data.yaml</c> 의 <c>names</c></item>
        /// <item>coco — <c>annotations/instances_train.json</c> 의 <c>categories</c> (id 순)</item>
        /// <item>imagefolder — <c>train/</c> 아래 폴더 이름 (사전순)</item>
        /// </list>
        /// </summary>
        public static IReadOnlyList<string> Read(string? exportDirectory)
        {
            if (string.IsNullOrWhiteSpace(exportDirectory) || !Directory.Exists(exportDirectory))
                return Array.Empty<string>();

            try
            {
                var yaml = Path.Combine(exportDirectory, "data.yaml");
                if (File.Exists(yaml)) return ReadDataYaml(yaml);

                var coco = Path.Combine(exportDirectory, "annotations", "instances_train.json");
                if (File.Exists(coco)) return ReadCoco(coco);

                var trainDir = Path.Combine(exportDirectory, "train");
                if (Directory.Exists(trainDir))
                {
                    var folders = Directory.GetDirectories(trainDir)
                        .Select(Path.GetFileName)
                        .Where(n => !string.IsNullOrEmpty(n))
                        .Select(n => n!)
                        .OrderBy(n => n, StringComparer.Ordinal)
                        .ToList();
                    if (folders.Count > 0) return folders;
                }
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                Debug.WriteLine($"[TrainingExportClasses] {exportDirectory} 를 읽지 못했습니다: {ex.Message}");
            }
            return Array.Empty<string>();
        }

        /// <summary>
        /// <c>names</c> 만 읽는 최소 파서. YAML 라이브러리를 끌어오지 않으려는 것이고,
        /// 이 파일은 우리가 쓴 것이라 모양이 정해져 있다 (MLOps 의 DatasetExportWriter, WPF 의 ExportYolo).
        /// 두 표기를 모두 받는다 — <c>0: name</c> 로 번호를 붙인 것과 <c>- name</c> 목록.
        /// </summary>
        private static List<string> ReadDataYaml(string path)
        {
            var indexed = new SortedDictionary<int, string>();
            var listed = new List<string>();
            bool inNames = false;

            foreach (var raw in File.ReadLines(path))
            {
                var line = raw.TrimEnd();
                if (line.Length == 0) continue;

                var trimmed = line.TrimStart();
                bool indented = line.Length != trimmed.Length;

                if (!indented)
                {
                    // 들여쓰기가 없는 새 키를 만나면 names 블록이 끝난 것이다
                    inNames = trimmed.StartsWith("names:", StringComparison.Ordinal);
                    if (inNames)
                    {
                        // names: [a, b] 한 줄 표기
                        var inline = trimmed["names:".Length..].Trim();
                        if (inline.StartsWith('[') && inline.EndsWith(']'))
                        {
                            listed.AddRange(inline[1..^1].Split(',')
                                .Select(v => Unquote(v.Trim()))
                                .Where(v => v.Length > 0));
                            inNames = false;
                        }
                    }
                    continue;
                }

                if (!inNames) continue;

                if (trimmed.StartsWith("- ", StringComparison.Ordinal))
                {
                    listed.Add(Unquote(trimmed[2..].Trim()));
                    continue;
                }

                var colon = trimmed.IndexOf(':');
                if (colon > 0 && int.TryParse(trimmed[..colon].Trim(), out var index))
                    indexed[index] = Unquote(trimmed[(colon + 1)..].Trim());
            }

            return indexed.Count > 0 ? indexed.Values.ToList() : listed;
        }

        private static List<string> ReadCoco(string path)
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            if (!document.RootElement.TryGetProperty("categories", out var categories)
                || categories.ValueKind != JsonValueKind.Array)
                return new List<string>();

            return categories.EnumerateArray()
                .Select(c => (
                    Id: c.TryGetProperty("id", out var id) && id.TryGetInt32(out var value) ? value : int.MaxValue,
                    Name: c.TryGetProperty("name", out var n) ? n.GetString() : null))
                .Where(c => !string.IsNullOrEmpty(c.Name))
                .OrderBy(c => c.Id)
                .Select(c => c.Name!)
                .ToList();
        }

        private static string Unquote(string value)
        {
            var v = value.Trim();
            if (v.Length >= 2 && ((v[0] == '"' && v[^1] == '"') || (v[0] == '\'' && v[^1] == '\'')))
                v = v[1..^1];
            return v.Trim();
        }
    }
}
