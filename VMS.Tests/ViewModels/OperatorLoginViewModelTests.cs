using System.Threading.Tasks;
using VMS.Core.Services;
using VMS.VisionSetup.ViewModels;
using Xunit;

namespace VMS.Tests.ViewModels
{
    /// <summary>
    /// VM-32 — 작업자 로그인이 실패하면 오류 문구가 남아 있어야 한다.
    /// 종전에는 실패 직후 PIN 을 비우는 순간 OnPinChanged 가 방금 넣은 문구를 지웠다.
    /// </summary>
    public class OperatorLoginViewModelTests
    {
        [Fact]
        public async Task FailedLogin_KeepsErrorMessage_AndClearsPin()
        {
            // 입력 검증에서 거부되는 사번(제어 문자) — 네트워크 없이 실패 경로를 탄다
            var auth = new OperatorAuthService("https://boda-vms.example", clientIndex: 1);
            var vm = new OperatorLoginViewModel(auth)
            {
                EmployeeNumber = "E1\u0001",
                Pin = "1234"
            };

            await vm.LoginCommand.ExecuteAsync(null);

            Assert.False(string.IsNullOrEmpty(vm.ErrorMessage));
            Assert.Equal("", vm.Pin);
            Assert.Null(vm.Result);
        }

        [Fact]
        public void TypingAgain_ClearsErrorMessage()
        {
            // 다시 입력하기 시작하면 이전 오류는 지운다 (종전 동작 유지)
            var auth = new OperatorAuthService("https://boda-vms.example", clientIndex: 1);
            var vm = new OperatorLoginViewModel(auth) { ErrorMessage = "사번 또는 PIN이 올바르지 않습니다." };

            vm.Pin = "5";

            Assert.Equal("", vm.ErrorMessage);
        }
    }
}
