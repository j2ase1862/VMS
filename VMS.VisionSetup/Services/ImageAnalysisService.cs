using System;
using System.Collections.Generic;
using System.Linq;
using OpenCvSharp;
using VMS.VisionSetup.Interfaces;
using VMS.VisionSetup.Models;

namespace VMS.VisionSetup.Services
{
    /// <summary>
    /// OpenCV 기반 이미지 통계 분석. SLM이 ImageDependent 파라미터를 결정할 때 호출.
    /// 결정적이고 빠른 연산만 사용.
    /// </summary>
    public class ImageAnalysisService : IImageAnalysisService
    {
        public HistogramAnalysis AnalyzeHistogram(Mat image, Rect? roi = null)
        {
            using var gray = ToGray(image, roi);
            int total = gray.Rows * gray.Cols;

            // 평균/표준편차
            Cv2.MeanStdDev(gray, out var meanScalar, out var stdScalar);

            // 256-bin 히스토그램
            using var hist = new Mat();
            var channels = new[] { 0 };
            var histSize = new[] { 256 };
            var ranges = new[] { new Rangef(0, 256) };
            Cv2.CalcHist(new[] { gray }, channels, null, hist, 1, histSize, ranges);

            var counts = new int[256];
            for (int i = 0; i < 256; i++)
                counts[i] = (int)hist.Get<float>(i);

            // min / max (0이 아닌 첫·마지막 bin)
            int minVal = 0, maxVal = 255;
            for (int i = 0; i < 256; i++) { if (counts[i] > 0) { minVal = i; break; } }
            for (int i = 255; i >= 0; i--) { if (counts[i] > 0) { maxVal = i; break; } }

            // 백분위
            int p10 = Percentile(counts, total, 0.10);
            int p50 = Percentile(counts, total, 0.50);
            int p90 = Percentile(counts, total, 0.90);

            // Otsu 임계값(실제 이진화는 버림, threshold 값만 채택)
            using var dst = new Mat();
            double otsu = Cv2.Threshold(gray, dst, 0, 255,
                ThresholdTypes.Otsu | ThresholdTypes.Binary);

            // Bimodal 검출
            DetectBimodal(counts, out bool bimodal, out int? darkPeak, out int? lightPeak);

            return new HistogramAnalysis
            {
                Mean = Math.Round(meanScalar.Val0, 2),
                StdDev = Math.Round(stdScalar.Val0, 2),
                Min = minVal,
                Max = maxVal,
                P10 = p10,
                P50 = p50,
                P90 = p90,
                OtsuThreshold = (int)Math.Round(otsu),
                HasBimodal = bimodal,
                DarkPeak = darkPeak,
                LightPeak = lightPeak,
                PixelCount = total,
            };
        }

        public EdgeAnalysis AnalyzeEdges(Mat image, Rect? roi = null)
        {
            using var gray = ToGray(image, roi);
            int total = gray.Rows * gray.Cols;

            // Sobel로 그래디언트
            using var gx = new Mat();
            using var gy = new Mat();
            Cv2.Sobel(gray, gx, MatType.CV_32F, 1, 0, ksize: 3);
            Cv2.Sobel(gray, gy, MatType.CV_32F, 0, 1, ksize: 3);

            using var mag = new Mat();
            using var angle = new Mat();
            Cv2.CartToPolar(gx, gy, mag, angle, angleInDegrees: true);

            Cv2.MeanStdDev(mag, out var magMean, out var magStd);

            // 권장 Canny: 그래디언트 크기 P70 ~ P70*3
            int low = SuggestCannyLowFromMagnitude(mag);
            int high = Math.Clamp(low * 3, low + 1, 255);

            // Canny 결과로 엣지 비율 계산
            using var edges = new Mat();
            using var gray8 = new Mat();
            gray.ConvertTo(gray8, MatType.CV_8U);
            Cv2.Canny(gray8, edges, low, high);
            int edgePixels = Cv2.CountNonZero(edges);
            double density = total > 0 ? (double)edgePixels / total : 0.0;

            // 우세 방향 (엣지 픽셀에 한해서)
            double dominantAngle = ComputeDominantAngle(mag, angle, threshold: low);

            return new EdgeAnalysis
            {
                EdgeDensity = Math.Round(density, 4),
                MeanGradientMagnitude = Math.Round(magMean.Val0, 2),
                GradientStdDev = Math.Round(magStd.Val0, 2),
                DominantAngleDeg = Math.Round(dominantAngle, 1),
                SuggestedCannyLow = low,
                SuggestedCannyHigh = high,
            };
        }

        public ImageAnalysisBundle AnalyzeBundle(Mat image, string[] requests, Rect? roi = null)
        {
            var bundle = new ImageAnalysisBundle();
            if (requests == null || requests.Length == 0)
                return bundle;

            var set = new HashSet<string>(requests.Select(r => r.Trim().ToLowerInvariant()));
            if (set.Contains("histogram"))
                bundle.Histogram = AnalyzeHistogram(image, roi);
            if (set.Contains("edges"))
                bundle.Edges = AnalyzeEdges(image, roi);

            if (roi.HasValue)
            {
                var r = roi.Value;
                bundle.RoiSummary = $"x={r.X},y={r.Y},w={r.Width},h={r.Height}";
            }
            return bundle;
        }

