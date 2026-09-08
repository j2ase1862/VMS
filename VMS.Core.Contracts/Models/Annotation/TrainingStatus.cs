using CommunityToolkit.Mvvm.ComponentModel;

namespace VMS.Core.Models.Annotation
{
    public enum TrainingState
    {
        Idle,
        Running,
        Completed,
        Failed,
        Cancelled
    }

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
        public double Progress
        {
            get => _progress;
            set => SetProperty(ref _progress, value);
        }

        private double _loss;
        public double Loss
        {
            get => _loss;
            set => SetProperty(ref _loss, value);
        }

        private double _accuracy;
        public double Accuracy
        {
            get => _accuracy;
            set => SetProperty(ref _accuracy, value);
        }

        private string _onnxOutputPath = string.Empty;
        public string OnnxOutputPath
        {
            get => _onnxOutputPath;
            set => SetProperty(ref _onnxOutputPath, value);
        }

        private string _message = string.Empty;
        public string Message
        {
            get => _message;
            set => SetProperty(ref _message, value);
        }
    }
}
