using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VMS.Core.Models.Annotation;
using VMS.Core.Services;

namespace VMS.Core.ViewModels
{
    /// <summary>
    /// 방금 학습한 ONNX 를 MLOps 레지스트리에 올린다 (개발 문서 §5.5).
    ///
    /// <para>
    /// 지금까지는 학습이 끝나면 사람이 파일을 찾아 라인 PC 로 복사했다. 그래서 어느 PC 가 어느
    /// 모델을 쓰는지 아무도 몰랐다. 레지스트리에 올려 두면 버전이 남고, 승격·롤백이 기록되고,
    /// 라인은 <c>model://</c> 참조로 알아서 받아 간다.
    /// </para>
    /// <para><b>인증.</b>
    /// 사람의 계정으로 올린다. BODA.VMS.Web 에 로그인해 받은 JWT 를 그대로 MLOps 가 받는다
    /// (같은 키·발급자를 쓴다). 라인 토큰은 쓰지 않는다 — 그것은 받아 가는 쪽 자격이라
    /// 등록 권한이 없고, 있어서도 안 된다.
    /// </para>
    /// <para><b>올린 뒤.</b>
    /// 새 버전은 Candidate 로 들어간다. 바로 라인에 나가지 않는다.
    /// 사람이 레지스트리에서 확인하고 승격해야 라인이 가져간다.
    /// </para>
    /// </summary>
    public sealed partial class ModelUploadViewModel : ObservableObject
    {
        /// <summary>
        /// 목록 한 줄 — 이미 있는 모델 계열.
        ///
        /// <para>
        /// <c>ToString</c> 을 이름으로 덮는다. 레코드의 기본 <c>ToString</c> 은
        /// <c>ModelChoice { Id = ..., Name = ... }</c> 를 내놓는데, 목록 템플릿이 없는 자리에서는
        /// 그게 그대로 화면에 나온다.
        /// </para>
        /// </summary>
        public sealed record ModelChoice(Guid Id, string Name, string Summary)
        {
            public override string ToString() => Name;
        }

        private readonly Func<string, string, ModelRegistryClient> _clientFactory;
        private readonly Func<string, WebAuthClient> _authFactory;

        private readonly string _registryUrl;
        private readonly string _webServerUrl;
        private readonly string _onnxPath;
        private readonly DatasetTaskType _taskType;
        private readonly IReadOnlyList<string> _classes;

        public ModelUploadViewModel(
            string registryUrl,
            string webServerUrl,
            string onnxPath,
            DatasetTaskType taskType,
            IReadOnlyList<string> classes,
            string suggestedName,
            Func<string, string, ModelRegistryClient>? clientFactory = null,
            Func<string, WebAuthClient>? authFactory = null)
        {
            _registryUrl = (registryUrl ?? "").Trim();
            _webServerUrl = (webServerUrl ?? "").Trim();
            _onnxPath = onnxPath ?? "";
            _taskType = taskType;
            _classes = classes ?? Array.Empty<string>();
            _clientFactory = clientFactory ?? ((url, token) => new ModelRegistryClient(url, token));
            _authFactory = authFactory ?? (url => new WebAuthClient(url));

            NewModelName = suggestedName ?? "";
            FileName = Path.GetFileName(_onnxPath);
            FileSizeText = DescribeSize(_onnxPath);
            TaskTypeText = RegistryTaskType.Describe(taskType);
            ClassesText = _classes.Count == 0 ? "클래스 없음" : string.Join(", ", _classes);
        }

        // ───────────── 보여 주는 값 ─────────────

        public string FileName { get; }
        public string FileSizeText { get; }
        public string TaskTypeText { get; }
        public string ClassesText { get; }
        public string RegistryUrl => _registryUrl;

        public ObservableCollection<ModelChoice> Models { get; } = [];

        [ObservableProperty] private string _username = "";
        [ObservableProperty] private string _password = "";
        [ObservableProperty] private bool _signedIn;
        [ObservableProperty] private string _signedInAs = "";

        [ObservableProperty] private ModelChoice? _selectedModel;
        [ObservableProperty] private bool _createNew = true;
        [ObservableProperty] private string _newModelName = "";
        [ObservableProperty] private string _notes = "";
        [ObservableProperty] private string _license = "";

