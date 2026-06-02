using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace VMS.Views
{
    /// <summary>
    /// 자체 다크 메시지 다이얼로그 — WPF 기본 MessageBox.Show 의 흰 배경을 대체.
    /// 인디고/슬레이트 디자인 토큰을 따라 일관된 톤 제공. DialogService 의
    /// ShowInformation/Warning/Error/Confirmation 이 이 클래스를 호출.
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
            var dlg = new MessageDialog(message, title, kind, isConfirmation);
            if (owner != null && !ReferenceEquals(owner, dlg))
                dlg.Owner = owner;
            return dlg.ShowDialog() == true;
        }

        private MessageDialog(string message, string title, MessageDialogKind kind, bool isConfirmation)
        {
            InitializeComponent();

            MessageText.Text = message;
            TitleText.Text = title;

            (IconText.Text, IconText.Foreground) = kind switch
            {
                MessageDialogKind.Info     => ("\uE946", FindBrush("Brush.Primary")),
                MessageDialogKind.Warning  => ("\uE7BA", FindBrush("Brush.Warning")),
                MessageDialogKind.Error    => ("\uEB90", FindBrush("Brush.Danger")),
                MessageDialogKind.Question => ("\uE9CE", FindBrush("Brush.Accent")),
                _                          => ("\uE946", FindBrush("Brush.Primary")),
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

        private static Brush FindBrush(string key)
        {
            return (Brush)Application.Current.FindResource(key);
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
