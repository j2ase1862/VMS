using CommunityToolkit.Mvvm.ComponentModel;

namespace VMS.VisionSetup.Models.Annotation
{
    /// <summary>
    /// 학습 대상 모델 종류
    /// </summary>
    public enum TrainingTarget
    {
        /// <summary>텍스트 검출 (Detection) 모델</summary>
        Detection,
        /// <summary>텍스트 인식 (Recognition) 모델</summary>
        Recognition
    }

    /// <summary>
    /// 딥러닝 학습 설정.
    /// Python 학습 스크립트에 전달할 하이퍼파라미터를 관리합니다.
    /// </summary>
    public class TrainingConfig : ObservableObject
    {
        private TrainingTarget _target = TrainingTarget.Recognition;
        /// <summary>학습 대상 (Detection / Recognition)</summary>
        public TrainingTarget Target
        {
            get => _target;
            set => SetProperty(ref _target, value);
        }

        private string _datasetPath = string.Empty;
        /// <summary>학습 데이터셋 경로 (Export된 PaddleOCR 포맷 폴더)</summary>
        public string DatasetPath
        {
            get => _datasetPath;
            set => SetProperty(ref _datasetPath, value);
        }

        private string _pretrainedModelPath = string.Empty;
        /// <summary>사전학습 모델 경로 (Fine-tuning 기반 모델)</summary>
        public string PretrainedModelPath
        {
            get => _pretrainedModelPath;
            set => SetProperty(ref _pretrainedModelPath, value);
        }

        private string _outputDir = string.Empty;
        /// <summary>학습 결과 출력 디렉토리</summary>
        public string OutputDir
        {
            get => _outputDir;
            set => SetProperty(ref _outputDir, value);
        }

        private int _epochs = 100;
        /// <summary>학습 에폭 수</summary>
        public int Epochs
        {
            get => _epochs;
            set => SetProperty(ref _epochs, value);
        }

        private double _learningRate = 0.001;
        /// <summary>학습률</summary>
        public double LearningRate
        {
            get => _learningRate;
            set => SetProperty(ref _learningRate, value);
        }

        private int _batchSize = 8;
        /// <summary>배치 크기</summary>
        public int BatchSize
        {
            get => _batchSize;
            set => SetProperty(ref _batchSize, value);
        }

        private string _pythonPath = "python";
        /// <summary>Python 실행 경로 (예: python, python3, C:\Anaconda3\python.exe)</summary>
        public string PythonPath
        {
            get => _pythonPath;
            set => SetProperty(ref _pythonPath, value);
        }

        private string _trainingScriptPath = string.Empty;
        /// <summary>학습 스크립트 경로 (.py)</summary>
        public string TrainingScriptPath
        {
            get => _trainingScriptPath;
            set => SetProperty(ref _trainingScriptPath, value);
        }

        private bool _exportOnnx = true;
        /// <summary>학습 완료 후 자동 ONNX 변환</summary>
        public bool ExportOnnx
        {
            get => _exportOnnx;
            set => SetProperty(ref _exportOnnx, value);
        }
    }
}
