using System;
using System.IO;
using VMS.Core.Security;
using Xunit;

namespace VMS.Core.Tests.Security
{
    public class InputValidatorTests
    {
        // ─── IsPathWithinDirectory ────────────────────────────────

        [Fact]
        public void IsPathWithinDirectory_FileInside_True()
        {
            var dir = Path.GetTempPath();
            var file = Path.Combine(dir, "test.txt");
            Assert.True(InputValidator.IsPathWithinDirectory(file, dir));
        }

        [Fact]
        public void IsPathWithinDirectory_SubdirectoryInside_True()
        {
            var dir = Path.GetTempPath();
            var sub = Path.Combine(dir, "sub", "deep", "file.txt");
            Assert.True(InputValidator.IsPathWithinDirectory(sub, dir));
        }

        [Fact]
        public void IsPathWithinDirectory_TraversalUp_False()
        {
            var dir = Path.Combine(Path.GetTempPath(), "allowed");
            // ../ 점프로 부모 디렉토리 시도
            var attack = Path.Combine(dir, "..", "..", "windows", "system32", "evil.exe");
            Assert.False(InputValidator.IsPathWithinDirectory(attack, dir));
        }

        [Fact]
        public void IsPathWithinDirectory_AbsolutePathOutside_False()
        {
            var dir = Path.Combine(Path.GetTempPath(), "allowed");
            var outside = @"C:\Windows\System32\evil.exe";
            Assert.False(InputValidator.IsPathWithinDirectory(outside, dir));
        }

        [Theory]
        [InlineData("", "C:\\dir")]
        [InlineData("C:\\file.txt", "")]
        [InlineData(null, "C:\\dir")]
        public void IsPathWithinDirectory_EmptyOrNull_False(string? path, string dir)
        {
            Assert.False(InputValidator.IsPathWithinDirectory(path!, dir));
        }

        [Fact]
        public void IsPathWithinDirectory_PrefixCollision_False()
        {
            // /tmp/foo 가 /tmp/foobar 의 접두라 false-positive 가능성 — 디렉토리 구분자로 차단되어야.
            var dir = Path.Combine(Path.GetTempPath(), "foo");
            var collide = Path.Combine(Path.GetTempPath(), "foobar", "file.txt");
            Assert.False(InputValidator.IsPathWithinDirectory(collide, dir));
        }

        [Fact]
        public void EnsurePathInside_Outside_Throws()
        {
            var dir = Path.Combine(Path.GetTempPath(), "allowed");
            var outside = @"C:\Windows\evil.exe";
            Assert.Throws<ArgumentException>(() =>
                InputValidator.EnsurePathInside(outside, dir, "path"));
        }

        // ─── HasAllowedExtension ──────────────────────────────────

        [Theory]
        [InlineData("model.onnx", ".onnx")]
        [InlineData("MODEL.ONNX", ".onnx")]  // case-insensitive
        [InlineData("dict.txt", "txt")]      // 점 없는 인자
        public void HasAllowedExtension_Match_True(string path, string allowed)
        {
            Assert.True(InputValidator.HasAllowedExtension(path, allowed));
        }

        [Theory]
        [InlineData("model.exe", ".onnx")]
        [InlineData("dict", ".txt")]          // 확장자 없음
        [InlineData("", ".txt")]
        public void HasAllowedExtension_NoMatch_False(string path, string allowed)
        {
            Assert.False(InputValidator.HasAllowedExtension(path, allowed));
        }

        [Fact]
        public void HasAllowedExtension_MultipleAllowed_OneMatches_True()
        {
            Assert.True(InputValidator.HasAllowedExtension("dict.txt", ".onnx", ".txt"));
        }

        [Fact]
        public void EnsureAllowedExtension_Mismatch_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
                InputValidator.EnsureAllowedExtension("evil.exe", "path", ".onnx"));
        }

        // ─── IsStringLengthValid ──────────────────────────────────

        [Theory]
        [InlineData("abc", 1, 5, true)]
        [InlineData("abc", 3, 3, true)]   // 정확히 경계
        [InlineData("", 0, 5, true)]
        [InlineData("ab", 3, 5, false)]   // 너무 짧음
        [InlineData("abcdef", 1, 5, false)]  // 너무 김
        public void IsStringLengthValid(string value, int min, int max, bool expected)
        {
            Assert.Equal(expected, InputValidator.IsStringLengthValid(value, min, max));
        }

        // ─── IsInRange ────────────────────────────────────────────

        [Theory]
        [InlineData(5, 1, 10, true)]
        [InlineData(1, 1, 10, true)]   // 하한
        [InlineData(10, 1, 10, true)]  // 상한
        [InlineData(0, 1, 10, false)]
        [InlineData(11, 1, 10, false)]
        public void IsInRange_Int(int value, int min, int max, bool expected)
        {
            Assert.Equal(expected, InputValidator.IsInRange(value, min, max));
        }

        // ─── IsResponseSizeValid ──────────────────────────────────

        [Theory]
        [InlineData(null, true)]   // Content-Length 미지정 = 통과
        [InlineData(0L, true)]
        [InlineData(1024L, true)]
        public void IsResponseSizeValid_Allowed(long? length, bool expected)
        {
            Assert.Equal(expected, InputValidator.IsResponseSizeValid(length));
        }

        [Fact]
        public void IsResponseSizeValid_ExactlyDefaultMax_True()
        {
            // InlineData 가 int → long? 자동 변환 못함 — 명시 캐스팅이 필요한 케이스만 별도 분리.
            Assert.True(InputValidator.IsResponseSizeValid((long)InputValidator.DefaultMaxResponseBytes));
        }

        [Fact]
        public void IsResponseSizeValid_Exceeds_False()
        {
            long over = InputValidator.DefaultMaxResponseBytes + 1L;
            Assert.False(InputValidator.IsResponseSizeValid(over));
        }

        [Fact]
        public void IsResponseSizeValid_CustomLimit()
        {
            Assert.True(InputValidator.IsResponseSizeValid(500, maxBytes: 1024));
            Assert.False(InputValidator.IsResponseSizeValid(2000, maxBytes: 1024));
        }
    }
}
