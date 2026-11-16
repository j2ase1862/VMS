using System.Windows;
using VMS.VisionSetup.Interfaces;
using VMS.VisionSetup.ViewModels;

namespace VMS.VisionSetup.Views
{
    /// <summary>
    /// 추론 엔진 설정 창 — 읽기 · 저장 · 폴더 선택은 <see cref="OnnxSettingsViewModel"/> 이 한다.
    /// 코드 비하인드는 VM 의 닫기 요청을 DialogResult 로 옮기기만 한다.
    /// </summary>
    public partial class OnnxSettingsDialog : Window
    {
        public OnnxSettingsDialog(IDialogService dialogService)
        {
            InitializeComponent();
            var vm = new OnnxSettingsViewModel(dialogService);
            vm.CloseRequested += (_, ok) => DialogResult = ok;
            DataContext = vm;
        }
    }
}
