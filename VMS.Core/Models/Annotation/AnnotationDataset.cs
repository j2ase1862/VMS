using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.ObjectModel;
using System.Linq;

namespace VMS.Core.Models.Annotation
{
    public class AnnotationDataset : ObservableObject
    {
        private string _id = Guid.NewGuid().ToString();
        public string Id
        {
            get => _id;
            set => SetProperty(ref _id, value);
        }

        private string _name = string.Empty;
        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }

        private string _description = string.Empty;
        public string Description
        {
            get => _description;
            set => SetProperty(ref _description, value);
        }

        private LabelType _taskType = LabelType.BoundingBox;
        public LabelType TaskType
        {
            get => _taskType;
            set => SetProperty(ref _taskType, value);
        }

        private DatasetTaskType _datasetTaskType = DatasetTaskType.Detection;
        /// <summary>데이터셋 작업 유형 (Detection/Classification/OCR/AnomalyDetection)</summary>
        public DatasetTaskType DatasetTaskType
        {
            get => _datasetTaskType;
            set => SetProperty(ref _datasetTaskType, value);
        }

        private ObservableCollection<string> _classes = new();
        public ObservableCollection<string> Classes
        {
            get => _classes;
            set => SetProperty(ref _classes, value);
        }

        private ObservableCollection<AnnotationImage> _images = new();
        public ObservableCollection<AnnotationImage> Images
        {
            get => _images;
            set => SetProperty(ref _images, value);
        }

        private double _trainRatio = 0.8;
        public double TrainRatio
        {
            get => _trainRatio;
            set => SetProperty(ref _trainRatio, Math.Clamp(value, 0.1, 0.99));
        }

        private DateTime _createdAt = DateTime.Now;
        public DateTime CreatedAt
        {
            get => _createdAt;
            set => SetProperty(ref _createdAt, value);
        }

        private DateTime _modifiedAt = DateTime.Now;
        public DateTime ModifiedAt
        {
            get => _modifiedAt;
            set => SetProperty(ref _modifiedAt, value);
        }

        private string _lastOnnxModelPath = string.Empty;
        /// <summary>
        /// 마지막 학습 결과 ONNX 모델 경로. Inference Mode 자동 복원용.
        /// 학습 완료 시 TrainingStatus.OnnxOutputPath를 저장하고, 데이터셋 다시 로드할 때 참조.
        /// </summary>
        public string LastOnnxModelPath
        {
            get => _lastOnnxModelPath;
            set => SetProperty(ref _lastOnnxModelPath, value);
        }

        public int TotalImages => Images.Count;
        public int LabeledImages => Images.Count(i => i.IsLabeled);
        public int TotalLabels => Images.Sum(i => i.Labels.Count);
        public int TrainCount => Images.Count(i => i.Split == DataSplit.Train);
        public int ValidationCount => Images.Count(i => i.Split == DataSplit.Validation);

        public void RefreshStatistics()
        {
            OnPropertyChanged(nameof(TotalImages));
            OnPropertyChanged(nameof(LabeledImages));
            OnPropertyChanged(nameof(TotalLabels));
            OnPropertyChanged(nameof(TrainCount));
            OnPropertyChanged(nameof(ValidationCount));
        }
    }
}
