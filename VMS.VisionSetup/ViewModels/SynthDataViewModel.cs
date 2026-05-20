using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VMS.VisionSetup.Services.SynthData;
using VMS.VisionSetup.VisionTools.Identification;

namespace VMS.VisionSetup.ViewModels
{
    /// <summary>
    /// 합성 OCR 데이터 생성기 ViewModel.
    /// 설정 입력 → SyntheticOcrDataGenerator 실행 → 진행률 / 결과 상태 노출.
    /// </summary>
    public partial class SynthDataViewModel : ObservableObject
    {
        public SynthDataViewModel()
        {
            // 시스템 설치 폰트 목록 (가독성 있는 것만)
            var fontFamilies = new List<string>();
            foreach (var ff in System.Drawing.FontFamily.Families)
                fontFamilies.Add(ff.Name);
            fontFamilies.Sort();
            AvailableFonts = fontFamilies;

            // 프리셋 enum 표시명 매핑
            FormatPresetItems = Enum.GetValues(typeof(OcrOutputFormatPreset))
                .Cast<OcrOutputFormatPreset>()
                .Where(p => p != OcrOutputFormatPreset.Custom && p != OcrOutputFormatPreset.None)
                .Select(p => (Preset: p, Pattern: OcrFormatMatcher.GetPatternFor(p)))
                .ToList();

            BrowseOutputDirCommand = new RelayCommand(BrowseOutputDir);
            BrowseBackgroundFolderCommand = new RelayCommand(BrowseBackgroundFolder);
            BrowsePretrainedCommand = new RelayCommand(BrowsePretrained);
            BrowseTrainOutputCommand = new RelayCommand(BrowseTrainOutput);
            BrowsePythonCommand = new RelayCommand(BrowsePython);
            GenerateCommand = new AsyncRelayCommand(GenerateAsync, () => !IsBusy);
            GenerateAndTrainCommand = new AsyncRelayCommand(GenerateAndTrainAsync, () => !IsBusy);
            CancelTrainCommand = new RelayCommand(CancelTrain, () => IsTraining);
            AddPresetCommand = new RelayCommand<OcrOutputFormatPreset>(AddPreset);

            TrainScriptPath = PaddleOcrTrainingService.GetDefaultScriptPath();
            PythonPath = ResolveDefaultPythonPath();
        }

        /// <summary>
        /// PaddleOCR 학습 환경(paddlepaddle 설치된 Python) 자동 탐색.
        /// 우선순위: 일반적인 venv 위치 → py launcher의 3.12 → 시스템 PATH의 python.
        /// </summary>
        private static string ResolveDefaultPythonPath()
        {
            // 1) 흔히 사용하는 venv 위치
            string[] commonVenvs =
            {
                @"D:\ppocr_env\Scripts\python.exe",
                @"C:\ppocr_env\Scripts\python.exe",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "ppocr_env", "Scripts", "python.exe"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "ppocr_env", "Scripts", "python.exe"),
            };
            foreach (var p in commonVenvs)
            {
                try
                {
                    string full = Path.GetFullPath(p);
                    if (File.Exists(full) && HasPaddlepaddle(full)) return full;
                }
                catch { /* path expansion 실패는 무시 */ }
            }

            // 2) py launcher로 3.12/3.11/3.10 시도
            foreach (var ver in new[] { "3.12", "3.11", "3.10" })
            {
                string? path = TryGetPyLauncherPath(ver);
                if (!string.IsNullOrEmpty(path) && HasPaddlepaddle(path)) return path;
            }

            // 3) PATH의 python
            string? sys = TryGetSystemPython();
            if (!string.IsNullOrEmpty(sys)) return sys;

            return "python";
        }

