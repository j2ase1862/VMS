using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Threading.Tasks;
using VMS.Core.Models.ParameterSync;
using VMS.Core.Services;

namespace VMS.VisionSetup.ViewModels
{
    public partial class OperatorLoginViewModel : ObservableObject
    {
        private readonly OperatorAuthService _authService;

        public OperatorLoginViewModel(OperatorAuthService authService)
        {
            _authService = authService;
            LoginCommand = new AsyncRelayCommand(LoginAsync, CanLogin);
        }

        [ObservableProperty] private string _employeeNumber = "";
        [ObservableProperty] private string _pin = "";
        [ObservableProperty] private bool _isBusy;
        [ObservableProperty] private string _errorMessage = "";

        public IAsyncRelayCommand LoginCommand { get; }

        public OperatorSessionDto? Result { get; private set; }
        public event Action<bool>? Finished;  // true=success

        partial void OnEmployeeNumberChanged(string value) { LoginCommand.NotifyCanExecuteChanged(); ErrorMessage = ""; }
        partial void OnPinChanged(string value) { LoginCommand.NotifyCanExecuteChanged(); ErrorMessage = ""; }
        partial void OnIsBusyChanged(bool value) => LoginCommand.NotifyCanExecuteChanged();

        private bool CanLogin() =>
            !IsBusy && !string.IsNullOrWhiteSpace(EmployeeNumber) && !string.IsNullOrWhiteSpace(Pin);

        private async Task LoginAsync()
        {
            IsBusy = true;
            ErrorMessage = "";
            try
            {
                var (ok, session, error) = await _authService.LoginAsync(EmployeeNumber.Trim(), Pin);
                if (ok && session != null)
                {
                    Result = session;
                    Finished?.Invoke(true);
                }
                else
                {
                    // PIN 을 먼저 비운다 — OnPinChanged 가 오류 문구를 지우므로, 문구는 그 뒤에 넣어야 남는다 (VM-32)
                    Pin = "";  // PIN 클리어 (화면 PIN 칸은 대화상자가 이 변경을 보고 비운다)
                    ErrorMessage = error ?? "로그인 실패";
                }
            }
            finally
            {
                IsBusy = false;
            }
        }
    }
}
