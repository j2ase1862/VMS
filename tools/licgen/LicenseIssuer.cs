using System.Globalization;
using System.IO; // WPF(UseWPF) 는 암시적 using 에서 System.IO 를 제외 — 공용 파일이라 명시
using System.Text.Json;
using System.Text.Json.Nodes;
using VMS.Core.Security.Licensing;

namespace Boda.LicGen;

/// <summary>발급 요청 — 날짜는 yyyy-MM-dd 문자열 (라이선스 페이로드 표기와 동일).</summary>
public sealed record IssueRequest(
    string KeyPath,
    string KeyId,
    string Customer,
    string Fingerprint,
    LicenseKind Kind,
    int MaxClients,
    string? MaintenanceUntil,
    string? ExpiresAt,
    string Edition = "Standard");

public sealed record IssueResult(string LicenseId, string LicenseJson, string LedgerPath);

/// <summary>
/// 라이선스 발급 코어 — 규칙 검증 + 서명 + 대장 기록. CLI 와 LicGen.App 공용.
/// 발급 시점 규칙은 제품 검증기(LicenseValidator)와 이중 방어.
/// </summary>
public static class LicenseIssuer
{
    /// <summary>발급 규칙 위반 시 ArgumentException — UI 는 메시지를 그대로 표시하면 된다.</summary>
    public static void ValidateRules(IssueRequest request, DateOnly today)
    {
        if (string.IsNullOrWhiteSpace(request.Customer))
            throw new ArgumentException("고객명은 비울 수 없음 — 발급 대장이 계약 기록의 근거");
        if (string.IsNullOrWhiteSpace(request.Fingerprint))
            throw new ArgumentException("지문 필수 — 대상 PC 의 AppSetup 7페이지 또는 licgen fingerprint");
        if (!File.Exists(request.KeyPath))
            throw new ArgumentException($"개인키 파일 없음: {request.KeyPath}");
        if (request.MaxClients < 1)
            throw new ArgumentException("좌석 수는 1 이상");

        if (request.Kind == LicenseKind.Production && request.Fingerprint == MachineFingerprint.Wildcard)
            throw new ArgumentException("Production 은 지문 와일드카드 불가 — 대상 PC 의 fingerprint 필요");
        if (request.Kind is LicenseKind.Internal or LicenseKind.Trial && request.ExpiresAt == null)
            throw new ArgumentException($"{request.Kind} 은 실행 만료(expires) 필수");
        if (request.Kind == LicenseKind.Internal && request.ExpiresAt != null)
        {
            var exp = DateOnly.ParseExact(request.ExpiresAt, "yyyy-MM-dd", CultureInfo.InvariantCulture);
            if (exp > today.AddDays(LicenseValidator.InternalMaxValidityDays))
                throw new ArgumentException($"Internal 만료는 최대 {LicenseValidator.InternalMaxValidityDays}일 (유출 피해 최소화)");
        }
    }

    /// <summary>검증 → 서명 → 대장 기록까지 수행하고 라이선스 파일 내용(JSON)을 돌려준다.</summary>
    public static IssueResult Issue(IssueRequest request, DateOnly today)
    {
        ValidateRules(request, today);

        var keyDir = Path.GetDirectoryName(Path.GetFullPath(request.KeyPath))!;
        var licenseId = LedgerStore.NextLicenseId(keyDir, today);

        var payload = new LicensePayload
        {
            LicenseId = licenseId,
            Customer = request.Customer,
            Kind = request.Kind.ToString(),
            Edition = request.Edition,
            MaxClients = request.MaxClients,
            Fingerprint = request.Fingerprint,
            IssuedAt = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            MaintenanceUntil = request.MaintenanceUntil,
            ExpiresAt = request.ExpiresAt,
            KeyId = request.KeyId
        };

        var payloadNode = JsonSerializer.SerializeToNode(payload)!;
        var signature = LicenseCrypto.Sign(
            File.ReadAllText(request.KeyPath), LicenseCanonicalJson.Serialize(payloadNode));
        var file = new JsonObject { ["payload"] = payloadNode, ["signature"] = signature };
        var licenseJson = file.ToJsonString(new JsonSerializerOptions { WriteIndented = true });

        LedgerStore.Append(keyDir, new LedgerEntry(
            licenseId, request.Customer, request.Kind.ToString(), request.Fingerprint,
            request.MaxClients, request.MaintenanceUntil, request.ExpiresAt,
            DateTime.UtcNow.ToString("o"), Environment.UserName));

        return new IssueResult(licenseId, licenseJson, LedgerStore.PathFor(keyDir));
    }
}
