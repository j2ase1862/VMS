namespace Boda.LicGen.App.Messages
{
    /// <summary>발급 완료 통지 — 대장 목록·백업 배너 갱신용 (VM 간 직접 참조 금지 관례).</summary>
    public sealed record LicenseIssuedMessage(string LicenseId);
}