        [ObservableProperty] private bool _busy;
        [ObservableProperty] private string _status = "";
        [ObservableProperty] private string? _error;
        [ObservableProperty] private string? _result;

        private string _token = "";

        /// <summary>설정이 없으면 아무것도 못 한다. 창을 여는 쪽이 먼저 이 값을 본다.</summary>
        public bool IsConfigured => _registryUrl.Length > 0 && _webServerUrl.Length > 0;

        public string ConfigurationHint =>
            _registryUrl.Length == 0 && _webServerUrl.Length == 0
                ? "MLOps 서버 주소와 웹 서버 주소가 설정되지 않았습니다. 설정 마법사에서 지정하세요."
                : _registryUrl.Length == 0
                    ? "MLOps 서버 주소가 설정되지 않았습니다. 설정 마법사 2단계 고급 설정에서 지정하세요."
                    : "웹 서버 주소가 설정되지 않았습니다. 로그인할 곳을 알 수 없습니다.";

        /// <summary>[선택] 이 살아 있는가</summary>
        public bool CanUpload =>
            SignedIn && !Busy && Result is null
            && (CreateNew ? NewModelName.Trim().Length > 0 : SelectedModel is not null);

        // 화면이 쓰는 갈래들. XAML 에 변환기를 늘어놓는 것보다 여기서 이름을 붙이는 편이 읽기 쉽다.
        public bool ShowSignIn => !SignedIn;

        /// <summary>
        /// 라디오 버튼 짝. <see cref="CreateNew"/> 의 반대인데, 켜질 때만 값을 넘긴다 —
        /// 꺼지는 쪽까지 반영하면 두 버튼이 서로를 되돌려 놓는다.
        /// </summary>
        public bool UseExisting
        {
            get => !CreateNew;
            set { if (value) CreateNew = false; }
        }

        public bool HasError => !string.IsNullOrEmpty(Error);
        public bool HasStatus => Status.Length > 0;
        public bool HasResult => Result is not null;

        partial void OnSignedInChanged(bool value)
        {
            OnPropertyChanged(nameof(ShowSignIn));
            NotifyUploadReady();
        }

        partial void OnErrorChanged(string? value) => OnPropertyChanged(nameof(HasError));
        partial void OnStatusChanged(string value) => OnPropertyChanged(nameof(HasStatus));
        partial void OnBusyChanged(bool value) => NotifyUploadReady();
        partial void OnCreateNewChanged(bool value)
        {
            OnPropertyChanged(nameof(UseExisting));
            NotifyUploadReady();
        }
        partial void OnNewModelNameChanged(string value) => NotifyUploadReady();
        partial void OnSelectedModelChanged(ModelChoice? value) => NotifyUploadReady();
        partial void OnResultChanged(string? value)
        {
            OnPropertyChanged(nameof(HasResult));
            NotifyUploadReady();
        }

        private void NotifyUploadReady()
        {
            OnPropertyChanged(nameof(CanUpload));
            UploadCommand.NotifyCanExecuteChanged();
        }

        // ───────────── 동작 ─────────────

        /// <summary>
        /// 웹 서버에 로그인해 JWT 를 받는다. 그 토큰으로 레지스트리 목록까지 받아 온다 —
        /// 목록이 보이면 권한과 연결이 함께 확인된 것이다.
        /// </summary>
        [RelayCommand]
        private async Task SignInAsync(CancellationToken ct)
        {
            if (Username.Trim().Length == 0 || Password.Length == 0)
            {
                Error = "아이디와 비밀번호를 넣으세요.";
                return;
            }

            Busy = true;
            Error = null;
            Status = "로그인 중…";
            try
            {
                using var auth = _authFactory(_webServerUrl);
                var result = await auth.LoginAsync(Username.Trim(), Password, ct).ConfigureAwait(true);
                if (!result.IsSuccess || string.IsNullOrWhiteSpace(result.Token))
                {
                    Error = DescribeLoginFailure(result);
                    Status = "";
                    return;
                }

                _token = result.Token!;
                SignedInAs = string.IsNullOrWhiteSpace(result.DisplayName) ? Username.Trim() : result.DisplayName!;
                Password = "";   // 더 들고 있을 이유가 없다
                SignedIn = true;

                await LoadModelsAsync(ct).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                Error = ex.Message;
                Status = "";
            }
            finally { Busy = false; }
        }

