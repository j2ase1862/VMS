using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using VMS.VisionSetup.Interfaces;
using VMS.VisionSetup.Models;
using VMS.VisionSetup.Models.Annotation;

namespace VMS.VisionSetup.ViewModels
{
    /// <summary>
    /// 딥러닝 라벨링 뷰모델.
    /// 데이터셋 관리, 이미지 탐색, 바운딩 박스 라벨링을 담당합니다.
    /// </summary>
    public partial class AnnotationViewModel : ObservableObject
    {
        private readonly IAnnotationService _annotationService;
        private readonly IDialogService _dialogService;
        private readonly ITrainingService _trainingService;
        private CancellationTokenSource? _trainingCts;

        public AnnotationViewModel(
            IAnnotationService annotationService,
            IDialogService dialogService,
            ITrainingService trainingService)
        {
            _annotationService = annotationService;
            _dialogService = dialogService;
            _trainingService = trainingService;

            TrainingConfig = new TrainingConfig();
            TrainingStatus = trainingService.Status;

            trainingService.LogReceived += (_, msg) =>
            {
                System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                {
                    TrainingLog.Add(msg);
                    // 최대 500줄 유지
                    while (TrainingLog.Count > 500)
                        TrainingLog.RemoveAt(0);
                });
            };

            trainingService.StatusChanged += (_, status) =>
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
        private LabelType _newDatasetTaskType = LabelType.TextLine;

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

        [ObservableProperty]
        private ObservableCollection<ROIShape> _labelROIs = new();

        [ObservableProperty]
        private ROIShape? _selectedROI;

        // ── Class Management ──

        [ObservableProperty]
        private string _newClassName = string.Empty;

        [ObservableProperty]
        private string? _selectedClassName;

        // ── Status ──

        [ObservableProperty]
        private string _statusMessage = "데이터셋을 선택하거나 생성하세요.";

        // ── ROI ↔ Label 매핑 ──
        private readonly Dictionary<string, string> _roiToLabelMap = new(); // ROI hashcode → LabelInfo.Id

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
            StatusMessage = $"데이터셋 '{dataset.Name}' 생성 완료.";
        }

