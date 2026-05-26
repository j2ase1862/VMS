using System;
using System.Windows;
using System.Windows.Input;
using VMS.Core.Models.ParameterSync;
using VMS.Core.Services;
using VMS.VisionSetup.ViewModels;

namespace VMS.VisionSetup.Views
{
    public partial class WorkOrderListWindow : Window
    {
        private readonly WorkOrderListViewModel _vm;

        public WorkOrderDto? Result => _vm.Result;

        public WorkOrderListWindow(WorkOrderClient client)
        {
            // VMS Launcher 환경에서도 다크 스타일 로드 보장 (OperatorLoginDialog와 동일 패턴)
            EnsureWindowStylesMerged();
            InitializeComponent();
            _vm = new WorkOrderListViewModel(client);
            _vm.Finished += success =>
            {
                if (success)
                {
                    DialogResult = true;
                    Close();
                }
            };
            DataContext = _vm;
        }

        private void DataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (_vm.SelectCommand.CanExecute(null))
                _vm.SelectCommand.Execute(null);
        }

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
                System.Diagnostics.Debug.WriteLine($"[WorkOrderListWindow] MergeStyles failed: {ex.Message}");
            }
        }
    }
}
