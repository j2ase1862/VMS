using OpenCvSharp;
using System;

namespace VMS.VisionSetup.Services.SynthData
{
    /// <summary>
    /// OCR 합성 데이터 증강 설정. 각 항목은 무작위 범위(min, max)로 지정.
    /// 학습 분포의 다양성을 확보하기 위함 — 산업 라벨의 실제 조명/각도 변동 모사.
    /// </summary>
    public class AugmentationConfig
    {
        public double RotationDegMax { get; set; } = 5.0;
        /// <summary>원근 변형 강도(0~0.3). 모서리를 이만큼 ±perturb. 0이면 비활성.</summary>
        public double PerspectiveJitter { get; set; } = 0.02;
        public int BlurMaxKernel { get; set; } = 3;        // 0 / 3 / 5 (Gaussian 커널 크기). 0이면 비활성.
        public double NoiseStdMax { get; set; } = 5.0;     // Gaussian 노이즈 표준편차 최댓값(0~25)
        public double BrightnessJitter { get; set; } = 0.15; // ±15% brightness
        public double ContrastJitter { get; set; } = 0.15;   // ±15% contrast
        public bool DotMatrixMode { get; set; } = false;   // 도트 마킹 효과 (수직/수평 dot grid 마스크)
    }

    /// <summary>
    /// OpenCV 기반 증강 — 회전 → 원근 → blur → 노이즈 → 밝기/대비 → (dot matrix).
    /// 각 단계는 config 범위 내 무작위 적용.
    /// </summary>
    public static class Augmentation
    {
        public static Mat Apply(Mat input, AugmentationConfig cfg, Random rng)
        {
            Mat current = input.Clone();

            // 1) 회전 (캔버스 확장)
            if (cfg.RotationDegMax > 0)
            {
                double angle = (rng.NextDouble() * 2 - 1) * cfg.RotationDegMax;
                if (Math.Abs(angle) > 0.1)
                {
                    var rotated = RotateExpand(current, angle);
                    current.Dispose();
                    current = rotated;
                }
            }

            // 2) 원근 jitter
            if (cfg.PerspectiveJitter > 0)
            {
                var warped = PerspectiveJitter(current, cfg.PerspectiveJitter, rng);
                current.Dispose();
                current = warped;
            }

            // 3) Gaussian blur (kernel ∈ {0, 3, 5} 등)
            if (cfg.BlurMaxKernel >= 3)
            {
                int[] kernels = cfg.BlurMaxKernel >= 5 ? new[] { 0, 3, 5 } : new[] { 0, 3 };
                int k = kernels[rng.Next(kernels.Length)];
                if (k > 0)
                {
                    var blurred = new Mat();
                    Cv2.GaussianBlur(current, blurred, new Size(k, k), 0);
                    current.Dispose();
                    current = blurred;
                }
            }

            // 4) Gaussian noise
            if (cfg.NoiseStdMax > 0)
            {
                double std = rng.NextDouble() * cfg.NoiseStdMax;
                if (std > 0.5)
                {
                    using var noise = new Mat(current.Size(), current.Type());
                    Cv2.Randn(noise, Scalar.All(0), Scalar.All(std));
                    var noisy = new Mat();
                    Cv2.Add(current, noise, noisy);
                    current.Dispose();
                    current = noisy;
                }
            }

            // 5) 밝기/대비
            if (cfg.BrightnessJitter > 0 || cfg.ContrastJitter > 0)
            {
                double alpha = 1.0 + (rng.NextDouble() * 2 - 1) * cfg.ContrastJitter; // contrast
                double beta = (rng.NextDouble() * 2 - 1) * cfg.BrightnessJitter * 128; // brightness
                var adj = new Mat();
                current.ConvertTo(adj, current.Type(), alpha, beta);
                current.Dispose();
                current = adj;
            }

            // 6) 도트 매트릭스 효과 (모듈형 그리드 마스킹)
            if (cfg.DotMatrixMode)
            {
                var dotted = ApplyDotMatrix(current);
                current.Dispose();
                current = dotted;
            }

            return current;
        }

