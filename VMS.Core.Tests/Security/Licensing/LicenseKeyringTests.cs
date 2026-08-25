using System;
using System.Security.Cryptography;
using VMS.Core.Security.Licensing;
using Xunit;

namespace VMS.Core.Tests.Security.Licensing
{
    /// <summary>
    /// 내장 키링 무결성 — 등록된 공개키가 전부 유효한 P-256 SPKI 인지 검증.
    /// 키 등록 시 복사 실수(잘림/오타)는 현장에서 "서명 불일치"로만 나타나므로 여기서 차단.
    /// </summary>
    public class LicenseKeyringTests
    {
        [Fact]
        public void All_keyring_entries_are_importable_p256_public_keys()
        {
            Assert.NotEmpty(LicenseKeyring.Default);

            foreach (var (keyId, publicKeyB64) in LicenseKeyring.Default)
            {
                using var ecdsa = ECDsa.Create();
                var bytes = Convert.FromBase64String(publicKeyB64);
                ecdsa.ImportSubjectPublicKeyInfo(bytes, out var read);

                Assert.True(read == bytes.Length, $"이유: keyId '{keyId}' 공개키가 SPKI 전체를 소비하지 않음");
                Assert.Equal(256, ecdsa.KeySize);
            }
        }

        [Fact]
        public void Keyring_contains_expected_key_ids()
        {
            // 운영 발급 체계의 전제 (docs/license_operations.md) — 실수로 키를 지우면 기존
            // 발급분 전체가 "신뢰하지 않는 키"로 무효화되므로 명시적으로 고정.
            Assert.True(LicenseKeyring.Default.ContainsKey("dev-2026"));
            Assert.True(LicenseKeyring.Default.ContainsKey("prod-2026"));
        }
    }
}
