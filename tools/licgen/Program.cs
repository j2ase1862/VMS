using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using VMS.Core.Security.Licensing;

// BODA VMS 라이선스 발급 도구 (사내 전용 — MSI 비동봉, spec §7).
//
// 명령:
//   licgen keygen --key-id dev-2026 [--out <dir>]
//   licgen fingerprint
//   licgen issue --key <private.pem> --key-id dev-2026 --customer "OO정공" --fingerprint XXXXX-XXXXX-XXXXX
//                [--kind Production|Internal|Trial] [--edition Standard] [--max-clients 1]
//                [--maintenance-until yyyy-MM-dd] [--expires yyyy-MM-dd] [--out license.lic]
//   licgen verify --file license.lic [--fingerprint <code>] [--key-id <id> --pubkey <spki-base64>]
//   licgen backup --to <USB 드라이브/폴더> [--from <키 폴더>]
//
// 기본 키 보관 위치: %USERPROFILE%\.boda-licgen (리포 밖 — 개인키는 절대 커밋 금지).
// 발급 대장(ledger.jsonl)은 키 파일 옆에 자동 축적 — 계약 관리의 근거 (spec §7).
// 백업 규칙(운영 절차 §1a): 발급이 있었던 주의 말일에 오프라인 USB 2부 갱신 —
// backup 명령이 키 폴더 옆 backup-marker.json 에 매체별 시점을 기록하고,
// issue 가 마커 이후 발급분을 감지해 경고한다 (담당자 교체에도 도구가 규칙을 상기).

var positional = new List<string>();
var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
for (var i = 0; i < args.Length; i++)
{
    if (args[i].StartsWith("--", StringComparison.Ordinal))
    {
        var key = args[i][2..];
        var value = i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal)
            ? args[++i] : "true";
        options[key] = value;
    }
    else
    {
        positional.Add(args[i]);
    }
}

var defaultKeyDir = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".boda-licgen");

try
{
    return positional.FirstOrDefault()?.ToLowerInvariant() switch
    {
        "keygen" => Keygen(),
        "fingerprint" => Fingerprint(),
        "issue" => Issue(),
        "verify" => Verify(),
        "backup" => Backup(),
        _ => Usage()
    };
}
catch (Exception ex)
{
    Console.Error.WriteLine($"오류: {ex.Message}");
    return 1;
}

int Usage()
{
    Console.WriteLine("사용법: licgen keygen|fingerprint|issue|verify|backup [옵션] — 소스 상단 주석 참조");
    return 2;
}

string Require(string name) =>
    options.TryGetValue(name, out var v) ? v : throw new ArgumentException($"--{name} 옵션 필수");

int Keygen()
{
    var keyId = Require("key-id");
    var outDir = options.GetValueOrDefault("out", defaultKeyDir);
    Directory.CreateDirectory(outDir);

    var privatePath = Path.Combine(outDir, $"{keyId}.private.pem");
    if (File.Exists(privatePath))
        throw new InvalidOperationException($"이미 존재: {privatePath} — 키 재생성은 rotation 절차로만");

    var (privatePem, publicB64) = LicenseCrypto.CreateKeyPair();
    File.WriteAllText(privatePath, privatePem);
    File.WriteAllText(Path.Combine(outDir, $"{keyId}.public.txt"), publicB64);

    Console.WriteLine($"개인키: {privatePath}  (리포/MSI 반입 절대 금지)");
    Console.WriteLine($"공개키(LicenseKeyring 에 등록):");
    Console.WriteLine($"  [\"{keyId}\"] = \"{publicB64}\",");
    return 0;
}

int Fingerprint()
{
    Console.WriteLine(MachineFingerprint.GetCode());
    return 0;
}

int Issue()
{
    var keyPath = Require("key");
    var keyId = Require("key-id");
    var customer = Require("customer");
    var fingerprint = Require("fingerprint");
    var kind = Enum.Parse<LicenseKind>(options.GetValueOrDefault("kind", "Production"));
    var maxClients = int.Parse(options.GetValueOrDefault("max-clients", "1"), CultureInfo.InvariantCulture);
    var expires = options.GetValueOrDefault("expires");
    var maintenance = options.GetValueOrDefault("maintenance-until");
    var today = DateOnly.FromDateTime(DateTime.Now);

    // 발급 시점 규칙 검증 — 검증기(LicenseValidator)와 이중 방어
    if (kind == LicenseKind.Production && fingerprint == MachineFingerprint.Wildcard)
        throw new ArgumentException("Production 은 지문 와일드카드 불가 — 대상 PC 의 fingerprint 필요");
    if (kind is LicenseKind.Internal or LicenseKind.Trial && expires == null)
        throw new ArgumentException($"{kind} 은 --expires 필수");
    if (kind == LicenseKind.Internal && expires != null)
    {
        var exp = DateOnly.ParseExact(expires, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (exp > today.AddDays(LicenseValidator.InternalMaxValidityDays))
            throw new ArgumentException($"Internal 만료는 최대 {LicenseValidator.InternalMaxValidityDays}일 (유출 피해 최소화)");
    }

    // 발급 대장 — 키 파일 옆 ledger.jsonl, licenseId 는 연도별 순번
    var ledgerPath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(keyPath))!, "ledger.jsonl");
    var seq = File.Exists(ledgerPath) ? File.ReadAllLines(ledgerPath).Length + 1 : 1;
    var licenseId = $"LIC-{today.Year}-{seq:D4}";

    var payload = new LicensePayload
    {
        LicenseId = licenseId,
        Customer = customer,
        Kind = kind.ToString(),
        Edition = options.GetValueOrDefault("edition", "Standard"),
        MaxClients = maxClients,
        Fingerprint = fingerprint,
        IssuedAt = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        MaintenanceUntil = maintenance,
        ExpiresAt = expires,
        KeyId = keyId
    };

    var payloadNode = JsonSerializer.SerializeToNode(payload)!;
    var signature = LicenseCrypto.Sign(File.ReadAllText(keyPath), LicenseCanonicalJson.Serialize(payloadNode));
    var file = new JsonObject { ["payload"] = payloadNode, ["signature"] = signature };

    var outPath = options.GetValueOrDefault("out", LicenseFileStore.FileName);
    File.WriteAllText(outPath, file.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

    File.AppendAllText(ledgerPath, JsonSerializer.Serialize(new
    {
        licenseId,
        customer,
        kind = kind.ToString(),
        fingerprint,
        maxClients,
        maintenanceUntil = maintenance,
        expiresAt = expires,
        issuedAtUtc = DateTime.UtcNow.ToString("o"),
        issuer = Environment.UserName
    }) + Environment.NewLine);

    Console.WriteLine($"발급 완료: {licenseId} → {Path.GetFullPath(outPath)}");
    Console.WriteLine($"대장 기록: {ledgerPath}");
    WarnIfBackupStale(Path.GetDirectoryName(ledgerPath)!, seq);
    return 0;
}

