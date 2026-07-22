using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using System;
using VMS.VisionSetup.VisionTools.PointCloud;

namespace VMS.VisionSetup.ViewModels.ToolSettings
{
    public class PointCloudDeviationToolSettingsViewModel : ToolSettingsViewModelBase
    {
        private PointCloudDeviationTool TypedTool => (PointCloudDeviationTool)Tool;

        public PointCloudDeviationToolSettingsViewModel(PointCloudDeviationTool tool) : base(tool)
        {
            LoadAvailableParamCodes();
            SaveCurrentAsReferenceCommand = new RelayCommand(SaveCurrentAsReference);
            LoadReferenceCommand = new RelayCommand(LoadReference);
            ClearReferenceCommand = new RelayCommand(ClearReference);
        }

        public string ReferencePath { get => TypedTool.ReferencePath; set => TypedTool.ReferencePath = value; }
        public float ToleranceMm { get => TypedTool.ToleranceMm; set => TypedTool.ToleranceMm = value; }
        public float HeatmapRangeMm { get => TypedTool.HeatmapRangeMm; set => TypedTool.HeatmapRangeMm = value; }
        public float MaxDefectRatioPercent { get => TypedTool.MaxDefectRatioPercent; set => TypedTool.MaxDefectRatioPercent = value; }
        public PointCloudDeviationTool.DeviationOutputMode OutputMode { get => TypedTool.OutputMode; set => TypedTool.OutputMode = value; }
        public bool IsReferenceLoaded => TypedTool.IsReferenceLoaded;

        public IRelayCommand SaveCurrentAsReferenceCommand { get; }
        public IRelayCommand LoadReferenceCommand { get; }
        public IRelayCommand ClearReferenceCommand { get; }

        private string _trainStatus = "현재 점군을 기준으로 저장하려면 'Save Current as Reference' 클릭.";
        public string TrainStatus
        {
            get => _trainStatus;
            private set => SetProperty(ref _trainStatus, value);
        }

        private void SaveCurrentAsReference()
        {
            var dlg = new SaveFileDialog
            {
                Filter = "Point Cloud Files (*.vpc)|*.vpc|All Files|*.*",
                DefaultExt = ".vpc",
                FileName = "deviation_reference.vpc"
            };
            if (dlg.ShowDialog() != true)
            {
                TrainStatus = "저장 취소됨.";
                return;
            }
            bool ok = TypedTool.SaveCurrentAsReference(dlg.FileName);
            OnPropertyChanged(nameof(ReferencePath));
            OnPropertyChanged(nameof(IsReferenceLoaded));
            TrainStatus = ok
                ? $"Reference 저장됨: {System.IO.Path.GetFileName(dlg.FileName)}"
                : "저장 실패 — 점군이 없거나 경로 오류.";
        }

        /// <summary>기존 기준 파일 선택 — .vpc(양품 스캔) 또는 .stl(CAD, 표면 샘플링).</summary>
        private void LoadReference()
        {
            var dlg = new OpenFileDialog
            {
                Filter = "Reference Files (*.vpc;*.stl)|*.vpc;*.stl|Point Cloud (*.vpc)|*.vpc|CAD Mesh (*.stl)|*.stl|All Files|*.*"
            };
            if (dlg.ShowDialog() != true) return;

            TypedTool.ReferencePath = dlg.FileName;
            OnPropertyChanged(nameof(ReferencePath));
            OnPropertyChanged(nameof(IsReferenceLoaded));
            TrainStatus = $"Reference 설정됨: {System.IO.Path.GetFileName(dlg.FileName)}"
                + (dlg.FileName.EndsWith(".stl", StringComparison.OrdinalIgnoreCase)
                    ? " (CAD — 실행 시 표면 샘플링)" : "");
        }

        private void ClearReference()
        {
            TypedTool.ReferencePath = string.Empty;
            OnPropertyChanged(nameof(ReferencePath));
            OnPropertyChanged(nameof(IsReferenceLoaded));
            TrainStatus = "Reference 경로 해제됨.";
        }
    }
}
