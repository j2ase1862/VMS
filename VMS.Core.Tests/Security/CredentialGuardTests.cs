using System;
using VMS.Core.Security;
using Xunit;

namespace VMS.Core.Tests.Security
{
    public class CredentialGuardTests
    {
        // ─── ValidateIdentifier ────────────────────────────────────

        [Theory]
        [InlineData("user123")]
        [InlineData("EMP-001")]
        [InlineData("a")]
        public void ValidateIdentifier_Valid_DoesNotThrow(string value)
        {
            CredentialGuard.ValidateIdentifier(value, nameof(value));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void ValidateIdentifier_NullOrEmpty_Throws(string? value)
        {
            Assert.Throws<ArgumentException>(() =>
                CredentialGuard.ValidateIdentifier(value, nameof(value)));
        }

        [Fact]
        public void ValidateIdentifier_TooLong_Throws()
        {
            var tooLong = new string('a', CredentialGuard.MaxIdentifierLength + 1);
            Assert.Throws<ArgumentException>(() =>
                CredentialGuard.ValidateIdentifier(tooLong, nameof(tooLong)));
        }

        [Fact]
        public void ValidateIdentifier_ExactlyMaxLength_DoesNotThrow()
        {
            var exact = new string('a', CredentialGuard.MaxIdentifierLength);
            CredentialGuard.ValidateIdentifier(exact, nameof(exact));
        }

        [Theory]
        [InlineData("user\n123")]     // 개행
        [InlineData("user\t123")]     // 탭
        [InlineData("user\0123")]     // NUL
        [InlineData("user\x1B[31m")]  // ESC (ANSI)
        public void ValidateIdentifier_ControlChars_Throws(string value)
        {
            Assert.Throws<ArgumentException>(() =>
                CredentialGuard.ValidateIdentifier(value, nameof(value)));
        }

        // ─── ValidateSecret ────────────────────────────────────────

        [Theory]
        [InlineData("1234")]
        [InlineData("MyP@ssw0rd!")]
        [InlineData("a")]
        public void ValidateSecret_Valid_DoesNotThrow(string value)
        {
            CredentialGuard.ValidateSecret(value, nameof(value));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void ValidateSecret_NullOrEmpty_Throws(string? value)
        {
            Assert.Throws<ArgumentException>(() =>
                CredentialGuard.ValidateSecret(value, nameof(value)));
        }

        [Fact]
        public void ValidateSecret_MinLengthEnforced()
        {
            Assert.Throws<ArgumentException>(() =>
                CredentialGuard.ValidateSecret("ab", "pwd", minLength: 4));
        }

        [Fact]
        public void ValidateSecret_TooLong_Throws()
        {
            var tooLong = new string('x', CredentialGuard.MaxSecretLength + 1);
            Assert.Throws<ArgumentException>(() =>
                CredentialGuard.ValidateSecret(tooLong, nameof(tooLong)));
        }

        [Theory]
        [InlineData("pass\nword")]
        [InlineData("pass\tword")]
        [InlineData("pass\0word")]
        public void ValidateSecret_ControlChars_Throws(string value)
        {
            Assert.Throws<ArgumentException>(() =>
                CredentialGuard.ValidateSecret(value, nameof(value)));
        }

        // ─── ClearSecretField ──────────────────────────────────────

        [Fact]
        public void ClearSecretField_AssignsEmpty()
        {
            string secret = "MyPassword";
            CredentialGuard.ClearSecretField(ref secret);
            Assert.Equal(string.Empty, secret);
        }

        // ─── ZeroFill ─────────────────────────────────────────────

        [Fact]
        public void ZeroFill_ZerosAllBytes()
        {
            var buf = new byte[] { 1, 2, 3, 4, 5 };
            CredentialGuard.ZeroFill(buf);
            Assert.All(buf, b => Assert.Equal(0, b));
        }

        [Fact]
        public void ZeroFill_NullBuffer_NoThrow()
        {
            // null 입력은 silently 통과 — 호출자 보호.
            CredentialGuard.ZeroFill(null);
        }
    }
}
