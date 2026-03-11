using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using VMS.Core.Interfaces;
using VMS.Core.Models.Annotation;

namespace VMS.DeepLearning.ViewModels
{
    public partial class LabelingMainViewModel : ObservableObject
    {
        private readonly IAnnotationService _annotationService;
        private readonly ITrainingService _trainingService;
        private readonly ILabelingDialogService _dialogService;
        private CancellationTokenSource? _trainingCts;

        public LabelingMainViewModel(
            IAnnotationService annotationService,
            ITrainingService trainingService,
            ILabelingDialogService dialogService)
        {
            _annotationService = annotationService;
            _trainingService = trainingService;
            _dialogService = dialogService;

            TrainingConfig = new TrainingConfig();
            TrainingStatus = trainingService.Status;

            trainingService.LogReceived += (_, msg) =>
            {
                System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                {
                    TrainingLog.Add(msg);
                    while (TrainingLog.Count > 500)
                        TrainingLog.RemoveAt(0);
                });
            };

            trainingService.StatusChanged += (_, _) =>
            {
                System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                {
                    OnPropertyChanged(nameof(IsTraining));
                    OnPropertyChanged(nameof(TrainingStatus));
                });
            };

            RefreshDatasetList();
        }

        // ── Dataset ──

        [ObservableProperty]
        private ObservableCollection<AnnotationDataset> _datasets = new();

        [ObservableProperty]
        private AnnotationDataset? _currentDataset;

        [ObservableProperty]
        private string _newDatasetName = string.Empty;

        [ObservableProperty]
        private DatasetTaskType _newDatasetTaskType = DatasetTaskType.Detection;

        // ── Task Type Mode Properties ──

        /// <summary>현재 데이터셋이 BoundingBox 라벨링이 필요한 모드인지 (Detection, OCR)</summary>
        public bool IsBoundingBoxMode => CurrentDataset?.DatasetTaskType is DatasetTaskType.Detection or DatasetTaskType.OCR;

        /// <summary>현재 데이터셋이 이미지 단위 분류 모드인지</summary>
        public bool IsClassificationMode => CurrentDataset?.DatasetTaskType == DatasetTaskType.Classification;

        /// <summary>현재 데이터셋이 이상 탐지 모드인지</summary>
        public bool IsAnomalyMode => CurrentDataset?.DatasetTaskType == DatasetTaskType.AnomalyDetection;

        /// <summary>현재 이미지의 분류 클래스 (Classification/Anomaly 모드)</summary>
        public string? CurrentImageClass
        {
            get => CurrentImage?.Labels.FirstOrDefault()?.ClassName;
            set
            {
                if (CurrentImage == null || CurrentDataset == null || value == null) return;
                AssignImageClass(value);
            }
        }

        /// <summary>DatasetTaskType 열거값 (ComboBox 바인딩용)</summary>
        public Array DatasetTaskTypes => Enum.GetValues(typeof(DatasetTaskType));

        // ── Image Navigation ──

        [ObservableProperty]
        private AnnotationImage? _currentImage;

        [ObservableProperty]
        private Mat? _currentMat;

        [ObservableProperty]
        private int _currentImageIndex = -1;

        // ── Labels ──

        [ObservableProperty]
        private LabelInfo? _selectedLabel;

        // ── Class Management ──

        [ObservableProperty]
        private string _newClassName = string.Empty;

        [ObservableProperty]
        private string? _selectedClassName;

        // ── Status ──

        [ObservableProperty]
        private string _statusMessage = "데이터셋을 선택하거나 생성하세요.";

        [ObservableProperty]
        private string _windowTitle = "VMS Labeling";

        partial void OnCurrentDatasetChanged(AnnotationDataset? value)
        {
            OnPropertyChanged(nameof(IsBoundingBoxMode));
            OnPropertyChanged(nameof(IsClassificationMode));
            OnPropertyChanged(nameof(IsAnomalyMode));
            OnPropertyChanged(nameof(CurrentImageClass));
        }

        partial void OnCurrentImageChanged(AnnotationImage? value)
        {
            OnPropertyChanged(nameof(CurrentImageClass));
        }

        #region Dataset Commands

        [RelayCommand]
        private void CreateDataset()
        {
            if (string.IsNullOrWhiteSpace(NewDatasetName))
            {
                _dialogService.ShowWarning("데이터셋 이름을 입력하세요.", "경고");
                return;
            }

            var dataset = _annotationService.CreateDataset(NewDatasetName, NewDatasetTaskType);
            CurrentDataset = dataset;
            NewDatasetName = string.Empty;
            RefreshDatasetList();
            StatusMessage = $"데이터셋 '{dataset.Name}' 생성 완료. [{dataset.DatasetTaskType}]";
            UpdateWindowTitle();
        }

        [RelayCommand]
        private void SelectDataset(AnnotationDataset? dataset)
        {
            if (dataset == null) return;

            string datasetDir = Path.Combine(_annotationService.DatasetFolderPath,
                string.Join("_", dataset.Name.Split(Path.GetInvalidFileNameChars(),
                    StringSplitOptions.RemoveEmptyEntries)));

            var loaded = _annotationService.LoadDataset(datasetDir);
            CurrentDataset = loaded ?? dataset;

            if (CurrentDataset.Images.Count > 0)
                NavigateToImage(0);
            else
                ClearCurrentImage();

            StatusMessage = $"데이터셋 '{CurrentDataset.Name}' [{CurrentDataset.DatasetTaskType}] — 이미지 {CurrentDataset.TotalImages}장, 라벨 {CurrentDataset.TotalLabels}개";
            UpdateWindowTitle();
        }

        [RelayCommand]
        private void SaveDataset()
        {
            if (CurrentDataset == null) return;

            if (_annotationService.SaveDataset(CurrentDataset))
                StatusMessage = "저장 완료.";
            else
                _dialogService.ShowError("데이터셋 저장에 실패했습니다.", "오류");
        }

        [RelayCommand]
        private void DeleteDataset()
        {
            if (CurrentDataset == null) return;

            if (!_dialogService.ShowConfirmation(
                $"데이터셋 '{CurrentDataset.Name}'을(를) 삭제하시겠습니까?\n모든 이미지와 라벨이 영구 삭제됩니다.", "삭제 확인"))
                return;

            _annotationService.DeleteDataset(CurrentDataset.Id);
            CurrentDataset = null;
            ClearCurrentImage();
            RefreshDatasetList();
            StatusMessage = "데이터셋 삭제 완료.";
            UpdateWindowTitle();
        }

        #endregion

        #region Image Commands

        [RelayCommand]
        private void AddImages()
        {
            if (CurrentDataset == null)
            {
                _dialogService.ShowWarning("먼저 데이터셋을 선택하세요.", "경고");
                return;
            }

            var paths = _dialogService.ShowOpenFilesDialog(
                "이미지 파일 선택",
                "이미지 파일|*.bmp;*.jpg;*.jpeg;*.png;*.tif;*.tiff|모든 파일|*.*");

            if (paths == null || paths.Length == 0) return;

            var added = _annotationService.AddImages(CurrentDataset, paths);
            if (added.Count > 0)
            {
                CurrentDataset.RefreshStatistics();
                _annotationService.SaveDataset(CurrentDataset);

                int idx = CurrentDataset.Images.IndexOf(added[0]);
                if (idx >= 0)
                    NavigateToImage(idx);

                StatusMessage = $"{added.Count}장 추가 완료. 총 {CurrentDataset.TotalImages}장.";
            }
        }

        [RelayCommand]
        private void RemoveCurrentImage()
        {
            if (CurrentDataset == null || CurrentImage == null) return;

            if (!_dialogService.ShowConfirmation("현재 이미지를 삭제하시겠습니까?", "삭제 확인"))
                return;

            _annotationService.RemoveImage(CurrentDataset, CurrentImage.Id);
            _annotationService.SaveDataset(CurrentDataset);

            if (CurrentDataset.Images.Count > 0)
                NavigateToImage(Math.Min(CurrentImageIndex, CurrentDataset.Images.Count - 1));
            else
                ClearCurrentImage();
        }

        [RelayCommand]
        private void PreviousImage()
        {
            if (CurrentDataset == null || CurrentImageIndex <= 0) return;
            SaveCurrentLabels();
            NavigateToImage(CurrentImageIndex - 1);
        }

        [RelayCommand]
        private void NextImage()
        {
            if (CurrentDataset == null || CurrentImageIndex >= CurrentDataset.Images.Count - 1) return;
            SaveCurrentLabels();
            NavigateToImage(CurrentImageIndex + 1);
        }

        [RelayCommand]
        private void SelectImage(AnnotationImage? image)
        {
            if (CurrentDataset == null || image == null) return;
            SaveCurrentLabels();
            int idx = CurrentDataset.Images.IndexOf(image);
            if (idx >= 0)
                NavigateToImage(idx);
        }

        #endregion

        #region Label Commands

        /// <summary>
        /// 바운딩 박스가 생성되었을 때 호출 (View code-behind에서 호출)
        /// Detection/OCR 모드에서만 유효
        /// </summary>
        public LabelInfo? OnBoundingBoxCreated(Rect boundingBox)
        {
            if (CurrentDataset == null || CurrentImage == null) return null;
            if (!IsBoundingBoxMode) return null;

            string className = SelectedClassName ?? CurrentDataset.Classes.FirstOrDefault() ?? "object";

            if (!CurrentDataset.Classes.Contains(className))
                _annotationService.AddClass(CurrentDataset, className);

            var label = new LabelInfo
            {
                ClassName = className,
                LabelType = CurrentDataset.TaskType,
                BoundingBox = boundingBox,
                Points = new List<Point2d>
                {
                    new(boundingBox.X, boundingBox.Y),
                    new(boundingBox.X + boundingBox.Width, boundingBox.Y),
                    new(boundingBox.X + boundingBox.Width, boundingBox.Y + boundingBox.Height),
                    new(boundingBox.X, boundingBox.Y + boundingBox.Height)
                },
                IsVerified = true
            };

            _annotationService.AddLabel(CurrentImage, label);
            SelectedLabel = label;
            UpdateStatusMessage();
            return label;
        }

        public void OnBoundingBoxModified(string labelId, Rect newBounds)
        {
            if (CurrentImage == null) return;

            var label = CurrentImage.Labels.FirstOrDefault(l => l.Id == labelId);
            if (label != null)
            {
                label.BoundingBox = newBounds;
                label.Points = new List<Point2d>
                {
                    new(newBounds.X, newBounds.Y),
                    new(newBounds.X + newBounds.Width, newBounds.Y),
                    new(newBounds.X + newBounds.Width, newBounds.Y + newBounds.Height),
                    new(newBounds.X, newBounds.Y + newBounds.Height)
                };
                CurrentImage.ModifiedAt = DateTime.Now;
            }
        }

        public void OnBoundingBoxDeleted(string labelId)
        {
            if (CurrentImage == null) return;

            _annotationService.RemoveLabel(CurrentImage, labelId);

            if (SelectedLabel?.Id == labelId)
                SelectedLabel = null;

            UpdateStatusMessage();
        }

        [RelayCommand]
        private void DeleteSelectedLabel()
        {
            if (CurrentImage == null || SelectedLabel == null) return;

            _annotationService.RemoveLabel(CurrentImage, SelectedLabel.Id);
            SelectedLabel = null;
            OnPropertyChanged(nameof(CurrentImage));
            UpdateStatusMessage();
        }

        /// <summary>
        /// Classification/Anomaly 모드: 이미지에 클래스 할당 (기존 라벨 대체)
        /// </summary>
        [RelayCommand]
        private void AssignClass(string? className)
        {
            if (className != null)
                AssignImageClass(className);
        }

        private void AssignImageClass(string className)
        {
            if (CurrentImage == null || CurrentDataset == null) return;

            // 기존 라벨 모두 제거 (이미지 단위 분류는 1개 라벨만)
            foreach (var label in CurrentImage.Labels.ToList())
                _annotationService.RemoveLabel(CurrentImage, label.Id);

            var labelType = IsAnomalyMode ? LabelType.AnomalyMask : LabelType.ImageClass;

            var newLabel = new LabelInfo
            {
                ClassName = className,
                LabelType = labelType,
                IsVerified = true
            };
            _annotationService.AddLabel(CurrentImage, newLabel);

            OnPropertyChanged(nameof(CurrentImageClass));
            UpdateStatusMessage();
        }

        #endregion

        #region Class Commands

        [RelayCommand]
        private void AddClass()
        {
            if (CurrentDataset == null || string.IsNullOrWhiteSpace(NewClassName)) return;

            if (_annotationService.AddClass(CurrentDataset, NewClassName.Trim()))
            {
                SelectedClassName = NewClassName.Trim();
                NewClassName = string.Empty;
            }
        }

        [RelayCommand]
        private void RemoveClass(string? className)
        {
            if (CurrentDataset == null || string.IsNullOrEmpty(className)) return;

            if (_dialogService.ShowConfirmation(
                $"클래스 '{className}'을(를) 삭제하시겠습니까?\n이 클래스의 모든 라벨도 함께 삭제됩니다.", "삭제 확인"))
            {
                _annotationService.RemoveClass(CurrentDataset, className);
                if (SelectedClassName == className)
                    SelectedClassName = CurrentDataset.Classes.FirstOrDefault();
            }
        }

        #endregion

        #region Export Commands

        [RelayCommand]
        private void AutoSplit()
        {
            if (CurrentDataset == null) return;

            _annotationService.AutoSplitDataset(CurrentDataset);
            _annotationService.SaveDataset(CurrentDataset);
            StatusMessage = $"자동 분할 완료: Train {CurrentDataset.TrainCount}장, Val {CurrentDataset.ValidationCount}장";
        }

        [RelayCommand]
        private void ExportYolo()
        {
            if (CurrentDataset == null) return;

            var path = _dialogService.ShowFolderDialog("YOLO 내보내기 폴더 선택");
            if (path == null) return;

            if (_annotationService.ExportYolo(CurrentDataset, path))
                _dialogService.ShowInformation($"YOLO 포맷 내보내기 완료.\n{path}", "Export");
            else
                _dialogService.ShowError("YOLO Export에 실패했습니다.", "오류");
        }

        [RelayCommand]
        private void ExportPaddleOcr()
        {
            if (CurrentDataset == null) return;

            var path = _dialogService.ShowFolderDialog("PaddleOCR 내보내기 폴더 선택");
            if (path == null) return;

            bool detOk = _annotationService.ExportPaddleOcrDet(CurrentDataset, path);
            bool recOk = _annotationService.ExportPaddleOcrRec(CurrentDataset, path);

            if (detOk && recOk)
                _dialogService.ShowInformation($"PaddleOCR 포맷 내보내기 완료.\n{path}", "Export");
            else
                _dialogService.ShowError("PaddleOCR Export에 실패했습니다.", "오류");
        }

        [RelayCommand]
        private void ExportClassification()
        {
            if (CurrentDataset == null) return;

            var path = _dialogService.ShowFolderDialog("Classification 내보내기 폴더 선택");
            if (path == null) return;

            if (_annotationService.ExportClassification(CurrentDataset, path))
                _dialogService.ShowInformation($"Classification 포맷 내보내기 완료.\n{path}\n\ntrain_classifier.py로 학습할 수 있습니다.", "Export");
            else
                _dialogService.ShowError("Classification Export에 실패했습니다.", "오류");
        }

        [RelayCommand]
        private void ExportAnomaly()
        {
            if (CurrentDataset == null) return;

            var path = _dialogService.ShowFolderDialog("Anomaly 내보내기 폴더 선택");
            if (path == null) return;

            if (_annotationService.ExportAnomaly(CurrentDataset, path))
                _dialogService.ShowInformation($"Anomaly (MVTec) 포맷 내보내기 완료.\n{path}\n\ntrain_anomaly.py로 학습할 수 있습니다.", "Export");
            else
                _dialogService.ShowError("Anomaly Export에 실패했습니다.", "오류");
        }

        /// <summary>
        /// 현재 DatasetTaskType에 맞는 기본 Export 실행
        /// </summary>
        [RelayCommand]
        private void ExportForCurrentTask()
        {
            if (CurrentDataset == null) return;

            switch (CurrentDataset.DatasetTaskType)
            {
                case DatasetTaskType.Detection:
                    ExportYolo();
                    break;
                case DatasetTaskType.Classification:
                    ExportClassification();
                    break;
                case DatasetTaskType.AnomalyDetection:
                    ExportAnomaly();
                    break;
                case DatasetTaskType.OCR:
                    ExportPaddleOcr();
                    break;
            }
        }

        #endregion

        #region Training Commands

        [ObservableProperty]
        private TrainingConfig _trainingConfig = new();

        [ObservableProperty]
        private TrainingStatus _trainingStatus = new();

        [ObservableProperty]
        private ObservableCollection<string> _trainingLog = new();

        public bool IsTraining => _trainingService.IsTraining;

        [RelayCommand]
        private void SelectTrainingScript()
        {
            var path = _dialogService.ShowOpenFileDialog("Python 스크립트 선택", "Python|*.py|모든 파일|*.*");
            if (path != null)
                TrainingConfig.TrainingScriptPath = path;
        }

        [RelayCommand]
        private void SelectOutputDir()
        {
            var path = _dialogService.ShowFolderDialog("학습 출력 폴더 선택");
            if (path != null)
                TrainingConfig.OutputDir = path;
        }

        [RelayCommand]
        private async System.Threading.Tasks.Task StartTraining()
        {
            if (CurrentDataset == null)
            {
                _dialogService.ShowWarning("먼저 데이터셋을 선택하세요.", "경고");
                return;
            }

            if (string.IsNullOrEmpty(TrainingConfig.TrainingScriptPath))
            {
                _dialogService.ShowWarning("학습 스크립트 경로를 지정하세요.", "경고");
                return;
            }

            if (string.IsNullOrEmpty(TrainingConfig.DatasetPath))
            {
                string datasetDir = Path.Combine(
                    _annotationService.DatasetFolderPath,
                    string.Join("_", CurrentDataset.Name.Split(Path.GetInvalidFileNameChars(),
                        StringSplitOptions.RemoveEmptyEntries)));
                TrainingConfig.DatasetPath = datasetDir;
            }

            if (string.IsNullOrEmpty(TrainingConfig.OutputDir))
            {
                TrainingConfig.OutputDir = Path.Combine(
                    _annotationService.DatasetFolderPath, "training_output");
            }

            TrainingLog.Clear();
            _trainingCts = new CancellationTokenSource();
            OnPropertyChanged(nameof(IsTraining));

            try
            {
                await _trainingService.StartTrainingAsync(TrainingConfig, _trainingCts.Token);
            }
            catch (Exception ex)
            {
                _dialogService.ShowError($"학습 오류: {ex.Message}", "Training Error");
            }

            OnPropertyChanged(nameof(IsTraining));

            if (TrainingStatus.State == TrainingState.Completed &&
                !string.IsNullOrEmpty(TrainingStatus.OnnxOutputPath))
            {
                string toolName = CurrentDataset.DatasetTaskType switch
                {
                    DatasetTaskType.Detection => "Detection Tool > Model Path",
                    DatasetTaskType.Classification => "Classify Tool > Model Path",
                    DatasetTaskType.AnomalyDetection => "Anomaly Tool > Model Path",
                    _ => "해당 Tool > Model Path"
                };

                _dialogService.ShowInformation(
                    $"학습 완료!\n\nONNX 모델: {TrainingStatus.OnnxOutputPath}\n\n" +
                    $"VMS VisionSetup의 {toolName}에\n이 경로를 지정하면 즉시 적용됩니다.",
                    "Training Complete");
            }
        }

        [RelayCommand]
        private void StopTraining()
        {
            _trainingCts?.Cancel();
            _trainingService.StopTraining();
            OnPropertyChanged(nameof(IsTraining));
        }

        #endregion

        #region Navigation Helpers

        private void NavigateToImage(int index)
        {
            if (CurrentDataset == null || index < 0 || index >= CurrentDataset.Images.Count)
                return;

            CurrentImageIndex = index;
            CurrentImage = CurrentDataset.Images[index];

            string imgPath = Path.Combine(
                _annotationService.DatasetFolderPath,
                string.Join("_", CurrentDataset.Name.Split(Path.GetInvalidFileNameChars(),
                    StringSplitOptions.RemoveEmptyEntries)),
                "images",
                CurrentImage.ImagePath);

            CurrentMat?.Dispose();
            CurrentMat = File.Exists(imgPath) ? Cv2.ImRead(imgPath, ImreadModes.Color) : null;

            SelectedLabel = null;
            UpdateStatusMessage();
        }

        private void ClearCurrentImage()
        {
            CurrentImage = null;
            CurrentImageIndex = -1;
            CurrentMat?.Dispose();
            CurrentMat = null;
            SelectedLabel = null;
        }

        private void SaveCurrentLabels()
        {
            if (CurrentDataset != null)
                _annotationService.SaveDataset(CurrentDataset);
        }

        private void RefreshDatasetList()
        {
            Datasets = new ObservableCollection<AnnotationDataset>(_annotationService.GetDatasetList());
        }

        private void UpdateStatusMessage()
        {
            if (CurrentDataset == null)
            {
                StatusMessage = "데이터셋을 선택하거나 생성하세요.";
                return;
            }

            string taskLabel = CurrentDataset.DatasetTaskType switch
            {
                DatasetTaskType.Detection => "Detection",
                DatasetTaskType.Classification => "Classification",
                DatasetTaskType.AnomalyDetection => "Anomaly",
                DatasetTaskType.OCR => "OCR",
                _ => ""
            };

            string imgInfo = CurrentImage != null
                ? $"[{CurrentImageIndex + 1}/{CurrentDataset.TotalImages}] {CurrentImage.ImagePath}"
                : $"이미지 {CurrentDataset.TotalImages}장";

            // Classification/Anomaly 모드에서는 현재 이미지의 클래스를 표시
            if ((IsClassificationMode || IsAnomalyMode) && CurrentImage != null)
            {
                string cls = CurrentImageClass ?? "(미분류)";
                imgInfo += $" — {cls}";
            }
            else if (CurrentImage != null)
            {
                imgInfo += $" — 라벨 {CurrentImage.Labels.Count}개";
            }

            StatusMessage = $"[{taskLabel}] {CurrentDataset.Name}: {imgInfo}";
        }

        private void UpdateWindowTitle()
        {
            if (CurrentDataset != null)
            {
                string taskLabel = CurrentDataset.DatasetTaskType switch
                {
                    DatasetTaskType.Detection => "Detection",
                    DatasetTaskType.Classification => "Classification",
                    DatasetTaskType.AnomalyDetection => "Anomaly",
                    DatasetTaskType.OCR => "OCR",
                    _ => ""
                };
                WindowTitle = $"VMS Labeling — {CurrentDataset.Name} [{taskLabel}]";
            }
            else
            {
                WindowTitle = "VMS Labeling";
            }
        }

        public static System.Windows.Media.Color GetClassColor(string className)
        {
            // 특별 클래스에 대해 고정 색상
            if (className == "good") return System.Windows.Media.Color.FromRgb(76, 175, 80);    // green
            if (className == "defect") return System.Windows.Media.Color.FromRgb(244, 67, 54);  // red

            int hash = className.GetHashCode();
            byte r = (byte)((hash & 0xFF0000) >> 16 | 0x80);
            byte g = (byte)((hash & 0x00FF00) >> 8 | 0x80);
            byte b = (byte)((hash & 0x0000FF) | 0x80);
            return System.Windows.Media.Color.FromRgb(r, g, b);
        }

        #endregion
    }
}
