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
                    ErrorMessage = error ?? "로그인 실패";
                    Pin = "";  // PIN 클리어
                }
            }
            finally
            {
                IsBusy = false;
            }
        }
    }
}
