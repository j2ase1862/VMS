using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace VMS.VisionSetup.Services.SynthData
{
    public class TrainingConfig
    {
        public string PythonPath { get; set; } = "python";
        public string ScriptPath { get; set; } = string.Empty; // train_ppocr.py 경로
        public string DatasetDir { get; set; } = string.Empty;
        public string OutputDir { get; set; } = string.Empty;
        public string PretrainedModel { get; set; } = string.Empty;
        public int Epochs { get; set; } = 50;
        public int BatchSize { get; set; } = 8;
        public double LearningRate { get; set; } = 0.001;
        public string Target { get; set; } = "recognition"; // or "detection"
        public bool ExportOnnx { get; set; } = true;
    }

    public class TrainingEventArgs : EventArgs
    {
        public string Line { get; set; } = string.Empty;
        public int? EpochCurrent { get; set; }
        public int? EpochTotal { get; set; }
        public double? Progress { get; set; }   // 0~100
        public double? Loss { get; set; }
        public double? Accuracy { get; set; }
        public string? OnnxPath { get; set; }
        public bool Done { get; set; }
        public string? Error { get; set; }
    }

    /// <summary>
    /// train_ppocr.py를 subprocess로 실행하고 stdout 프로토콜을 파싱.
    /// 이벤트: Output(매 라인), Progress(파싱된 메트릭). Cancel 시 프로세스 종료.
    /// </summary>
    public class PaddleOcrTrainingService
    {
        public event EventHandler<TrainingEventArgs>? Output;

        public string? ResolvedOnnxPath { get; private set; }

        /// <summary>
        /// 기본 train_ppocr.py 경로 — VMS.VisionSetup 실행 폴더 옆 scripts/.
        /// </summary>
        public static string GetDefaultScriptPath()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string[] candidates =
            {
                Path.Combine(baseDir, "scripts", "train_ppocr.py"),
                Path.Combine(baseDir, "..", "..", "..", "scripts", "train_ppocr.py"),
                Path.Combine(baseDir, "..", "..", "..", "..", "VMS.VisionSetup", "scripts", "train_ppocr.py"),
            };
            foreach (var p in candidates)
                if (File.Exists(p)) return Path.GetFullPath(p);
            return candidates[0];
        }

        public async Task<bool> RunAsync(TrainingConfig cfg, CancellationToken ct = default)
        {
            ResolvedOnnxPath = null;

            if (!File.Exists(cfg.ScriptPath))
            {
                Emit(new TrainingEventArgs { Error = $"학습 스크립트를 찾을 수 없습니다: {cfg.ScriptPath}" });
                return false;
            }
            if (!Directory.Exists(cfg.DatasetDir))
            {
                Emit(new TrainingEventArgs { Error = $"데이터셋 폴더가 없습니다: {cfg.DatasetDir}" });
                return false;
            }

            Directory.CreateDirectory(cfg.OutputDir);

            var psi = new ProcessStartInfo
            {
                FileName = cfg.PythonPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };
            psi.ArgumentList.Add(cfg.ScriptPath);
            psi.ArgumentList.Add("--target"); psi.ArgumentList.Add(cfg.Target);
            psi.ArgumentList.Add("--dataset"); psi.ArgumentList.Add(cfg.DatasetDir);
            psi.ArgumentList.Add("--output"); psi.ArgumentList.Add(cfg.OutputDir);
            psi.ArgumentList.Add("--epochs"); psi.ArgumentList.Add(cfg.Epochs.ToString());
            psi.ArgumentList.Add("--batch_size"); psi.ArgumentList.Add(cfg.BatchSize.ToString());
            psi.ArgumentList.Add("--lr"); psi.ArgumentList.Add(cfg.LearningRate.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (!string.IsNullOrEmpty(cfg.PretrainedModel))
            {
                psi.ArgumentList.Add("--pretrained");
                psi.ArgumentList.Add(cfg.PretrainedModel);
            }
            if (cfg.ExportOnnx) psi.ArgumentList.Add("--export_onnx");

            using var proc = new Process { StartInfo = psi };
            try { proc.Start(); }
            catch (Exception ex)
            {
                Emit(new TrainingEventArgs { Error = $"Python 실행 실패 ('{cfg.PythonPath}'): {ex.Message}" });
                return false;
            }

            using var reg = ct.Register(() =>
            {
                try { if (!proc.HasExited) proc.Kill(true); } catch { }
            });

            // stdout 라인별 비동기 읽기 (stderr는 RedirectStandardOutput과 합쳐 못 받음 → 별도 처리)
            var stderrTask = Task.Run(async () =>
            {
                while (!proc.StandardError.EndOfStream)
                {
                    string? line = await proc.StandardError.ReadLineAsync().ConfigureAwait(false);
                    if (line != null) Emit(ParseLine(line));
                }
            });

            while (!proc.StandardOutput.EndOfStream)
            {
                string? line = await proc.StandardOutput.ReadLineAsync().ConfigureAwait(false);
                if (line == null) break;
                Emit(ParseLine(line));
                if (ct.IsCancellationRequested) break;
            }

            try { await proc.WaitForExitAsync(ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { /* killed */ }
            await stderrTask.ConfigureAwait(false);

            if (ct.IsCancellationRequested)
            {
                Emit(new TrainingEventArgs { Error = "사용자에 의해 중단됨" });
                return false;
            }
            return proc.ExitCode == 0;
        }

        private void Emit(TrainingEventArgs args)
        {
            if (!string.IsNullOrEmpty(args.OnnxPath)) ResolvedOnnxPath = args.OnnxPath;
            // Visual Studio Output 창(Debug 카테고리)으로도 동시 송출 — UI 로그가 잘리거나 복사 어려운 경우 대비
            if (!string.IsNullOrEmpty(args.Line))
                System.Diagnostics.Debug.WriteLine("[PPOCR] " + args.Line);
            if (!string.IsNullOrEmpty(args.Error))
                System.Diagnostics.Debug.WriteLine("[PPOCR][ERROR] " + args.Error);
            if (!string.IsNullOrEmpty(args.OnnxPath))
                System.Diagnostics.Debug.WriteLine("[PPOCR][ONNX] " + args.OnnxPath);
            Output?.Invoke(this, args);
        }

        /// <summary>train_ppocr.py의 stdout 프로토콜 파싱.</summary>
        private static TrainingEventArgs ParseLine(string line)
        {
            var args = new TrainingEventArgs { Line = line };
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            string trimmed = line.TrimStart();
            if (trimmed.StartsWith("[EPOCH]"))
            {
                var parts = trimmed.Substring(7).Trim().Split('/');
                if (parts.Length == 2 && int.TryParse(parts[0], out int c) && int.TryParse(parts[1], out int t))
                { args.EpochCurrent = c; args.EpochTotal = t; }
            }
            else if (trimmed.StartsWith("[PROGRESS]") &&
                double.TryParse(trimmed.Substring(10).Trim(),
                    System.Globalization.NumberStyles.Any, inv, out double p))
            { args.Progress = p; }
            else if (trimmed.StartsWith("[LOSS]") &&
                double.TryParse(trimmed.Substring(6).Trim(),
                    System.Globalization.NumberStyles.Any, inv, out double loss))
            { args.Loss = loss; }
            else if (trimmed.StartsWith("[ACC]") &&
                double.TryParse(trimmed.Substring(5).Trim(),
                    System.Globalization.NumberStyles.Any, inv, out double acc))
            { args.Accuracy = acc; }
            else if (trimmed.StartsWith("[ONNX]"))
            { args.OnnxPath = trimmed.Substring(6).Trim(); }
            else if (trimmed.StartsWith("[DONE]"))
            { args.Done = true; }
            else if (trimmed.StartsWith("[ERROR]"))
            { args.Error = trimmed.Substring(7).Trim(); }
            return args;
        }
    }
}
