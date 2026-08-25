using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using VMS.Core.Security.Licensing;
using VMS.Core.Services;
using Xunit;

namespace VMS.Core.Tests.Security.Licensing
{
    /// <summary>
    /// 좌석 임대 클라이언트 경로 (spec §5b) — SeatLeaseCache 왕복, LicenseBootCheck 의
    /// 임대 캐시 폴백, LicenseClient 응답 파싱/캐시 저장.
    /// </summary>
    public class SeatLeaseTests : IDisposable
    {
        private const string Fp = "AAAAA-BBBBB-CCCCC";
        private readonly string _tempDir;

        public SeatLeaseTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "boda-seat-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_tempDir, recursive: true); } catch { /* best effort */ }
        }

        private string LeasePath => Path.Combine(_tempDir, "license_lease.json");
        private string LicensePath => Path.Combine(_tempDir, "license.lic");

        private void WriteLease(DateTime validUntilUtc, bool overCapacity = false)
        {
            SeatLeaseCache.Save(new SeatLeaseCache
            {
                LicenseId = "LIC-2026-0001",
                Customer = "테스트고객",
                SeatsUsed = 3,
                MaxClients = overCapacity ? 2 : 5,
                OverCapacity = overCapacity,
                ValidUntilUtc = validUntilUtc,
                LeasedAtUtc = DateTime.UtcNow,
                ServerUrl = "http://localhost:5292",
            }, LeasePath);
        }

        // ─── SeatLeaseCache ──────────────────────────────────────

        [Fact]
        public void Cache_roundtrip_preserves_fields()
        {
            var until = new DateTime(2026, 8, 28, 1, 0, 0, DateTimeKind.Utc);
            WriteLease(until);

            var loaded = SeatLeaseCache.TryLoad(LeasePath);

            Assert.NotNull(loaded);
            Assert.Equal("LIC-2026-0001", loaded!.LicenseId);
            Assert.Equal(until, loaded.ValidUntilUtc);
            Assert.True(loaded.IsValidAt(until.AddHours(-1)));
            Assert.False(loaded.IsValidAt(until.AddHours(1)));
        }

        [Fact]
        public void Cache_corrupt_file_returns_null()
        {
            File.WriteAllText(LeasePath, "not json");
            Assert.Null(SeatLeaseCache.TryLoad(LeasePath));
        }

        // ─── LicenseBootCheck 임대 폴백 ──────────────────────────

        [Fact]
        public void BootCheck_missing_file_with_valid_lease_is_SeatLeased()
        {
            var now = DateTime.UtcNow;
            WriteLease(validUntilUtc: now.AddHours(48));

            var eval = LicenseBootCheck.Evaluate(LicensePath, Fp,
                DateOnly.FromDateTime(now), LeasePath, now);

            Assert.Equal(LicenseStatus.SeatLeased, eval.Status);
            Assert.False(eval.IsBlocking);
            Assert.False(eval.NeedsAttention);  // 클라이언트 PC 의 정상 상태 — 경고 아님
            Assert.Contains("LIC-2026-0001", eval.Message);
        }

        [Fact]
        public void BootCheck_over_capacity_lease_still_grants_but_warns_in_message()
        {
            var now = DateTime.UtcNow;
            WriteLease(validUntilUtc: now.AddHours(48), overCapacity: true);

            var eval = LicenseBootCheck.Evaluate(LicensePath, Fp,
                DateOnly.FromDateTime(now), LeasePath, now);

            Assert.Equal(LicenseStatus.SeatLeased, eval.Status);  // 전환기: 차단하지 않음
            Assert.Contains("좌석 초과", eval.Message);
        }

        [Fact]
        public void BootCheck_expired_lease_falls_back_to_Missing_with_hint()
        {
            var now = DateTime.UtcNow;
            WriteLease(validUntilUtc: now.AddHours(-1));

            var eval = LicenseBootCheck.Evaluate(LicensePath, Fp,
                DateOnly.FromDateTime(now), LeasePath, now);

            Assert.Equal(LicenseStatus.Missing, eval.Status);
            Assert.Contains("유예 만료", eval.Message);  // 서버 연결 확인 유도
        }

        [Fact]
        public void BootCheck_local_license_takes_precedence_over_lease()
        {
            // 로컬 파일이 있으면 (서명 불일치라도) 임대 캐시는 보지 않는다 — 진단 명확성
            File.WriteAllText(LicensePath, "garbage");
            WriteLease(validUntilUtc: DateTime.UtcNow.AddHours(48));

            var eval = LicenseBootCheck.Evaluate(LicensePath, Fp,
                DateOnly.FromDateTime(DateTime.UtcNow), LeasePath);

            Assert.Equal(LicenseStatus.Invalid, eval.Status);
        }

        // ─── LicenseClient ───────────────────────────────────────

        private sealed class StubHandler : HttpMessageHandler
        {
            public HttpStatusCode Status = HttpStatusCode.OK;
            public string Body = "{}";
            public HttpRequestMessage? LastRequest;
            public string? LastRequestBody;

            protected override async Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request, CancellationToken cancellationToken)
            {
                LastRequest = request;
                LastRequestBody = request.Content == null
                    ? null
                    : await request.Content.ReadAsStringAsync(cancellationToken);
                return new HttpResponseMessage(Status)
                {
                    Content = new StringContent(Body, Encoding.UTF8, "application/json")
                };
            }
        }

        private LicenseClient CreateClient(StubHandler handler) =>
            new(new HttpClient(handler), "http://localhost:5292", clientIndex: 3, LeasePath);

        [Fact]
        public async Task Lease_success_parses_response_and_saves_cache()
        {
            var handler = new StubHandler
            {
                Body = "{\"granted\":true,\"licenseStatus\":\"Valid\",\"licenseId\":\"LIC-2026-0007\"," +
                       "\"customer\":\"A사\",\"seatsUsed\":2,\"maxClients\":5,\"overCapacity\":false," +
                       "\"validUntilUtc\":\"2026-08-28T01:00:00Z\"}"
            };
            var lease = await CreateClient(handler).LeaseAsync();

            Assert.NotNull(lease);
            Assert.Equal("LIC-2026-0007", lease!.LicenseId);
            Assert.Equal(2, lease.SeatsUsed);
            Assert.Equal(new DateTime(2026, 8, 28, 1, 0, 0, DateTimeKind.Utc), lease.ValidUntilUtc);

            // 요청 본문에 좌석 식별 요소 포함
            Assert.Contains("\"clientIndex\":3", handler.LastRequestBody);
            Assert.Contains("machineFingerprint", handler.LastRequestBody);
            // 캐시 저장 확인 — 다음 기동의 BootCheck 근거
            Assert.Equal("LIC-2026-0007", SeatLeaseCache.TryLoad(LeasePath)?.LicenseId);
        }

        [Fact]
        public async Task Lease_denied_by_forward_compat_granted_false_returns_null_without_cache()
        {
            var handler = new StubHandler { Body = "{\"granted\":false,\"message\":\"만석\"}" };
            var lease = await CreateClient(handler).LeaseAsync();

            Assert.Null(lease);
            Assert.Null(SeatLeaseCache.TryLoad(LeasePath));
        }

        [Fact]
        public async Task Lease_http_error_returns_null()
        {
            var handler = new StubHandler { Status = HttpStatusCode.Unauthorized };
            Assert.Null(await CreateClient(handler).LeaseAsync());
        }

        [Fact]
        public async Task Lease_malformed_json_returns_null()
        {
            var handler = new StubHandler { Body = "oops" };
            Assert.Null(await CreateClient(handler).LeaseAsync());
        }
    }
}
