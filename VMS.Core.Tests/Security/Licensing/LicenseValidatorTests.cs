using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using VMS.Core.Security.Licensing;
using Xunit;

namespace VMS.Core.Tests.Security.Licensing
{
    /// <summary>
    /// LicenseValidator — 서명/스키마/지문/만료 상태 전이 (docs/design/license-spec.md §3·§5).
    /// 임시 키쌍으로 서명해 커스텀 키링으로 검증 — 내장 키링(dev-2026) 비의존.
    /// </summary>
    public class LicenseValidatorTests
    {
        private const string Fp = "AAAAA-BBBBB-CCCCC";
        private static readonly DateOnly Today = new(2026, 8, 25);

        private readonly string _privateKey;
        private readonly IReadOnlyDictionary<string, string> _keyring;

        public LicenseValidatorTests()
        {
            var (priv, pub) = LicenseCrypto.CreateKeyPair();
            _privateKey = priv;
            _keyring = new Dictionary<string, string> { ["test-key"] = pub };
        }

        private static LicensePayload MakePayload(
            string kind = "Production", string fingerprint = Fp,
            string? expiresAt = null, string? maintenanceUntil = null,
            int maxClients = 1, int schemaVersion = 1, string keyId = "test-key") => new()
        {
            SchemaVersion = schemaVersion,
            LicenseId = "LIC-2026-0001",
            Customer = "테스트고객",
            Kind = kind,
            Fingerprint = fingerprint,
            IssuedAt = "2026-08-25",
            ExpiresAt = expiresAt,
            MaintenanceUntil = maintenanceUntil,
            MaxClients = maxClients,
            KeyId = keyId
        };

        private string SignedFile(LicensePayload payload, Action<JsonObject>? mutateAfterSign = null)
        {
            var node = (JsonObject)JsonSerializer.SerializeToNode(payload)!;
            var signature = LicenseCrypto.Sign(_privateKey, LicenseCanonicalJson.Serialize(node));
            mutateAfterSign?.Invoke(node);
            return new JsonObject { ["payload"] = node, ["signature"] = signature }.ToJsonString();
        }

        private LicenseEvaluation Validate(string fileJson, string machineFp = Fp, DateOnly? today = null) =>
            LicenseValidator.Validate(fileJson, machineFp, today ?? Today, _keyring);

        // ─── 서명 / 형식 ─────────────────────────────────────────

        [Fact]
        public void Valid_production_license_passes()
        {
            var eval = Validate(SignedFile(MakePayload(maintenanceUntil: "2027-08-25")));
            Assert.Equal(LicenseStatus.Valid, eval.Status);
            Assert.False(eval.IsBlocking);
            Assert.NotNull(eval.Payload);
        }

        [Fact]
        public void Tampered_payload_is_Invalid()
        {
            // 서명 후 maxClients 를 조작 — 서명 불일치
            var json = SignedFile(MakePayload(), node => node["maxClients"] = 999);
            Assert.Equal(LicenseStatus.Invalid, Validate(json).Status);
        }

        [Fact]
        public void Unknown_keyId_is_Invalid()
        {
            var eval = Validate(SignedFile(MakePayload(keyId: "rogue-key")));
            Assert.Equal(LicenseStatus.Invalid, eval.Status);
            Assert.Contains("rogue-key", eval.Message);
        }

        [Fact]
        public void Garbage_json_is_Invalid()
        {
            Assert.Equal(LicenseStatus.Invalid, Validate("not json at all").Status);
            Assert.Equal(LicenseStatus.Invalid, Validate("{}").Status);
        }

        [Fact]
        public void Unknown_extra_field_keeps_signature_valid()
        {
            // 전방 호환 — 서명은 canonical(payload 원본 노드) 기준이므로,
            // 신규 필드가 포함된 채 서명된 파일은 구버전 검증기에서도 유효해야 한다.
            var payload = MakePayload();
            var node = (JsonObject)JsonSerializer.SerializeToNode(payload)!;
            node["futureField"] = "reserved";
            var signature = LicenseCrypto.Sign(_privateKey, LicenseCanonicalJson.Serialize(node));
            var json = new JsonObject { ["payload"] = node, ["signature"] = signature }.ToJsonString();

            Assert.Equal(LicenseStatus.Valid, Validate(json).Status);
        }

