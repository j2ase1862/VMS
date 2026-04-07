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
    }
}
