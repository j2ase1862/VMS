using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Data;
using VMS.VisionSetup.Interfaces;
using VMS.VisionSetup.Models;
using VMS.VisionSetup.Services.BatchTesting;

namespace VMS.VisionSetup.ViewModels
{
    public class AutoTuneToolEntry
    {
        public VisionToolBase Tool { get; init; } = null!;
        public string DisplayName { get; init; } = string.Empty;
        public override string ToString() => DisplayName;
    }

    /// <summary>UI에서 편집 가능한 sweep 한 줄.</summary>
    public partial class SweepRow : ObservableObject
    {
        public ObservableCollection<AutoTuneToolEntry> AvailableTools { get; }
        public ObservableCollection<TunableParameterDescriptor> AvailableParameters { get; } = new();

        public SweepRow(ObservableCollection<AutoTuneToolEntry> tools)
        {
            AvailableTools = tools;
        }

        [ObservableProperty] private AutoTuneToolEntry? _selectedTool;
        [ObservableProperty] private TunableParameterDescriptor? _selectedParameter;
        [ObservableProperty] private string _fromText = "0";
        [ObservableProperty] private string _toText = "100";
        [ObservableProperty] private string _stepText = "10";

        partial void OnSelectedToolChanged(AutoTuneToolEntry? value)
        {
            AvailableParameters.Clear();
            SelectedParameter = null;
            if (value == null) return;
            foreach (var p in TunableParameterDiscovery.Discover(value.Tool))
                if (p.Kind == TunableKind.Numeric)
                    AvailableParameters.Add(p);
        }

        partial void OnSelectedParameterChanged(TunableParameterDescriptor? value)
        {
            // 파라미터의 현재 값을 다시 읽어와 표시 갱신 — 외부에서 값이 변경된 경우 대비
            if (value != null && SelectedTool != null)
            {
                try { value.CurrentValue = value.GetValue(SelectedTool.Tool); }
                catch { /* skip */ }
            }
            OnPropertyChanged(nameof(CurrentValueText));
            OnPropertyChanged(nameof(HasCurrentValue));
        }

        public bool IsValid => SelectedTool != null && SelectedParameter != null;

        /// <summary>현재 도구에 설정된 이 파라미터 값 (UI 표시용 — From/To 범위 잡기 가이드).</summary>
        public string CurrentValueText => SelectedParameter != null
            ? SelectedParameter.FormatValue(SelectedParameter.CurrentValue)
            : "";

        public bool HasCurrentValue => SelectedParameter != null;

        public AutoTuneSweep? ToSweep()
        {
            if (!IsValid) return null;
            if (!double.TryParse(FromText, NumberStyles.Any, CultureInfo.InvariantCulture, out var from) ||
                !double.TryParse(ToText, NumberStyles.Any, CultureInfo.InvariantCulture, out var to) ||
                !double.TryParse(StepText, NumberStyles.Any, CultureInfo.InvariantCulture, out var step))
                return null;

            var values = AutoTuneRunner.BuildNumericRange(SelectedParameter!.Type, from, to, step);
            if (values.Count == 0) return null;

            return new AutoTuneSweep
            {
                Tool = SelectedTool!.Tool,
                Parameter = SelectedParameter!,
                Values = values
            };
        }
    }

    public partial class AutoTuneViewModel : ObservableObject
    {
        private readonly IVisionService _visionService;
        private readonly IRecipeService _recipeService;
        private readonly Recipe? _recipe;
        private CancellationTokenSource? _cts;

        public AutoTuneViewModel(IVisionService visionService, IRecipeService recipeService,
                                 Recipe? recipe, string initialImageFolder)
        {
            _visionService = visionService;
            _recipeService = recipeService;
            _recipe = recipe;
            _imageFolder = initialImageFolder;  // backing field 직접 set — partial method가 RunCommand null 시점에 발화하는 것 회피

            RunCommand = new AsyncRelayCommand(RunAsync, CanRun);
            CancelCommand = new RelayCommand(() => _cts?.Cancel(), () => IsRunning);
            ApplyBestCommand = new RelayCommand(ApplyBest, () => Results.Any(r => r.IsBest));
            AddSweepCommand = new RelayCommand(AddSweep);
            RemoveSweepCommand = new RelayCommand<SweepRow>(RemoveSweep);

            BuildToolEntries();
            AddSweep();   // 첫 sweep 한 줄 자동 추가

            ResultsView = CollectionViewSource.GetDefaultView(Results);
        }

        [ObservableProperty] private string _imageFolder = string.Empty;
        [ObservableProperty] private bool _recurseSubfolders;

        public ObservableCollection<AutoTuneToolEntry> ToolEntries { get; } = new();
        public ObservableCollection<SweepRow> Sweeps { get; } = new();

        [ObservableProperty] private bool _useRandomSearch;
        [ObservableProperty] private string _maxSamplesText = "50";

        [ObservableProperty] private bool _isRunning;
        [ObservableProperty] private int _progressCurrent;
        [ObservableProperty] private int _progressTotal;
        [ObservableProperty] private string _statusMessage = "준비됨";
        [ObservableProperty] private string _combinationCountText = "";

        public ObservableCollection<AutoTuneCombinationResult> Results { get; } = new();
        public ICollectionView ResultsView { get; private set; } = null!;

        public IAsyncRelayCommand RunCommand { get; }
        public IRelayCommand CancelCommand { get; }
        public IRelayCommand ApplyBestCommand { get; }
        public IRelayCommand AddSweepCommand { get; }
        public IRelayCommand<SweepRow> RemoveSweepCommand { get; }

