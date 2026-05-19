using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VMS.VisionSetup.Interfaces;
using VMS.VisionSetup.Services.BatchTesting;

namespace VMS.VisionSetup.ViewModels
{
    /// <summary>
    /// 무인 배치 테스트 러너 ViewModel. 폴더 이미지 → 현재 레시피 적용 → CSV 리포트.
    /// </summary>
    public partial class BatchTestViewModel : ObservableObject
    {
        private readonly IVisionService _visionService;
        private CancellationTokenSource? _cts;

        public BatchTestViewModel(IVisionService visionService)
        {
            _visionService = visionService;

            BrowseImageFolderCommand = new RelayCommand(BrowseImageFolder);
            BrowseOutputCsvCommand = new RelayCommand(BrowseOutputCsv);
            BrowseFailureDirCommand = new RelayCommand(BrowseFailureDir);
            RunCommand = new AsyncRelayCommand(RunAsync, () => !IsRunning && _visionService.Tools.Count > 0);
            CancelCommand = new RelayCommand(Cancel, () => IsRunning);

            // 기본 출력 경로 후보
            OutputCsvPath = Path.Combine(Path.GetTempPath(), $"batch_report_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
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

        public ObservableCollection<BatchImageResult> Results { get; } = new();
        public ObservableCollection<string> LogLines { get; } = new();

        partial void OnIsRunningChanged(bool value)
        {
            RunCommand.NotifyCanExecuteChanged();
            CancelCommand.NotifyCanExecuteChanged();
        }

        public IRelayCommand BrowseImageFolderCommand { get; }
        public IRelayCommand BrowseOutputCsvCommand { get; }
        public IRelayCommand BrowseFailureDirCommand { get; }
        public IAsyncRelayCommand RunCommand { get; }
        public IRelayCommand CancelCommand { get; }

        private void BrowseImageFolder()
        {
            var dlg = new OpenFolderDialog { Title = "이미지 폴더 선택" };
            if (!string.IsNullOrEmpty(ImageFolder) && Directory.Exists(ImageFolder))
                dlg.InitialDirectory = ImageFolder;
            if (dlg.ShowDialog() == true)
            {
                ImageFolder = dlg.FolderName;
                // 실패 오버레이 기본 위치
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
            });
            runner.Log += msg => SafeDispatch(() => LogLines.Add(msg));

            var cfg = new BatchTestConfig
            {
                ImageFolder = ImageFolder,
                RecurseSubfolders = RecurseSubfolders,
                OutputCsvPath = OutputCsvPath,
                SaveFailureOverlays = SaveFailureOverlays,
                FailureOverlayDir = string.IsNullOrEmpty(FailureOverlayDir) ? null : FailureOverlayDir
            };

            try
            {
                var results = await runner.RunAsync(cfg, _cts.Token);
                // CSV writer
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
    }
}
