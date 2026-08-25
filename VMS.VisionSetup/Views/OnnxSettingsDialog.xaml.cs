using Microsoft.Win32;
using System.Windows;
using VMS.VisionSetup.Services;
using VMS.VisionSetup.VisionTools.DeepLearning;

namespace VMS.VisionSetup.Views
{
    public partial class OnnxSettingsDialog : Window
    {
        public OnnxSettingsDialog()
        {
            InitializeComponent();
            Loaded += (_, _) => LoadCurrentSettings();
        }

        private void LoadCurrentSettings()
        {
            var s = OnnxSettingsService.Load();
            ProviderComboBox.SelectedItem = s.Provider;
            TrtCacheTextBox.Text = s.TensorRTCachePath;
            Fp16CheckBox.IsChecked = s.TensorRTFp16;
        }

        private void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFolderDialog
            {
                Title = "TensorRT 엔진 캐시 폴더 선택"
            };
            if (!string.IsNullOrWhiteSpace(TrtCacheTextBox.Text))
                dlg.InitialDirectory = TrtCacheTextBox.Text;

            if (dlg.ShowDialog() == true)
                TrtCacheTextBox.Text = dlg.FolderName;
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            var provider = (OnnxExecutionProvider)(ProviderComboBox.SelectedItem
                                                   ?? OnnxExecutionProvider.Auto);
            var settings = new OnnxSettingsService.OnnxSettings(
                provider,
                TrtCacheTextBox.Text?.Trim() ?? string.Empty,
                Fp16CheckBox.IsChecked == true);

            try
            {
                OnnxSettingsService.Save(settings);
                DialogResult = true;
            }
            catch (System.Exception ex)
            {
                Common.MessageDialog.Show(this,
                    $"설정 저장 실패: {ex.Message}",
                    "Inference Engine Settings",
                    Common.MessageDialogKind.Error);
            }
        }
    }
}
