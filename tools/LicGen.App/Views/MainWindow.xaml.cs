using System.Windows;

namespace Boda.LicGen.App.Views
{
    /// <summary>로직 없음 — 전부 MainViewModel 바인딩 (MVVM 관례).</summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();

            // chromeless 윈도우 — SystemCommands.MinimizeWindow / CloseWindow 활성화 (AppSetup 관례)
            CommandBindings.Add(new System.Windows.Input.CommandBinding(SystemCommands.MinimizeWindowCommand,
                (s, e) => SystemCommands.MinimizeWindow(this)));
            CommandBindings.Add(new System.Windows.Input.CommandBinding(SystemCommands.CloseWindowCommand,
                (s, e) => SystemCommands.CloseWindow(this)));
        }
    }
}
