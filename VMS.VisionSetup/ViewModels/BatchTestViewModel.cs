using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Data;
using VMS.VisionSetup.Interfaces;
using VMS.VisionSetup.Models;
using VMS.VisionSetup.Services.BatchTesting;

namespace VMS.VisionSetup.ViewModels
{
    public enum ResultFilter { All, PassOnly, FailOnly }

    /// <summary>Camera ComboBox 표시용 래퍼 — Step.CameraId 를 사용자 친화적 이름으로 보여주기 위함.</summary>
    public class CameraOption
    {
        public string Id { get; }
        public string DisplayName { get; }
        public CameraOption(string id, string displayName) { Id = id; DisplayName = displayName; }
        public override string ToString() => DisplayName;
        public override bool Equals(object? obj) => obj is CameraOption other && Id == other.Id;
        public override int GetHashCode() => Id?.GetHashCode() ?? 0;
    }

    /// <summary>
    /// 무인 배치 테스트 러너 ViewModel. 폴더 이미지 → 선택한 Recipe·Step 도구 파이프라인 적용 → CSV 리포트.
    /// </summary>
    public partial class BatchTestViewModel : ObservableObject
    {
        private readonly IVisionService _visionService;
        private readonly IRecipeService _recipeService;
        private readonly ICameraService _cameraService;
        private CancellationTokenSource? _cts;

        public BatchTestViewModel(IVisionService visionService, IRecipeService recipeService, ICameraService cameraService)
        {
            _visionService = visionService;
            _recipeService = recipeService;
            _cameraService = cameraService;

            BrowseImageFolderCommand = new RelayCommand(BrowseImageFolder);
            BrowseOutputCsvCommand = new RelayCommand(BrowseOutputCsv);
            BrowseFailureDirCommand = new RelayCommand(BrowseFailureDir);
            RunCommand = new AsyncRelayCommand(RunAsync, () => !IsRunning && _visionService.Tools.Count > 0 && SelectedStep != null);
            CancelCommand = new RelayCommand(Cancel, () => IsRunning);

            OpenOutputCsvCommand = new RelayCommand(OpenOutputCsv, () => File.Exists(OutputCsvPath));
            OpenFailureFolderCommand = new RelayCommand(OpenFailureFolder, () => !string.IsNullOrEmpty(FailureOverlayDir) && Directory.Exists(FailureOverlayDir));
            BrowseFailuresCommand = new RelayCommand(BrowseFailures, () => Results.Any(r => !r.Success));
            BrowseAllResultsCommand = new RelayCommand(BrowseAllResults, () => Results.Count > 0);
            EditThresholdsCommand = new RelayCommand(EditThresholds, () => _loadedRecipe != null);
            AutoTuneCommand = new RelayCommand(OpenAutoTune, () => _visionService.Tools.Count > 0);
            SaveAsGoldenCommand = new RelayCommand(SaveAsGolden, () => Results.Count > 0);
            LoadGoldenCommand = new RelayCommand(LoadGolden);
            ClearGoldenCommand = new RelayCommand(ClearGolden, () => ActiveGoldenSet != null);

            // 기본 출력 경로
            OutputCsvPath = Path.Combine(Path.GetTempPath(), $"batch_report_{DateTime.Now:yyyyMMdd_HHmmss}.csv");

            // 필터링 뷰
            ResultsView = CollectionViewSource.GetDefaultView(Results);
            ResultsView.Filter = FilterPredicate;

            // VisionService.Tools 변경 감지 — Step 선택으로 메인 워크스페이스 도구가 채워지면 Run 활성화
            _visionService.Tools.CollectionChanged += OnVisionToolsChanged;
        }

        private void OnVisionToolsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            RunCommand.NotifyCanExecuteChanged();
            AutoTuneCommand.NotifyCanExecuteChanged();
        }

