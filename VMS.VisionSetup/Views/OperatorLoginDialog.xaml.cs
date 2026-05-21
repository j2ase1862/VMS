using System;
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
            // VMS Launcher 환경에서도 동작하도록 WindowStyles 리소스 보장.
            // VMS.VisionSetup.App 이 별도 process로만 머지하므로, launcher가 띄울 때는
            // Application.Current.Resources 에 BrushBgWindow 등이 없다.
            // InitializeComponent() 전에 명시 머지 — XAML 파싱 시점에 리소스 사용 가능.
            EnsureWindowStylesMerged();
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

        /// <summary>
        /// Application.Current.Resources 에 BrushBgWindow 가 없으면 (= VMS launcher 환경)
        /// VMS.VisionSetup 의 WindowStyles.xaml 을 강제 머지. 이미 있으면 no-op.
        /// </summary>
        private static void EnsureWindowStylesMerged()
        {
            var app = Application.Current;
            if (app == null) return;
            if (app.Resources.Contains("BrushBgWindow")) return;

            try
            {
                var dict = new ResourceDictionary
                {
                    Source = new Uri(
                        "pack://application:,,,/VMS.VisionSetup;component/Styles/WindowStyles.xaml",
                        UriKind.Absolute)
                };
                app.Resources.MergedDictionaries.Add(dict);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[OperatorLoginDialog] Failed to merge WindowStyles: {ex.Message}");
            }
        }
    }
}
