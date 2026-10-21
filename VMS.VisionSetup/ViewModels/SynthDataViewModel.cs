using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
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
            GenerateCommand = new AsyncRelayCommand(GenerateAsync, () => !IsGenerating);
            AddPresetCommand = new RelayCommand<OcrOutputFormatPreset>(AddPreset);
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

        // ── 상태 ──
        [ObservableProperty] private bool _isGenerating;
        [ObservableProperty] private int _progressCurrent;
        [ObservableProperty] private int _progressTotal;
        [ObservableProperty] private string _statusMessage = "준비됨";

        partial void OnIsGeneratingChanged(bool value) => GenerateCommand.NotifyCanExecuteChanged();

        public IRelayCommand BrowseOutputDirCommand { get; }
        public IRelayCommand BrowseBackgroundFolderCommand { get; }
        public IAsyncRelayCommand GenerateCommand { get; }
        public IRelayCommand<OcrOutputFormatPreset> AddPresetCommand { get; }

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
            }
            finally
            {
                IsGenerating = false;
            }
        }

    }
}
