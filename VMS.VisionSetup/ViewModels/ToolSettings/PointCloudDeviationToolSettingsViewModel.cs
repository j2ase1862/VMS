using CommunityToolkit.Mvvm.Input;
using System;
using VMS.VisionSetup.Interfaces;
using VMS.VisionSetup.VisionTools.PointCloud;

namespace VMS.VisionSetup.ViewModels.ToolSettings
{
    public class PointCloudDeviationToolSettingsViewModel : ToolSettingsViewModelBase
    {
        private PointCloudDeviationTool TypedTool => (PointCloudDeviationTool)Tool;
        private readonly IDialogService? _dialogService;

        public PointCloudDeviationToolSettingsViewModel(PointCloudDeviationTool tool, IDialogService? dialogService = null) : base(tool)
        {
            _dialogService = dialogService;
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
            // 대화상자는 DialogService 가 연다 (VS-24)
            var fileName = _dialogService?.ShowSaveFileDialog(
                "Point Cloud Files (*.vpc)|*.vpc|All Files|*.*", ".vpc", "deviation_reference.vpc");
            if (fileName == null)
            {
                TrainStatus = "저장 취소됨.";
                return;
            }
            bool ok = TypedTool.SaveCurrentAsReference(fileName);
            OnPropertyChanged(nameof(ReferencePath));
            OnPropertyChanged(nameof(IsReferenceLoaded));
            TrainStatus = ok
                ? $"Reference 저장됨: {System.IO.Path.GetFileName(fileName)}"
                : "저장 실패 — 점군이 없거나 경로 오류.";
        }

        /// <summary>기존 기준 파일 선택 — .vpc(양품 스캔) 또는 .stl(CAD, 표면 샘플링).</summary>
        private void LoadReference()
        {
            var fileName = _dialogService?.ShowOpenFileDialog("Reference 파일 선택",
                "Reference Files (*.vpc;*.stl)|*.vpc;*.stl|Point Cloud (*.vpc)|*.vpc|CAD Mesh (*.stl)|*.stl|All Files|*.*");
            if (fileName == null) return;

            TypedTool.ReferencePath = fileName;
            OnPropertyChanged(nameof(ReferencePath));
            OnPropertyChanged(nameof(IsReferenceLoaded));
            TrainStatus = $"Reference 설정됨: {System.IO.Path.GetFileName(fileName)}"
                + (fileName.EndsWith(".stl", StringComparison.OrdinalIgnoreCase)
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
