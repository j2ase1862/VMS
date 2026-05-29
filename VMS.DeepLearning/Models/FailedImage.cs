using System.Collections.Generic;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;

namespace VMS.DeepLearning.Models
{
    /// <summary>
    /// 외부 테스트 폴더의 이미지에 대한 일괄 추론 결과.
    /// Active Learning 워크플로우에서 모델이 실패한 이미지를 식별/선별/데이터셋 추가에 사용.
    /// </summary>
    public partial class FailedImage : ObservableObject
    {
        /// <summary>원본 이미지 절대 경로 (테스트 폴더 내).</summary>
        public string SourcePath { get; set; } = string.Empty;

        public string FileName => Path.GetFileName(SourcePath);

        /// <summary>
        /// 모델이 본 raw 최고 confidence (NMS·threshold 무관). 0 = 후보 자체가 없음.
        /// 실패 판정 기준: FailureThreshold 미만이면 모델이 "못 봤다"고 간주.
        /// </summary>
        public float MaxConfidence { get; set; }

        /// <summary>임계값 통과한 검출 개수 (NMS 후).</summary>
        public int DetectionCount { get; set; }

        /// <summary>NMS 후 예측 박스. Add to Dataset 시 초기 라벨로 사용 가능.</summary>
        public List<DetectionPrediction> Predictions { get; set; } = new();

        [ObservableProperty]
        private bool _isSelected;
    }
}
