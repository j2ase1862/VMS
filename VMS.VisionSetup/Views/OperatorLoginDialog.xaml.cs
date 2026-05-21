using System.Windows;
using System.Windows.Input;
using VMS.Core.Models.ParameterSync;
using VMS.Core.Services;
using VMS.VisionSetup.ViewModels;

namespace VMS.VisionSetup.Views
{
    public partial class OperatorLoginDialog : Window
    {
        private readonly OperatorLoginViewModel _vm;

        public OperatorSessionDto? Result => _vm.Result;

        public OperatorLoginDialog(OperatorAuthService authService)
        {
            InitializeComponent();
            _vm = new OperatorLoginViewModel(authService);
            _vm.Finished += success =>
            {
                if (success)
                {
                    DialogResult = true;
                    Close();
                }
            };
            DataContext = _vm;
            Loaded += (_, _) => EmployeeNumberBox.Focus();
        }

        // PasswordBox.Password 는 BindingProperty가 아니라 코드 비하인드에서 동기화
        private void PinBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            _vm.Pin = PinBox.Password;
        }

        private void PinBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && _vm.LoginCommand.CanExecute(null))
            {
                _vm.LoginCommand.Execute(null);
                e.Handled = true;
            }
        }
    }
}
