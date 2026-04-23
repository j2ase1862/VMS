using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Linq;

namespace VMS.VisionSetup.VisionTools.DeepLearning
{
    /// <summary>
    /// Dot cluster 분석 알고리즘 종류.
    /// </summary>
    public enum DotDetectionMethod
    {
        /// <summary>Contour + circularity 기반 (일반적인 경우)</summary>
        Blob,
        /// <summary>Hough Circle Transform (깔끔한 원형 dot에 유리)</summary>
        HoughCircles
    }

    /// <summary>
    /// 저대비 이미지를 위한 ROI 전처리 모드.
    /// 여러 옵션을 조합해 최적을 찾도록 단일 선택형으로 제공.
    /// </summary>
    public enum DotPreprocessMode
    {
        /// <summary>전처리 없음 (기본)</summary>
        None,
        /// <summary>ROI에 국소 CLAHE 적용 — 저대비 환경에서 대비 향상</summary>
        Clahe,
        /// <summary>Top-hat(원본 - Opening): dot이 주변보다 밝을 때 효과적</summary>
        TopHat,
        /// <summary>Bottom-hat(Closing - 원본): dot이 주변보다 어두울 때 효과적</summary>
        BottomHat
    }

    /// <summary>
    /// 이진화 방식.
    /// </summary>
    public enum DotThresholdMode
    {
        /// <summary>전역 Otsu (대비가 명확한 이미지에 적합)</summary>
        Otsu,
        /// <summary>국소 평균 기준 Adaptive (조명 불균일/저대비에 강함)</summary>
        Adaptive
    }

    /// <summary>
    /// dot 배치 각도 계산 방식.
    /// </summary>
    public enum DotPatternMetric
    {
        /// <summary>모든 dot 중심점을 PCA 해서 주축 방향 각도를 구함. dot 수에 관계없이 동작.</summary>
        MainAxisAngle,
        /// <summary>가장 멀리 떨어진 두 dot을 연결한 선의 각도.</summary>
        LongestLineAngle
    }

    /// <summary>
    /// 한 번의 cluster 분석 결과.
    /// </summary>
    public class DotClusterResult
    {
        public int DotCount { get; set; }
        public double PatternAngle { get; set; }        // degrees, -90 ~ +90 (수평 기준)
        public List<Point2f> DotPositions { get; set; } = new(); // ROI 상대 좌표
        public bool AngleComputed { get; set; }         // dot 2개 이상일 때만 true
    }

    /// <summary>
    /// YOLO 바운딩 박스 내부의 dot 군락을 검출하고, 개수와 배치 각도를 측정한다.
    /// </summary>
    public static class DotClusterAnalyzer
    {
        public static DotClusterResult Analyze(
            Mat roi,
            DotDetectionMethod method,
            int minArea, int maxArea,
            double circularityThreshold,
            int minDotDistance,
            DotPatternMetric metric,
            DotPreprocessMode preprocess = DotPreprocessMode.None,
            double claheClipLimit = 3.0,
            int morphKernelSize = 15,
            DotThresholdMode thresholdMode = DotThresholdMode.Otsu,
            int adaptiveBlockSize = 25,
            double adaptiveC = 5.0)
        {
            var result = new DotClusterResult();
            if (roi == null || roi.Empty()) return result;

            // Grayscale 변환
            using var gray = new Mat();
            if (roi.Channels() == 1) roi.CopyTo(gray);
            else if (roi.Channels() == 4) Cv2.CvtColor(roi, gray, ColorConversionCodes.BGRA2GRAY);
            else Cv2.CvtColor(roi, gray, ColorConversionCodes.BGR2GRAY);

            // 노이즈 감소
            using var blurred = new Mat();
            Cv2.GaussianBlur(gray, blurred, new Size(5, 5), 0);

            // 저대비 이미지를 위한 ROI 전처리
            using var enhanced = ApplyPreprocess(blurred, preprocess, claheClipLimit, morphKernelSize);

            List<Point2f> centers;
            if (method == DotDetectionMethod.HoughCircles)
            {
                centers = DetectWithHough(enhanced, minArea, maxArea);
            }
            else
            {
                centers = DetectWithBlob(enhanced, minArea, maxArea, circularityThreshold,
                    thresholdMode, adaptiveBlockSize, adaptiveC, preprocess);
            }

            // 인접 dot 병합 (minDotDistance 이하)
            centers = MergeClose(centers, minDotDistance);
            result.DotPositions = centers;
            result.DotCount = centers.Count;

            // 각도 계산 (dot 2개 이상 필요)
            if (centers.Count >= 2)
            {
                result.AngleComputed = true;
                result.PatternAngle = metric == DotPatternMetric.LongestLineAngle
                    ? ComputeLongestLineAngle(centers)
                    : ComputeMainAxisAngle(centers);
            }

            return result;
        }

