using System.Globalization;
using System.Text.RegularExpressions;
using VMS.Core.Models.Annotation;

namespace VMS.Core.Services
{
    /// <summary>학습 스크립트 stdout 한 줄이 프로토콜의 어떤 태그였는지</summary>
    public enum TrainingOutputKind
    {
        /// <summary>프로토콜 태그가 아닌 일반 로그([INFO]/[WARN] 포함)</summary>
        None,
        Epoch,
        Loss,
        Accuracy,
        Progress,
        Onnx,
        Done,
        Error
    }

    /// <summary>
    /// train_*.py 의 stdout 프로토콜 파서 — 프로세스 호스트(TrainingService, 학습 워커)와 분리된 순수 로직.
    ///   [EPOCH] a/b · [LOSS] x · [ACC] x · [PROGRESS] p · [ONNX] path · [DONE] · [ERROR] message
    /// 한 줄을 TrainingStatus 에 반영하고 태그 종류를 돌려준다. 태그가 아니면 상태를 바꾸지 않는다.
    /// 규약 변경 시 스크립트 5종·TrainingService·워커·이 파서를 함께 갱신할 것 (MLOps 개발 문서 §3.2 원칙 2).
    /// </summary>
    public static class TrainingOutputParser
    {
        private static readonly Regex EpochPattern = new(@"\[EPOCH\]\s*(\d+)\s*/\s*(\d+)", RegexOptions.Compiled);
        private static readonly Regex LossPattern = new(@"\[LOSS\]\s*([\d.]+)", RegexOptions.Compiled);
        private static readonly Regex AccPattern = new(@"\[ACC\]\s*([\d.]+)", RegexOptions.Compiled);
        private static readonly Regex ProgressPattern = new(@"\[PROGRESS\]\s*([\d.]+)", RegexOptions.Compiled);
        private static readonly Regex OnnxPattern = new(@"\[ONNX\]\s*(.+)", RegexOptions.Compiled);
        private static readonly Regex ErrorPattern = new(@"\[ERROR\]\s*(.+)", RegexOptions.Compiled);

        /// <summary>한 줄을 해석해 status 를 갱신하고 태그 종류를 반환한다.</summary>
        public static TrainingOutputKind Apply(string line, TrainingStatus status)
        {
            if (string.IsNullOrEmpty(line)) return TrainingOutputKind.None;

            var inv = CultureInfo.InvariantCulture;
            Match m;

            m = EpochPattern.Match(line);
            if (m.Success)
            {
                status.CurrentEpoch = int.Parse(m.Groups[1].Value, inv);
                status.TotalEpochs = int.Parse(m.Groups[2].Value, inv);
                if (status.TotalEpochs > 0)
                    status.Progress = (double)status.CurrentEpoch / status.TotalEpochs * 100;
                status.Message = $"Epoch {status.CurrentEpoch}/{status.TotalEpochs}";
                return TrainingOutputKind.Epoch;
            }

            m = LossPattern.Match(line);
            if (m.Success && double.TryParse(m.Groups[1].Value, NumberStyles.Float, inv, out var loss))
            {
                status.Loss = loss;
                return TrainingOutputKind.Loss;
            }

            m = AccPattern.Match(line);
            if (m.Success && double.TryParse(m.Groups[1].Value, NumberStyles.Float, inv, out var acc))
            {
                status.Accuracy = acc;
                return TrainingOutputKind.Accuracy;
            }

            m = ProgressPattern.Match(line);
            if (m.Success && double.TryParse(m.Groups[1].Value, NumberStyles.Float, inv, out var progress))
            {
                status.Progress = progress;
                return TrainingOutputKind.Progress;
            }

            m = OnnxPattern.Match(line);
            if (m.Success)
            {
                status.OnnxOutputPath = m.Groups[1].Value.Trim();
                return TrainingOutputKind.Onnx;
            }

            if (line.Contains("[DONE]"))
            {
                status.Progress = 100;
                return TrainingOutputKind.Done;
            }

            m = ErrorPattern.Match(line);
            if (m.Success)
            {
                status.Message = m.Groups[1].Value.Trim();
                return TrainingOutputKind.Error;
            }

            return TrainingOutputKind.None;
        }
    }
}
