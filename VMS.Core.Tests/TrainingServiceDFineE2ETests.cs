using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using VMS.Core.Models.Annotation;
using VMS.Core.Services;
using Xunit;

namespace VMS.Core.Tests
{
    /// <summary>
    /// DeepLearning 앱의 [Start Training] 이 호출하는 TrainingService 를 앱과 같은 TrainingConfig 로 실제 실행하는
    /// 종단 테스트 — python 탐색(DetectPython) → 인자 조립(BuildArguments) → train_dfine.py → stdout 프로토콜 파싱 →
    /// ONNX 산출까지. 실제 GPU·파이썬 환경이 필요하므로 환경 변수 VMS_DFINE_E2E=1 일 때만 실행된다 (CI 스킵).
    ///   VMS_DFINE_E2E_DATASET    데이터셋 폴더 (기본: 리포의 VMS.DeepLearning/scripts 샘플)
    ///   VMS_DFINE_E2E_PRETRAINED 사전학습 모델 ID/폴더 (기본: 빈 값 = 스크립트 기본 HF 다운로드)
    ///   VMS_DFINE_E2E_EPOCHS     에폭 수 (기본 3)
    ///   VMS_DFINE_E2E_LOG        학습 로그를 저장할 파일 경로 (선택)
    /// </summary>
    public class TrainingServiceDFineE2ETests
    {
        [Fact]
        public async Task App_training_service_runs_train_dfine_and_produces_onnx()
        {
            if (Environment.GetEnvironmentVariable("VMS_DFINE_E2E") != "1")
                return; // 실환경 전용

            string repoRoot = FindRepoRoot();
            string dataset = Environment.GetEnvironmentVariable("VMS_DFINE_E2E_DATASET")
                             ?? Path.Combine(repoRoot, "VMS.DeepLearning", "scripts");
            string script = Path.Combine(repoRoot, "VMS.DeepLearning", "scripts", "train_dfine.py");
            string output = Path.Combine(Path.GetTempPath(), "vms-dfine-e2e-" + Guid.NewGuid().ToString("N"));
            int epochs = int.TryParse(Environment.GetEnvironmentVariable("VMS_DFINE_E2E_EPOCHS"), out var e) ? e : 3;

            Assert.True(File.Exists(script), $"학습 스크립트 없음: {script}");
            Assert.True(File.Exists(Path.Combine(dataset, "data.yaml")), $"data.yaml 없음: {dataset}");

            // 앱과 동일: TrainingConfig 기본값(DetectPython, lr 0.001, batch 8, ExportOnnx, 증강 기본값) 위에 경로만 지정
            var config = new TrainingConfig
            {
                TrainingScriptPath = script,
                DatasetPath = dataset,
                OutputDir = output,
                Epochs = epochs,
                BatchSize = 2,
                PretrainedModelPath = Environment.GetEnvironmentVariable("VMS_DFINE_E2E_PRETRAINED") ?? string.Empty,
                Target = TrainingTarget.YoloDetection, // ViewModel 이 Detection 데이터셋에 설정하는 값
            };

            var service = new TrainingService();
            var log = new List<string>();
            var epochsSeen = new List<int>();
            service.LogReceived += (_, line) => log.Add(line);
            service.StatusChanged += (_, s) => { if (s.CurrentEpoch > 0 && !epochsSeen.Contains(s.CurrentEpoch)) epochsSeen.Add(s.CurrentEpoch); };

            try
            {
                await service.StartTrainingAsync(config);
            }
            finally
            {
                var logPath = Environment.GetEnvironmentVariable("VMS_DFINE_E2E_LOG");
                if (!string.IsNullOrEmpty(logPath))
                    File.WriteAllLines(logPath, log);
            }

            Assert.Contains(log, l => l.Contains("train_dfine.py") && l.Contains("--hsv_v"));   // 앱 인자 조립: hsv_* 가 dfine 에 전달
            Assert.True(service.Status.State == TrainingState.Completed,
                $"State={service.Status.State} Message={service.Status.Message}\n" + string.Join("\n", log));
            Assert.Equal(epochs, service.Status.TotalEpochs);
            Assert.Equal(epochs, epochsSeen.Count);                                             // [EPOCH] n/N 파싱
            Assert.Equal(100, service.Status.Progress);
            Assert.False(string.IsNullOrEmpty(service.Status.OnnxOutputPath), "ONNX 경로 미수신");
            Assert.True(File.Exists(service.Status.OnnxOutputPath), $"ONNX 파일 없음: {service.Status.OnnxOutputPath}");
            Assert.True(new FileInfo(service.Status.OnnxOutputPath).Length > 10_000_000, "ONNX 크기 비정상");
            Assert.Contains(log, l => l.Contains("ONNX 검증 OK"));                              // 스크립트의 onnxruntime 자체 검증
        }

        private static string FindRepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "VMS.sln")))
                dir = dir.Parent;
            return dir?.FullName ?? throw new DirectoryNotFoundException("VMS.sln 을 찾을 수 없음");
        }
    }
}
