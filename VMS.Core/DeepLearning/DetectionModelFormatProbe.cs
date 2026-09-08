using System;
using System.Collections.Generic;
using System.Linq;

namespace VMS.Core.DeepLearning
{
    /// <summary>검출 ONNX 모델의 출력 규약 종류</summary>
    public enum DetectionModelFormat
    {
        /// <summary>Ultralytics YOLOv8/v11 — 단일 출력 [1, 4+nc, N] 또는 [1, N, 4+nc], letterbox 전처리</summary>
        Yolo,
        /// <summary>
        /// D-FINE / RT-DETR 계열 (Apache-2.0) — train_dfine.py 가 export 한 deploy 규약
        /// (images + orig_target_sizes → labels/boxes/scores) 또는 HF 원본 규약 (logits/pred_boxes).
        /// </summary>
        DFine
    }

    /// <summary>
    /// ONNX 파일을 InferenceSession 없이 읽어 검출 모델 규약을 판별한다.
    /// 1) metadata_props 'model_format' (train_dfine.py 가 기록) → 2) 그래프 입출력 이름 구조.
    /// OpenCV·ONNX Runtime 의존이 없어 VisionSetup 추론 엔진, DeepLearning 앱, Web 모델 레지스트리(업로드 검증),
    /// 학습 워커가 같은 코드를 쓴다 (MLOps Phase 1 §4 검증 파이프라인).
    /// </summary>
    public static class DetectionModelFormatProbe
    {
        public const string MetadataKey = "model_format";

        public static DetectionModelFormat Probe(string modelPath)
        {
            try
            {
                var meta = OnnxMetadataReader.Read(modelPath);
                if (meta.TryGetValue(MetadataKey, out var fmt))
                {
                    var f = fmt.Trim();
                    if (f.Equals("dfine", StringComparison.OrdinalIgnoreCase) ||
                        f.Equals("rtdetr", StringComparison.OrdinalIgnoreCase))
                        return DetectionModelFormat.DFine;
                    if (f.Equals("yolo", StringComparison.OrdinalIgnoreCase))
                        return DetectionModelFormat.Yolo;
                }

                var (inputs, outputs) = OnnxMetadataReader.ReadGraphIoNames(modelPath);
                return IsDFineLayout(inputs, outputs) ? DetectionModelFormat.DFine : DetectionModelFormat.Yolo;
            }
            catch
            {
                return DetectionModelFormat.Yolo; // 판별 실패 시 기존 동작(YOLO) 유지
            }
        }

        /// <summary>그래프 입출력 이름만으로 D-FINE(DETR) 규약인지 판정</summary>
        public static bool IsDFineLayout(IReadOnlyCollection<string> inputs, IReadOnlyCollection<string> outputs)
        {
            if (inputs.Contains("orig_target_sizes")) return true;
            if (outputs.Contains("labels") && outputs.Contains("boxes") && outputs.Contains("scores")) return true;
            if (outputs.Contains("logits") && outputs.Contains("pred_boxes")) return true;
            return false;
        }
    }
}
