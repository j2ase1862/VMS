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
    /// <summary>실행 백엔드 콤보 항목 — 이 PC 에서 쓸 수 없으면 표시 이름에 적는다.</summary>
    public sealed record OnnxProviderOption(OnnxExecutionProvider Provider, bool IsAvailable)
    {
        public string Display => IsAvailable ? Provider.ToString() : $"{Provider} (이 PC 에서 사용 불가)";
    }

    public partial class OnnxSettingsViewModel : ObservableObject
    {
        private readonly IDialogService _dialogService;

        public OnnxSettingsViewModel(IDialogService dialogService)
            : this(dialogService, OnnxSettingsService.Load(), OnnxModelBase.IsProviderAvailable,
                   OnnxModelBase.LastActiveProvider)
        {
        }

        internal OnnxSettingsViewModel(IDialogService dialogService, OnnxSettingsService.OnnxSettings current,
            Func<OnnxExecutionProvider, bool>? isAvailable = null, string? lastActiveProvider = null)
        {
            _dialogService = dialogService;
            _provider = current.Provider;
            _tensorRTCachePath = current.TensorRTCachePath;
            _tensorRTFp16 = current.TensorRTFp16;

            // 창을 열 때 이 PC 에서 붙일 수 있는 백엔드를 확인한다 (VS-14, 사용자 결정 11-16)
            var check = isAvailable ?? (_ => true);
            ProviderOptions = Enum.GetValues(typeof(OnnxExecutionProvider)).Cast<OnnxExecutionProvider>()
                .Select(p => new OnnxProviderOption(p, check(p)))
                .ToList();
            ActiveProviderText = string.IsNullOrEmpty(lastActiveProvider)
                ? "실제 사용 백엔드: 아직 모델을 불러오지 않았습니다 (도구를 실행하면 표시)"
                : $"실제 사용 백엔드(마지막 모델 로드): {DisplayName(lastActiveProvider)}";
        }

        /// <summary>실행 백엔드 콤보 항목 — 사용 불가 표시 포함.</summary>
        public IReadOnlyList<OnnxProviderOption> ProviderOptions { get; }

        /// <summary>마지막으로 모델을 불러올 때 실제로 붙은 백엔드 한 줄.</summary>
        public string ActiveProviderText { get; }

        /// <summary>고른 백엔드를 이 PC 에서 쓸 수 없을 때 안내 (쓸 수 있으면 빈 문자열).</summary>
        public string ProviderWarning =>
            ProviderOptions.FirstOrDefault(o => o.Provider == Provider) is { IsAvailable: false }
                ? "고른 백엔드를 이 PC 에서 쓸 수 없어 다른 백엔드(최종적으로 CPU)로 실행됩니다."
                : string.Empty;

        private static string DisplayName(string ep) => ep switch
        {
            "DML" => "DirectML",
            "CUDA" => "Cuda",
            "CPU" => "Cpu",
            _ => ep
        };

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ProviderWarning))]
        private OnnxExecutionProvider _provider;
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
            // 빈 칸은 기본 캐시 폴더 — 다시 시작하면 어차피 기본 폴더로 읽히므로 이번 세션부터 같게 (VS-37)
            var cachePath = TensorRTCachePath?.Trim();
            if (string.IsNullOrEmpty(cachePath))
            {
                cachePath = OnnxSettingsService.DefaultTensorRTCachePath;
                TensorRTCachePath = cachePath;
            }

            var settings = new OnnxSettingsService.OnnxSettings(Provider, cachePath, TensorRTFp16);

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
