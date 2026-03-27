using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using VMS.VisionSetup.Interfaces;
using VMS.VisionSetup.Models.Annotation;

namespace VMS.VisionSetup.Services
{
    /// <summary>
    /// 딥러닝 학습 프로세스 관리 서비스.
    ///
    /// Python 학습 스크립트를 외부 프로세스로 실행하고,
    /// stdout의 JSON/텍스트 프로토콜을 파싱하여 진행률을 추적합니다.
    ///
    /// 기대하는 stdout 프로토콜:
    ///   [EPOCH] 5/100               → 에폭 진행
    ///   [LOSS] 0.0234               → 현재 Loss
    ///   [ACC] 0.9512                → 현재 Accuracy
    ///   [PROGRESS] 45.5             → 전체 진행률 (%)
    ///   [ONNX] C:\output\model.onnx → ONNX 변환 완료 경로
    ///   [DONE]                      → 학습 완료
    ///   [ERROR] message             → 오류 발생
    /// </summary>
    public class TrainingService : ITrainingService
    {
        private static readonly Lazy<TrainingService> _instance = new(() => new TrainingService());
        public static TrainingService Instance => _instance.Value;

        private Process? _trainingProcess;
        private CancellationTokenSource? _cts;

        public TrainingStatus Status { get; } = new();
        public bool IsTraining => Status.State == TrainingState.Running;

        public event EventHandler<string>? LogReceived;
        public event EventHandler<TrainingStatus>? StatusChanged;

        // stdout 파싱 패턴
        private static readonly Regex EpochPattern = new(@"\[EPOCH\]\s*(\d+)\s*/\s*(\d+)", RegexOptions.Compiled);
        private static readonly Regex LossPattern = new(@"\[LOSS\]\s*([\d.]+)", RegexOptions.Compiled);
        private static readonly Regex AccPattern = new(@"\[ACC\]\s*([\d.]+)", RegexOptions.Compiled);
        private static readonly Regex ProgressPattern = new(@"\[PROGRESS\]\s*([\d.]+)", RegexOptions.Compiled);
        private static readonly Regex OnnxPattern = new(@"\[ONNX\]\s*(.+)", RegexOptions.Compiled);
        private static readonly Regex ErrorPattern = new(@"\[ERROR\]\s*(.+)", RegexOptions.Compiled);

        private TrainingService() { }

        public async Task StartTrainingAsync(TrainingConfig config, CancellationToken cancellationToken = default)
        {
            if (IsTraining)
                throw new InvalidOperationException("학습이 이미 진행 중입니다.");

            ValidateConfig(config);

            // 상태 초기화
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
                string arguments = BuildArguments(config);
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

                _trainingProcess = new Process { StartInfo = startInfo };
                _trainingProcess.Start();

                // stdout/stderr 비동기 읽기
                var stdoutTask = ReadOutputAsync(_trainingProcess.StandardOutput, isError: false, _cts.Token);
                var stderrTask = ReadOutputAsync(_trainingProcess.StandardError, isError: true, _cts.Token);

                // 프로세스 종료 대기
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
                    if (line == null) break; // EOF

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
            Match m;

            // [EPOCH] 5/100
            m = EpochPattern.Match(line);
            if (m.Success)
            {
                Status.CurrentEpoch = int.Parse(m.Groups[1].Value);
                Status.TotalEpochs = int.Parse(m.Groups[2].Value);
                if (Status.TotalEpochs > 0)
                    Status.Progress = (double)Status.CurrentEpoch / Status.TotalEpochs * 100;
                Status.Message = $"Epoch {Status.CurrentEpoch}/{Status.TotalEpochs}";
                StatusChanged?.Invoke(this, Status);
                return;
            }

            // [LOSS] 0.0234
            m = LossPattern.Match(line);
            if (m.Success)
            {
                Status.Loss = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                StatusChanged?.Invoke(this, Status);
                return;
            }

            // [ACC] 0.9512
            m = AccPattern.Match(line);
            if (m.Success)
            {
                Status.Accuracy = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                StatusChanged?.Invoke(this, Status);
                return;
            }

            // [PROGRESS] 45.5
            m = ProgressPattern.Match(line);
            if (m.Success)
            {
                Status.Progress = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                StatusChanged?.Invoke(this, Status);
                return;
            }

            // [ONNX] path
            m = OnnxPattern.Match(line);
            if (m.Success)
            {
                Status.OnnxOutputPath = m.Groups[1].Value.Trim();
                Log($"ONNX 모델 생성: {Status.OnnxOutputPath}");
                StatusChanged?.Invoke(this, Status);
                return;
            }

            // [DONE]
            if (line.Contains("[DONE]"))
            {
                Status.Progress = 100;
                return;
            }

            // [ERROR]
            m = ErrorPattern.Match(line);
            if (m.Success)
            {
                Status.Message = m.Groups[1].Value.Trim();
                return;
            }
        }

        private static string BuildArguments(TrainingConfig config)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append($"\"{config.TrainingScriptPath}\"");
            sb.Append($" --target {config.Target.ToString().ToLower()}");
            sb.Append($" --dataset \"{config.DatasetPath}\"");
            sb.Append($" --output \"{config.OutputDir}\"");
            sb.Append($" --epochs {config.Epochs}");
            sb.Append($" --lr {config.LearningRate.ToString(CultureInfo.InvariantCulture)}");
            sb.Append($" --batch_size {config.BatchSize}");

            if (!string.IsNullOrEmpty(config.PretrainedModelPath))
                sb.Append($" --pretrained \"{config.PretrainedModelPath}\"");

            if (config.ExportOnnx)
                sb.Append(" --export_onnx");

            return sb.ToString();
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
