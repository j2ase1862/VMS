using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using VMS.VisionSetup.VisionTools.DeepLearning;

namespace VMS.VisionSetup.ViewModels.ToolSettings
{
    public class AnomalyToolSettingsViewModel : ToolSettingsViewModelBase
    {
        private AnomalyTool TypedTool => (AnomalyTool)Tool;

        public AnomalyToolSettingsViewModel(AnomalyTool tool) : base(tool)
        {
            BrowseCalibrationFolderCommand = new RelayCommand(BrowseCalibrationFolder);
            CalibrateCommand = new AsyncRelayCommand(CalibrateAsync);

            TypedTool.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(TypedTool.CalibrationFolder))
                    OnPropertyChanged(nameof(CalibrationFolder));
                else if (e.PropertyName == nameof(TypedTool.CalibrationSigma))
                    OnPropertyChanged(nameof(CalibrationSigma));
                else if (e.PropertyName == nameof(TypedTool.CalibrationReport))
                    OnPropertyChanged(nameof(CalibrationReport));
                else if (e.PropertyName == nameof(TypedTool.AnomalyThreshold))
                    OnPropertyChanged(nameof(AnomalyThreshold));
            };
        }

        // Model
        public string ModelPath { get => TypedTool.ModelPath; set => TypedTool.ModelPath = value; }
        public int InputSize { get => TypedTool.InputSize; set => TypedTool.InputSize = value; }

        // Anomaly Detection
        public double AnomalyThreshold { get => TypedTool.AnomalyThreshold; set => TypedTool.AnomalyThreshold = value; }

        // Display
        public bool DrawOverlay { get => TypedTool.DrawOverlay; set => TypedTool.DrawOverlay = value; }
        public bool ShowHeatmap { get => TypedTool.ShowHeatmap; set => TypedTool.ShowHeatmap = value; }
        public double HeatmapOpacity { get => TypedTool.HeatmapOpacity; set => TypedTool.HeatmapOpacity = value; }

        // Auto-calibration
        public string CalibrationFolder { get => TypedTool.CalibrationFolder; set => TypedTool.CalibrationFolder = value; }
        public double CalibrationSigma { get => TypedTool.CalibrationSigma; set => TypedTool.CalibrationSigma = value; }
        public string CalibrationReport => TypedTool.CalibrationReport;

        public IRelayCommand BrowseCalibrationFolderCommand { get; }
        public IAsyncRelayCommand CalibrateCommand { get; }

        private bool _isCalibrating;
        public bool IsCalibrating
        {
            get => _isCalibrating;
            private set { if (SetProperty(ref _isCalibrating, value)) CalibrateCommand.NotifyCanExecuteChanged(); }
        }

        private void BrowseCalibrationFolder()
        {
            var dlg = new OpenFolderDialog
            {
                Title = "정상 이미지 폴더 선택 (Anomaly 캘리브레이션용)"
            };
            if (!string.IsNullOrWhiteSpace(CalibrationFolder))
                dlg.InitialDirectory = CalibrationFolder;
            if (dlg.ShowDialog() == true)
                CalibrationFolder = dlg.FolderName;
        }

        private async Task CalibrateAsync()
        {
            IsCalibrating = true;
            try
            {
                await TypedTool.CalibrateThresholdAsync(CancellationToken.None);
            }
            catch (OperationCanceledException) { /* ignore */ }
            catch (Exception ex)
            {
                TypedTool.CalibrationReport = $"오류: {ex.Message}";
            }
            finally
            {
                IsCalibrating = false;
            }
        }
    }
}
