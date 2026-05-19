using System.Collections.Specialized;
using System.Linq;
using System.Windows;
using VMS.VisionSetup.ViewModels;

namespace VMS.VisionSetup.Views.SynthData
{
    public partial class SynthDataWindow : Window
    {
        private readonly SynthDataViewModel _vm;

        public SynthDataWindow()
        {
            InitializeComponent();
            _vm = new SynthDataViewModel();
            DataContext = _vm;
            // 로그 추가 시 TextBox에 append + 자동 스크롤
            _vm.TrainLog.CollectionChanged += TrainLog_CollectionChanged;
        }

        private void TrainLog_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action == NotifyCollectionChangedAction.Reset)
            {
                LogTextBox.Clear();
                return;
            }
            if (e.NewItems != null)
            {
                foreach (string line in e.NewItems.OfType<string>())
                    LogTextBox.AppendText(line + System.Environment.NewLine);
                LogTextBox.ScrollToEnd();
            }
        }

        private void CopyOnnxPath_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(_vm.ResultOnnxPath))
                Clipboard.SetText(_vm.ResultOnnxPath);
        }

        private void CopyLog_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(LogTextBox.Text))
                Clipboard.SetText(LogTextBox.Text);
        }

        private void ClearLog_Click(object sender, RoutedEventArgs e)
        {
            _vm.TrainLog.Clear();
            LogTextBox.Clear();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
    }
}
