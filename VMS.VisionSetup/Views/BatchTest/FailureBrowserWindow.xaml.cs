using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using VMS.VisionSetup.Services.BatchTesting;
using VMS.VisionSetup.ViewModels;

namespace VMS.VisionSetup.Views.BatchTest
{
    public partial class FailureBrowserWindow : Window
    {
        public FailureBrowserWindow(IEnumerable<BatchImageResult> items, string? overlayDir, int startIndex = 0)
        {
            InitializeComponent();
            DataContext = new FailureBrowserViewModel(items, overlayDir, startIndex);
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (DataContext is not FailureBrowserViewModel vm) return;

            switch (e.Key)
            {
                case Key.Left:
                    if (vm.PrevCommand.CanExecute(null)) vm.PrevCommand.Execute(null);
                    e.Handled = true;
                    break;
                case Key.Right:
                    if (vm.NextCommand.CanExecute(null)) vm.NextCommand.Execute(null);
                    e.Handled = true;
                    break;
                case Key.Escape:
                    Close();
                    e.Handled = true;
                    break;
            }
        }
    }
}
