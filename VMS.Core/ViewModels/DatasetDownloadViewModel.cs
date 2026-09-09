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
    /// 웹에서 라벨링한 데이터셋을 받아 학습에 쓴다 (개발 문서 §4).
    ///
    /// <para>
    /// 받는 것은 <b>내보내기 폴더</b>다 — 학습 스크립트가 그대로 읽는 형식으로 서버가 굽는다.
    /// 학습 도구의 데이터셋으로 되돌리지 않는다. 두 형식을 오가면 어느 쪽이 정본인지 흐려지고,
    /// 조용히 어긋난 라벨이 다음 학습에 들어간다. 웹에서 라벨링한 것은 웹이 정본이다.
    /// 그래서 이 창이 끝내 놓는 것은 <b>학습 데이터셋 경로</b> 하나다 — Export 버튼들과 같다.
    /// </para>
    /// <para><b>판.</b>
    /// 데이터셋은 계속 바뀌므로 학습은 "그때 그 판"을 받아야 재현이 된다.
    /// 이미 뜬 판을 고르거나, 지금 상태로 새 판을 뜰 수 있다.
    /// </para>
    /// </summary>
    public sealed partial class DatasetDownloadViewModel : ObservableObject
    {
        /// <summary>목록 한 줄 — 웹의 데이터셋</summary>
        public sealed record DatasetChoice(Guid Id, string Name, string Summary, string TaskType)
        {
            public override string ToString() => Name;
        }

        /// <summary>목록 한 줄 — 그 데이터셋의 한 판</summary>
        public sealed record VersionChoice(RegistryDatasetVersion Version, string Label, string Summary)
        {
            public override string ToString() => Label;
        }

        private readonly Func<string, string, DatasetRegistryClient> _clientFactory;
        private readonly Func<string, WebAuthClient> _authFactory;

        private readonly string _serverUrl;
        private readonly string _webServerUrl;
        private readonly DatasetTaskType _taskType;

        public DatasetDownloadViewModel(
            string serverUrl,
            string webServerUrl,
            DatasetTaskType taskType,
            string targetDirectory,
            Func<string, string, DatasetRegistryClient>? clientFactory = null,
            Func<string, WebAuthClient>? authFactory = null)
        {
            _serverUrl = (serverUrl ?? "").Trim();
            _webServerUrl = (webServerUrl ?? "").Trim();
            _taskType = taskType;
            _clientFactory = clientFactory ?? ((url, token) => new DatasetRegistryClient(url, token));
            _authFactory = authFactory ?? (url => new WebAuthClient(url));

            TargetDirectory = targetDirectory ?? "";
            TaskTypeText = RegistryTaskType.Describe(taskType);
        }

        // ───────────── 보여 주는 값 ─────────────

        public string TaskTypeText { get; }
        public string ServerUrl => _serverUrl;

        public ObservableCollection<DatasetChoice> Datasets { get; } = [];
        public ObservableCollection<VersionChoice> Versions { get; } = [];

        [ObservableProperty] private string _username = "";
        [ObservableProperty] private string _password = "";
        [ObservableProperty] private bool _signedIn;
        [ObservableProperty] private string _signedInAs = "";

        [ObservableProperty] private DatasetChoice? _selectedDataset;
        [ObservableProperty] private VersionChoice? _selectedVersion;
        [ObservableProperty] private string _targetDirectory = "";

        /// <summary>라벨 없는 이미지도 담는다. 검출에서 배경 샘플이 필요할 때만 켠다.</summary>
        [ObservableProperty] private bool _includeUnlabeled;

        /// <summary>검토를 마친 것만 담는다.</summary>
        [ObservableProperty] private bool _reviewedOnly;

        [ObservableProperty] private bool _busy;
        [ObservableProperty] private string _status = "";
        [ObservableProperty] private double _progressPercent;
        [ObservableProperty] private string? _error;

        /// <summary>다 받았으면 푼 폴더. 창을 닫은 쪽이 이 값을 학습 데이터셋 경로로 쓴다.</summary>
        [ObservableProperty] private string? _downloadedPath;

        private string _token = "";

        public bool IsConfigured => _serverUrl.Length > 0 && _webServerUrl.Length > 0;

        public string ConfigurationHint =>
            _serverUrl.Length == 0 && _webServerUrl.Length == 0
                ? "MLOps 서버 주소와 웹 서버 주소가 설정되지 않았습니다. 설정 마법사에서 지정하세요."
                : _serverUrl.Length == 0
                    ? "MLOps 서버 주소가 설정되지 않았습니다. 설정 마법사 2단계 고급 설정에서 지정하세요."
                    : "웹 서버 주소가 설정되지 않았습니다. 로그인할 곳을 알 수 없습니다.";

        public bool ShowSignIn => !SignedIn;
        public bool HasError => !string.IsNullOrEmpty(Error);
        public bool HasStatus => Status.Length > 0;
        public bool HasResult => DownloadedPath is not null;

        /// <summary>고른 판이 있어야 받는다. 없으면 먼저 떠야 한다.</summary>
        public bool CanDownload => SignedIn && !Busy && DownloadedPath is null
                                   && SelectedVersion is not null && TargetDirectory.Trim().Length > 0;

        /// <summary>
        /// 새 판은 데이터셋을 고른 뒤에 뜬다. 목록을 받는 중에는 막는다 —
        /// 그 사이에 판을 뜨면 늦게 도착한 목록이 <see cref="Versions"/> 를 갈아 끼우면서
        /// 방금 뜬 판을 목록에서도 선택에서도 지운다.
        /// </summary>
        public bool CanSnapshot => SignedIn && !Busy && !LoadingVersions
                                   && DownloadedPath is null && SelectedDataset is not null;

        /// <summary>버전 목록을 받는 중. 속성 설정자에서 시작하는 일이라 <see cref="Busy"/> 를 쓰지 않는다 —
        /// Busy 는 사람이 누른 동작에만 켠다.</summary>
        [ObservableProperty] private bool _loadingVersions;

        partial void OnLoadingVersionsChanged(bool value) => NotifyCommands();

        partial void OnSignedInChanged(bool value)
        {
            OnPropertyChanged(nameof(ShowSignIn));
            NotifyCommands();
        }

        partial void OnBusyChanged(bool value) => NotifyCommands();
        partial void OnSelectedVersionChanged(VersionChoice? value) => NotifyCommands();
        partial void OnTargetDirectoryChanged(string value) => NotifyCommands();
        partial void OnErrorChanged(string? value) => OnPropertyChanged(nameof(HasError));
        partial void OnStatusChanged(string value) => OnPropertyChanged(nameof(HasStatus));

        partial void OnDownloadedPathChanged(string? value)
        {
            OnPropertyChanged(nameof(HasResult));
            NotifyCommands();
        }

        private void NotifyCommands()
        {
            OnPropertyChanged(nameof(CanDownload));
            OnPropertyChanged(nameof(CanSnapshot));
            DownloadCommand.NotifyCanExecuteChanged();
            CreateSnapshotCommand.NotifyCanExecuteChanged();
        }

        // ───────────── 동작 ─────────────

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
                Password = "";
                SignedIn = true;

                await LoadDatasetsAsync(ct).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                Error = ex.Message;
                Status = "";
            }
            finally { Busy = false; }
        }

        private async Task LoadDatasetsAsync(CancellationToken ct)
        {
            Status = "데이터셋 목록을 받는 중…";
            Datasets.Clear();
            Versions.Clear();
            try
            {
                using var client = _clientFactory(_serverUrl, _token);
                foreach (var d in await client.ListDatasetsAsync(RegistryTaskType.From(_taskType), ct).ConfigureAwait(true))
                    Datasets.Add(new DatasetChoice(d.Id, d.Name, DescribeDataset(d), d.TaskType));

                SelectedDataset = Datasets.FirstOrDefault();
                Status = Datasets.Count == 0
                    ? $"웹에 {TaskTypeText} 데이터셋이 없습니다."
                    : $"{SignedInAs} 로 연결됨 · {TaskTypeText} 데이터셋 {Datasets.Count}개";
            }
            catch (ModelRegistryException ex)
            {
                Error = ex.Message;
                Status = "";
            }
        }

        /// <summary>
        /// 데이터셋을 바꾸면 고른 판을 버린다 — 그대로 두면 다른 데이터셋의 판을 받게 된다.
        /// 목록 받기는 기다리지 않고 띄운다(속성 설정자라 await 할 곳이 없다). 그래서
        /// <see cref="LoadVersionsAsync"/> 는 예외를 스스로 다 받아 <see cref="Error"/> 로 옮긴다 —
        /// 여기서 새는 예외는 아무 데도 나타나지 않는다.
        /// </summary>
        partial void OnSelectedDatasetChanged(DatasetChoice? value)
        {
            Versions.Clear();
            SelectedVersion = null;
            NotifyCommands();
            if (value is null) return;
            _ = LoadVersionsAsync(value.Id, CancellationToken.None);
        }

        private async Task LoadVersionsAsync(Guid datasetId, CancellationToken ct)
        {
            // 토큰이 없으면 물어볼 것이 없다. 로그인 전에 목록을 부르면 401 만 돌아온다.
            if (!SignedIn || _token.Length == 0) return;

            LoadingVersions = true;
            try
            {
                using var client = _clientFactory(_serverUrl, _token);
                var versions = await client.ListVersionsAsync(datasetId, ct).ConfigureAwait(true);

                // 기다리는 사이에 사람이 다른 데이터셋을 골랐으면 그 결과는 버린다
                if (SelectedDataset?.Id != datasetId) return;

                Versions.Clear();
                foreach (var v in versions) Versions.Add(ToChoice(v));

                SelectedVersion = Versions.FirstOrDefault();
                if (Versions.Count == 0)
                    Status = "아직 뜬 판이 없습니다. [새 판 뜨기] 로 지금 상태를 굳히세요.";
            }
            catch (Exception ex)
            {
                Error = ex.Message;
            }
            finally
            {
                // 사람이 그새 다른 데이터셋을 골랐으면 그쪽 조회가 아직 돌고 있다 — 그때는 끄지 않는다.
                if (SelectedDataset?.Id == datasetId) LoadingVersions = false;
            }
        }

        /// <summary>
        /// 지금 상태로 새 판을 뜬다. 학습은 "그때 그 판" 을 받아야 재현이 되므로,
        /// 받기 직전에 굳히는 이 단계가 필요하다.
        /// </summary>
        [RelayCommand(CanExecute = nameof(CanSnapshot))]
        private async Task CreateSnapshotAsync(CancellationToken ct)
        {
            var dataset = SelectedDataset;
            if (dataset is null) return;

            Busy = true;
            Error = null;
            Status = "새 판을 뜨는 중…";
            try
            {
                using var client = _clientFactory(_serverUrl, _token);
                var created = await client.CreateSnapshotAsync(
                    dataset.Id, name: null, IncludeUnlabeled, ReviewedOnly, ct).ConfigureAwait(true);

                var choice = ToChoice(created);
                Versions.Insert(0, choice);
                SelectedVersion = choice;
                Status = $"새 판을 떴습니다 — {choice.Summary}";
            }
            catch (Exception ex)
            {
                Error = ex.Message;
                Status = "";
            }
            finally { Busy = false; }
        }

        [RelayCommand(CanExecute = nameof(CanDownload))]
        private async Task DownloadAsync(CancellationToken ct)
        {
            var version = SelectedVersion;
            if (version is null) return;

            Busy = true;
            Error = null;
            ProgressPercent = 0;
            try
            {
                using var client = _clientFactory(_serverUrl, _token);
                var progress = new Progress<DatasetDownloadProgress>(p =>
                {
                    ProgressPercent = p.Percent;
                    Status = p.Total > 0
                        ? $"{p.Message} {Describe(p.Received)} / {Describe(p.Total)}"
                        : p.Message;
                });

                var folder = Path.Combine(TargetDirectory.Trim(), FolderNameFor(version.Version));
                var path = await client.DownloadExportAsync(version.Version, folder, progress, ct).ConfigureAwait(true);

                DownloadedPath = path;
                ProgressPercent = 100;
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

        /// <summary>
        /// 폴더 이름에 해시 앞자리를 붙인다. 같은 데이터셋의 다른 판을 나란히 두고 비교할 수 있고,
        /// 어느 학습이 어느 판으로 돌았는지 폴더 이름만 봐도 안다.
        /// </summary>
        private static string FolderNameFor(RegistryDatasetVersion v)
        {
            var name = new string(v.Name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray());
            if (name.Trim().Length == 0) name = "dataset";
            var hash = v.ManifestHash.Length >= 8 ? v.ManifestHash.Substring(0, 8) : v.Id.ToString("N")[..8];
            return $"{name.Trim()}-{hash}";
        }

        private static VersionChoice ToChoice(RegistryDatasetVersion v)
        {
            var hash = v.ManifestHash.Length >= 8 ? v.ManifestHash.Substring(0, 8) : "";
            var label = $"{v.Name} ({v.CreatedAt:yyyy-MM-dd HH:mm})";
            var summary = $"{v.ExportFormat} · 이미지 {v.ImageCount}장 · 라벨 {v.AnnotationCount}개 · " +
                          $"{Describe(v.SizeBytes)}" + (hash.Length > 0 ? $" · {hash}" : "");
            return new VersionChoice(v, label, summary);
        }

        private static string DescribeDataset(RegistryDataset d)
        {
            var classes = d.Classes.Length == 0 ? "클래스 없음" : string.Join(", ", d.Classes);
            if (d.Stats is null) return classes;
            return $"{classes} · 이미지 {d.Stats.ImageCount}장 " +
                   $"(라벨 {d.Stats.Labeled} · 검토 {d.Stats.Reviewed} · 미라벨 {d.Stats.Unlabeled})";
        }

        private static string DescribeLoginFailure(WebAuthResult result) => result.Kind switch
        {
            WebAuthResultKind.InvalidCredentials => "아이디나 비밀번호가 맞지 않습니다.",
            WebAuthResultKind.WebUnreachable => "웹 서버에 닿지 못했습니다. 주소와 네트워크를 확인하세요.",
            _ => "로그인하지 못했습니다. (" + (result.ErrorDetail ?? "이유 없음") + ")",
        };

        private static string Describe(long bytes) =>
            bytes >= 1024L * 1024 * 1024 ? $"{bytes / 1024.0 / 1024 / 1024:0.##} GB"
            : bytes >= 1024L * 1024 ? $"{bytes / 1024.0 / 1024:0.#} MB"
            : $"{bytes / 1024.0:0.#} KB";
    }
}
