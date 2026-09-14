using System;
using System.IO;
using VMS.AppSetup.Services;
using Xunit;

namespace VMS.AppSetup.Tests
{
    /// <summary>
    /// Web 운영 설정 파일의 접근 권한 제한.
    ///
    /// <para><b>왜 필요한가.</b> <c>appsettings.Production.json</c> 에는 JWT 서명 키와 초기 admin
    /// 비밀번호가 평문으로 들어간다. 키가 새면 임의의 Admin 토큰을 위조할 수 있어 계정 잠금·
    /// rate limit·감사 로그가 한꺼번에 무력화되는데, 기본 상속 권한으로는 그 PC 의 모든 사용자가
    /// 읽을 수 있었다(두 리포 통틀어 ACL 코드가 0건이었다).</para>
    /// </summary>
    public class WebConfigSecretAclTests
    {
        [Fact]
        public void Restricts_an_existing_file()
        {
            var path = Path.Combine(Path.GetTempPath(), $"boda-acl-{Guid.NewGuid():N}.json");
            File.WriteAllText(path, "{}");
            try
            {
                Assert.True(WebServerConfigApplier.ProtectSecretFile(path));

                // 상속이 끊기고 Users/Everyone 이 남아 있지 않아야 한다.
                var acl = new FileInfo(path).GetAccessControl();
                Assert.True(acl.AreAccessRulesProtected, "상속이 끊겨 있어야 한다");

                foreach (System.Security.AccessControl.FileSystemAccessRule rule in
                         acl.GetAccessRules(true, true, typeof(System.Security.Principal.SecurityIdentifier)))
                {
                    var sid = (System.Security.Principal.SecurityIdentifier)rule.IdentityReference;
                    Assert.False(
                        sid.IsWellKnown(System.Security.Principal.WellKnownSidType.BuiltinUsersSid)
                        || sid.IsWellKnown(System.Security.Principal.WellKnownSidType.WorldSid),
                        $"일반 사용자 권한이 남아 있다: {sid}");
                }
            }
            finally
            {
                try { File.Delete(path); } catch { /* 정리 실패는 무시 */ }
            }
        }

        /// <summary>없는 파일에 대해서는 실패를 알려야 한다 — 조용히 true 를 주면 안 됐는데 안 된 걸 모른다.</summary>
        [Fact]
        public void Reports_failure_for_a_missing_file()
        {
            var missing = Path.Combine(Path.GetTempPath(), $"boda-acl-missing-{Guid.NewGuid():N}.json");
            Assert.False(WebServerConfigApplier.ProtectSecretFile(missing));
        }
    }
}
