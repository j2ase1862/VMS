using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using VMS.AppSetup.Models;

namespace VMS.AppSetup;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // chromeless 윈도우 — SystemCommands.MinimizeWindow / CloseWindow 활성화
        CommandBindings.Add(new CommandBinding(SystemCommands.MinimizeWindowCommand,
            (s, e) => SystemCommands.MinimizeWindow(this)));
        CommandBindings.Add(new CommandBinding(SystemCommands.CloseWindowCommand,
            (s, e) => SystemCommands.CloseWindow(this)));
    }

    // C3 (Option C, 2026-06-04): PasswordBox 는 보안상 데이터바인딩 미지원.
    // PasswordChanged 이벤트로 ViewModel 에 평문 전달 — Save 시점에 BCrypt 시드 후 폐기.
    private void InitialAdminPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is ViewModels.SetupViewModel vm)
            vm.InitialAdminPassword = InitialAdminPasswordBox.Password;
    }

    // SSO 체크로 입력란이 비활성화되면 이미 입력된 값도 폐기 — 비활성 상태의
    // 잔여 입력이 "설정했다" 는 착각을 남기지 않도록. (PasswordChanged 가 이어서
    // 발생해 ViewModel 쪽 평문도 함께 비워짐)
    private void InitialAdminPasswordBox_IsEnabledChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is false && InitialAdminPasswordBox.Password.Length > 0)
            InitialAdminPasswordBox.Password = string.Empty;
    }

    private void LocalAdminPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is ViewModels.SetupViewModel vm)
            vm.LocalFallbackAdminPassword = LocalAdminPasswordBox.Password;
    }

    private void BrowseDcfFile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button) return;

        var camera = button.DataContext as CameraConfiguration;
        if (camera == null) return;

        var dialog = new OpenFileDialog
        {
            Title = "Select DCF File",
            Filter = "DCF Files (*.dcf)|*.dcf|All Files (*.*)|*.*",
            DefaultExt = ".dcf"
        };

        if (dialog.ShowDialog() == true)
        {
            camera.DcfFilePath = dialog.FileName;
        }
    }
}