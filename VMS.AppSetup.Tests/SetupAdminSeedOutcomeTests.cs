using VMS.AppSetup.ViewModels;
using Xunit;

namespace VMS.AppSetup.Tests
{
    /// <summary>
    /// [Finish] 저장 완료 창의 관리자 계정 처리 결과 줄.
    ///
    /// <para>사용자 매뉴얼 3.3 은 VMS Admin 비밀번호를 "신규 설치 시 필수" 라고 적는데, 비워 둔 채 저장하면
    /// 아무 안내 없이 admin 계정이 없는 상태로 끝났다 (GS 매뉴얼 대조 01 #13 ①). 저장을 막지는 않고 알려 준다.</para>
    /// </summary>
    public class SetupAdminSeedOutcomeTests
    {
        [Fact]
        public void Blank_password_on_a_new_install_warns_that_admin_is_missing()
        {
            var msg = SetupViewModel.DescribeAdminSeedOutcome(
                seededAdmin: false, adminAlreadyExists: false, adminInputGiven: false,
                adminMissingWithoutInput: true, updatedLocalAdmin: false, localInputGiven: false);

            Assert.Contains("admin 계정이 아직 없습니다", msg);
            Assert.Contains("8자 이상", msg);
        }

        [Fact]
        public void Blank_password_with_an_existing_admin_says_nothing()
        {
            // 이미 계정이 있는 PC 에서는 비워 두면 바꾸지 않는다 — 안내할 것이 없다
            var msg = SetupViewModel.DescribeAdminSeedOutcome(
                seededAdmin: false, adminAlreadyExists: false, adminInputGiven: false,
                adminMissingWithoutInput: false, updatedLocalAdmin: false, localInputGiven: false);

            Assert.Equal(string.Empty, msg);
        }

        [Fact]
        public void Existing_lines_are_unchanged()
        {
            Assert.Contains("✓ admin 계정 생성 완료.", SetupViewModel.DescribeAdminSeedOutcome(true, false, true, false, false, false));
            Assert.Contains("이미 존재하여 입력한 비밀번호는 적용되지 않았습니다",
                SetupViewModel.DescribeAdminSeedOutcome(false, true, true, false, false, false));
            Assert.Contains("admin 비밀번호가 8자 미만이라 계정이 생성되지 않았습니다",
                SetupViewModel.DescribeAdminSeedOutcome(false, false, true, false, false, false));
            Assert.Contains("✓ local-admin 비밀번호 변경 완료.", SetupViewModel.DescribeAdminSeedOutcome(false, false, false, false, true, true));
            Assert.Contains("local-admin 비밀번호가 8자 미만이라 변경되지 않았습니다",
                SetupViewModel.DescribeAdminSeedOutcome(false, false, false, false, false, true));
        }

        [Fact]
        public void Short_password_message_wins_over_the_missing_admin_notice()
        {
            // 입력은 했는데 짧았던 경우 — "입력하지 않아" 문구가 나오면 안 된다
            var msg = SetupViewModel.DescribeAdminSeedOutcome(
                seededAdmin: false, adminAlreadyExists: false, adminInputGiven: true,
                adminMissingWithoutInput: true, updatedLocalAdmin: false, localInputGiven: false);

            Assert.Contains("8자 미만", msg);
            Assert.DoesNotContain("입력하지 않아", msg);
        }
    }
}
