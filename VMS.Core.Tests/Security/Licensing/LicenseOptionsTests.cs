using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using VMS.Core.Security.Licensing;
using Xunit;

namespace VMS.Core.Tests.Security.Licensing
{
    /// <summary>
    /// 확장 옵션 — options 를 적은 라이선스만 그 목록을 따르고, 나머지는 전부 켠다
    /// (옛 발급분 · 라이선스 없는 PC 의 현장 기능 보호).
    /// </summary>
    public class LicenseOptionsTests
    {
        private const string Fp = "AAAAA-BBBBB-CCCCC";
        private static readonly DateOnly Today = new(2026, 10, 30);

        private readonly string _privateKey;
        private readonly IReadOnlyDictionary<string, string> _keyring;

        public LicenseOptionsTests()
        {
            var (priv, pub) = LicenseCrypto.CreateKeyPair();
            _privateKey = priv;
            _keyring = new Dictionary<string, string> { ["test-key"] = pub };
        }

        private static LicensePayload MakePayload(string[]? options) => new()
        {
            LicenseId = "LIC-2026-0099",
            Customer = "테스트고객",
            Fingerprint = Fp,
            IssuedAt = "2026-10-30",
            KeyId = "test-key",
            Options = options
        };

        private string SignedFile(JsonObject payloadNode)
        {
            var signature = LicenseCrypto.Sign(_privateKey, LicenseCanonicalJson.Serialize(payloadNode));
            return new JsonObject { ["payload"] = payloadNode, ["signature"] = signature }.ToJsonString();
        }

        private string SignedFile(LicensePayload payload) =>
            SignedFile((JsonObject)JsonSerializer.SerializeToNode(payload)!);

        private LicenseOptions OptionsOf(string file) =>
            LicenseOptions.From(LicenseValidator.Validate(file, Fp, Today, _keyring));

        [Fact]
        public void License_without_options_field_enables_everything()
        {
            var node = (JsonObject)JsonSerializer.SerializeToNode(MakePayload(null))!;
            Assert.False(node.ContainsKey("options")); // 옛 발급분과 같은 모양 — 필드 자체가 없다

            var options = OptionsOf(SignedFile(node));

            Assert.True(options.IsUnrestricted);
            Assert.True(options.Camera3D && options.Robot && options.MLOps);
        }

        [Fact]
        public void Empty_options_is_the_base_edition()
        {
            var options = OptionsOf(SignedFile(MakePayload(Array.Empty<string>())));

            Assert.False(options.IsUnrestricted);
            Assert.False(options.Camera3D);
            Assert.False(options.Robot);
            Assert.False(options.MLOps);
            Assert.Equal("기본 구성 (확장 옵션 없음)", options.Describe());
        }

        [Fact]
        public void Listed_options_are_the_only_ones_enabled()
        {
            var options = OptionsOf(SignedFile(MakePayload(new[] { LicenseOptionKeys.Robot })));

            Assert.True(options.Robot);
            Assert.False(options.Camera3D);
            Assert.False(options.MLOps);
        }

        [Fact]
        public void Options_license_stays_schema_version_1_so_older_validators_accept_it()
        {
            var eval = LicenseValidator.Validate(SignedFile(MakePayload(Array.Empty<string>())), Fp, Today, _keyring);

            Assert.Equal(LicenseStatus.Valid, eval.Status);
            Assert.Equal(1, eval.Payload!.SchemaVersion);
        }

        [Fact]
        public void Tampering_with_options_breaks_the_signature_and_falls_back_to_everything()
        {
            var node = (JsonObject)JsonSerializer.SerializeToNode(MakePayload(Array.Empty<string>()))!;
            var signature = LicenseCrypto.Sign(_privateKey, LicenseCanonicalJson.Serialize(node));
            node["options"] = new JsonArray("robot");
            var file = new JsonObject { ["payload"] = node, ["signature"] = signature }.ToJsonString();

            var eval = LicenseValidator.Validate(file, Fp, Today, _keyring);

            Assert.Equal(LicenseStatus.Invalid, eval.Status);
            Assert.True(LicenseOptions.From(eval).IsUnrestricted);
        }

        [Fact]
        public void Fingerprint_mismatch_still_honours_the_signed_options()
        {
            var eval = LicenseValidator.Validate(SignedFile(MakePayload(Array.Empty<string>())),
                "ZZZZZ-YYYYY-XXXXX", Today, _keyring);

            Assert.Equal(LicenseStatus.FingerprintMismatch, eval.Status);
            Assert.False(LicenseOptions.From(eval).Robot);
        }

        [Fact]
        public void No_license_enables_everything()
        {
            Assert.True(LicenseOptions.From(null).IsUnrestricted);
            Assert.True(LicenseOptions.From(new LicenseEvaluation { Status = LicenseStatus.Missing }).IsUnrestricted);
        }

        [Fact]
        public void Option_keys_are_case_insensitive()
        {
            var options = LicenseOptions.Of(new[] { "3D-CAMERA" });

            Assert.True(options.Camera3D);
            Assert.False(options.Robot);
        }
    }
}
