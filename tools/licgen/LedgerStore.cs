using System.IO; // WPF(UseWPF) 는 암시적 using 에서 System.IO 를 제외 — 공용 파일이라 명시
using System.Text.Json;

namespace Boda.LicGen;

/// <summary>
/// 발급 대장(ledger.jsonl) 읽기/추가 — CLI 와 LicGen.App 이 Linked Compile 로 공유.
/// 대장은 append-only 계약 기록이라 기존 줄은 절대 고쳐 쓰지 않는다.
/// licenseId 순번은 줄 수 기반이므로 대장에 발급 외 줄을 추가해서는 안 된다
/// (비고 등 부가 정보는 별도 파일 — LicGen.App 의 ledger-notes.json).
/// </summary>
public sealed record LedgerEntry(
    string LicenseId,
    string Customer,
    string Kind,
    string Fingerprint,
    int MaxClients,
    string? MaintenanceUntil,
    string? ExpiresAt,
    string IssuedAtUtc,
    string Issuer);

public static class LedgerStore
{
    public const string FileName = "ledger.jsonl";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static string PathFor(string keyDir) => Path.Combine(keyDir, FileName);

    /// <summary>대장 총 줄 수 — licenseId 순번의 근거 (파싱 실패 줄도 순번에는 포함).</summary>
    public static int CountLines(string keyDir)
    {
        var path = PathFor(keyDir);
        return File.Exists(path) ? File.ReadAllLines(path).Length : 0;
    }

    public static string NextLicenseId(string keyDir, DateOnly today) =>
        $"LIC-{today.Year}-{CountLines(keyDir) + 1:D4}";

    public static IReadOnlyList<LedgerEntry> Read(string keyDir)
    {
        var path = PathFor(keyDir);
        if (!File.Exists(path)) return Array.Empty<LedgerEntry>();

        var entries = new List<LedgerEntry>();
        foreach (var line in File.ReadAllLines(path))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                var entry = JsonSerializer.Deserialize<LedgerEntry>(line, Json);
                if (entry != null) entries.Add(entry);
            }
            catch (JsonException)
            {
                // 손상 줄은 표시만 건너뛴다 — 순번(CountLines)에는 여전히 포함
            }
        }
        return entries;
    }

    public static void Append(string keyDir, LedgerEntry entry) =>
        File.AppendAllText(PathFor(keyDir), JsonSerializer.Serialize(entry, Json) + Environment.NewLine);
}
