using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace VMS.VisionSetup.Models.Annotation
{
    /// <summary>
    /// 데이터 분할 용도
    /// </summary>
    public enum DataSplit
    {
        Train,
        Validation,
        Test,
        Unassigned
    }

    /// <summary>
    /// 이미지 한 장에 대한 어노테이션 정보.
    /// 이미지 경로와 해당 이미지의 모든 라벨을 포함합니다.
    /// </summary>
    public class AnnotationImage : ObservableObject
    {
        private string _id = Guid.NewGuid().ToString();
        /// <summary>고유 ID</summary>
        public string Id
        {
            get => _id;
            set => SetProperty(ref _id, value);
        }

        private string _imagePath = string.Empty;
        /// <summary>원본 이미지 파일 경로 (데이터셋 루트 기준 상대 경로)</summary>
        public string ImagePath
        {
            get => _imagePath;
            set => SetProperty(ref _imagePath, value);
        }

        private int _imageWidth;
        /// <summary>이미지 너비 (px)</summary>
        public int ImageWidth
        {
            get => _imageWidth;
            set => SetProperty(ref _imageWidth, value);
        }

        private int _imageHeight;
        /// <summary>이미지 높이 (px)</summary>
        public int ImageHeight
        {
            get => _imageHeight;
            set => SetProperty(ref _imageHeight, value);
        }

        private DataSplit _split = DataSplit.Unassigned;
        /// <summary>학습/검증/테스트 분할</summary>
        public DataSplit Split
        {
            get => _split;
            set => SetProperty(ref _split, value);
        }

        private bool _isLabeled;
        /// <summary>라벨링 완료 여부</summary>
        public bool IsLabeled
        {
            get => _isLabeled;
            set => SetProperty(ref _isLabeled, value);
        }

        private ObservableCollection<LabelInfo> _labels = new();
        /// <summary>이 이미지의 어노테이션 목록</summary>
        public ObservableCollection<LabelInfo> Labels
        {
            get => _labels;
            set => SetProperty(ref _labels, value);
        }

        private DateTime _createdAt = DateTime.Now;
        /// <summary>등록 시간</summary>
        public DateTime CreatedAt
        {
            get => _createdAt;
            set => SetProperty(ref _createdAt, value);
        }

        private DateTime _modifiedAt = DateTime.Now;
        /// <summary>최종 수정 시간</summary>
        public DateTime ModifiedAt
        {
            get => _modifiedAt;
            set => SetProperty(ref _modifiedAt, value);
        }
    }
}