        [Fact]
        public void Unsupported_schemaVersion_is_Invalid()
        {
            var eval = Validate(SignedFile(MakePayload(schemaVersion: 2)));
            Assert.Equal(LicenseStatus.Invalid, eval.Status);
            Assert.Contains("schemaVersion", eval.Message);
        }

        // ─── kind 별 스키마 규칙 ─────────────────────────────────

        [Fact]
        public void Internal_without_expiry_is_Invalid()
        {
            var eval = Validate(SignedFile(MakePayload(kind: "Internal", fingerprint: "*")));
            Assert.Equal(LicenseStatus.Invalid, eval.Status);
        }

        [Fact]
        public void Internal_with_wildcard_and_expiry_is_Valid()
        {
            var json = SignedFile(MakePayload(kind: "Internal", fingerprint: "*", expiresAt: "2026-11-20"));
            Assert.Equal(LicenseStatus.Valid, Validate(json, machineFp: "ZZZZZ-YYYYY-XXXXX").Status);
        }

        [Fact]
        public void Production_with_wildcard_is_Invalid()
        {
            var eval = Validate(SignedFile(MakePayload(fingerprint: "*")));
            Assert.Equal(LicenseStatus.Invalid, eval.Status);
        }

        [Fact]
        public void Trial_without_expiry_is_Invalid()
        {
            var eval = Validate(SignedFile(MakePayload(kind: "Trial")));
            Assert.Equal(LicenseStatus.Invalid, eval.Status);
        }

        [Fact]
        public void Invalid_maxClients_is_Invalid()
        {
            var eval = Validate(SignedFile(MakePayload(maxClients: 0)));
            Assert.Equal(LicenseStatus.Invalid, eval.Status);
        }

        // ─── 지문 ────────────────────────────────────────────────

        [Fact]
        public void Two_of_three_segments_matching_passes()
        {
            var eval = Validate(SignedFile(MakePayload()), machineFp: "AAAAA-BBBBB-ZZZZZ");
            Assert.Equal(LicenseStatus.Valid, eval.Status);
        }

        [Fact]
        public void One_of_three_segments_matching_is_FingerprintMismatch()
        {
            var eval = Validate(SignedFile(MakePayload()), machineFp: "AAAAA-YYYYY-ZZZZZ");
            Assert.Equal(LicenseStatus.FingerprintMismatch, eval.Status);
            Assert.True(eval.IsBlocking);
        }

        // ─── 만료 상태 전이 (spec §5 표) ─────────────────────────

        [Theory]
        [InlineData("2026-12-31", LicenseStatus.Valid)]           // 경고 구간 밖
        [InlineData("2026-09-10", LicenseStatus.Expiring)]        // 30일 이내
        [InlineData("2026-08-25", LicenseStatus.Expiring)]        // 만료 당일 — 아직 유효
        [InlineData("2026-08-20", LicenseStatus.ExpiredGrace)]    // 만료 + 유예 14일 이내
        [InlineData("2026-08-11", LicenseStatus.ExpiredGrace)]    // 유예 마지막 날
        [InlineData("2026-08-10", LicenseStatus.Expired)]         // 유예 경과
        public void Subscription_expiry_transitions(string expiresAt, LicenseStatus expected)
        {
            var eval = Validate(SignedFile(MakePayload(expiresAt: expiresAt)));
            Assert.Equal(expected, eval.Status);
        }

        [Theory]
        [InlineData("2027-08-25", LicenseStatus.Valid)]
        [InlineData("2026-09-10", LicenseStatus.MaintenanceExpiring)]
        [InlineData("2026-08-01", LicenseStatus.MaintenanceExpired)]
        public void Maintenance_expiry_transitions(string maintenanceUntil, LicenseStatus expected)
        {
            var eval = Validate(SignedFile(MakePayload(maintenanceUntil: maintenanceUntil)));
            Assert.Equal(expected, eval.Status);
            Assert.False(eval.IsBlocking);  // 유지보수 축은 실행을 막지 않는다
        }

        [Fact]
        public void Bad_date_format_is_Invalid()
        {
            var eval = Validate(SignedFile(MakePayload(expiresAt: "2026/12/31")));
            Assert.Equal(LicenseStatus.Invalid, eval.Status);
        }
    }
}