        partial void OnIsRunningChanged(bool value)
        {
            RunCommand.NotifyCanExecuteChanged();
            CancelCommand.NotifyCanExecuteChanged();
        }

        private void BuildToolEntries()
        {
            ToolEntries.Clear();
            foreach (var t in _visionService.Tools)
                ToolEntries.Add(new AutoTuneToolEntry
                {
                    Tool = t,
                    DisplayName = string.IsNullOrEmpty(t.Name) ? t.ToolType : t.Name
                });
        }

        private void AddSweep()
        {
            var row = new SweepRow(ToolEntries);
            if (ToolEntries.Count > 0) row.SelectedTool = ToolEntries[0];
            row.PropertyChanged += OnSweepRowChanged;
            Sweeps.Add(row);
            UpdateCombinationCount();
            RunCommand.NotifyCanExecuteChanged();
        }

        private void RemoveSweep(SweepRow? row)
        {
            if (row == null) return;
            row.PropertyChanged -= OnSweepRowChanged;
            Sweeps.Remove(row);
            UpdateCombinationCount();
            RunCommand.NotifyCanExecuteChanged();
        }

        // SweepRow의 Tool/Parameter/From/To/Step 변경 시 카운트·CanExecute 재평가
        private void OnSweepRowChanged(object? sender, PropertyChangedEventArgs e)
        {
            UpdateCombinationCount();
            RunCommand.NotifyCanExecuteChanged();
        }

        partial void OnImageFolderChanged(string value)
        {
            RunCommand?.NotifyCanExecuteChanged();
        }

        private void UpdateCombinationCount()
        {
            long total = 1;
            int validCount = 0;
            foreach (var r in Sweeps)
            {
                var sweep = r.ToSweep();
                if (sweep == null) continue;
                validCount++;
                total *= sweep.Values.Count;
                if (total > 100_000) { total = 100_000; break; }
            }
            if (validCount == 0)
            {
                CombinationCountText = "(sweep을 추가하세요)";
                return;
            }
            CombinationCountText = UseRandomSearch
                ? $"전체 {total} 조합 · Random {MaxSamplesText} 샘플"
                : $"전체 {total} 조합 (Grid)";
        }

        partial void OnUseRandomSearchChanged(bool value) => UpdateCombinationCount();
        partial void OnMaxSamplesTextChanged(string value) => UpdateCombinationCount();

        private bool CanRun() => !IsRunning &&
                                 !string.IsNullOrWhiteSpace(ImageFolder) &&
                                 Sweeps.Any(r => r.IsValid);

        private async Task RunAsync()
        {
            var validSweeps = Sweeps.Select(r => r.ToSweep()).Where(s => s != null).Cast<AutoTuneSweep>().ToList();
            if (validSweeps.Count == 0)
            {
                StatusMessage = "유효한 sweep이 없습니다.";
                return;
            }

            int? maxRandom = null;
            if (UseRandomSearch &&
                int.TryParse(MaxSamplesText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) &&
                n > 0)
            {
                maxRandom = n;
            }

            var combinations = AutoTuneRunner.BuildCombinations(validSweeps, maxRandom, null);
            if (combinations.Count == 0)
            {
                StatusMessage = "조합이 비었습니다.";
                return;
            }

            Results.Clear();
            ProgressCurrent = 0;
            ProgressTotal = combinations.Count;
            IsRunning = true;
            StatusMessage = $"{combinations.Count}개 조합 실행 중...";
            _cts = new CancellationTokenSource();

            var cfg = new AutoTuneConfig
            {
                ImageFolder = ImageFolder,
                RecurseSubfolders = RecurseSubfolders,
                Sweeps = validSweeps,
                MaxRandomSamples = maxRandom,
                Criteria = _recipe?.Criteria
            };

            var runner = new AutoTuneRunner(_visionService);
            runner.Progress += (cur, tot) => Dispatch(() =>
            {
                ProgressCurrent = cur;
                StatusMessage = $"진행 중... {cur}/{tot}";
            });
            runner.CombinationDone += r => Dispatch(() => Results.Add(r));

            try
            {
                await runner.RunAsync(cfg, _cts.Token);
                ApplyBestCommand.NotifyCanExecuteChanged();

                var best = Results.FirstOrDefault(r => r.IsBest);
                StatusMessage = best != null
                    ? $"완료 — 최고: {best.CombinationText} (PASS {best.PassRateText}, {best.AvgMs:F1}ms)"
                    : "완료 — 결과 없음";
            }
            catch (OperationCanceledException)
            {
                StatusMessage = "중단됨";
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
            }
        }

        private void ApplyBest()
        {
            var best = Results.FirstOrDefault(r => r.IsBest);
            if (best == null) return;
            try
            {
                foreach (var (sweep, value) in best.Bindings)
                {
                    sweep.Parameter.SetValue(sweep.Tool, value);
                    sweep.Parameter.CurrentValue = value;
                }
                if (_recipe != null)
                {
                    try { _recipeService.SaveRecipe(_recipe); } catch { /* best effort */ }
                }
                StatusMessage = $"적용됨: {best.CombinationText}";
            }
            catch (Exception ex)
            {
                StatusMessage = $"적용 실패: {ex.Message}";
            }
        }

        private static void Dispatch(Action a)
        {
            var app = System.Windows.Application.Current;
            if (app == null) a();
            else app.Dispatcher.Invoke(a);
        }
    }
}
