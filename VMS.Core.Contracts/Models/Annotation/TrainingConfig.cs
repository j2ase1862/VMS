using System;
using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;

namespace VMS.Core.Models.Annotation
{
    public enum TrainingTarget
    {
        /// <summary>텍스트 검출 모델</summary>
        Detection,
        /// <summary>텍스트 인식 모델</summary>
        Recognition,
        /// <summary>YOLO 객체 검출 모델</summary>
        YoloDetection,
        /// <summary>이미지 분류 모델</summary>
        Classification,
        /// <summary>이상 탐지 모델</summary>
        AnomalyDetection,
        /// <summary>YOLO 인스턴스 세그멘테이션 모델</summary>
        YoloSegmentation
    }

    public class TrainingConfig : ObservableObject
    {
        private TrainingTarget _target = TrainingTarget.Recognition;
        public TrainingTarget Target
        {
            get => _target;
            set => SetProperty(ref _target, value);
        }

        private string _datasetPath = string.Empty;
        public string DatasetPath
        {
            get => _datasetPath;
            set => SetProperty(ref _datasetPath, value);
        }

        private string _pretrainedModelPath = string.Empty;
        public string PretrainedModelPath
        {
            get => _pretrainedModelPath;
            set => SetProperty(ref _pretrainedModelPath, value);
        }

        private string _outputDir = string.Empty;
        public string OutputDir
        {
            get => _outputDir;
            set => SetProperty(ref _outputDir, value);
        }

        private int _epochs = 100;
        public int Epochs
        {
            get => _epochs;
            set => SetProperty(ref _epochs, value);
        }

        private double _learningRate = 0.001;
        public double LearningRate
        {
            get => _learningRate;
            set => SetProperty(ref _learningRate, value);
        }

        private int _batchSize = 8;
        public int BatchSize
        {
            get => _batchSize;
            set => SetProperty(ref _batchSize, value);
        }

        private string _pythonPath = DetectPython();
        public string PythonPath
        {
            get => _pythonPath;
            set => SetProperty(ref _pythonPath, value);
        }

        private static string DetectPython()
        {
            // py launcher로 3.12 → 3.11 → 3.10 순서로 torch 설치된 Python 탐색
            string[] versions = ["3.12", "3.11", "3.10"];
            foreach (var ver in versions)
            {
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = "py",
                        Arguments = $"-{ver} -c \"import torch; import sys; print(sys.executable)\"",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    };
                    using var proc = Process.Start(psi);
                    if (proc == null) continue;
                    string output = proc.StandardOutput.ReadToEnd().Trim();
                    proc.WaitForExit(5000);
                    if (proc.ExitCode == 0 && File.Exists(output))
                        return output;
                }
                catch { }
            }
            return "python";
        }

        private string _trainingScriptPath = string.Empty;
        public string TrainingScriptPath
        {
            get => _trainingScriptPath;
            set => SetProperty(ref _trainingScriptPath, value);
        }

        private bool _exportOnnx = true;
        public bool ExportOnnx
        {
            get => _exportOnnx;
            set => SetProperty(ref _exportOnnx, value);
        }

        // ─────────────── Data Augmentation (YOLO) ───────────────

        private double _mosaic = 1.0;
        /// <summary>Mosaic 증강 확률 (0.0~1.0). Ultralytics 기본 1.0. 작은 객체 검출 향상에 효과적.</summary>
        public double Mosaic
        {
            get => _mosaic;
            set => SetProperty(ref _mosaic, value);
        }

        private double _mixup;
        /// <summary>Mixup 증강 확률 (0.0~1.0). 기본 0.0. 분류 분기 강건화.</summary>
        public double Mixup
        {
            get => _mixup;
            set => SetProperty(ref _mixup, value);
        }

        private double _hsvH = 0.015;
        /// <summary>Hue 증강 범위 (0.0~1.0). 기본 0.015. 카메라/조명 색감 차이 대응.</summary>
        public double HsvH
        {
            get => _hsvH;
            set => SetProperty(ref _hsvH, value);
        }

        private double _hsvS = 0.7;
        /// <summary>Saturation 증강 범위 (0.0~1.0). 기본 0.7. 색 포화도 변동 대응.</summary>
        public double HsvS
        {
            get => _hsvS;
            set => SetProperty(ref _hsvS, value);
        }

        private double _hsvV = 0.4;
        /// <summary>Value(Brightness) 증강 범위 (0.0~1.0). 기본 0.4. 공장 조명 변동 대응에 가장 중요.</summary>
        public double HsvV
        {
            get => _hsvV;
            set => SetProperty(ref _hsvV, value);
        }

        // ─────────────── Anomaly (PatchCore) ───────────────

        private string _anomalyMethod = "patchcore";
        /// <summary>Anomaly Detection 모델 종류 (patchcore / fastflow / efficient_ad)</summary>
        public string AnomalyMethod
        {
            get => _anomalyMethod;
            set => SetProperty(ref _anomalyMethod, value);
        }

        private string _anomalyBackbone = "resnet18";
        /// <summary>PatchCore 백본 네트워크 (resnet18 / resnet50 / wide_resnet50_2).
        /// 깊은 백본일수록 특징 표현력↑ + 메모리/시간↑. 미세 결함에는 resnet50 권장.</summary>
        public string AnomalyBackbone
        {
            get => _anomalyBackbone;
            set => SetProperty(ref _anomalyBackbone, value);
        }

        private double _coresetRatio = 0.1;
        /// <summary>Coreset 샘플링 비율 (0.01~1.0). 기본 0.1 (10%).
        /// 값↑ = 더 많은 특징 보유 → 미검(FN) 감소, 메모리↑. 값↓ = 빠르고 경량화.</summary>
        public double CoresetRatio
        {
            get => _coresetRatio;
            set => SetProperty(ref _coresetRatio, value);
        }
    }
}
