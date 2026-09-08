using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using VMS.Core.Interfaces;
using VMS.Core.Models.Annotation;

namespace VMS.Core.Services
{
    public class TrainingService : ITrainingService
    {
        private Process? _trainingProcess;
        private CancellationTokenSource? _cts;

        public TrainingStatus Status { get; } = new();
        public bool IsTraining => Status.State == TrainingState.Running;

        public event EventHandler<string>? LogReceived;
        public event EventHandler<TrainingStatus>? StatusChanged;

        public async Task StartTrainingAsync(TrainingConfig config, CancellationToken cancellationToken = default)
        {
            if (IsTraining)
                throw new InvalidOperationException("학습이 이미 진행 중입니다.");

            ValidateConfig(config);

            Status.State = TrainingState.Running;
            Status.CurrentEpoch = 0;
            Status.TotalEpochs = config.Epochs;
            Status.Progress = 0;
            Status.Loss = 0;
            Status.Accuracy = 0;
            Status.OnnxOutputPath = string.Empty;
            Status.Message = "학습 시작 중...";
            StatusChanged?.Invoke(this, Status);

            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            try
            {
                string arguments = TrainingArgumentBuilder.Build(config);
                Log($"실행: {config.PythonPath} {arguments}");

                var startInfo = new ProcessStartInfo
                {
                    FileName = config.PythonPath,
                    Arguments = arguments,
                    WorkingDirectory = Path.GetDirectoryName(config.TrainingScriptPath) ?? ".",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = System.Text.Encoding.UTF8,
                    StandardErrorEncoding = System.Text.Encoding.UTF8
                };
                startInfo.Environment["PYTHONIOENCODING"] = "utf-8";

                _trainingProcess = new Process { StartInfo = startInfo };
                _trainingProcess.Start();

                var stdoutTask = ReadOutputAsync(_trainingProcess.StandardOutput, isError: false, _cts.Token);
                var stderrTask = ReadOutputAsync(_trainingProcess.StandardError, isError: true, _cts.Token);

                await _trainingProcess.WaitForExitAsync(_cts.Token);
                await Task.WhenAll(stdoutTask, stderrTask);

                int exitCode = _trainingProcess.ExitCode;

                if (_cts.Token.IsCancellationRequested)
                {
                    Status.State = TrainingState.Cancelled;
                    Status.Message = "학습이 취소되었습니다.";
                }
                else if (exitCode == 0)
                {
                    Status.State = TrainingState.Completed;
                    Status.Progress = 100;
                    Status.Message = string.IsNullOrEmpty(Status.OnnxOutputPath)
                        ? "학습 완료."
                        : $"학습 완료. ONNX: {Status.OnnxOutputPath}";
                }
                else
                {
                    Status.State = TrainingState.Failed;
                    Status.Message = $"학습 실패 (exit code: {exitCode}).";
                }
            }
            catch (OperationCanceledException)
            {
                Status.State = TrainingState.Cancelled;
                Status.Message = "학습이 취소되었습니다.";
            }
            catch (Exception ex)
            {
                Status.State = TrainingState.Failed;
                Status.Message = $"학습 오류: {ex.Message}";
                Log($"[ERROR] {ex.Message}");
            }
            finally
            {
                StatusChanged?.Invoke(this, Status);
                CleanupProcess();
            }
        }

        public void StopTraining()
        {
            if (!IsTraining) return;

            Log("학습 중지 요청...");
            _cts?.Cancel();

            try
            {
                if (_trainingProcess != null && !_trainingProcess.HasExited)
                {
                    _trainingProcess.Kill(entireProcessTree: true);
                    Log("프로세스 종료됨.");
                }
            }
            catch (Exception ex)
            {
                Log($"프로세스 종료 실패: {ex.Message}");
            }
        }

        #region Process Management

        private async Task ReadOutputAsync(StreamReader reader, bool isError, CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    string? line = await reader.ReadLineAsync(ct);
                    if (line == null) break;

                    Log(isError ? $"[STDERR] {line}" : line);

                    if (!isError)
                        ParseOutputLine(line);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Log($"[READ ERROR] {ex.Message}");
            }
        }

        private void ParseOutputLine(string line)
        {
            // stdout 프로토콜 해석은 TrainingOutputParser(학습 워커와 공용) — 여기서는 이벤트 발행만
            var kind = TrainingOutputParser.Apply(line, Status);
            switch (kind)
            {
                case TrainingOutputKind.Epoch:
                case TrainingOutputKind.Loss:
                case TrainingOutputKind.Accuracy:
                case TrainingOutputKind.Progress:
                    StatusChanged?.Invoke(this, Status);
                    break;
                case TrainingOutputKind.Onnx:
                    Log($"ONNX 모델 생성: {Status.OnnxOutputPath}");
                    StatusChanged?.Invoke(this, Status);
                    break;
                case TrainingOutputKind.Done:
                case TrainingOutputKind.Error:
                case TrainingOutputKind.None:
                    break;
            }
        }

        private static void ValidateConfig(TrainingConfig config)
        {
            if (string.IsNullOrEmpty(config.TrainingScriptPath) || !File.Exists(config.TrainingScriptPath))
                throw new FileNotFoundException($"학습 스크립트를 찾을 수 없습니다: {config.TrainingScriptPath}");

            if (string.IsNullOrEmpty(config.DatasetPath) || !Directory.Exists(config.DatasetPath))
                throw new DirectoryNotFoundException($"데이터셋 폴더를 찾을 수 없습니다: {config.DatasetPath}");

            if (string.IsNullOrEmpty(config.OutputDir))
                throw new ArgumentException("출력 디렉토리를 지정하세요.");
        }

        private void CleanupProcess()
        {
            _trainingProcess?.Dispose();
            _trainingProcess = null;
            _cts?.Dispose();
            _cts = null;
        }

        private void Log(string message)
        {
            LogReceived?.Invoke(this, message);
            System.Diagnostics.Debug.WriteLine($"[Training] {message}");
        }

        #endregion
    }
}