        private static bool HasPaddlepaddle(string pythonPath)
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = pythonPath,
                    Arguments = "-c \"import paddle\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                using var p = System.Diagnostics.Process.Start(psi);
                if (p == null) return false;
                p.WaitForExit(8000);
                return p.HasExited && p.ExitCode == 0;
            }
            catch { return false; }
        }

        private static string? TryGetPyLauncherPath(string version)
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "py",
                    Arguments = $"-{version} -c \"import sys; print(sys.executable)\"",
                    RedirectStandardOutput = true, RedirectStandardError = true,
                    UseShellExecute = false, CreateNoWindow = true,
                };
                using var p = System.Diagnostics.Process.Start(psi);
                if (p == null) return null;
                string output = p.StandardOutput.ReadToEnd().Trim();
                p.WaitForExit(5000);
                return p.ExitCode == 0 && File.Exists(output) ? output : null;
            }
            catch { return null; }
        }

        private static string? TryGetSystemPython()
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "where", Arguments = "python",
                    RedirectStandardOutput = true, RedirectStandardError = true,
                    UseShellExecute = false, CreateNoWindow = true,
                };
                using var p = System.Diagnostics.Process.Start(psi);
                if (p == null) return null;
                string first = p.StandardOutput.ReadLine()?.Trim() ?? "";
                p.WaitForExit(5000);
                return File.Exists(first) ? first : null;
            }
            catch { return null; }
        }

        // ── 폰트 ──
        public List<string> AvailableFonts { get; }

        [ObservableProperty] private string _selectedFont = "Arial";
        [ObservableProperty] private string _additionalFontsText = "Consolas"; // 줄바꿈 추가
        [ObservableProperty] private float _fontSizeMin = 28;
        [ObservableProperty] private float _fontSizeMax = 64;
        [ObservableProperty] private bool _randomBoldItalic = true;

        // ── 패턴 ──
        public List<(OcrOutputFormatPreset Preset, string Pattern)> FormatPresetItems { get; }
        [ObservableProperty] private string _patternsText = "DD/MM/YYYY\nDDDDDDD";

        // ── 샘플 ──
        [ObservableProperty] private int _sampleCount = 200;
        [ObservableProperty] private double _valRatio = 0.2;
        [ObservableProperty] private string _outputDir = string.Empty;
        [ObservableProperty] private int _outputFormatIndex = 0; // 0=PaddleOcrRec, 1=OcvCharPatches

        // ── 배경 ──
        [ObservableProperty] private int _backgroundModeIndex = 0; // 0=Solid, 1=Gradient, 2=FromFolder
        [ObservableProperty] private string _backgroundFolderPath = string.Empty;
        [ObservableProperty] private bool _randomizeInvert = true;

        // ── 증강 ──
        [ObservableProperty] private double _rotationDegMax = 5;
        [ObservableProperty] private double _perspectiveJitter = 0.02;
        [ObservableProperty] private int _blurMaxKernel = 3;
        [ObservableProperty] private double _noiseStdMax = 5;
        [ObservableProperty] private double _brightnessJitter = 0.15;
        [ObservableProperty] private double _contrastJitter = 0.15;
        [ObservableProperty] private bool _dotMatrixMode;

        // ── 학습 (Generate & Train) ──
        [ObservableProperty] private string _pythonPath = "python";
        [ObservableProperty] private string _trainScriptPath = string.Empty;
        [ObservableProperty] private string _trainOutputDir = string.Empty;
        [ObservableProperty] private string _pretrainedModel = string.Empty;
        [ObservableProperty] private int _epochs = 50;
        [ObservableProperty] private int _batchSize = 8;
        [ObservableProperty] private double _learningRate = 0.001;
        [ObservableProperty] private bool _exportOnnx = true;

        // ── 상태 ──
        [ObservableProperty] private bool _isGenerating;
        [ObservableProperty] private bool _isTraining;
        [ObservableProperty] private int _progressCurrent;
        [ObservableProperty] private int _progressTotal;
        [ObservableProperty] private string _statusMessage = "준비됨";
        [ObservableProperty] private int _trainEpoch;
        [ObservableProperty] private int _trainEpochTotal;
        [ObservableProperty] private double _trainProgress;
        [ObservableProperty] private double? _lastLoss;
        [ObservableProperty] private double? _lastAcc;
        [ObservableProperty] private string _resultOnnxPath = string.Empty;
        public ObservableCollection<string> TrainLog { get; } = new();

        public bool IsBusy => IsGenerating || IsTraining;
        partial void OnIsGeneratingChanged(bool value) { OnPropertyChanged(nameof(IsBusy)); RefreshCommands(); }
        partial void OnIsTrainingChanged(bool value) { OnPropertyChanged(nameof(IsBusy)); RefreshCommands(); }

        private void RefreshCommands()
        {
            GenerateCommand.NotifyCanExecuteChanged();
            GenerateAndTrainCommand.NotifyCanExecuteChanged();
            CancelTrainCommand.NotifyCanExecuteChanged();
        }

        public IRelayCommand BrowseOutputDirCommand { get; }
        public IRelayCommand BrowseBackgroundFolderCommand { get; }
        public IRelayCommand BrowsePretrainedCommand { get; }
        public IRelayCommand BrowseTrainOutputCommand { get; }
        public IRelayCommand BrowsePythonCommand { get; }
        public IAsyncRelayCommand GenerateCommand { get; }
        public IAsyncRelayCommand GenerateAndTrainCommand { get; }
        public IRelayCommand CancelTrainCommand { get; }
        public IRelayCommand<OcrOutputFormatPreset> AddPresetCommand { get; }

        private CancellationTokenSource? _trainCts;

        private void AddPreset(OcrOutputFormatPreset preset)
        {
            string p = OcrFormatMatcher.GetPatternFor(preset);
            if (string.IsNullOrEmpty(p)) return;
            var lines = PatternsText.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
            if (!lines.Contains(p, StringComparer.Ordinal)) lines.Add(p);
            PatternsText = string.Join("\n", lines);
        }

        private void BrowseOutputDir()
        {
            var dlg = new OpenFolderDialog { Title = "출력 폴더 선택" };
            if (!string.IsNullOrEmpty(OutputDir) && Directory.Exists(OutputDir))
                dlg.InitialDirectory = OutputDir;
            if (dlg.ShowDialog() == true)
                OutputDir = dlg.FolderName;
        }

        private void BrowseBackgroundFolder()
        {
            var dlg = new OpenFolderDialog { Title = "배경 이미지 폴더 선택" };
            if (!string.IsNullOrEmpty(BackgroundFolderPath) && Directory.Exists(BackgroundFolderPath))
                dlg.InitialDirectory = BackgroundFolderPath;
            if (dlg.ShowDialog() == true)
                BackgroundFolderPath = dlg.FolderName;
        }

        private void BrowsePretrained()
        {
            var dlg = new OpenFileDialog
            {
                Title = "사전학습 모델 선택 (.pdparams 또는 디렉토리 내 inference)",
                Filter = "All Files (*.*)|*.*|Paddle Params (*.pdparams)|*.pdparams"
            };
            if (!string.IsNullOrEmpty(PretrainedModel))
            {
                try { dlg.InitialDirectory = Path.GetDirectoryName(PretrainedModel) ?? ""; }
                catch { }
            }
            if (dlg.ShowDialog() == true)
            {
                // .pdparams의 경우 확장자 제거 — Paddle은 prefix를 받음
                string p = dlg.FileName;
                if (p.EndsWith(".pdparams", StringComparison.OrdinalIgnoreCase))
                    p = p.Substring(0, p.Length - ".pdparams".Length);
                PretrainedModel = p;
            }
        }

        private void BrowseTrainOutput()
        {
            var dlg = new OpenFolderDialog { Title = "학습 출력 폴더 선택" };
            if (!string.IsNullOrEmpty(TrainOutputDir) && Directory.Exists(TrainOutputDir))
                dlg.InitialDirectory = TrainOutputDir;
            if (dlg.ShowDialog() == true)
                TrainOutputDir = dlg.FolderName;
        }

        private void BrowsePython()
        {
            var dlg = new OpenFileDialog
            {
                Title = "Python 실행파일 선택",
                Filter = "Python (python.exe)|python.exe|All Files (*.*)|*.*"
            };
            if (!string.IsNullOrEmpty(PythonPath))
            {
                try { dlg.InitialDirectory = Path.GetDirectoryName(PythonPath) ?? ""; }
                catch { }
            }
            if (dlg.ShowDialog() == true)
                PythonPath = dlg.FileName;
        }

        private async Task GenerateAsync()
        {
            if (string.IsNullOrWhiteSpace(OutputDir))
            {
                StatusMessage = "출력 폴더를 선택하세요.";
                return;
            }

            var patterns = PatternsText
                .Split(new[] { '\n', '\r', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
            if (patterns.Count == 0)
            {
                StatusMessage = "패턴을 하나 이상 입력하세요.";
                return;
            }

            var fonts = new List<string> { SelectedFont };
            fonts.AddRange((AdditionalFontsText ?? "")
                .Split(new[] { '\n', '\r', ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Trim()).Where(l => l.Length > 0));
            fonts = fonts.Distinct().ToList();

            var cfg = new SynthDataConfig
            {
                Patterns = patterns,
                FontFamilies = fonts,
                FontSizeMin = FontSizeMin,
                FontSizeMax = Math.Max(FontSizeMin, FontSizeMax),
                RandomBoldItalic = RandomBoldItalic,
                SampleCount = SampleCount,
                ValRatio = ValRatio,
                OutputDir = OutputDir,
                OutputFormat = OutputFormatIndex == 0 ? DatasetFormat.PaddleOcrRec : DatasetFormat.OcvCharPatches,
                Background = new BackgroundConfig
                {
                    Mode = (BackgroundMode)BackgroundModeIndex,
                    SolidColor = Color.White,
                    FolderPath = BackgroundFolderPath,
                    RandomizeInvert = RandomizeInvert
                },
                Augmentation = new AugmentationConfig
                {
                    RotationDegMax = RotationDegMax,
                    PerspectiveJitter = PerspectiveJitter,
                    BlurMaxKernel = BlurMaxKernel,
                    NoiseStdMax = NoiseStdMax,
                    BrightnessJitter = BrightnessJitter,
                    ContrastJitter = ContrastJitter,
                    DotMatrixMode = DotMatrixMode
                }
            };

            IsGenerating = true;
            StatusMessage = "생성 중...";
            ProgressCurrent = 0;
            ProgressTotal = SampleCount;

            try
            {
                var gen = new SyntheticOcrDataGenerator();
                gen.Progress += (cur, tot) =>
                {
                    System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                    {
                        ProgressCurrent = cur;
                        ProgressTotal = tot;
                        StatusMessage = $"생성 중... {cur}/{tot}";
                    });
                };

                var (train, val) = await Task.Run(() => gen.Generate(cfg));
                StatusMessage = $"데이터 생성 완료 — train {train} + val {val} 샘플";
            }
            catch (Exception ex)
            {
                StatusMessage = $"실패: {ex.Message}";
                throw;
            }
            finally
            {
                IsGenerating = false;
            }
        }

        /// <summary>Generate → 학습(Python subprocess) → ONNX 변환까지 자동 실행.</summary>
        private async Task GenerateAndTrainAsync()
        {
            try { await GenerateAsync(); }
            catch { return; } // GenerateAsync에서 상태 메시지 설정 + early exit

            if (string.IsNullOrWhiteSpace(TrainOutputDir))
            {
                StatusMessage = "학습 출력 폴더를 지정하세요.";
                return;
            }
            if (!File.Exists(TrainScriptPath))
            {
                StatusMessage = $"학습 스크립트 없음: {TrainScriptPath}";
                return;
            }

            IsTraining = true;
            TrainLog.Clear();
            ResultOnnxPath = string.Empty;
            TrainEpoch = 0; TrainEpochTotal = Epochs; TrainProgress = 0;
            LastLoss = null; LastAcc = null;
            StatusMessage = "학습 시작...";

            _trainCts = new CancellationTokenSource();
            var svc = new PaddleOcrTrainingService();
            svc.Output += (_, e) =>
            {
                System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                {
                    if (!string.IsNullOrEmpty(e.Line))
                    {
                        TrainLog.Add(e.Line);
                        if (TrainLog.Count > 500) TrainLog.RemoveAt(0);
                    }
                    if (e.EpochCurrent.HasValue) TrainEpoch = e.EpochCurrent.Value;
                    if (e.EpochTotal.HasValue) TrainEpochTotal = e.EpochTotal.Value;
                    if (e.Progress.HasValue) TrainProgress = e.Progress.Value;
                    if (e.Loss.HasValue) LastLoss = e.Loss.Value;
                    if (e.Accuracy.HasValue) LastAcc = e.Accuracy.Value;
                    if (!string.IsNullOrEmpty(e.OnnxPath)) ResultOnnxPath = e.OnnxPath!;
                    if (!string.IsNullOrEmpty(e.Error)) StatusMessage = "오류: " + e.Error;
                });
            };

            var tcfg = new TrainingConfig
            {
                PythonPath = PythonPath,
                ScriptPath = TrainScriptPath,
                DatasetDir = OutputDir,
                OutputDir = TrainOutputDir,
                PretrainedModel = PretrainedModel,
                Epochs = Epochs,
                BatchSize = BatchSize,
                LearningRate = LearningRate,
                Target = "recognition",
                ExportOnnx = ExportOnnx
            };

            try
            {
                bool ok = await svc.RunAsync(tcfg, _trainCts.Token);
                if (ok)
                    StatusMessage = string.IsNullOrEmpty(ResultOnnxPath)
                        ? "학습 완료 (ONNX 미생성)"
                        : $"학습 완료 — ONNX: {ResultOnnxPath}";
                else if (string.IsNullOrEmpty(StatusMessage) || !StatusMessage.StartsWith("오류"))
                    StatusMessage = "학습 종료 (실패 또는 취소)";
            }
            catch (Exception ex)
            {
                StatusMessage = $"학습 실패: {ex.Message}";
            }
            finally
            {
                IsTraining = false;
                _trainCts?.Dispose();
                _trainCts = null;
            }
        }

        private void CancelTrain()
        {
            _trainCts?.Cancel();
            StatusMessage = "학습 중단 중...";
        }
    }
}