        [RelayCommand]
        private void SelectDataset(AnnotationDataset? dataset)
        {
            if (dataset == null) return;

            // 이미 로드된 게 아니라면 파일에서 다시 로드
            string datasetDir = Path.Combine(_annotationService.DatasetFolderPath,
                string.Join("_", dataset.Name.Split(Path.GetInvalidFileNameChars(),
                    StringSplitOptions.RemoveEmptyEntries)));

            var loaded = _annotationService.LoadDataset(datasetDir);
            CurrentDataset = loaded ?? dataset;

            // 첫 번째 이미지 선택
            if (CurrentDataset.Images.Count > 0)
                NavigateToImage(0);
            else
                ClearCurrentImage();

            StatusMessage = $"데이터셋 '{CurrentDataset.Name}' — 이미지 {CurrentDataset.TotalImages}장, 라벨 {CurrentDataset.TotalLabels}개";
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

            // 다중 파일 선택 (IDialogService에 ShowOpenFileDialog만 있으므로 단일 선택)
            var path = _dialogService.ShowOpenFileDialog(
                "이미지 파일 선택",
                "이미지 파일|*.bmp;*.jpg;*.jpeg;*.png;*.tif;*.tiff|모든 파일|*.*");

            if (path == null) return;

            var added = _annotationService.AddImage(CurrentDataset, path);
            if (added != null)
            {
                CurrentDataset.RefreshStatistics();
                _annotationService.SaveDataset(CurrentDataset);

                // 추가된 이미지로 이동
                int idx = CurrentDataset.Images.IndexOf(added);
                if (idx >= 0)
                    NavigateToImage(idx);

                StatusMessage = $"이미지 추가 완료. 총 {CurrentDataset.TotalImages}장.";
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

            // 다음 이미지로 이동
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
        /// ImageCanvas에서 ROI가 생성되었을 때 호출 (View code-behind에서 호출)
        /// </summary>
        public void OnROICreated(ROIShape roi)
        {
            if (CurrentDataset == null || CurrentImage == null) return;

            string className = SelectedClassName ?? CurrentDataset.Classes.FirstOrDefault() ?? "text";

            // 클래스가 아직 없으면 자동 추가
            if (!CurrentDataset.Classes.Contains(className))
                _annotationService.AddClass(CurrentDataset, className);

            var rect = roi.GetBoundingRect();
            var label = new LabelInfo
            {
                ClassName = className,
                LabelType = CurrentDataset.TaskType,
                BoundingBox = rect,
                Points = new List<Point2d>
                {
                    new(rect.X, rect.Y),
                    new(rect.X + rect.Width, rect.Y),
                    new(rect.X + rect.Width, rect.Y + rect.Height),
                    new(rect.X, rect.Y + rect.Height)
                },
                IsVerified = true
            };

            _annotationService.AddLabel(CurrentImage, label);
            _roiToLabelMap[GetROIKey(roi)] = label.Id;

            // ROI 색상을 클래스 색상으로 설정
            roi.Color = GetClassColor(className);
            roi.Name = className;

            SelectedLabel = label;
            UpdateStatusMessage();
        }

        /// <summary>
        /// ImageCanvas에서 ROI가 수정되었을 때 호출
        /// </summary>
        public void OnROIModified(ROIShape roi)
        {
            if (CurrentImage == null) return;

            if (_roiToLabelMap.TryGetValue(GetROIKey(roi), out string? labelId))
            {
                var label = CurrentImage.Labels.FirstOrDefault(l => l.Id == labelId);
                if (label != null)
                {
                    var rect = roi.GetBoundingRect();
                    label.BoundingBox = rect;
                    label.Points = new List<Point2d>
                    {
                        new(rect.X, rect.Y),
                        new(rect.X + rect.Width, rect.Y),
                        new(rect.X + rect.Width, rect.Y + rect.Height),
                        new(rect.X, rect.Y + rect.Height)
                    };
                    CurrentImage.ModifiedAt = DateTime.Now;
                }
            }
        }

        /// <summary>
        /// ImageCanvas에서 ROI가 삭제되었을 때 호출
        /// </summary>
        public void OnROIDeleted(ROIShape roi)
        {
            if (CurrentImage == null) return;

            if (_roiToLabelMap.TryGetValue(GetROIKey(roi), out string? labelId))
            {
                _annotationService.RemoveLabel(CurrentImage, labelId);
                _roiToLabelMap.Remove(GetROIKey(roi));

                if (SelectedLabel?.Id == labelId)
                    SelectedLabel = null;

                UpdateStatusMessage();
            }
        }

        /// <summary>
        /// ROI 선택 변경 시 호출
        /// </summary>
        public void OnROISelectionChanged(ROIShape? roi)
        {
            if (roi == null || CurrentImage == null)
            {
                SelectedLabel = null;
                return;
            }

            if (_roiToLabelMap.TryGetValue(GetROIKey(roi), out string? labelId))
                SelectedLabel = CurrentImage.Labels.FirstOrDefault(l => l.Id == labelId);
        }

        [RelayCommand]
        private void DeleteSelectedLabel()
        {
            if (CurrentImage == null || SelectedLabel == null) return;

            // 대응하는 ROI 찾아서 삭제
            var roiKey = _roiToLabelMap.FirstOrDefault(kv => kv.Value == SelectedLabel.Id).Key;
            if (roiKey != null)
            {
                var roi = LabelROIs.FirstOrDefault(r => GetROIKey(r) == roiKey);
                if (roi != null)
                    LabelROIs.Remove(roi);
                _roiToLabelMap.Remove(roiKey);
            }

            _annotationService.RemoveLabel(CurrentImage, SelectedLabel.Id);
            SelectedLabel = null;
            UpdateStatusMessage();
        }

        [RelayCommand]
        private void UpdateLabelClass(string? className)
        {
            if (SelectedLabel == null || string.IsNullOrEmpty(className)) return;

            SelectedLabel.ClassName = className;

            // ROI 색상도 업데이트
            var roiKey = _roiToLabelMap.FirstOrDefault(kv => kv.Value == SelectedLabel.Id).Key;
            if (roiKey != null)
            {
                var roi = LabelROIs.FirstOrDefault(r => GetROIKey(r) == roiKey);
                if (roi != null)
                {
                    roi.Color = GetClassColor(className);
                    roi.Name = className;
                }
            }
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

                // ROI 갱신
                if (CurrentImage != null)
                    LoadLabelsToROIs();
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

            var path = _dialogService.ShowSaveFileDialog("YOLO 폴더|*.yaml", ".yaml", "data");
            if (path == null) return;

            string outputDir = Path.GetDirectoryName(path)!;
            if (_annotationService.ExportYolo(CurrentDataset, outputDir))
                _dialogService.ShowInformation($"YOLO 포맷 내보내기 완료.\n{outputDir}", "Export");
            else
                _dialogService.ShowError("YOLO Export에 실패했습니다.", "오류");
        }

        [RelayCommand]
        private void ExportPaddleOcr()
        {
            if (CurrentDataset == null) return;

            var path = _dialogService.ShowSaveFileDialog("PaddleOCR 폴더|*.txt", ".txt", "train_det");
            if (path == null) return;

            string outputDir = Path.GetDirectoryName(path)!;
            bool detOk = _annotationService.ExportPaddleOcrDet(CurrentDataset, outputDir);
            bool recOk = _annotationService.ExportPaddleOcrRec(CurrentDataset, outputDir);

            if (detOk && recOk)
                _dialogService.ShowInformation($"PaddleOCR 포맷 내보내기 완료.\n{outputDir}", "Export");
            else
                _dialogService.ShowError("PaddleOCR Export에 실패했습니다.", "오류");
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
            var path = _dialogService.ShowSaveFileDialog("출력 폴더 지정|*.txt", ".txt", "output");
            if (path != null)
                TrainingConfig.OutputDir = Path.GetDirectoryName(path) ?? path;
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

            // 데이터셋 경로 자동 설정
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

            // 학습 완료 후 ONNX 경로 안내
            if (TrainingStatus.State == TrainingState.Completed &&
                !string.IsNullOrEmpty(TrainingStatus.OnnxOutputPath))
            {
                _dialogService.ShowInformation(
                    $"학습 완료!\n\nONNX 모델: {TrainingStatus.OnnxOutputPath}\n\n" +
                    "OCR Tool의 Custom Model Path에 이 경로를 지정하면 즉시 적용됩니다.",
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

            // 이미지 로드
            string imgPath = Path.Combine(
                _annotationService.DatasetFolderPath,
                string.Join("_", CurrentDataset.Name.Split(Path.GetInvalidFileNameChars(),
                    StringSplitOptions.RemoveEmptyEntries)),
                "images",
                CurrentImage.ImagePath);

            CurrentMat?.Dispose();
            CurrentMat = File.Exists(imgPath) ? Cv2.ImRead(imgPath, ImreadModes.Color) : null;

            // 라벨 → ROI 로드
            LoadLabelsToROIs();
            UpdateStatusMessage();
        }

        private void LoadLabelsToROIs()
        {
            LabelROIs.Clear();
            _roiToLabelMap.Clear();
            SelectedLabel = null;

            if (CurrentImage == null) return;

            foreach (var label in CurrentImage.Labels)
            {
                var b = label.BoundingBox;
                var roi = new RectangleROI(b.X, b.Y, b.Width, b.Height)
                {
                    Color = GetClassColor(label.ClassName),
                    Name = label.ClassName
                };

                LabelROIs.Add(roi);
                _roiToLabelMap[GetROIKey(roi)] = label.Id;
            }
        }

        private void ClearCurrentImage()
        {
            CurrentImage = null;
            CurrentImageIndex = -1;
            CurrentMat?.Dispose();
            CurrentMat = null;
            LabelROIs.Clear();
            _roiToLabelMap.Clear();
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

            string imgInfo = CurrentImage != null
                ? $"[{CurrentImageIndex + 1}/{CurrentDataset.TotalImages}] {CurrentImage.ImagePath} — 라벨 {CurrentImage.Labels.Count}개"
                : $"이미지 {CurrentDataset.TotalImages}장";

            StatusMessage = $"{CurrentDataset.Name}: {imgInfo}";
        }

        private static string GetROIKey(ROIShape roi) => roi.GetHashCode().ToString();

        private static System.Windows.Media.Color GetClassColor(string className)
        {
            // 클래스 이름 해시 기반 색상 생성
            int hash = className.GetHashCode();
            byte r = (byte)((hash & 0xFF0000) >> 16 | 0x80);
            byte g = (byte)((hash & 0x00FF00) >> 8 | 0x80);
            byte b = (byte)((hash & 0x0000FF) | 0x80);
            return System.Windows.Media.Color.FromRgb(r, g, b);
        }

        #endregion
    }
}
