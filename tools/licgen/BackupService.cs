using System.IO; // WPF(UseWPF) 는 암시적 using 에서 System.IO 를 제외 — 공용 파일이라 명시
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace Boda.LicGen;

/// <summary>
/// 키 폴더(.boda-licgen) 오프라인 백업 + 마커 기록 — CLI 와 LicGen.App 공용.
/// 마커 키는 볼륨 라벨+시리얼(매체 식별) — 드라이브 문자는 USB 를 바꿔 꽂으면
/// 같은 문자가 재사용돼 매체를 구분하지 못한다 (2026-08-26 실백업에서 확인).
/// </summary>
public static class BackupService
{
    public const string BackupFolderName = "boda-licgen-backup";
    public const int RequiredCopies = 2; // 운영 절차 §1a — 오프라인 매체 2부

    public sealed record BackupResult(string MediaKey, string TargetDir, int FileCount, int LedgerLines, int MediaCount);

    /// <summary>발급 경고·배너 공용 상태. NewestUtc 가 null 이면 백업 기록 없음.</summary>
    public sealed record BackupStatus(int LedgerLines, int MediaCount, string? NewestUtc, int NewestLedgerLines)
    {
        public int PendingIssues => Math.Max(0, LedgerLines - NewestLedgerLines);
        public bool HasRecord => NewestUtc != null;
    }

    public static BackupResult Run(string keyDir, string destinationRoot)
    {
        keyDir = Path.GetFullPath(keyDir);
        if (!Directory.Exists(keyDir))
            throw new DirectoryNotFoundException($"키 폴더 없음: {keyDir}");

        var targetDir = Path.Combine(Path.GetFullPath(destinationRoot), BackupFolderName);
        Directory.CreateDirectory(targetDir);

        var files = Directory.GetFiles(keyDir);
        foreach (var file in files)
            File.Copy(file, Path.Combine(targetDir, Path.GetFileName(file)), overwrite: true);

        // 반쪽 백업은 없느니만 못하다 — 계약 기록인 대장만큼은 복사본 크기를 대조
        var ledgerPath = LedgerStore.PathFor(keyDir);
        var ledgerLines = 0;
        if (File.Exists(ledgerPath))
        {
            ledgerLines = File.ReadAllLines(ledgerPath).Length;
            if (new FileInfo(Path.Combine(targetDir, LedgerStore.FileName)).Length != new FileInfo(ledgerPath).Length)
                throw new IOException($"대장 복사 크기 불일치 — 백업 매체 확인 필요: {targetDir}");
        }

        var mediaKey = GetMediaKey(targetDir);
        var marker = BackupMarker.Load(keyDir);
        marker.Targets[mediaKey] = new BackupMarker.Entry(DateTime.UtcNow.ToString("o"), ledgerLines, targetDir);
        marker.Save(keyDir);

        return new BackupResult(mediaKey, targetDir, files.Length, ledgerLines, marker.Targets.Count);
    }

    public static BackupStatus GetStatus(string keyDir)
    {
        var marker = BackupMarker.Load(keyDir);
        var ledgerLines = LedgerStore.CountLines(keyDir);
        if (marker.Targets.Count == 0)
            return new BackupStatus(ledgerLines, 0, null, 0);

        var newest = marker.Targets.Values.MaxBy(e => e.LedgerLines)!;
        return new BackupStatus(ledgerLines, marker.Targets.Count, newest.Utc, newest.LedgerLines);
    }

    /// <summary>볼륨 라벨 + 시리얼로 매체 식별 (조회 실패 시 경로 폴백).</summary>
    public static string GetMediaKey(string anyPathOnMedia)
    {
        var root = Path.GetPathRoot(Path.GetFullPath(anyPathOnMedia));
        if (root == null) return anyPathOnMedia;
        try
        {
            var label = new DriveInfo(root).VolumeLabel;
            var name = new StringBuilder(261);
            if (GetVolumeInformationW(root, name, name.Capacity, out var serial, out _, out _, null, 0))
                return $"{(string.IsNullOrWhiteSpace(label) ? "이름없음" : label)} [{serial:X8}]";
            return string.IsNullOrWhiteSpace(label) ? anyPathOnMedia : label;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return anyPathOnMedia; // 네트워크 경로 등 — 경로 자체를 식별자로
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetVolumeInformationW(
        string rootPathName, StringBuilder? volumeName, int volumeNameSize,
        out uint serialNumber, out uint maxComponentLength, out uint fileSystemFlags,
        StringBuilder? fileSystemName, int fileSystemNameSize);
}

/// <summary>
/// 백업 마커 — 키 폴더의 backup-marker.json 에 매체별 마지막 백업 시점·대장 줄 수 기록.
/// 백업 대상 폴더에도 함께 복사되므로 인수인계 시 매체만 봐도 이력을 알 수 있다.
/// </summary>
public sealed class BackupMarker
{
    public const string FileName = "backup-marker.json";

    public Dictionary<string, Entry> Targets { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Path 는 표시용 마지막 복사 위치 (v1 마커에는 없음 — null 허용).</summary>
    public sealed record Entry(string Utc, int LedgerLines, string? Path = null);

    public static BackupMarker Load(string dir)
    {
        var path = System.IO.Path.Combine(dir, FileName);
        if (!File.Exists(path)) return new BackupMarker();
        var marker = JsonSerializer.Deserialize<BackupMarker>(File.ReadAllText(path)) ?? new BackupMarker();

        // v1 마커는 키가 경로("E:\...")였다 — 드라이브 문자는 매체 식별자가 못 되므로 폐기.
        // 폐기해도 다음 backup 실행이 볼륨 키로 다시 기록한다.
        foreach (var legacy in marker.Targets.Keys.Where(k => k.Contains(":\\")).ToList())
            marker.Targets.Remove(legacy);
        return marker;
    }

    public void Save(string dir) =>
        File.WriteAllText(System.IO.Path.Combine(dir, FileName),
            JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
}
