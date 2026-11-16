using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VMS.VisionSetup.Interfaces;
using VMS.VisionSetup.Services;
using VMS.VisionSetup.VisionTools.DeepLearning;

namespace VMS.VisionSetup.ViewModels
{
    /// <summary>
    /// 추론 엔진 설정(Inference Settings) 창 ViewModel — 실행 백엔드 · TensorRT 캐시 폴더 · FP16.
    /// 읽기 · 저장은 <see cref="OnnxSettingsService"/>, 폴더 선택 · 오류 안내는 <see cref="IDialogService"/>.
    /// </summary>
    public partial class OnnxSettingsViewModel : ObservableObject
    {
        private readonly IDialogService _dialogService;

        public OnnxSettingsViewModel(IDialogService dialogService)
            : this(dialogService, OnnxSettingsService.Load())
        {
        }

        internal OnnxSettingsViewModel(IDialogService dialogService, OnnxSettingsService.OnnxSettings current)
        {
            _dialogService = dialogService;
            _provider = current.Provider;
            _tensorRTCachePath = current.TensorRTCachePath;
            _tensorRTFp16 = current.TensorRTFp16;
        }

        public IReadOnlyList<OnnxExecutionProvider> Providers { get; } =
            Enum.GetValues(typeof(OnnxExecutionProvider)).Cast<OnnxExecutionProvider>().ToList();

        [ObservableProperty] private OnnxExecutionProvider _provider;
        [ObservableProperty] private string _tensorRTCachePath;
        [ObservableProperty] private bool _tensorRTFp16;

        /// <summary>저장에 성공하면 true 로 창을 닫으라는 요청.</summary>
        public event EventHandler<bool>? CloseRequested;

        /// <summary>테스트 시임 — 실제 저장(파일 쓰기) 대신 쓸 함수.</summary>
        internal Action<OnnxSettingsService.OnnxSettings> SaveAction { get; set; } = OnnxSettingsService.Save;

        [RelayCommand]
        private void Browse()
        {
            var folder = _dialogService.ShowFolderBrowserDialog("TensorRT 엔진 캐시 폴더 선택", TensorRTCachePath);
            if (folder != null)
                TensorRTCachePath = folder;
        }

        [RelayCommand]
        private void Save()
        {
            var settings = new OnnxSettingsService.OnnxSettings(
                Provider,
                TensorRTCachePath?.Trim() ?? string.Empty,
                TensorRTFp16);

            try
            {
                SaveAction(settings);
                CloseRequested?.Invoke(this, true);
            }
            catch (Exception ex)
            {
                _dialogService.ShowError($"설정 저장 실패: {ex.Message}", "Inference Engine Settings");
            }
        }
    }
}
