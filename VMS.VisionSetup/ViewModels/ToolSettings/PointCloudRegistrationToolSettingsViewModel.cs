using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using System;
using VMS.VisionSetup.VisionTools.PointCloud;

namespace VMS.VisionSetup.ViewModels.ToolSettings
{
    public class PointCloudRegistrationToolSettingsViewModel : ToolSettingsViewModelBase
    {
        private PointCloudRegistrationTool TypedTool => (PointCloudRegistrationTool)Tool;

        public PointCloudRegistrationToolSettingsViewModel(PointCloudRegistrationTool tool) : base(tool)
        {
            LoadAvailableParamCodes();
            SaveCurrentAsReferenceCommand = new RelayCommand(SaveCurrentAsReference);
            LoadReferenceCommand = new RelayCommand(LoadReference);
            ClearReferenceCommand = new RelayCommand(ClearReference);
        }

        public string ReferencePath { get => TypedTool.ReferencePath; set => TypedTool.ReferencePath = value; }
        public int MaxIterations { get => TypedTool.MaxIterations; set => TypedTool.MaxIterations = value; }
        public float Tolerance { get => TypedTool.Tolerance; set => TypedTool.Tolerance = value; }
        public bool ApplyTransformToSource { get => TypedTool.ApplyTransformToSource; set => TypedTool.ApplyTransformToSource = value; }
        public bool EnableCoarseAlignment { get => TypedTool.EnableCoarseAlignment; set => TypedTool.EnableCoarseAlignment = value; }
        public float ConfidenceDistanceMm { get => TypedTool.ConfidenceDistanceMm; set => TypedTool.ConfidenceDistanceMm = value; }
        public bool IsReferenceLoaded => TypedTool.IsReferenceLoaded;

        public IRelayCommand SaveCurrentAsReferenceCommand { get; }
        public IRelayCommand LoadReferenceCommand { get; }
        public IRelayCommand ClearReferenceCommand { get; }

        private string _trainStatus = "현재 점군을 Reference로 저장하려면 'Save Current as Reference' 클릭.";
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
                FileName = "reference.vpc"
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

        /// <summary>기존 기준 파일 선택 — .vpc(스캔) 또는 .stl(CAD, 표면 샘플링).</summary>
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