        // ─────────────── 내부 유틸 ───────────────

        private static Mat ToGray(Mat src, Rect? roi)
        {
            var workSrc = roi.HasValue ? new Mat(src, ClampRoi(roi.Value, src)) : src;
            Mat gray;
            if (workSrc.Channels() > 1)
            {
                gray = new Mat();
                Cv2.CvtColor(workSrc, gray, ColorConversionCodes.BGR2GRAY);
            }
            else
            {
                gray = workSrc.Clone();
            }
            if (roi.HasValue) workSrc.Dispose();
            return gray;
        }

        private static Rect ClampRoi(Rect r, Mat image)
        {
            int x = Math.Clamp(r.X, 0, image.Width);
            int y = Math.Clamp(r.Y, 0, image.Height);
            int w = Math.Clamp(r.Width, 0, image.Width - x);
            int h = Math.Clamp(r.Height, 0, image.Height - y);
            return new Rect(x, y, w, h);
        }

        private static int Percentile(int[] counts, int total, double p)
        {
            if (total <= 0) return 0;
            long threshold = (long)(total * p);
            long cum = 0;
            for (int i = 0; i < counts.Length; i++)
            {
                cum += counts[i];
                if (cum >= threshold) return i;
            }
            return counts.Length - 1;
        }

        /// <summary>
        /// 단순 bimodal 검출: 두 개의 강한 peak가 의미 있는 valley로 분리되면 true.
        /// </summary>
        private static void DetectBimodal(int[] counts, out bool bimodal, out int? darkPeak, out int? lightPeak)
        {
            // 윈도우 5의 평활화
            var smoothed = new double[256];
            for (int i = 0; i < 256; i++)
            {
                double sum = 0;
                int cnt = 0;
                for (int k = -2; k <= 2; k++)
                {
                    int j = i + k;
                    if (j < 0 || j >= 256) continue;
                    sum += counts[j];
                    cnt++;
                }
                smoothed[i] = sum / cnt;
            }

            // 모든 지역 최대 후보
            var peaks = new List<(int index, double value)>();
            for (int i = 2; i < 254; i++)
            {
                if (smoothed[i] > smoothed[i - 1] && smoothed[i] > smoothed[i + 1]
                    && smoothed[i] > smoothed[i - 2] && smoothed[i] > smoothed[i + 2])
                {
                    peaks.Add((i, smoothed[i]));
                }
            }

            if (peaks.Count < 2)
            {
                bimodal = false;
                darkPeak = lightPeak = null;
                return;
            }

            // 상위 2개 peak
            var top = peaks.OrderByDescending(p => p.value).Take(2)
                          .OrderBy(p => p.index).ToArray();

            int p1 = top[0].index;
            int p2 = top[1].index;

            // peak 간 최소(valley)값
            double valley = double.MaxValue;
            for (int i = p1; i <= p2; i++)
                if (smoothed[i] < valley) valley = smoothed[i];

            double minPeak = Math.Min(top[0].value, top[1].value);
            // 두 peak 충분히 떨어져 있고, valley가 작은 peak의 60% 이하면 bimodal
            bool farEnough = (p2 - p1) >= 30;
            bool deepEnough = valley < minPeak * 0.6;

            bimodal = farEnough && deepEnough;
            darkPeak = bimodal ? p1 : null;
            lightPeak = bimodal ? p2 : null;
        }

        private static int SuggestCannyLowFromMagnitude(Mat magFloat)
        {
            // P70 of gradient magnitude
            using var dst = new Mat();
            magFloat.ConvertTo(dst, MatType.CV_8U);
            using var hist = new Mat();
            var ranges = new[] { new Rangef(0, 256) };
            Cv2.CalcHist(new[] { dst }, new[] { 0 }, null, hist, 1, new[] { 256 }, ranges);
            int total = dst.Rows * dst.Cols;
            long threshold = (long)(total * 0.70);
            long cum = 0;
            for (int i = 0; i < 256; i++)
            {
                cum += (long)hist.Get<float>(i);
                if (cum >= threshold) return Math.Max(10, i);
            }
            return 50;
        }

        private static double ComputeDominantAngle(Mat magnitude, Mat angleDeg, double threshold)
        {
            // 18개 bin(10도 단위)로 가중 히스토그램
            var bins = new double[18];
            int rows = magnitude.Rows;
            int cols = magnitude.Cols;
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    float m = magnitude.Get<float>(r, c);
                    if (m < threshold) continue;
                    double a = angleDeg.Get<float>(r, c);
                    a %= 180.0; // 0~180 (양방향 동치)
                    if (a < 0) a += 180;
                    int idx = (int)(a / 10.0);
                    if (idx >= 18) idx = 17;
                    bins[idx] += m;
                }
            }

            int maxIdx = 0;
            double maxVal = bins[0];
            for (int i = 1; i < 18; i++)
                if (bins[i] > maxVal) { maxVal = bins[i]; maxIdx = i; }

            return maxIdx * 10.0 + 5.0;
        }
    }
}
