using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VMS.Core.DeepLearning;
using VMS.Core.Services;

namespace VMS.VisionSetup.ViewModels
{
    /// <summary>레지스트리 모델 계열 한 줄 (목록 표시용)</summary>
    public sealed class RegistryModelRow
    {
        public RegistryModelRow(RegistryModel model) { Model = model; }
        public RegistryModel Model { get; }
        public string Name => Model.Name;
        public string Summary
        {
            get
            {
                var classes = Model.Classes.Length == 0
                    ? "클래스 없음"
                    : string.Join(", ", Model.Classes.Take(4)) + (Model.Classes.Length > 4 ? " …" : "");
                return $"{Model.TaskType} · {classes}";
            }
        }
    }

    /// <summary>모델 한 계열의 버전 한 줄</summary>
    public sealed class RegistryVersionRow
    {
        public RegistryVersionRow(RegistryModelVersion version) { Version = version; }
        public RegistryModelVersion Version { get; }
        public string Summary =>
            $"v{Version.Number} · {StageText(Version.Stage)} · " +
            $"{Version.SizeBytes / 1024 / 1024}MB · {Version.CreatedAt.ToLocalTime():yyyy-MM-dd}";

        public static string StageText(string? stage) => stage?.ToLowerInvariant() switch
        {
            "production" => "운영",
            "staging" => "테스트",
            "candidate" => "후보",
            "archived" => "보관",
            _ => stage ?? "",
        };
    }

    /// <summary>단계 선택 콤보 항목</summary>
    public sealed record RegistryStageOption(string Tag, string Label);

    /// <summary>
    /// MLOps 모델 레지스트리에서 참조(<c>model://…</c>)를 고르는 창의 ViewModel.
    /// HTTP 조회는 생성자로 받은 델리게이트가 하므로 화면 코드(code-behind)에는 로직이 없고,
    /// 테스트는 가짜 델리게이트로 목록·미리보기·확정 조건을 검증한다.
    /// </summary>
    public partial class ModelRegistryPickerViewModel : ObservableObject
    {
        public delegate Task<IReadOnlyList<RegistryModel>> ListModelsFunc(string? taskType, CancellationToken ct);
        public delegate Task<IReadOnlyList<RegistryModelVersion>> ListVersionsFunc(Guid modelId, CancellationToken ct);

        private readonly ListModelsFunc _listModels;
        private readonly ListVersionsFunc _listVersions;
        private readonly string? _taskType;

        public ModelRegistryPickerViewModel(ListModelsFunc listModels, ListVersionsFunc listVersions, string? taskType)
        {
            _listModels = listModels ?? throw new ArgumentNullException(nameof(listModels));
            _listVersions = listVersions ?? throw new ArgumentNullException(nameof(listVersions));
            _taskType = taskType;
            SelectedStage = Stages[0].Tag;
        }

        /// <summary>실제 레지스트리 클라이언트로 동작하는 인스턴스 — 호출마다 클라이언트를 만들고 닫는다.</summary>
        public static ModelRegistryPickerViewModel ForClient(Func<ModelRegistryClient> clientFactory, string? taskType)
        {
            if (clientFactory is null) throw new ArgumentNullException(nameof(clientFactory));
            return new ModelRegistryPickerViewModel(
                async (type, ct) => { using var c = clientFactory(); return await c.ListModelsAsync(type, ct).ConfigureAwait(false); },
                async (id, ct) => { using var c = clientFactory(); return await c.ListVersionsAsync(id, ct).ConfigureAwait(false); },
                taskType);
        }

        public ObservableCollection<RegistryModelRow> Models { get; } = new();
        public ObservableCollection<RegistryVersionRow> Versions { get; } = new();

        public IReadOnlyList<RegistryStageOption> Stages { get; } = new[]
        {
            new RegistryStageOption("production", "production (운영)"),
            new RegistryStageOption("staging", "staging (테스트 라인)"),
            new RegistryStageOption("candidate", "candidate (후보)"),
        };