        // 회전 후 잘림 없도록 캔버스 확장 (WarpAffine + 확장된 dsize)
        private static Mat RotateExpand(Mat src, double angle)
        {
            int w = src.Width, h = src.Height;
            var center = new Point2f(w / 2f, h / 2f);
            using var rotMat = Cv2.GetRotationMatrix2D(center, angle, 1.0);

            // 회전 후 bounding box 계산
            double cos = Math.Abs(rotMat.At<double>(0, 0));
            double sin = Math.Abs(rotMat.At<double>(0, 1));
            int newW = (int)(h * sin + w * cos);
            int newH = (int)(h * cos + w * sin);

            // 회전 행렬의 translation을 새 캔버스 중심으로 보정
            rotMat.Set(0, 2, rotMat.At<double>(0, 2) + (newW - w) / 2.0);
            rotMat.Set(1, 2, rotMat.At<double>(1, 2) + (newH - h) / 2.0);

            var result = new Mat();
            Cv2.WarpAffine(src, result, rotMat, new Size(newW, newH),
                InterpolationFlags.Linear, BorderTypes.Replicate);
            return result;
        }

        private static Mat PerspectiveJitter(Mat src, double jitter, Random rng)
        {
            int w = src.Width, h = src.Height;
            float jx = (float)(w * jitter);
            float jy = (float)(h * jitter);

            var srcCorners = new[]
            {
                new Point2f(0, 0), new Point2f(w - 1, 0),
                new Point2f(w - 1, h - 1), new Point2f(0, h - 1)
            };
            var dstCorners = new[]
            {
                new Point2f(Jitter(0, jx, rng), Jitter(0, jy, rng)),
                new Point2f(w - 1 + Jitter(0, jx, rng), Jitter(0, jy, rng)),
                new Point2f(w - 1 + Jitter(0, jx, rng), h - 1 + Jitter(0, jy, rng)),
                new Point2f(Jitter(0, jx, rng), h - 1 + Jitter(0, jy, rng))
            };
            using var H = Cv2.GetPerspectiveTransform(srcCorners, dstCorners);
            var result = new Mat();
            Cv2.WarpPerspective(src, result, H, new Size(w, h),
                InterpolationFlags.Linear, BorderTypes.Replicate);
            return result;
        }

        private static float Jitter(float baseVal, float maxDelta, Random rng)
            => baseVal + (float)((rng.NextDouble() * 2 - 1) * maxDelta);

        // 도트 매트릭스: 글자를 작은 도트로 분해 — 잉크젯/도트 프린트 효과 모사
        private static Mat ApplyDotMatrix(Mat src)
        {
            using Mat gray = src.Channels() > 1
                ? src.CvtColor(ColorConversionCodes.BGR2GRAY) : src.Clone();
            using var bin = new Mat();
            Cv2.Threshold(gray, bin, 0, 255, ThresholdTypes.Binary | ThresholdTypes.Otsu);

            // 어두운 글자만 남기기 (배경 흰색 가정)
            using var inv = new Mat();
            Cv2.BitwiseNot(bin, inv);

            // 도트 그리드 마스크 (3 px 간격)
            using var dotMask = Mat.Zeros(src.Size(), MatType.CV_8UC1).ToMat();
            for (int y = 0; y < src.Height; y += 3)
                for (int x = 0; x < src.Width; x += 3)
                    dotMask.Set(y, x, (byte)255);

            // inv(글자) ∩ dotMask = 도트 글자
            using var dotChars = new Mat();
            Cv2.BitwiseAnd(inv, dotMask, dotChars);

            // 결과: 흰 배경 + 도트 글자 (검은 점)
            var result = new Mat(src.Size(), MatType.CV_8UC3, Scalar.White);
            result.SetTo(Scalar.Black, dotChars);
            return result;
        }
    }
}
