using System;
using System.Security.Cryptography;

namespace VMS.Core.Security.Licensing
{
    /// <summary>
    /// 라이선스 서명 프리미티브 — ECDSA P-256 + SHA-256.
    ///
    /// spec 초안은 Ed25519 였으나 .NET 8 표준 라이브러리에 없어 외부 패키지 없이 가능한
    /// P-256 으로 확정 (spec §3 개정). 공개키는 SubjectPublicKeyInfo base64,
    /// 개인키는 PKCS#8 PEM — LicGen 도구만 개인키를 다룬다.
    /// </summary>
    public static class LicenseCrypto
    {
        /// <summary>새 서명 키쌍 생성 — LicGen keygen 전용.</summary>
        public static (string privateKeyPem, string publicKeySpkiBase64) CreateKeyPair()
        {
            using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var privatePem = ecdsa.ExportPkcs8PrivateKeyPem();
            var publicB64 = Convert.ToBase64String(ecdsa.ExportSubjectPublicKeyInfo());
            return (privatePem, publicB64);
        }

        /// <summary>canonical payload 바이트 서명 — LicGen issue 전용.</summary>
        public static string Sign(string privateKeyPem, byte[] canonicalPayload)
        {
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportFromPem(privateKeyPem);
            return Convert.ToBase64String(
                ecdsa.SignData(canonicalPayload, HashAlgorithmName.SHA256));
        }

        /// <summary>서명 검증 — 형식 오류 포함 모든 실패는 false (예외 비전파).</summary>
        public static bool Verify(string publicKeySpkiBase64, byte[] canonicalPayload, string signatureBase64)
        {
            try
            {
                using var ecdsa = ECDsa.Create();
                ecdsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publicKeySpkiBase64), out _);
                return ecdsa.VerifyData(
                    canonicalPayload, Convert.FromBase64String(signatureBase64), HashAlgorithmName.SHA256);
            }
            catch
            {
                return false;
            }
        }
    }
}