int Backup()
{
    var to = Require("to");
    var from = Path.GetFullPath(options.GetValueOrDefault("from", defaultKeyDir));
    if (!Directory.Exists(from))
        throw new DirectoryNotFoundException($"키 폴더 없음: {from}");

    var targetDir = Path.Combine(Path.GetFullPath(to), "boda-licgen-backup");
    Directory.CreateDirectory(targetDir);

    var files = Directory.GetFiles(from);
    foreach (var file in files)
        File.Copy(file, Path.Combine(targetDir, Path.GetFileName(file)), overwrite: true);

    // 반쪽 백업은 없느니만 못하다 — 계약 기록인 대장만큼은 복사본 크기를 대조
    var ledgerPath = Path.Combine(from, "ledger.jsonl");
    var ledgerLines = 0;
    if (File.Exists(ledgerPath))
    {
        ledgerLines = File.ReadAllLines(ledgerPath).Length;
        if (new FileInfo(Path.Combine(targetDir, "ledger.jsonl")).Length != new FileInfo(ledgerPath).Length)
            throw new IOException($"대장 복사 크기 불일치 — 백업 매체 확인 필요: {targetDir}");
    }

    var marker = BackupMarker.Load(from);
    marker.Targets[targetDir] = new BackupMarker.Entry(DateTime.UtcNow.ToString("o"), ledgerLines);
    marker.Save(from);

    Console.WriteLine($"백업 완료: {files.Length}개 파일 → {targetDir} (대장 {ledgerLines}건)");
    if (marker.Targets.Count < 2)
        Console.WriteLine("참고: 백업 규칙은 오프라인 매체 2부 — 다른 USB 에도 'backup --to' 실행 (docs/license_operations.md §1a)");
    return 0;
}

void WarnIfBackupStale(string keyDir, int ledgerLines)
{
    var marker = BackupMarker.Load(keyDir);
    if (marker.Targets.Count == 0)
    {
        Console.WriteLine("⚠ 백업 기록 없음 — 'licgen backup --to <USB>' 로 오프라인 백업 2부를 만드세요 (docs/license_operations.md §1a)");
        return;
    }

    var newest = marker.Targets.Values.MaxBy(e => e.LedgerLines)!;
    var pending = ledgerLines - newest.LedgerLines;
    if (pending > 0)
        Console.WriteLine($"⚠ 백업 미반영 발급 {pending}건 (마지막 백업 {newest.Utc[..10]}) — 이번 주 말일까지 'licgen backup --to <USB>' 2부 갱신 (§1a)");
}

int Verify()
{
    var file = Require("file");
    var fingerprint = options.GetValueOrDefault("fingerprint") ?? MachineFingerprint.GetCode();

    // 기본은 제품 내장 키링, --key-id/--pubkey 로 미등록 키 검증 지원 (keygen 직후 자가 점검용)
    IReadOnlyDictionary<string, string> keyring = LicenseKeyring.Default;
    if (options.TryGetValue("pubkey", out var pubkey))
    {
        var extended = new Dictionary<string, string>(LicenseKeyring.Default)
        {
            [Require("key-id")] = File.Exists(pubkey) ? File.ReadAllText(pubkey).Trim() : pubkey
        };
        keyring = extended;
    }

    var eval = LicenseValidator.Validate(
        File.ReadAllText(file), fingerprint, DateOnly.FromDateTime(DateTime.Now), keyring);

    Console.WriteLine($"상태: {eval.Status}");
    Console.WriteLine($"내용: {eval.Message}");
    return eval.Status == LicenseStatus.Valid ? 0 : 1;
}

// 백업 마커 — 키 폴더의 backup-marker.json 에 매체별 마지막 백업 시점·대장 줄 수를 기록.
// 백업 대상 폴더에도 함께 복사되므로 인수인계 시 매체만 봐도 이력을 알 수 있다.
sealed class BackupMarker
{
    public const string FileName = "backup-marker.json";

    public Dictionary<string, Entry> Targets { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public sealed record Entry(string Utc, int LedgerLines);

    public static BackupMarker Load(string dir)
    {
        var path = Path.Combine(dir, FileName);
        if (!File.Exists(path)) return new BackupMarker();
        return JsonSerializer.Deserialize<BackupMarker>(File.ReadAllText(path)) ?? new BackupMarker();
    }

    public void Save(string dir) =>
        File.WriteAllText(Path.Combine(dir, FileName),
            JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
}
