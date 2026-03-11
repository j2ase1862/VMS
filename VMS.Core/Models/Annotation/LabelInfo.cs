using CommunityToolkit.Mvvm.ComponentModel;
using OpenCvSharp;
using System;
using System.Collections.Generic;

namespace VMS.Core.Models.Annotation
{
    /// <summary>
    /// 어노테이션 타입
    /// </summary>
    public enum LabelType
    {
        /// <summary>바운딩 박스 (Detection용)</summary>
        BoundingBox,

        /// <summary>폴리곤 (Segmentation용)</summary>
        Polygon,

        /// <summary>텍스트 라인 (OCR Recognition용)</summary>
        TextLine,

        /// <summary>이미지 단위 분류 (Classification용)</summary>
        ImageClass,

        /// <summary>정상/이상 마킹 (Anomaly Detection용)</summary>
        AnomalyMask
    }

    /// <summary>
    /// 데이터셋 작업 유형 (ViDi Tool 대응)
    /// </summary>
    public enum DatasetTaskType
    {
        /// <summary>Locate — 객체 검출 (YOLO)</summary>
        Detection,

        /// <summary>Classify — 이미지 분류 (Green)</summary>
        Classification,

        /// <summary>Read — OCR 텍스트 인식</summary>
        OCR,

        /// <summary>Analyze — 이상 탐지 (Red)</summary>
        AnomalyDetection
    }

    /// <summary>
    /// 개별 어노테이션 라벨 정보.
    /// 이미지 내 하나의 객체/텍스트 영역을 나타냅니다.
    /// </summary>
    public class LabelInfo : ObservableObject
    {
        private string _id = Guid.NewGuid().ToString();
        public string Id
        {
            get => _id;
            set => SetProperty(ref _id, value);
        }

        private string _className = string.Empty;
        public string ClassName
        {
            get => _className;
            set => SetProperty(ref _className, value);
        }

        private LabelType _labelType = LabelType.BoundingBox;
        public LabelType LabelType
        {
            get => _labelType;
            set => SetProperty(ref _labelType, value);
        }

        private Rect _boundingBox;
        public Rect BoundingBox
        {
            get => _boundingBox;
            set => SetProperty(ref _boundingBox, value);
        }

        private List<Point2d> _points = new();
        public List<Point2d> Points
        {
            get => _points;
            set => SetProperty(ref _points, value);
        }

        private string _transcription = string.Empty;
        public string Transcription
        {
            get => _transcription;
            set => SetProperty(ref _transcription, value);
        }

        private double _confidence;
        public double Confidence
        {
            get => _confidence;
            set => SetProperty(ref _confidence, value);
        }

        private bool _isVerified;
        public bool IsVerified
        {
            get => _isVerified;
            set => SetProperty(ref _isVerified, value);
        }
    }
}
