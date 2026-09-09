using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
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

        private List<string> _lastTrainedImageIds = new();
        /// <summary>
        /// 마지막 학습에 사용된 이미지 ID 스냅샷.
        /// 학습 후 추가/삭제된 이미지를 식별해 "미학습" 카운트 계산에 사용.
        /// </summary>
        public List<string> LastTrainedImageIds
        {
            get => _lastTrainedImageIds;
            set => SetProperty(ref _lastTrainedImageIds, value);
        }

        private List<string> _lastTrainedClasses = new();
        /// <summary>
        /// 마지막 학습에 실제로 들어간 클래스 이름 (순서 포함).
        ///
        /// <para>
        /// 이 데이터셋의 <see cref="Classes"/> 와 다를 수 있다 — 웹에서 받은 데이터셋으로 학습하면
        /// 클래스는 그 내보내기가 정본이다. 학습한 모델을 레지스트리에 올릴 때 보내는 값이라,
        /// 여기가 비어 있으면 그때만 <see cref="Classes"/> 로 되돌아간다.
        /// </para>
        /// </summary>
        public List<string> LastTrainedClasses
        {
            get => _lastTrainedClasses;
            set => SetProperty(ref _lastTrainedClasses, value);
        }

        private DateTime? _lastTrainedAt;
        /// <summary>마지막 학습 완료 시각. null 이면 한 번도 학습 안 됨.</summary>
        public DateTime? LastTrainedAt
        {
            get => _lastTrainedAt;
            set => SetProperty(ref _lastTrainedAt, value);
        }

        public int TotalImages => Images.Count;
        public int LabeledImages => Images.Count(i => i.IsLabeled);
        public int TotalLabels => Images.Sum(i => i.Labels.Count);
        public int TrainCount => Images.Count(i => i.Split == DataSplit.Train);
        public int ValidationCount => Images.Count(i => i.Split == DataSplit.Validation);

        /// <summary>마지막 학습에 포함됐고 현재도 데이터셋에 남아있는 이미지 수.</summary>
        public int TrainedImagesCount
        {
            get
            {
                if (LastTrainedImageIds.Count == 0) return 0;
                var trainedSet = new HashSet<string>(LastTrainedImageIds);
                return Images.Count(i => trainedSet.Contains(i.Id));
            }
        }

        /// <summary>학습 후 추가됐거나 한 번도 학습 안 된 이미지 수 (라벨 유무 무관).</summary>
        public int UnTrainedImagesCount
        {
            get
            {
                if (LastTrainedImageIds.Count == 0) return Images.Count;
                var trainedSet = new HashSet<string>(LastTrainedImageIds);
                return Images.Count(i => !trainedSet.Contains(i.Id));
            }
        }

        public void RefreshStatistics()
        {
            OnPropertyChanged(nameof(TotalImages));
            OnPropertyChanged(nameof(LabeledImages));
            OnPropertyChanged(nameof(TotalLabels));
            OnPropertyChanged(nameof(TrainCount));
            OnPropertyChanged(nameof(ValidationCount));
            OnPropertyChanged(nameof(TrainedImagesCount));
            OnPropertyChanged(nameof(UnTrainedImagesCount));
        }
    }
}
