using CommunityToolkit.Mvvm.ComponentModel;
using OpenCvSharp;
using System;
using System.Collections.Generic;

namespace VMS.VisionSetup.Models.Annotation
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
        TextLine
    }

    /// <summary>
    /// 개별 어노테이션 라벨 정보.
    /// 이미지 내 하나의 객체/텍스트 영역을 나타냅니다.
    /// </summary>
    public class LabelInfo : ObservableObject
    {
        private string _id = Guid.NewGuid().ToString();
        /// <summary>고유 ID</summary>
        public string Id
        {
            get => _id;
            set => SetProperty(ref _id, value);
        }

        private string _className = string.Empty;
        /// <summary>클래스 이름 (예: "text", "defect", "OK", "NG")</summary>
        public string ClassName
        {
            get => _className;
            set => SetProperty(ref _className, value);
        }

        private LabelType _labelType = LabelType.BoundingBox;
        /// <summary>어노테이션 타입</summary>
        public LabelType LabelType
        {
            get => _labelType;
            set => SetProperty(ref _labelType, value);
        }

        private Rect _boundingBox;
        /// <summary>바운딩 박스 (픽셀 좌표)</summary>
        public Rect BoundingBox
        {
            get => _boundingBox;
            set => SetProperty(ref _boundingBox, value);
        }

        private List<Point2d> _points = new();
        /// <summary>폴리곤 포인트 (BoundingBox의 4점 또는 자유 폴리곤)</summary>
        public List<Point2d> Points
        {
            get => _points;
            set => SetProperty(ref _points, value);
        }

        private string _transcription = string.Empty;
        /// <summary>텍스트 정답 (OCR 학습용). TextLine 타입에서 사용.</summary>
        public string Transcription
        {
            get => _transcription;
            set => SetProperty(ref _transcription, value);
        }

        private double _confidence;
        /// <summary>오토 라벨링 시 모델 신뢰도 (0~1). 수동 라벨링이면 0.</summary>
        public double Confidence
        {
            get => _confidence;
            set => SetProperty(ref _confidence, value);
        }

        private bool _isVerified;
        /// <summary>사용자가 검토/확인 완료 여부</summary>
        public bool IsVerified
        {
            get => _isVerified;
            set => SetProperty(ref _isVerified, value);
        }
    }
}
