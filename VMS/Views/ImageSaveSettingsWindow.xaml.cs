using System.Windows;

namespace VMS.Views
{
    /// <summary>
    /// 검사 판정 이미지(양품/불량) 저장 설정 윈도우. DataContext 는
    /// ImageSaveSettingsViewModel. 저장 값은 system_config.json 의 imageSave 키에
    /// 기록되어 VMS 재시작(또는 윈도우 종료 후 재로드) 시 검사 파이프라인에 적용.
    /// </summary>
    public partial class ImageSaveSettingsWindow : Window
    {
        public ImageSaveSettingsWindow()
        {
            InitializeComponent();
        }

        // 윈도우 닫기 — 순수 UI 동작이므로 code-behind 에서 직접 처리.
        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
    }
}
