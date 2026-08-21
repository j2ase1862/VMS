using System.Windows;
using System.Windows.Input;

namespace VMS.Updater
{
    /// <summary>업데이트 진행률 창 — 로직은 UpdaterViewModel, 여기는 창 드래그 배선만.</summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private void OnDragWindow(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
                DragMove();
        }
    }
}