        private async Task LoadModelsAsync(CancellationToken ct)
        {
            Status = "모델 목록을 받는 중…";
            Models.Clear();
            try
            {
                using var client = _clientFactory(_registryUrl, _token);
                var taskType = RegistryTaskType.From(_taskType);
                foreach (var model in await client.ListModelsAsync(taskType, ct).ConfigureAwait(true))
                {
                    var summary = model.Classes.Length == 0
                        ? "클래스 없음"
                        : string.Join(", ", model.Classes);
                    Models.Add(new ModelChoice(model.Id, model.Name, summary));
                }

                // 클래스가 그대로 맞는 계열이 있으면 그것을 먼저 고른다 — 보통 그게 맞다
                SelectedModel = Models.FirstOrDefault(m => m.Summary == ClassesText) ?? Models.FirstOrDefault();
                CreateNew = SelectedModel is null;
                Status = Models.Count == 0
                    ? $"{TaskTypeText} 모델이 아직 없습니다. 새로 만들어 올립니다."
                    : $"{SignedInAs} 로 연결됨 · {TaskTypeText} 모델 {Models.Count}개";
            }
            catch (ModelRegistryException ex)
            {
                Error = ex.Message;
                Status = "";
            }
        }

        [RelayCommand(CanExecute = nameof(CanUpload))]
        private async Task UploadAsync(CancellationToken ct)
        {
            Busy = true;
            Error = null;
            try
            {
                using var client = _clientFactory(_registryUrl, _token);

                Guid modelId;
                if (CreateNew)
                {
                    Status = "모델 계열을 만드는 중…";
                    var created = await client.CreateModelAsync(
                        NewModelName.Trim(), RegistryTaskType.From(_taskType), _classes,
                        description: $"AI 학습 도구에서 올림 ({SignedInAs})", ct).ConfigureAwait(true);
                    modelId = created.Id;
                }
                else
                {
                    modelId = SelectedModel!.Id;
                }

                Status = $"{FileSizeText} 올리는 중…";
                var version = await client.UploadVersionAsync(modelId, _onnxPath, _classes,
                    license: string.IsNullOrWhiteSpace(License) ? null : License.Trim(),
                    notes: string.IsNullOrWhiteSpace(Notes) ? null : Notes.Trim(),
                    ct).ConfigureAwait(true);

                var warnings = version.Warnings.Length > 0
                    ? "\n주의: " + string.Join(" / ", version.Warnings)
                    : "";
                Result =
                    $"v{version.Number} 로 등록했습니다 ({version.Format}, {DescribeBytes(version.SizeBytes)}).\n" +
                    "아직 Candidate 라 라인에는 나가지 않습니다. 레지스트리에서 확인하고 승격하세요." + warnings;
                Status = "";
            }
            catch (Exception ex)
            {
                Error = ex.Message;
                Status = "";
            }
            finally { Busy = false; }
        }

        // ───────────── 문구 ─────────────

        private static string DescribeLoginFailure(WebAuthResult result) => result.Kind switch
        {
            WebAuthResultKind.InvalidCredentials => "아이디나 비밀번호가 맞지 않습니다.",
            WebAuthResultKind.WebUnreachable => "웹 서버에 닿지 못했습니다. 주소와 네트워크를 확인하세요.",
            _ => "로그인하지 못했습니다. (" + (result.ErrorDetail ?? "이유 없음") + ")",
        };

        private static string DescribeSize(string path)
        {
            try
            {
                return File.Exists(path) ? DescribeBytes(new FileInfo(path).Length) : "파일 없음";
            }
            catch (IOException) { return "크기를 알 수 없음"; }
        }

        private static string DescribeBytes(long bytes) =>
            bytes >= 1024L * 1024 ? $"{bytes / 1024.0 / 1024:0.#} MB" : $"{bytes / 1024.0:0.#} KB";
    }
}
