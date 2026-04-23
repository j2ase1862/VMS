using System.Collections.Specialized;
using System.Windows;
using System.Windows.Input;
using VMS.VisionSetup.ViewModels;

namespace VMS.VisionSetup.Views
{
    public partial class ChatWindow : Window
    {
        public ChatWindow()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (DataContext is ChatViewModel vm)
            {
                // Auto-scroll on new messages
                ((INotifyCollectionChanged)vm.Messages).CollectionChanged += (_, _) =>
                {
                    ChatScrollViewer.ScrollToEnd();
                };

                vm.CloseAction = () => Hide();
            }

            InputTextBox.Focus();
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                WindowState = WindowState == WindowState.Maximized
                    ? WindowState.Normal
                    : WindowState.Maximized;
            }
            else
            {
                DragMove();
            }
        }

        private void InputTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && DataContext is ChatViewModel vm)
            {
                if (vm.SendMessageCommand.CanExecute(null))
                    vm.SendMessageCommand.Execute(null);
            }
        }
    }
}