        // ── 저대비 이미지를 위한 ROI 전처리 ──
        private static Mat ApplyPreprocess(Mat src, DotPreprocessMode mode, double clipLimit, int kernelSize)
        {
            var dst = new Mat();
            switch (mode)
            {
                case DotPreprocessMode.Clahe:
                {
                    using var clahe = Cv2.CreateCLAHE(clipLimit, new Size(8, 8));
                    clahe.Apply(src, dst);
                    break;
                }
                case DotPreprocessMode.TopHat:
                {
                    // 원본 - Opening = 주변보다 작고 밝은 구조
                    int k = Math.Max(3, kernelSize | 1); // 홀수 보정
                    var kernel = Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(k, k));
                    Cv2.MorphologyEx(src, dst, MorphTypes.TopHat, kernel);
                    break;
                }
                case DotPreprocessMode.BottomHat:
                {
                    // Closing - 원본 = 주변보다 작고 어두운 구조
                    int k = Math.Max(3, kernelSize | 1);
                    var kernel = Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(k, k));
                    Cv2.MorphologyEx(src, dst, MorphTypes.BlackHat, kernel);
                    break;
                }
                default:
                    src.CopyTo(dst);
                    break;
            }
            return dst;
        }

        // ── Blob (contour + circularity) ──
        private static List<Point2f> DetectWithBlob(Mat blurred, int minArea, int maxArea, double circThresh,
            DotThresholdMode thresholdMode, int adaptiveBlockSize, double adaptiveC, DotPreprocessMode preprocess)
        {
            using var thresh = new Mat();

            // Top-hat / Bottom-hat 결과는 이미 "관심 구조만 밝게" 강조된 상태라
            // BinaryInv가 아닌 Binary로 Otsu/Adaptive를 적용해야 dot이 전경(흰색)이 된다.
            bool preprocessProducesHighlight =
                preprocess == DotPreprocessMode.TopHat || preprocess == DotPreprocessMode.BottomHat;
            ThresholdTypes binaryType = preprocessProducesHighlight ? ThresholdTypes.Binary : ThresholdTypes.BinaryInv;

            if (thresholdMode == DotThresholdMode.Adaptive)
            {
                int block = Math.Max(3, adaptiveBlockSize | 1);
                Cv2.AdaptiveThreshold(blurred, thresh, 255,
                    AdaptiveThresholdTypes.GaussianC, binaryType,
                    block, adaptiveC);
            }
            else
            {
                Cv2.Threshold(blurred, thresh, 0, 255, binaryType | ThresholdTypes.Otsu);
            }

            using var morph = new Mat();
            var kernel = Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(3, 3));
            Cv2.MorphologyEx(thresh, morph, MorphTypes.Open, kernel);

            Cv2.FindContours(morph, out var contours, out _,
                RetrievalModes.External, ContourApproximationModes.ApproxSimple);

            var centers = new List<Point2f>();
            foreach (var c in contours)
            {
                double area = Cv2.ContourArea(c);
                if (area < minArea || area > maxArea) continue;

                double perimeter = Cv2.ArcLength(c, true);
                if (perimeter <= 0) continue;

                double circularity = 4 * Math.PI * area / (perimeter * perimeter);
                if (circularity < circThresh) continue;

                var m = Cv2.Moments(c);
                if (m.M00 > 0)
                    centers.Add(new Point2f((float)(m.M10 / m.M00), (float)(m.M01 / m.M00)));
            }
            return centers;
        }

        // ── HoughCircles ──
        private static List<Point2f> DetectWithHough(Mat blurred, int minArea, int maxArea)
        {
            // 면적 범위 → 반지름 범위로 변환 (원 면적 = πr²)
            int minRadius = Math.Max(1, (int)Math.Sqrt(minArea / Math.PI));
            int maxRadius = Math.Max(minRadius + 1, (int)Math.Sqrt(maxArea / Math.PI));

            var circles = Cv2.HoughCircles(blurred, HoughModes.Gradient,
                dp: 1.0, minDist: minRadius * 2,
                param1: 100, param2: 20,
                minRadius: minRadius, maxRadius: maxRadius);

            return circles.Select(c => new Point2f(c.Center.X, c.Center.Y)).ToList();
        }

        // ── 인접점 병합 ──
        private static List<Point2f> MergeClose(List<Point2f> pts, int minDist)
        {
            if (minDist <= 0 || pts.Count < 2) return pts;
            var merged = new List<Point2f>();
            var used = new bool[pts.Count];
            double minDistSq = (double)minDist * minDist;

            for (int i = 0; i < pts.Count; i++)
            {
                if (used[i]) continue;
                float sx = pts[i].X, sy = pts[i].Y;
                int count = 1;
                used[i] = true;
                for (int j = i + 1; j < pts.Count; j++)
                {
                    if (used[j]) continue;
                    float dx = pts[i].X - pts[j].X;
                    float dy = pts[i].Y - pts[j].Y;
                    if (dx * dx + dy * dy <= minDistSq)
                    {
                        sx += pts[j].X; sy += pts[j].Y;
                        count++;
                        used[j] = true;
                    }
                }
                merged.Add(new Point2f(sx / count, sy / count));
            }
            return merged;
        }

        // ── MainAxisAngle: PCA 주축 방향 (도 단위, -90~+90) ──
        private static double ComputeMainAxisAngle(List<Point2f> pts)
        {
            if (pts.Count < 2) return 0.0;

            // 평균점 계산
            double mx = pts.Average(p => p.X);
            double my = pts.Average(p => p.Y);

            // 2×2 공분산 행렬
            double sxx = 0, syy = 0, sxy = 0;
            foreach (var p in pts)
            {
                double dx = p.X - mx;
                double dy = p.Y - my;
                sxx += dx * dx;
                syy += dy * dy;
                sxy += dx * dy;
            }

            // 주축 각도: atan2(2*sxy, sxx - syy) / 2
            double angleRad = 0.5 * Math.Atan2(2 * sxy, sxx - syy);
            double angleDeg = angleRad * 180.0 / Math.PI;
            // -90 ~ +90 범위로 정규화
            if (angleDeg > 90) angleDeg -= 180;
            if (angleDeg < -90) angleDeg += 180;
            return angleDeg;
        }

        // ── LongestLineAngle: 가장 멀리 떨어진 두 점의 연결선 각도 ──
        private static double ComputeLongestLineAngle(List<Point2f> pts)
        {
            if (pts.Count < 2) return 0.0;

            int bi = 0, bj = 1;
            double bestSq = -1;
            for (int i = 0; i < pts.Count; i++)
                for (int j = i + 1; j < pts.Count; j++)
                {
                    double dx = pts[i].X - pts[j].X;
                    double dy = pts[i].Y - pts[j].Y;
                    double d2 = dx * dx + dy * dy;
                    if (d2 > bestSq) { bestSq = d2; bi = i; bj = j; }
                }

            double angleRad = Math.Atan2(pts[bj].Y - pts[bi].Y, pts[bj].X - pts[bi].X);
            double angleDeg = angleRad * 180.0 / Math.PI;
            // -90 ~ +90 범위로 정규화 (방향성 무시)
            while (angleDeg > 90) angleDeg -= 180;
            while (angleDeg < -90) angleDeg += 180;
            return angleDeg;
        }
    }
}
