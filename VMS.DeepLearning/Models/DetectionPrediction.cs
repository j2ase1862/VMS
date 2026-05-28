namespace VMS.DeepLearning.Models
{
    /// <summary>
    /// 학습된 YOLO ONNX 모델이 예측한 단일 객체.
    /// VMS.DeepLearning 앱 내 추론 결과 표시용. VMS.VisionSetup의 DetectionResult와 별도 — 의존성 분리.
    /// </summary>
    public class DetectionPrediction
    {
        /// <summary>좌상단 X (원본 이미지 좌표)</summary>
        public int X { get; set; }

        /// <summary>좌상단 Y (원본 이미지 좌표)</summary>
        public int Y { get; set; }

        public int Width { get; set; }
        public int Height { get; set; }

        public int ClassId { get; set; }

        /// <summary>모델 메타데이터에서 추출한 클래스 이름. 없으면 ClassId 문자열.</summary>
        public string ClassName { get; set; } = string.Empty;

        /// <summary>0~1 신뢰도</summary>
        public float Confidence { get; set; }
    }
}
