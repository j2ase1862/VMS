using CommunityToolkit.Mvvm.ComponentModel;

namespace VMS.VisionSetup.Models.Annotation
{
    /// <summary>
    /// 학습 진행 상태
    /// </summary>
    public enum TrainingState
    {
        Idle,
        Running,
        Completed,
        Failed,
        Cancelled
    }

    /// <summary>
    /// 학습 진행률 및 메트릭 정보.
    /// Python 프로세스의 stdout에서 파싱한 실시간 학습 상태입니다.
    /// </summary>
    public class TrainingStatus : ObservableObject
    {
        private TrainingState _state = TrainingState.Idle;
        public TrainingState State
        {
            get => _state;
            set => SetProperty(ref _state, value);
        }

        private int _currentEpoch;
        public int CurrentEpoch
        {
            get => _currentEpoch;
            set => SetProperty(ref _currentEpoch, value);
        }

        private int _totalEpochs;
        public int TotalEpochs
        {
            get => _totalEpochs;
            set => SetProperty(ref _totalEpochs, value);
        }

        private double _progress;
        /// <summary>진행률 (0~100)</summary>
        public double Progress
        {
            get => _progress;
            set => SetProperty(ref _progress, value);
        }

        private double _loss;
        /// <summary>현재 Loss 값</summary>
        public double Loss
        {
            get => _loss;
            set => SetProperty(ref _loss, value);
        }

        private double _accuracy;
        /// <summary>현재 Accuracy (0~1)</summary>
        public double Accuracy
        {
            get => _accuracy;
            set => SetProperty(ref _accuracy, value);
        }

        private string _onnxOutputPath = string.Empty;
        /// <summary>학습 완료 후 생성된 ONNX 모델 경로</summary>
        public string OnnxOutputPath
        {
            get => _onnxOutputPath;
            set => SetProperty(ref _onnxOutputPath, value);
        }

        private string _message = string.Empty;
        /// <summary>상태 메시지</summary>
        public string Message
        {
            get => _message;
            set => SetProperty(ref _message, value);
        }
    }
}
