using System;
using System.Collections.Generic;
using OpenCvSharp;
using VMS.DeepLearning.Models;

namespace VMS.DeepLearning.Interfaces
{
    /// <summary>
    /// Predict 결과 + 진단 정보 묶음.
    /// 검출 0건일 때 사용자가 threshold를 낮춰야 할지 판단할 수 있도록 raw 최고 confidence 노출.
    /// </summary>
    public class InferenceResult
    {
        public List<DetectionPrediction> Predictions { get; set; } = new();
        public float MaxRawConfidence { get; set; }
        public int CandidatesAboveZero { get; set; }
        public int InputSize { get; set; }
        public int NumClasses { get; set; }
        public string[]? ClassNames { get; set; }
    }

    /// <summary>
    /// 학습된 ONNX 모델로 데이터셋 이미지에 즉시 추론을 수행하는 서비스.
    /// v1: YOLO Detection 전용. Classification/Anomaly는 v2.
    /// </summary>
    public interface IInferenceService : IDisposable
    {
        /// <summary>모델 로드 여부</summary>
        bool IsLoaded { get; }

        /// <summary>현재 로드된 모델 경로(없으면 빈 문자열)</summary>
        string CurrentModelPath { get; }

        /// <summary>현재 로드된 모델의 클래스 수(메타데이터 또는 출력 차원에서 추출). 모델 없으면 0.</summary>
        int NumClasses { get; }

        /// <summary>현재 로드된 모델의 입력 크기.</summary>
        int InputSize { get; }

        /// <summary>
        /// ONNX 모델 로드. 실패 시 예외.
        /// </summary>
        void LoadModel(string onnxPath);

        /// <summary>모델 언로드(메모리 해제)</summary>
        void UnloadModel();

        /// <summary>
        /// 단일 이미지에 대해 YOLO 추론 + 진단 정보 반환.
        /// </summary>
        /// <param name="image">BGR Mat</param>
        /// <param name="confThreshold">기본 0.10 (학습 검증용으로 낮게)</param>
        /// <param name="iouThreshold">NMS IoU, 기본 0.45</param>
        InferenceResult Predict(Mat image, float confThreshold = 0.10f, float iouThreshold = 0.45f);
    }
}
