using System; // 시험 프로젝트(VMS.Core.Tests)가 암시적 using 없이 링크해 컴파일 — 명시
using System.Collections.Generic;
using System.Linq;
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

    /// <summary>매체 하나의 백업 상태 — PendingIssues 는 그 매체에 아직 들어가지 않은 발급 건수.</summary>
    public sealed record MediaStatus(string MediaKey, string Utc, int LedgerLines, int PendingIssues);

    /// <summary>
    /// 발급 경고·배너 공용 상태. Media 는 마지막 백업이 최근인 순. 기록이 없으면 Media 가 비어 있다.
    /// 예전에는 대장 줄 수가 가장 많은 매체 하나로만 미반영 건수를 셌다 — 두 매체를 한 번씩 등록한 뒤로는
    /// USB 하나만 갱신해도 '미반영 0건' 으로 보여 두 번째 매체가 뒤처진 것을 몰랐다(VO-14). 이제 매체마다 센다.
    /// </summary>
    public sealed record BackupStatus(int LedgerLines, IReadOnlyList<MediaStatus> Media)
    {
        public int MediaCount => Media.Count;
        public bool HasRecord => Media.Count > 0;
        public string? NewestUtc => HasRecord ? Media[0].Utc : null;

        /// <summary>대장을 모두 담고 있는(미반영 0건) 매체 수.</summary>
        public int UpToDateCount => Media.Count(m => m.PendingIssues == 0);

        /// <summary>최신 매체 2부(RequiredCopies)가 갖춰지지 않았으면 true — 배너 경고 · 백업 완료 안내의 기준.</summary>
        public bool NeedsBackup => UpToDateCount < RequiredCopies;

        /// <summary>
        /// 2부를 갖추려면 더 반영해야 하는 발급 건수 — 가장 잘 갖춘 매체 2개 중 덜 갖춘 쪽 기준.
        /// 매체가 2개 미만이면 없는 매체는 0줄로 본다(= 대장 전체가 미반영).
        /// </summary>
        public int PendingIssues
        {
            get
            {
                var best = Media.Select(m => m.LedgerLines).OrderByDescending(n => n).Take(RequiredCopies).ToList();
                while (best.Count < RequiredCopies) best.Add(0);
                return Math.Max(0, LedgerLines - best.Min());
            }
        }

        /// <summary>배너용 매체별 미반영 건수 — 예: "USB-A [1A2B3C4D] 0건 · USB-B [5E6F7A8B] 3건".</summary>
        public string DescribeMedia() =>
            string.Join(" · ", Media.Select(m => $"{m.MediaKey} {m.PendingIssues}건"));
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
        var media = marker.Targets
            .Select(t => new MediaStatus(t.Key, t.Value.Utc, t.Value.LedgerLines,
                                         Math.Max(0, ledgerLines - t.Value.LedgerLines)))
            .OrderByDescending(m => m.Utc, StringComparer.Ordinal)   // ISO 8601(UTC "o") — 문자열 순서 = 시간 순서
            .ToList();
        return new BackupStatus(ledgerLines, media);
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
