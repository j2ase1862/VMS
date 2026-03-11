using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace VMS.VisionSetup.VisionTools.DeepLearning
{
    /// <summary>
    /// ONNX 추론 공용 베이스.
    /// YOLOv8, Classification, Anomaly 모델에서 공통으로 사용하는 전처리/세션 관리를 제공합니다.
    /// </summary>
    public abstract class OnnxModelBase : IDisposable
    {
        protected InferenceSession? _session;
        private bool _disposed;

        protected void LoadModel(string modelPath)
        {
            if (!File.Exists(modelPath))
                throw new FileNotFoundException($"ONNX 모델을 찾을 수 없습니다: {modelPath}");

            var options = new SessionOptions
            {
                InterOpNumThreads = 1,
                IntraOpNumThreads = Math.Max(1, Environment.ProcessorCount / 2),
                GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL
            };

            _session?.Dispose();
            _session = new InferenceSession(modelPath, options);
        }

        /// <summary>
        /// 이미지를 NCHW float 텐서로 변환 (ImageNet 정규화)
        /// </summary>
        protected static DenseTensor<float> PreprocessImage(Mat image, int targetW, int targetH,
            float[] mean, float[] std)
        {
            using var resized = new Mat();
            Cv2.Resize(image, resized, new Size(targetW, targetH));

            using var rgb = new Mat();
            if (resized.Channels() == 1)
                Cv2.CvtColor(resized, rgb, ColorConversionCodes.GRAY2RGB);
            else if (resized.Channels() == 4)
                Cv2.CvtColor(resized, rgb, ColorConversionCodes.BGRA2RGB);
            else
                Cv2.CvtColor(resized, rgb, ColorConversionCodes.BGR2RGB);

            var tensor = new DenseTensor<float>(new[] { 1, 3, targetH, targetW });

            unsafe
            {
                byte* ptr = (byte*)rgb.Data;
                int stride = (int)rgb.Step();

                for (int y = 0; y < targetH; y++)
                {
                    byte* row = ptr + y * stride;
                    for (int x = 0; x < targetW; x++)
                    {
                        int idx = x * 3;
                        tensor[0, 0, y, x] = (row[idx] / 255f - mean[0]) / std[0];
                        tensor[0, 1, y, x] = (row[idx + 1] / 255f - mean[1]) / std[1];
                        tensor[0, 2, y, x] = (row[idx + 2] / 255f - mean[2]) / std[2];
                    }
                }
            }

            return tensor;
        }

        /// <summary>
        /// 단순 0~1 정규화 (mean=0, std=1)
        /// </summary>
        protected static DenseTensor<float> PreprocessImageSimple(Mat image, int targetW, int targetH)
        {
            return PreprocessImage(image, targetW, targetH,
                new[] { 0f, 0f, 0f }, new[] { 1f, 1f, 1f });
        }

        /// <summary>
        /// ImageNet 정규화
        /// </summary>
        protected static DenseTensor<float> PreprocessImageNet(Mat image, int targetW, int targetH)
        {
            return PreprocessImage(image, targetW, targetH,
                new[] { 0.485f, 0.456f, 0.406f }, new[] { 0.229f, 0.224f, 0.225f });
        }

        protected List<NamedOnnxValue> CreateInput(string name, DenseTensor<float> tensor)
        {
            return new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor(name, tensor)
            };
        }

        protected string GetInputName()
        {
            return _session?.InputNames.FirstOrDefault() ?? "images";
        }

        public bool IsLoaded => _session != null;

        public void Dispose()
        {
            if (!_disposed)
            {
                _session?.Dispose();
                _session = null;
                _disposed = true;
            }
            GC.SuppressFinalize(this);
        }
    }
}
