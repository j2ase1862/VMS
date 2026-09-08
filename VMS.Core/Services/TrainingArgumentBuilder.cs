using System.Globalization;
using System.IO;
using VMS.Core.Models.Annotation;

namespace VMS.Core.Services
{
    /// <summary>
    /// TrainingConfig → train_*.py 명령줄 인자 조립. TrainingService(WPF 도구)와 학습 워커가 공유한다.
    /// 스크립트별로 의미 있는 인자만 붙인다 — 화이트리스트 밖 키는 존재하지 않으므로 임의 인자 주입 불가.
    ///   공통      : --dataset --output --epochs --lr --batch_size [--pretrained] [--export_onnx]
    ///   ppocr     : --target detection|recognition
    ///   yolo/dfine: --mosaic --mixup --hsv_h --hsv_s --hsv_v (dfine 은 hsv_* 만 사용, mosaic/mixup 은 스크립트가 무시)
    ///   anomaly   : --method --backbone --coreset_ratio
    /// </summary>
    public static class TrainingArgumentBuilder
    {
        public static string Build(TrainingConfig config)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append($"\"{config.TrainingScriptPath}\"");

            var scriptName = Path.GetFileName(config.TrainingScriptPath).ToLowerInvariant();
            var inv = CultureInfo.InvariantCulture;

            // --target 은 train_ppocr.py 만 사용 (detection/recognition)
            if (UsesOcrTarget(scriptName))
                sb.Append($" --target {config.Target.ToString().ToLower()}");

            sb.Append($" --dataset \"{NormalizeDir(config.DatasetPath)}\"");
            sb.Append($" --output \"{NormalizeDir(config.OutputDir)}\"");
            sb.Append($" --epochs {config.Epochs}");
            sb.Append($" --lr {config.LearningRate.ToString(inv)}");
            sb.Append($" --batch_size {config.BatchSize}");

            if (!string.IsNullOrEmpty(config.PretrainedModelPath))
                sb.Append($" --pretrained \"{config.PretrainedModelPath}\"");

            if (config.ExportOnnx)
                sb.Append(" --export_onnx");

            if (UsesAugmentation(scriptName))
            {
                sb.Append($" --mosaic {config.Mosaic.ToString(inv)}");
                sb.Append($" --mixup {config.Mixup.ToString(inv)}");
                sb.Append($" --hsv_h {config.HsvH.ToString(inv)}");
                sb.Append($" --hsv_s {config.HsvS.ToString(inv)}");
                sb.Append($" --hsv_v {config.HsvV.ToString(inv)}");
            }

            if (UsesAnomalyOptions(scriptName))
            {
                if (!string.IsNullOrWhiteSpace(config.AnomalyMethod))
                    sb.Append($" --method {config.AnomalyMethod}");
                if (!string.IsNullOrWhiteSpace(config.AnomalyBackbone))
                    sb.Append($" --backbone {config.AnomalyBackbone}");
                sb.Append($" --coreset_ratio {config.CoresetRatio.ToString(inv)}");
            }

            return sb.ToString();
        }

        /// <summary>증강 인자(mosaic/mixup/hsv_*)를 받는 스크립트 — YOLO 계열과 D-FINE</summary>
        public static bool UsesAugmentation(string scriptFileName)
        {
            var s = scriptFileName.ToLowerInvariant();
            return s.Contains("yolo") || s.Contains("dfine");
        }

        public static bool UsesOcrTarget(string scriptFileName) => scriptFileName.ToLowerInvariant().Contains("ppocr");

        public static bool UsesAnomalyOptions(string scriptFileName) => scriptFileName.ToLowerInvariant().Contains("anomaly");

        /// <summary>
        /// 드라이브 루트(D:\)를 --dataset/--output 으로 전달할 때 발생하는 두 가지 문제를 회피한다:
        ///  1) TrimEnd('\\') 적용 시 "D:"가 되어, Python 의 os.path.join("D:", "x") 가 드라이브-상대 경로 "D:x" 를 반환해 파일을 찾지 못함.
        ///  2) 반대로 "D:\" 그대로 넘기면 Windows 커맨드라인에서 "D:\" 가 이스케이프된 쿼트로 파싱되어 인자 경계가 깨짐.
        /// 드라이브 루트는 "D:\." 형태로 치환해 두 문제를 동시에 해결한다. 그 외 경로는 후행 구분자만 제거.
        /// </summary>
        public static string NormalizeDir(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;
            if (path.Length == 3 && path[1] == ':' && (path[2] == '\\' || path[2] == '/'))
                return path[0] + ":\\.";
            return path.TrimEnd('\\', '/');
        }
    }
}