        [ObservableProperty] private RegistryModelRow? _selectedModel;
        [ObservableProperty] private RegistryVersionRow? _selectedVersion;
        [ObservableProperty] private string _selectedStage = "production";
        [ObservableProperty] private bool _pinVersion;
        [ObservableProperty] private string _status = string.Empty;
        [ObservableProperty] private bool _isBusy;

        /// <summary>"지금 운영 중인 버전" 라디오 — PinVersion 의 반대. 두 라디오를 한 값에 묶기 위한 것.</summary>
        public bool UseStage
        {
            get => !PinVersion;
            set { if (PinVersion == value) PinVersion = !value; }
        }

        /// <summary>[선택] 을 누르면 저장될 참조 문자열. 조건이 안 맞으면 null.</summary>
        public string? SelectedReference { get; private set; }

        public string PreviewText => SelectedReference ?? "—";

        /// <summary>창을 닫아 달라는 요청 (true = 선택 확정, false = 취소)</summary>
        public event EventHandler<bool>? CloseRequested;

        /// <summary>테스트가 버전 목록 로드 완료를 기다릴 때 쓴다.</summary>
        public Task? PendingVersionsLoad { get; private set; }

        [RelayCommand]
        private async Task LoadModelsAsync(CancellationToken ct)
        {
            IsBusy = true;
            Status = "레지스트리에서 모델 목록을 받는 중…";
            try
            {
                var models = await _listModels(_taskType, ct);
                Models.Clear();
                foreach (var m in models) Models.Add(new RegistryModelRow(m));
                Status = Models.Count == 0 ? "등록된 모델이 없습니다." : $"모델 {Models.Count}개";
                if (Models.Count > 0) SelectedModel = Models[0];
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Status = ex.Message;
            }
            finally { IsBusy = false; }
        }

        partial void OnSelectedModelChanged(RegistryModelRow? value)
        {
            Versions.Clear();
            SelectedVersion = null;
            Refresh();
            if (value is null) return;
            PendingVersionsLoad = LoadVersionsAsync(value);
        }

        private async Task LoadVersionsAsync(RegistryModelRow row)
        {
            try
            {
                var versions = await _listVersions(row.Model.Id, CancellationToken.None);
                if (!ReferenceEquals(SelectedModel, row)) return; // 그 사이 다른 모델을 골랐다
                Versions.Clear();
                foreach (var v in versions.OrderByDescending(v => v.Number)) Versions.Add(new RegistryVersionRow(v));
            }
            catch (Exception ex)
            {
                Status = ex.Message;
            }
            Refresh();
        }

        partial void OnSelectedVersionChanged(RegistryVersionRow? value) => Refresh();
        partial void OnSelectedStageChanged(string value) => Refresh();
        partial void OnPinVersionChanged(bool value)
        {
            OnPropertyChanged(nameof(UseStage));
            Refresh();
        }

        private void Refresh()
        {
            SelectedReference = BuildReference();
            OnPropertyChanged(nameof(SelectedReference));
            OnPropertyChanged(nameof(PreviewText));
            ConfirmCommand.NotifyCanExecuteChanged();
        }

        private string? BuildReference()
        {
            if (SelectedModel is null) return null;
            if (PinVersion)
            {
                return SelectedVersion is null
                    ? null
                    : ModelReference.ForVersion(SelectedModel.Model.Id, SelectedVersion.Version.Number).ToString();
            }
            var stage = string.IsNullOrWhiteSpace(SelectedStage) ? "production" : SelectedStage;
            return ModelReference.ForStage(SelectedModel.Model.Id, stage).ToString();
        }

        private bool CanConfirm() => SelectedReference is not null;

        [RelayCommand(CanExecute = nameof(CanConfirm))]
        private void Confirm() => CloseRequested?.Invoke(this, true);

        [RelayCommand]
        private void Cancel() => CloseRequested?.Invoke(this, false);
    }
}