        /// <summary>
        /// 윈도우 Loaded 이벤트에서 호출 — 동기 디스크 IO(GetRecipeList)를 생성자 밖으로 이전해
        /// ShowDialog 시점의 UI 스레드 멈춤을 제거.
        /// </summary>
        public async Task InitializeAsync()
        {
            // 백그라운드에서 Recipe 목록 스캔
            var infos = await Task.Run(() =>
            {
                try { return _recipeService.GetRecipeList(); }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[BatchTest] GetRecipeList failed: {ex.Message}");
                    return new List<RecipeInfo>();
                }
            });

            // UI 스레드에서 채움
            RecipeList.Clear();
            foreach (var info in infos) RecipeList.Add(info);

            SyncFromCurrentRecipe();
        }

        [ObservableProperty] private string _imageFolder = string.Empty;
        [ObservableProperty] private bool _recurseSubfolders;
        [ObservableProperty] private string _outputCsvPath = string.Empty;
        [ObservableProperty] private bool _saveFailureOverlays = true;
        [ObservableProperty] private string _failureOverlayDir = string.Empty;

        [ObservableProperty] private bool _isRunning;
        [ObservableProperty] private int _progressCurrent;
        [ObservableProperty] private int _progressTotal;
        [ObservableProperty] private int _passCount;
        [ObservableProperty] private int _failCount;
        [ObservableProperty] private double _avgMs;
        [ObservableProperty] private string _statusMessage = "준비됨";

        [ObservableProperty] private ResultFilter _currentFilter = ResultFilter.All;
        [ObservableProperty] private BatchImageResult? _selectedResult;

        // ─── Active Pipeline (Recipe → Camera → Step) ───
        public ObservableCollection<RecipeInfo> RecipeList { get; } = new();
        public ObservableCollection<CameraOption> Cameras { get; } = new();
        public ObservableCollection<InspectionStep> StepsForCamera { get; } = new();

        [ObservableProperty] private RecipeInfo? _selectedRecipeInfo;
        [ObservableProperty] private CameraOption? _selectedCamera;
        [ObservableProperty] private InspectionStep? _selectedStep;
        [ObservableProperty] private int _activeToolCount;

        private Recipe? _loadedRecipe;  // 셀렉터로 선택한 Recipe (전체 객체)

        public ObservableCollection<BatchImageResult> Results { get; } = new();
        public ICollectionView ResultsView { get; }
        public ObservableCollection<string> LogLines { get; } = new();

        public bool IsAllFilter => CurrentFilter == ResultFilter.All;
        public bool IsPassFilter => CurrentFilter == ResultFilter.PassOnly;
        public bool IsFailFilter => CurrentFilter == ResultFilter.FailOnly;

        partial void OnIsRunningChanged(bool value)
        {
            RunCommand.NotifyCanExecuteChanged();
            CancelCommand.NotifyCanExecuteChanged();
        }

        partial void OnOutputCsvPathChanged(string value) => OpenOutputCsvCommand.NotifyCanExecuteChanged();
        partial void OnFailureOverlayDirChanged(string value) => OpenFailureFolderCommand.NotifyCanExecuteChanged();

        // ─── Cascading selectors ───
        private const string NoCameraId = "__no_camera__";

        partial void OnSelectedRecipeInfoChanged(RecipeInfo? value)
        {
            Cameras.Clear();
            StepsForCamera.Clear();
            SelectedCamera = null;
            SelectedStep = null;
            _loadedRecipe = null;

            if (value == null) return;

            _loadedRecipe = _recipeService.LoadRecipe(value.FilePath);
            if (_loadedRecipe == null) return;

            // 이 Recipe의 Steps에서 distinct CameraId → CameraOption 으로 매핑
            var cameraIds = _loadedRecipe.Steps
                .Select(s => string.IsNullOrEmpty(s.CameraId) ? NoCameraId : s.CameraId)
                .Distinct()
                .ToList();

            foreach (var id in cameraIds)
            {
                Cameras.Add(BuildCameraOption(id));
            }

            if (Cameras.Count == 1) SelectedCamera = Cameras[0];

            EditThresholdsCommand.NotifyCanExecuteChanged();
        }

        partial void OnSelectedCameraChanged(CameraOption? value)
        {
            StepsForCamera.Clear();
            SelectedStep = null;
            if (_loadedRecipe == null || value == null) return;

            var targetId = value.Id;
            var steps = _loadedRecipe.Steps
                .Where(s => (string.IsNullOrEmpty(s.CameraId) ? NoCameraId : s.CameraId) == targetId)
                .OrderBy(s => s.Sequence);
            foreach (var s in steps) StepsForCamera.Add(s);

            if (StepsForCamera.Count == 1) SelectedStep = StepsForCamera[0];
        }

        private CameraOption BuildCameraOption(string id)
        {
            if (id == NoCameraId) return new CameraOption(NoCameraId, "(카메라 미지정)");

            try
            {
                var info = _cameraService.GetCamera(id);
                if (info != null)
                {
                    var name = string.IsNullOrWhiteSpace(info.Name) ? "(unnamed)" : info.Name;
                    var maker = string.IsNullOrWhiteSpace(info.Manufacturer) ? "" : $" · {info.Manufacturer}";
                    return new CameraOption(id, $"{name}{maker}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[BatchTest] GetCamera('{id}') failed: {ex.Message}");
            }

            // 카메라 레지스트리에 없으면 ID 끝 8자리만 표시 (GUID 풀 폭 회피)
            var shortId = id.Length > 8 ? id[^8..] : id;
            return new CameraOption(id, $"Unknown · {shortId}");
        }

        partial void OnSelectedStepChanged(InspectionStep? value)
        {
            if (value == null || _loadedRecipe == null)
            {
                RunCommand.NotifyCanExecuteChanged();
                return;
            }

            // 메인 워크스페이스에 이 Step 로드 요청 → VisionService.Tools 채워짐
            WeakReferenceMessenger.Default.Send(new RequestLoadStepMessage(_loadedRecipe, value));

            ActiveToolCount = value.Tools.Count;
            StatusMessage = $"Step 로드됨: {value.Name} ({ActiveToolCount}개 도구)";

            // 메시지 발행 후 — VisionService.Tools가 채워진 다음 CanExecute 재평가
            // (CollectionChanged 핸들러도 같이 트리거하지만 동기 경로를 명시적으로 보장)
            RunCommand.NotifyCanExecuteChanged();
        }

        partial void OnCurrentFilterChanged(ResultFilter value)
        {
            OnPropertyChanged(nameof(IsAllFilter));
            OnPropertyChanged(nameof(IsPassFilter));
            OnPropertyChanged(nameof(IsFailFilter));
            ResultsView.Refresh();
        }

        private bool FilterPredicate(object obj)
        {
            if (obj is not BatchImageResult r) return false;
            return CurrentFilter switch
            {
                ResultFilter.PassOnly => r.Success,
                ResultFilter.FailOnly => !r.Success,
                _ => true,
            };
        }

        public IRelayCommand BrowseImageFolderCommand { get; }
        public IRelayCommand BrowseOutputCsvCommand { get; }
        public IRelayCommand BrowseFailureDirCommand { get; }
        public IAsyncRelayCommand RunCommand { get; }
        public IRelayCommand CancelCommand { get; }
        public IRelayCommand OpenOutputCsvCommand { get; }
        public IRelayCommand OpenFailureFolderCommand { get; }
        public IRelayCommand BrowseFailuresCommand { get; }
        public IRelayCommand BrowseAllResultsCommand { get; }
        public IRelayCommand EditThresholdsCommand { get; }
        public IRelayCommand AutoTuneCommand { get; }
        public IRelayCommand SaveAsGoldenCommand { get; }
        public IRelayCommand LoadGoldenCommand { get; }
        public IRelayCommand ClearGoldenCommand { get; }

        [ObservableProperty] private GoldenSet? _activeGoldenSet;
        [ObservableProperty] private string _goldenStatusText = "골든셋: 없음";

        partial void OnActiveGoldenSetChanged(GoldenSet? value)
        {
            GoldenStatusText = value == null ? "골든셋: 없음" : $"골든셋: {value.Name} ({value.Count}장)";
            ClearGoldenCommand.NotifyCanExecuteChanged();
        }

        private void SaveAsGolden()
        {
            if (Results.Count == 0) return;
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Title = "골든셋 저장",
                Filter = "Golden Set (*.golden.json)|*.golden.json|JSON (*.json)|*.json",
                FileName = $"golden_{DateTime.Now:yyyyMMdd_HHmmss}.golden.json"
            };
            if (dlg.ShowDialog() != true) return;

            var toolNames = _visionService.Tools.ToDictionary(t => t.Id, t => t.Name);
            var golden = GoldenSet.FromBatchResults(
                Path.GetFileNameWithoutExtension(dlg.FileName),
                _loadedRecipe?.Name ?? "",
                Results.ToList(),
                toolNames);

            if (golden.Save(dlg.FileName))
            {
                ActiveGoldenSet = golden;
                StatusMessage = $"골든셋 저장됨: {dlg.FileName} ({golden.Count}장)";
            }
            else
            {
                StatusMessage = "골든셋 저장 실패";
            }
        }

        private void LoadGolden()
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "골든셋 로드",
                Filter = "Golden Set (*.golden.json;*.json)|*.golden.json;*.json"
            };
            if (dlg.ShowDialog() != true) return;

            var golden = GoldenSet.Load(dlg.FileName);
            if (golden == null)
            {
                StatusMessage = "골든셋 로드 실패 (파일 형식 오류)";
                return;
            }
            ActiveGoldenSet = golden;
            StatusMessage = $"골든셋 로드됨: {golden.Name} ({golden.Count}장)";
        }

        private void ClearGolden()
        {
            ActiveGoldenSet = null;
            StatusMessage = "골든셋 비활성화";
        }

        private void OpenAutoTune()
        {
            var win = new Views.BatchTest.AutoTuneWindow(_visionService, _recipeService, _loadedRecipe, ImageFolder)
            {
                Owner = System.Windows.Application.Current?.Windows
                    .Cast<System.Windows.Window>()
                    .FirstOrDefault(w => w is Views.BatchTest.BatchTestWindow)
            };
            win.Show();
        }

        private void EditThresholds()
        {
            if (_loadedRecipe == null) return;
            var win = new Views.BatchTest.ThresholdEditorWindow(_recipeService, _visionService, _loadedRecipe)
            {
                Owner = System.Windows.Application.Current?.Windows
                    .Cast<System.Windows.Window>()
                    .FirstOrDefault(w => w is Views.BatchTest.BatchTestWindow)
            };
            win.ShowDialog();
        }

        /// <summary>특정 결과로 Failure Browser 오픈 — DataGrid 더블클릭이 호출.</summary>
        public void OpenBrowserAt(BatchImageResult target)
        {
            var list = Results.ToList();
            var idx = list.IndexOf(target);
            if (idx < 0) return;
            ShowBrowser(list, idx);
        }

        private void BrowseFailures()
        {
            var failures = Results.Where(r => !r.Success).ToList();
            if (failures.Count > 0) ShowBrowser(failures, 0);
        }

        private void BrowseAllResults()
        {
            if (Results.Count > 0) ShowBrowser(Results.ToList(), 0);
        }

        private void ShowBrowser(IList<BatchImageResult> items, int startIndex)
        {
            var win = new Views.BatchTest.FailureBrowserWindow(items, FailureOverlayDir, startIndex)
            {
                Owner = System.Windows.Application.Current?.Windows
                    .Cast<System.Windows.Window>()
                    .FirstOrDefault(w => w is Views.BatchTest.BatchTestWindow)
            };
            win.Show();
        }

        [RelayCommand] private void SetFilter(string filter)
        {
            CurrentFilter = filter switch
            {
                "Pass" => ResultFilter.PassOnly,
                "Fail" => ResultFilter.FailOnly,
                _ => ResultFilter.All,
            };
        }

        [RelayCommand] private void OpenResult(BatchImageResult? r)
        {
            if (r == null) return;
            // 실패면 오버레이 이미지(있으면) 우선, 아니면 원본
            string? path = null;
            if (!r.Success && !string.IsNullOrEmpty(FailureOverlayDir))
            {
                var overlay = Path.Combine(FailureOverlayDir,
                    Path.GetFileNameWithoutExtension(r.ImagePath) + "_fail.png");
                if (File.Exists(overlay)) path = overlay;
            }
            path ??= r.ImagePath;
            TryShellOpen(path);
        }

        private void BrowseImageFolder()
        {
            var dlg = new OpenFolderDialog { Title = "이미지 폴더 선택" };
            if (!string.IsNullOrEmpty(ImageFolder) && Directory.Exists(ImageFolder))
                dlg.InitialDirectory = ImageFolder;
            if (dlg.ShowDialog() == true)
            {
                ImageFolder = dlg.FolderName;
                if (string.IsNullOrEmpty(FailureOverlayDir))
                    FailureOverlayDir = Path.Combine(ImageFolder, "failures");
            }
        }

        private void BrowseOutputCsv()
        {
            var dlg = new SaveFileDialog
            {
                Title = "CSV 리포트 저장",
                Filter = "CSV (*.csv)|*.csv",
                FileName = Path.GetFileName(OutputCsvPath),
                DefaultExt = ".csv"
            };
            if (!string.IsNullOrEmpty(OutputCsvPath))
            {
                try { dlg.InitialDirectory = Path.GetDirectoryName(OutputCsvPath) ?? ""; }
                catch { }
            }
            if (dlg.ShowDialog() == true)
                OutputCsvPath = dlg.FileName;
        }

        private void BrowseFailureDir()
        {
            var dlg = new OpenFolderDialog { Title = "실패 오버레이 저장 폴더" };
            if (!string.IsNullOrEmpty(FailureOverlayDir) && Directory.Exists(FailureOverlayDir))
                dlg.InitialDirectory = FailureOverlayDir;
            if (dlg.ShowDialog() == true)
                FailureOverlayDir = dlg.FolderName;
        }

        private void OpenOutputCsv()
        {
            if (File.Exists(OutputCsvPath)) TryShellOpen(OutputCsvPath);
        }

        private void OpenFailureFolder()
        {
            if (!string.IsNullOrEmpty(FailureOverlayDir) && Directory.Exists(FailureOverlayDir))
                TryShellOpen(FailureOverlayDir);
        }

        private static void TryShellOpen(string path)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[BatchTest] OpenPath '{path}' failed: {ex.Message}");
            }
        }

        private async Task RunAsync()
        {
            if (string.IsNullOrWhiteSpace(ImageFolder) || !Directory.Exists(ImageFolder))
            {
                StatusMessage = "이미지 폴더가 유효하지 않습니다.";
                return;
            }
            if (string.IsNullOrWhiteSpace(OutputCsvPath))
            {
                StatusMessage = "출력 CSV 경로를 지정하세요.";
                return;
            }
            if (_visionService.Tools.Count == 0)
            {
                StatusMessage = "현재 레시피에 도구가 없습니다.";
                return;
            }

            Results.Clear();
            LogLines.Clear();
            PassCount = 0; FailCount = 0; AvgMs = 0;
            ProgressCurrent = 0; ProgressTotal = 0;
            StatusMessage = "준비 중...";
            IsRunning = true;
            _cts = new CancellationTokenSource();

            var runner = new BatchTestRunner(_visionService);
            double totalMs = 0;
            runner.Progress += (cur, tot) => SafeDispatch(() =>
            {
                ProgressCurrent = cur; ProgressTotal = tot;
                StatusMessage = $"진행 중... {cur}/{tot}";
            });
            runner.ImageDone += r => SafeDispatch(() =>
            {
                Results.Add(r);
                if (r.Success) PassCount++; else FailCount++;
                totalMs += r.TotalMs;
                AvgMs = totalMs / Math.Max(1, Results.Count);
                if (LogLines.Count > 500) LogLines.RemoveAt(0);
                LogLines.Add($"{Path.GetFileName(r.ImagePath)}: {(r.Success ? "PASS" : "FAIL")} ({r.TotalMs:F1}ms)" +
                    (string.IsNullOrEmpty(r.FailureReason) ? "" : $" — {r.FailureReason}"));
                BrowseFailuresCommand.NotifyCanExecuteChanged();
                BrowseAllResultsCommand.NotifyCanExecuteChanged();
                SaveAsGoldenCommand.NotifyCanExecuteChanged();
            });
            runner.Log += msg => SafeDispatch(() => LogLines.Add(msg));

            var cfg = new BatchTestConfig
            {
                ImageFolder = ImageFolder,
                RecurseSubfolders = RecurseSubfolders,
                OutputCsvPath = OutputCsvPath,
                SaveFailureOverlays = SaveFailureOverlays,
                FailureOverlayDir = string.IsNullOrEmpty(FailureOverlayDir) ? null : FailureOverlayDir,
                Criteria = _loadedRecipe?.Criteria ?? _recipeService.CurrentRecipe?.Criteria,
                GoldenSet = ActiveGoldenSet
            };

            try
            {
                var results = await runner.RunAsync(cfg, _cts.Token);
                try
                {
                    CsvReportWriter.Write(OutputCsvPath, results, _visionService.Tools);
                    StatusMessage = $"완료 — PASS {PassCount} / FAIL {FailCount}, CSV: {OutputCsvPath}";
                }
                catch (Exception ex)
                {
                    StatusMessage = $"CSV 쓰기 실패: {ex.Message}";
                }
            }
            catch (OperationCanceledException)
            {
                StatusMessage = $"중단됨 — PASS {PassCount} / FAIL {FailCount}";
            }
            catch (Exception ex)
            {
                StatusMessage = $"실패: {ex.Message}";
            }
            finally
            {
                IsRunning = false;
                _cts?.Dispose();
                _cts = null;
                OpenOutputCsvCommand.NotifyCanExecuteChanged();
                OpenFailureFolderCommand.NotifyCanExecuteChanged();
            }
        }

        private void Cancel()
        {
            _cts?.Cancel();
            StatusMessage = "중단 중...";
        }

        private static void SafeDispatch(Action action)
        {
            var app = System.Windows.Application.Current;
            if (app == null) action();
            else app.Dispatcher.Invoke(action);
        }

        // ─── Pipeline selector helpers ───
        /// <summary>창 오픈 시 현재 활성 Recipe/Step에 셀렉터를 동기화.</summary>
        private void SyncFromCurrentRecipe()
        {
            var current = _recipeService.CurrentRecipe;
            if (current == null) return;

            var info = RecipeList.FirstOrDefault(r => r.Id == current.Id)
                       ?? RecipeList.FirstOrDefault(r => string.Equals(r.Name, current.Name, StringComparison.OrdinalIgnoreCase));
            if (info != null)
            {
                SelectedRecipeInfo = info;
                // 활성 도구가 이미 있으면, 그게 어느 Step에서 왔는지 추정 — Step.Tools.Count로 매칭
                var activeCount = _visionService.Tools.Count;
                if (activeCount > 0)
                {
                    var bestStep = current.Steps.FirstOrDefault(s => s.Tools.Count == activeCount);
                    if (bestStep != null)
                    {
                        var camId = string.IsNullOrEmpty(bestStep.CameraId) ? NoCameraId : bestStep.CameraId;
                        SelectedCamera = Cameras.FirstOrDefault(c => c.Id == camId);
                        SelectedStep = StepsForCamera.FirstOrDefault(s => s.Id == bestStep.Id);
                    }
                }
            }
        }
    }
}
