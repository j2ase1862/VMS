using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace VMS.VisionSetup.Models.Annotation
{
    /// <summary>
    /// 딥러닝 학습용 데이터셋.
    /// 클래스 목록, 이미지별 어노테이션, 학습/검증 분할 정보를 관리합니다.
    /// </summary>
    public class AnnotationDataset : ObservableObject
    {
        private string _id = Guid.NewGuid().ToString();
        /// <summary>데이터셋 고유 ID</summary>
        public string Id
        {
            get => _id;
            set => SetProperty(ref _id, value);
        }

        private string _name = string.Empty;
        /// <summary>데이터셋 이름</summary>
        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }

        private string _description = string.Empty;
        /// <summary>데이터셋 설명</summary>
        public string Description
        {
            get => _description;
            set => SetProperty(ref _description, value);
        }

        private LabelType _taskType = LabelType.BoundingBox;
        /// <summary>데이터셋 작업 유형 (Detection / OCR 등)</summary>
        public LabelType TaskType
        {
            get => _taskType;
            set => SetProperty(ref _taskType, value);
        }

        private ObservableCollection<string> _classes = new();
        /// <summary>클래스 이름 목록</summary>
        public ObservableCollection<string> Classes
        {
            get => _classes;
            set => SetProperty(ref _classes, value);
        }

        private ObservableCollection<AnnotationImage> _images = new();
        /// <summary>어노테이션된 이미지 목록</summary>
        public ObservableCollection<AnnotationImage> Images
        {
            get => _images;
            set => SetProperty(ref _images, value);
        }

        private double _trainRatio = 0.8;
        /// <summary>학습 데이터 비율 (0~1, 기본 0.8)</summary>
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

        // ── 통계 (직렬화하지 않음, 런타임 계산) ──

        /// <summary>전체 이미지 수</summary>
        public int TotalImages => Images.Count;

        /// <summary>라벨링 완료 이미지 수</summary>
        public int LabeledImages => Images.Count(i => i.IsLabeled);

        /// <summary>전체 라벨 수</summary>
        public int TotalLabels => Images.Sum(i => i.Labels.Count);

        /// <summary>학습용 이미지 수</summary>
        public int TrainCount => Images.Count(i => i.Split == DataSplit.Train);

        /// <summary>검증용 이미지 수</summary>
        public int ValidationCount => Images.Count(i => i.Split == DataSplit.Validation);

        /// <summary>통계 속성 변경 알림 갱신</summary>
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
