using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace VMS.VisionSetup.Views.Common
{
    /// <summary>
    /// 자체 다크 메시지 다이얼로그 — WPF 기본 MessageBox.Show 의 흰 배경을 대체.
    /// VMS 본체의 MessageDialog 를 솔루션 공용으로 이동한 것 (2026-08-25 검토):
    /// 테마 키를 WindowStyles.xaml 토큰으로 통일해 VMS(ProjectReference)·
    /// VisionSetup(원본)·AppSetup(Linked Page) 세 앱에서 동일하게 동작한다.
    /// 각 앱의 DialogService 가 ShowInformation/Warning/Error/Confirmation 에서 호출.
    ///
    /// 사용:
    ///   bool ok = MessageDialog.Show(owner, "메시지", "타이틀",
    ///                                MessageDialogKind.Question, isConfirmation: true);
    /// </summary>
    public partial class MessageDialog : Window
    {
        public static bool Show(
            Window? owner,
            string message,
            string title,
            MessageDialogKind kind,
            bool isConfirmation = false)
        {
            // 기본 MessageBox 와 달리 커스텀 Window 는 UI 스레드에서만 생성 가능 —
            // 백그라운드 스레드 호출(폴링/워커의 오류 보고)을 크래시 없이 수용한다.
            var app = Application.Current;
            if (app != null && !app.Dispatcher.CheckAccess())
                return app.Dispatcher.Invoke(() => Show(owner, message, title, kind, isConfirmation));

            var dlg = new MessageDialog(message, title, kind, isConfirmation);
            var effectiveOwner = owner ?? app?.MainWindow;
            // 부팅 경로(MainWindow 생성 전)는 owner 없이 화면 중앙에 표시
            if (effectiveOwner != null && !ReferenceEquals(effectiveOwner, dlg) && effectiveOwner.IsLoaded)
                dlg.Owner = effectiveOwner;
            else
                dlg.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            return dlg.ShowDialog() == true;
        }

        private MessageDialog(string message, string title, MessageDialogKind kind, bool isConfirmation)
        {
            InitializeComponent();

            MessageText.Text = message;
            TitleText.Text = title;

            (IconText.Text, IconText.Foreground) = kind switch
            {
                MessageDialogKind.Info     => ("\uE946", FindBrush("BrushInfo", 0x5B, 0x9B, 0xE0)),
                MessageDialogKind.Warning  => ("\uE7BA", FindBrush("BrushWarning", 0xE0, 0xA3, 0x2E)),
                MessageDialogKind.Error    => ("\uEB90", FindBrush("BrushDanger", 0xE0, 0x5B, 0x5B)),
                MessageDialogKind.Question => ("\uE9CE", FindBrush("BrushAccent", 0x6A, 0x7B, 0xF0)),
                _                          => ("\uE946", FindBrush("BrushInfo", 0x5B, 0x9B, 0xE0)),
            };

            if (isConfirmation)
            {
                SecondaryButton.Visibility = Visibility.Visible;
                PrimaryButton.Content   = "예";
                SecondaryButton.Content = "아니오";
            }

            // WindowStyle=None 이라 기본 드래그 영역이 없음 — 본문 어디든 잡고 이동 가능.
            MouseLeftButtonDown += (_, e) =>
            {
                if (e.ButtonState == MouseButtonState.Pressed)
                    DragMove();
            };
        }

        /// <summary>테마 키 조회 — 호스트 앱에 키가 없어도 죽지 않도록 폴백 색 사용.</summary>
        private static Brush FindBrush(string key, byte r, byte g, byte b)
        {
            return Application.Current?.TryFindResource(key) as Brush
                ?? new SolidColorBrush(Color.FromRgb(r, g, b));
        }

        private void OnPrimaryClick(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void OnSecondaryClick(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }

    public enum MessageDialogKind
    {
        Info,
        Warning,
        Error,
        Question,
    }
}
