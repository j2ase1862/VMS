using System;
using System.IO;
using VMS.Core.Security.Licensing;
using Xunit;

namespace VMS.Core.Tests.Security.Licensing
{
    /// <summary>
    /// LicenseBootCheck.Evaluate — 파일 부재/손상/정상 3분기 (감사 기록 없는 주입 경로).
    /// </summary>
    public class LicenseBootCheckTests : IDisposable
    {
        private const string Fp = "AAAAA-BBBBB-CCCCC";
        private readonly string _tempDir;

        public LicenseBootCheckTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "boda-lic-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_tempDir, recursive: true); } catch { /* best effort */ }
        }

        private string PathOf(string name) => Path.Combine(_tempDir, name);

        [Fact]
        public void Missing_file_is_Missing_not_exception()
        {
            var eval = LicenseBootCheck.Evaluate(PathOf("none.lic"), Fp, new DateOnly(2026, 8, 25));

            Assert.Equal(LicenseStatus.Missing, eval.Status);
            Assert.True(eval.IsBlocking);       // 강제 모드에서 차단 대상
            Assert.True(eval.NeedsAttention);   // 전환기에는 경고 표면화 대상
        }

        [Fact]
        public void Corrupt_file_is_Invalid()
        {
            var path = PathOf("bad.lic");
            File.WriteAllText(path, "corrupted!!");

            var eval = LicenseBootCheck.Evaluate(path, Fp, new DateOnly(2026, 8, 25));
            Assert.Equal(LicenseStatus.Invalid, eval.Status);
        }

        [Fact]
        public void Default_path_points_to_ProgramData_BODA_VMS()
        {
            // Web 서비스 계정과 공유하는 단일 규약 경로 (spec §3) — 회귀 방지 고정
            Assert.EndsWith(Path.Combine("BODA", "VMS", "license.lic"), LicenseFileStore.DefaultPath);
        }
    }
}
