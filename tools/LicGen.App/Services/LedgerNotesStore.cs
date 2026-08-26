using System.IO;
using System.Text.Json;

namespace Boda.LicGen.App.Services
{
    /// <summary>
    /// 발급 건 비고 저장 — 키 폴더의 ledger-notes.json (licenseId → 비고).
    /// 대장(ledger.jsonl)은 append-only 이고 licenseId 순번이 줄 수 기반이라
    /// 비고를 대장에 넣을 수 없다 — 별도 파일이 유일하게 안전한 방식.
    /// 키 폴더에 두므로 backup 명령이 자동으로 함께 백업한다.
    /// </summary>
    public sealed class LedgerNotesStore
    {
        public const string FileName = "ledger-notes.json";

        private readonly string _path;

        public LedgerNotesStore(string keyDir) => _path = Path.Combine(keyDir, FileName);

        public Dictionary<string, string> ReadAll()
        {
            if (!File.Exists(_path)) return new Dictionary<string, string>();
            return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(_path))
                   ?? new Dictionary<string, string>();
        }

        public void Save(string licenseId, string? note)
        {
            var notes = ReadAll();
            if (string.IsNullOrWhiteSpace(note))
                notes.Remove(licenseId);
            else
                notes[licenseId] = note.Trim();

            File.WriteAllText(_path,
                JsonSerializer.Serialize(notes, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                }));
        }
    }
}
