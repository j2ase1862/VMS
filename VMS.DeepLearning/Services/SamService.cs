using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace VMS.DeepLearning.Services
{
    /// <summary>
    /// SAM 클릭 포인트 정보
    /// </summary>
    public class SamPoint
    {
        /// <summary>이미지 좌표 X</summary>
        public double X { get; set; }
        /// <summary>이미지 좌표 Y</summary>
        public double Y { get; set; }
        /// <summary>1=전경, 0=배경</summary>
        public int Label { get; set; }

        public SamPoint(double x, double y, int label)
        {
            X = x;
            Y = y;
            Label = label;
        }
    }

    public interface ISamService
    {
        bool IsLoaded { get; }
        void LoadModel(string encoderPath, string decoderPath);
        Task ComputeEmbeddingAsync(Mat image);
        List<Point2d>? Predict(List<SamPoint> points);
        void ClearEmbedding();
        void Dispose();
    }

    public class SamService : ISamService, IDisposable
    {
        private InferenceSession? _encoderSession;
        private InferenceSession? _decoderSession;
        private float[]? _imageEmbedding;
        private int[]? _embeddingShape;
        private int _originalWidth;
        private int _originalHeight;
        private double _resizeScale; // longest-edge resize scale
        private const int SamInputSize = 1024;

        public bool IsLoaded => _encoderSession != null && _decoderSession != null;

        public void LoadModel(string encoderPath, string decoderPath)
        {
            _encoderSession?.Dispose();
            _decoderSession?.Dispose();
            _imageEmbedding = null;

            var options = new SessionOptions();
            options.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL;

            _encoderSession = new InferenceSession(encoderPath, options);
            _decoderSession = new InferenceSession(decoderPath, options);
        }

        public async Task ComputeEmbeddingAsync(Mat image)
        {
            if (_encoderSession == null)
                throw new InvalidOperationException("Encoder model not loaded.");

            _originalWidth = image.Width;
            _originalHeight = image.Height;

            // Preprocess: resize to 1024x1024, normalize, NCHW format
            var inputTensor = await Task.Run(() => PreprocessForEncoder(image));

            var results = await Task.Run(() =>
            {
                var inputs = new List<NamedOnnxValue>
                {
                    NamedOnnxValue.CreateFromTensor("input_image", inputTensor)
                };
                return _encoderSession.Run(inputs);
            });

            var embeddingResult = results.First();
            var embeddingTensor = embeddingResult.AsTensor<float>();
            _embeddingShape = embeddingTensor.Dimensions.ToArray();
            _imageEmbedding = embeddingTensor.ToArray();
        }

        public List<Point2d>? Predict(List<SamPoint> points)
        {
            if (_decoderSession == null || _imageEmbedding == null || _embeddingShape == null)
                return null;
            if (points.Count == 0)
                return null;

            // image_embeddings
            var embeddingTensor = new DenseTensor<float>(
                _imageEmbedding, _embeddingShape);

            // point_coords: (1, N, 2) — scaled to 1024x1024 padded space
            // Use the same single scale factor as the encoder preprocessing
            int n = points.Count;
            var pointCoords = new DenseTensor<float>(new[] { 1, n, 2 });
            var pointLabels = new DenseTensor<float>(new[] { 1, n });

            for (int i = 0; i < n; i++)
            {
                pointCoords[0, i, 0] = (float)(points[i].X * _resizeScale);
                pointCoords[0, i, 1] = (float)(points[i].Y * _resizeScale);
                pointLabels[0, i] = points[i].Label;
            }

            // mask_input: (1, 1, 256, 256) zeros
            var maskInput = new DenseTensor<float>(new[] { 1, 1, 256, 256 });

            // has_mask_input: (1,) = 0
            var hasMaskInput = new DenseTensor<float>(new[] { 1 });
            hasMaskInput[0] = 0.0f;

            // orig_im_size: (2,) = [height, width]
            var origImSize = new DenseTensor<float>(new[] { 2 });
            origImSize[0] = _originalHeight;
            origImSize[1] = _originalWidth;

            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor("image_embeddings", embeddingTensor),
                NamedOnnxValue.CreateFromTensor("point_coords", pointCoords),
                NamedOnnxValue.CreateFromTensor("point_labels", pointLabels),
                NamedOnnxValue.CreateFromTensor("mask_input", maskInput),
                NamedOnnxValue.CreateFromTensor("has_mask_input", hasMaskInput),
                NamedOnnxValue.CreateFromTensor("orig_im_size", origImSize),
            };

            var results = _decoderSession.Run(inputs);

            // Output: masks (1, N_masks, H, W) and iou_predictions (1, N_masks)
            var masksResult = results.First();
            var masksTensor = masksResult.AsTensor<float>();
            var dims = masksTensor.Dimensions.ToArray();

            // Pick the best mask (highest IoU score)
            int bestIdx = 0;
            var iouResult = results.ElementAtOrDefault(1);
            if (iouResult != null)
            {
                var iouTensor = iouResult.AsTensor<float>();
                float bestIou = float.MinValue;
                for (int i = 0; i < iouTensor.Dimensions[1]; i++)
                {
                    if (iouTensor[0, i] > bestIou)
                    {
                        bestIou = iouTensor[0, i];
                        bestIdx = i;
                    }
                }
            }

            // Convert mask to binary Mat
            int maskH = dims[2];
            int maskW = dims[3];
            var maskMat = new Mat(maskH, maskW, MatType.CV_8UC1);

            unsafe
            {
                byte* ptr = (byte*)maskMat.Data;
                for (int y = 0; y < maskH; y++)
                    for (int x = 0; x < maskW; x++)
                        ptr[y * maskW + x] = masksTensor[0, bestIdx, y, x] > 0 ? (byte)255 : (byte)0;
            }

            var polygon = MaskToPolygon(maskMat);
            maskMat.Dispose();
            return polygon;
        }

        /// <summary>
        /// 바이너리 마스크를 폴리곤 포인트 리스트로 변환
        /// </summary>
        private List<Point2d>? MaskToPolygon(Mat mask)
        {
            Cv2.FindContours(mask, out Point[][] contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);

            if (contours.Length == 0)
                return null;

            // 가장 큰 컨투어 선택
            var largest = contours.OrderByDescending(c => Cv2.ContourArea(c)).First();

            // 폴리곤 단순화
            double epsilon = 0.005 * Cv2.ArcLength(largest, true);
            var approx = Cv2.ApproxPolyDP(largest, epsilon, true);

            if (approx.Length < 3)
                return null;

            return approx.Select(p => new Point2d(p.X, p.Y)).ToList();
        }

        private DenseTensor<float> PreprocessForEncoder(Mat image)
        {
            // SAM preprocessing: resize longest edge to 1024, pad to 1024x1024
            double scale = (double)SamInputSize / Math.Max(image.Width, image.Height);
            _resizeScale = scale;
            int newW = (int)(image.Width * scale);
            int newH = (int)(image.Height * scale);

            using var resized = new Mat();
            Cv2.Resize(image, resized, new Size(newW, newH));

            // Pad to 1024x1024 (bottom-right padding with zeros)
            using var padded = new Mat(SamInputSize, SamInputSize, image.Type(), Scalar.All(0));
            resized.CopyTo(padded[new Rect(0, 0, newW, newH)]);

            // Convert to RGB float
            using var rgb = new Mat();
            if (padded.Channels() == 1)
                Cv2.CvtColor(padded, rgb, ColorConversionCodes.GRAY2RGB);
            else if (padded.Channels() == 4)
                Cv2.CvtColor(padded, rgb, ColorConversionCodes.BGRA2RGB);
            else
                Cv2.CvtColor(padded, rgb, ColorConversionCodes.BGR2RGB);

            using var floatMat = new Mat();
            rgb.ConvertTo(floatMat, MatType.CV_32FC3);

            // SAM pixel normalization: (pixel - mean) / std
            float[] mean = { 123.675f, 116.28f, 103.53f };
            float[] std = { 58.395f, 57.12f, 57.375f };

            var tensor = new DenseTensor<float>(new[] { 1, 3, SamInputSize, SamInputSize });

            unsafe
            {
                float* ptr = (float*)floatMat.Data;
                int stride = SamInputSize * 3;
                for (int y = 0; y < SamInputSize; y++)
                {
                    for (int x = 0; x < SamInputSize; x++)
                    {
                        int idx = y * stride + x * 3;
                        tensor[0, 0, y, x] = (ptr[idx + 0] - mean[0]) / std[0];
                        tensor[0, 1, y, x] = (ptr[idx + 1] - mean[1]) / std[1];
                        tensor[0, 2, y, x] = (ptr[idx + 2] - mean[2]) / std[2];
                    }
                }
            }

            return tensor;
        }

        public void ClearEmbedding()
        {
            _imageEmbedding = null;
            _embeddingShape = null;
        }

        public void Dispose()
        {
            _encoderSession?.Dispose();
            _decoderSession?.Dispose();
            _encoderSession = null;
            _decoderSession = null;
            _imageEmbedding = null;
        }
    }
}
