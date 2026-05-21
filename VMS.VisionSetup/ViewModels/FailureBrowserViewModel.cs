using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Media.Imaging;
using VMS.VisionSetup.Services.BatchTesting;

namespace VMS.VisionSetup.ViewModels
{
    /// <summary>한 도구의 결과 표시 — 도구 ID + PASS/FAIL + 메시지.</summary>
    public class ToolResultRow
    {
        public string ToolId { get; set; } = string.Empty;
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    /// <summary>
    /// Failure Browser ViewModel — Batch 결과 중 실패(또는 전체) 항목을 좌우 split으로 비교 검토.
    /// </summary>
    public partial class FailureBrowserViewModel : ObservableObject
    {
        private readonly List<BatchImageResult> _items;
        private readonly string? _overlayDir;

        public FailureBrowserViewModel(IEnumerable<BatchImageResult> items, string? overlayDir, int startIndex = 0)
        {
            _items = items?.ToList() ?? new List<BatchImageResult>();
            _overlayDir = overlayDir;

            PrevCommand = new RelayCommand(MovePrev, () => CurrentIndex > 0);
            NextCommand = new RelayCommand(MoveNext, () => CurrentIndex < _items.Count - 1);
            OpenInViewerCommand = new RelayCommand(OpenInViewer, () => Current != null);
            OpenContainingFolderCommand = new RelayCommand(OpenContainingFolder, () => Current != null);

            if (_items.Count == 0) return;
            CurrentIndex = Math.Clamp(startIndex, 0, _items.Count - 1);
        }

        public int TotalCount => _items.Count;

        [ObservableProperty] private int _currentIndex = -1;
        [ObservableProperty] private BatchImageResult? _current;
        [ObservableProperty] private BitmapImage? _originalImage;
        [ObservableProperty] private BitmapImage? _overlayImage;
        [ObservableProperty] private string _positionText = "";
        [ObservableProperty] private string _fileName = "";
        [ObservableProperty] private string _filePath = "";
        [ObservableProperty] private string _failureReason = "";
        [ObservableProperty] private bool _hasOverlay;
        [ObservableProperty] private bool _isFailure;
        [ObservableProperty] private double _elapsedMs;

        public ObservableCollection<ToolResultRow> ToolRows { get; } = new();
        public ObservableCollection<string> ThresholdViolations { get; } = new();
        public bool HasViolations => ThresholdViolations.Count > 0;
        public ObservableCollection<string> GoldenMismatches { get; } = new();
        public bool HasGoldenMismatches => GoldenMismatches.Count > 0;

        public IRelayCommand PrevCommand { get; }
        public IRelayCommand NextCommand { get; }
        public IRelayCommand OpenInViewerCommand { get; }
        public IRelayCommand OpenContainingFolderCommand { get; }

        partial void OnCurrentIndexChanged(int value)
        {
            if (value < 0 || value >= _items.Count)
            {
                Current = null;
                OriginalImage = null;
                OverlayImage = null;
                HasOverlay = false;
                PositionText = "";
                NotifyCommands();
                return;
            }

            var r = _items[value];
            Current = r;
            FileName = Path.GetFileName(r.ImagePath);
            FilePath = r.ImagePath;
            FailureReason = r.FailureReason ?? string.Empty;
            IsFailure = !r.Success;
            ElapsedMs = r.TotalMs;
            PositionText = $"{value + 1} / {_items.Count}";

            OriginalImage = TryLoadImage(r.ImagePath);

            string? overlayPath = ResolveOverlayPath(r);
            OverlayImage = overlayPath != null ? TryLoadImage(overlayPath) : null;
            HasOverlay = OverlayImage != null;

            // 도구별 결과 행
            ToolRows.Clear();
            foreach (var kv in r.ToolResults)
            {
                ToolRows.Add(new ToolResultRow
                {
                    ToolId = kv.Key,
                    Success = kv.Value.Success,
                    Message = kv.Value.Message ?? string.Empty
                });
            }

            // 임계치 위반
            ThresholdViolations.Clear();
            if (r.ThresholdViolations != null)
                foreach (var v in r.ThresholdViolations)
                    ThresholdViolations.Add(v);
            OnPropertyChanged(nameof(HasViolations));

            // 골든셋 mismatch
            GoldenMismatches.Clear();
            if (r.GoldenMismatches != null)
                foreach (var m in r.GoldenMismatches)
                    GoldenMismatches.Add(m);
            OnPropertyChanged(nameof(HasGoldenMismatches));

            NotifyCommands();
        }

        private void NotifyCommands()
        {
            PrevCommand.NotifyCanExecuteChanged();
            NextCommand.NotifyCanExecuteChanged();
            OpenInViewerCommand.NotifyCanExecuteChanged();
            OpenContainingFolderCommand.NotifyCanExecuteChanged();
        }

        private string? ResolveOverlayPath(BatchImageResult r)
        {
            if (string.IsNullOrEmpty(_overlayDir)) return null;
            var path = Path.Combine(_overlayDir,
                Path.GetFileNameWithoutExtension(r.ImagePath) + "_fail.png");
            return File.Exists(path) ? path : null;
        }

        private static BitmapImage? TryLoadImage(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;  // 파일 핸들 즉시 해제
                bmp.UriSource = new Uri(path, UriKind.Absolute);
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FailureBrowser] LoadImage('{path}') failed: {ex.Message}");
                return null;
            }
        }

        private void MovePrev()
        {
            if (CurrentIndex > 0) CurrentIndex--;
        }

        private void MoveNext()
        {
            if (CurrentIndex < _items.Count - 1) CurrentIndex++;
        }

        private void OpenInViewer()
        {
            if (Current == null) return;
            var path = HasOverlay ? ResolveOverlayPath(Current) : Current.ImagePath;
            TryShellOpen(path ?? Current.ImagePath);
        }

        private void OpenContainingFolder()
        {
            if (Current == null) return;
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{Current.ImagePath}\"",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FailureBrowser] OpenContainingFolder failed: {ex.Message}");
            }
        }

        private static void TryShellOpen(string path)
        {
            try
            {
                Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FailureBrowser] ShellOpen('{path}') failed: {ex.Message}");
            }
        }
    }
}
