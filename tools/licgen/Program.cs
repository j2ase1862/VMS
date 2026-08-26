using System.Globalization;
using Boda.LicGen;
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
// backup 명령이 키 폴더 옆 backup-marker.json 에 매체(볼륨 라벨+시리얼)별 시점을
// 기록하고, issue 가 마커 이후 발급분을 감지해 경고한다.
//
// 발급·대장·백업 코어는 LicenseIssuer/LedgerStore/BackupService — 비개발 담당자용
// GUI(tools\LicGen.App)와 Linked Compile 로 공유하므로 로직 수정은 그 파일들에서.

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
    var request = new IssueRequest(
        KeyPath: Require("key"),
        KeyId: Require("key-id"),
        Customer: Require("customer"),
        Fingerprint: Require("fingerprint"),
        Kind: Enum.Parse<LicenseKind>(options.GetValueOrDefault("kind", "Production")),
        MaxClients: int.Parse(options.GetValueOrDefault("max-clients", "1"), CultureInfo.InvariantCulture),
        MaintenanceUntil: options.GetValueOrDefault("maintenance-until"),
        ExpiresAt: options.GetValueOrDefault("expires"),
        Edition: options.GetValueOrDefault("edition", "Standard"));

    var result = LicenseIssuer.Issue(request, DateOnly.FromDateTime(DateTime.Now));

    var outPath = options.GetValueOrDefault("out", LicenseFileStore.FileName);
    File.WriteAllText(outPath, result.LicenseJson);

    Console.WriteLine($"발급 완료: {result.LicenseId} → {Path.GetFullPath(outPath)}");
    Console.WriteLine($"대장 기록: {result.LedgerPath}");
    WarnIfBackupStale(Path.GetDirectoryName(result.LedgerPath)!);
    return 0;
}

int Backup()
{
    var result = BackupService.Run(
        options.GetValueOrDefault("from", defaultKeyDir), Require("to"));

    Console.WriteLine($"백업 완료: {result.FileCount}개 파일 → {result.TargetDir} (매체 {result.MediaKey}, 대장 {result.LedgerLines}건)");
    if (result.MediaCount < BackupService.RequiredCopies)
        Console.WriteLine("참고: 백업 규칙은 오프라인 매체 2부 — 다른 USB 에도 'backup --to' 실행 (docs/license_operations.md §1a)");
    return 0;
}

void WarnIfBackupStale(string keyDir)
{
    var status = BackupService.GetStatus(keyDir);
    if (!status.HasRecord)
    {
        Console.WriteLine("⚠ 백업 기록 없음 — 'licgen backup --to <USB>' 로 오프라인 백업 2부를 만드세요 (docs/license_operations.md §1a)");
        return;
    }
    if (status.PendingIssues > 0)
        Console.WriteLine($"⚠ 백업 미반영 발급 {status.PendingIssues}건 (마지막 백업 {status.NewestUtc![..10]}) — 이번 주 말일까지 'licgen backup --to <USB>' 2부 갱신 (§1a)");
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
