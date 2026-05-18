using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Linq;

namespace VMS.VisionSetup.VisionTools.CodeReading
{
    /// <summary>
    /// ISO/IEC 15415 + AIM DPM-1-2006 등급(A~F = 4~0).
    /// </summary>
    public enum CodeQualityGrade { A = 4, B = 3, C = 2, D = 1, F = 0 }

    /// <summary>
    /// 단일 DataMatrix 심볼에 대한 품질 측정 결과.
    /// </summary>
    public class DataMatrixQualityReport
    {
        public CodeQualityGrade OverallGrade { get; set; } = CodeQualityGrade.F;
        public CodeQualityGrade SymbolContrastGrade { get; set; } = CodeQualityGrade.F;
        public CodeQualityGrade ModulationGrade { get; set; } = CodeQualityGrade.F;
        public CodeQualityGrade FixedPatternDamageGrade { get; set; } = CodeQualityGrade.F;
        public CodeQualityGrade AxialNonuniformityGrade { get; set; } = CodeQualityGrade.F;
        public CodeQualityGrade DecodeGrade { get; set; } = CodeQualityGrade.F;

        public double SymbolContrast { get; set; }
        public double Modulation { get; set; }
        public double FixedPatternDamage { get; set; }
        public double AxialNonuniformity { get; set; }
        public double PixelsPerModule { get; set; }
        public int SymbolSize { get; set; }

        public string FormatSummary() =>
            $"Grade: {OverallGrade} | SC={SymbolContrast:F2}({SymbolContrastGrade}) " +
            $"MOD={Modulation:F2}({ModulationGrade}) FPD={FixedPatternDamage:F2}({FixedPatternDamageGrade}) " +
            $"AN={AxialNonuniformity:F3}({AxialNonuniformityGrade}) PPM={PixelsPerModule:F1} N={SymbolSize}";
    }

    /// <summary>
    /// DataMatrix 품질 등급 계산기 (ISO/IEC 15415 간소화).
    /// 우선 bbox(locator 결과)로 corner를 재추정하고, 없으면 ZXing ResultPoints에 fallback.
    /// 전체 사양 준수가 아닌 산업 현장 추세 모니터링용 근사치.
    /// </summary>
    public static class DataMatrixQualityGrader
    {
        // DataMatrix 정사각형 표준 크기 (n × n 모듈, 클럭트랙/L-finder 포함)
        private static readonly int[] SquareSizes =
            { 10, 12, 14, 16, 18, 20, 22, 24, 26, 32, 36, 40, 44, 48, 52, 64, 72, 80, 88, 96, 104, 120, 132, 144 };

        private const int NormalizedSize = 256;

        /// <summary>
        /// 신규 진입점 — bbox hint를 받아 corner 재추정 우선, ResultPoints는 fallback.
        /// </summary>
        public static DataMatrixQualityReport Grade(Mat grayImage, Rect? bboxHint, Point2f[]? pointsHint, bool decoded)
        {
            var report = new DataMatrixQualityReport
            {
                DecodeGrade = decoded ? CodeQualityGrade.A : CodeQualityGrade.F
            };

            if (grayImage == null || grayImage.Empty()) return report;

            // 1) Corner 추정: bbox 우선, 실패 시 pointsHint
            Point2f[]? corners = null;
            if (bboxHint.HasValue)
                corners = TryFindCornersInBbox(grayImage, bboxHint.Value);
            if (corners == null && pointsHint != null && pointsHint.Length >= 3)
                corners = NormalizeCornersFromPoints(pointsHint);
            if (corners == null) return report;

            return ComputeMetrics(grayImage, corners, report);
        }

        /// <summary>레거시 호환 — ZXing ResultPoints만으로 등급 산출.</summary>
        public static DataMatrixQualityReport Grade(Mat grayImage, Point2f[] points, bool decoded)
            => Grade(grayImage, null, points, decoded);

        // ── Core 등급 산출 ──
        private static DataMatrixQualityReport ComputeMetrics(Mat gray, Point2f[] corners, DataMatrixQualityReport report)
        {
            double sidePixelsAvg = (Distance(corners[0], corners[1]) + Distance(corners[1], corners[2])
                                  + Distance(corners[2], corners[3]) + Distance(corners[3], corners[0])) / 4.0;

            using var normalized = WarpToNormalized(gray, corners);

            // L-finder 방향 + SymbolSize 동시 탐색.
            // 4가지 회전 × 표준 후보 N 모두 평가하여 L-finder × 클럭트랙 곱 점수 최대인
            // (회전, N) 쌍을 선택. edge 밝기 휴리스틱만으로는 일부 이미지에서 오판.
            int bestRot = 0;
            int symbolSize = 16;
            double bestScore = double.MinValue;
            for (int rot = 0; rot < 4; rot++)
            {
                using var oriented = ApplyRotation(normalized, rot);
                foreach (var n in SquareSizes)
                {
                    var modulesAt = SampleModules(oriented, n);
                    double score = ScoreCandidateSize(modulesAt, n);
                    if (score > bestScore) { bestScore = score; symbolSize = n; bestRot = rot; }
                }
            }
            using var finalOriented = ApplyRotation(normalized, bestRot);
            report.SymbolSize = symbolSize;
            report.PixelsPerModule = sidePixelsAvg / symbolSize;

            byte[,] modules = SampleModules(finalOriented, symbolSize);

            // Symbol Contrast
            byte rMin = 255, rMax = 0;
            for (int y = 0; y < symbolSize; y++)
                for (int x = 0; x < symbolSize; x++)
                {
                    var v = modules[y, x];
                    if (v < rMin) rMin = v;
                    if (v > rMax) rMax = v;
                }
            report.SymbolContrast = (rMax - rMin) / 255.0;
            report.SymbolContrastGrade = GradeSymbolContrast(report.SymbolContrast);

            // Modulation — 모듈별 평균이 GT에서 얼마나 떨어졌는지의 최솟값
            // 노이즈 모듈 하나에 좌우되지 않도록 하위 5% percentile 사용
            double globalThreshold = (rMax + rMin) / 2.0;
            double contrast = Math.Max(1, rMax - rMin);
            var mods = new List<double>(symbolSize * symbolSize);
            for (int y = 0; y < symbolSize; y++)
                for (int x = 0; x < symbolSize; x++)
                    mods.Add(2.0 * Math.Abs(modules[y, x] - globalThreshold) / contrast);
            mods.Sort();
            // 5th percentile — 단일 outlier 무시
            int p5 = Math.Max(0, (int)(mods.Count * 0.05));
            report.Modulation = mods[p5];
            report.ModulationGrade = GradeModulation(report.Modulation);

            // Fixed Pattern Damage — L-finder + clock track 무결성
            report.FixedPatternDamage = ComputeFixedPatternDamage(modules, symbolSize, globalThreshold);
            report.FixedPatternDamageGrade = GradeFixedPatternDamage(report.FixedPatternDamage);

            // Axial Non-uniformity
            double xModule = (Distance(corners[0], corners[1]) + Distance(corners[3], corners[2])) / 2.0 / symbolSize;
            double yModule = (Distance(corners[0], corners[3]) + Distance(corners[1], corners[2])) / 2.0 / symbolSize;
            double mean = (xModule + yModule) / 2.0;
            report.AxialNonuniformity = mean > 0 ? Math.Abs(xModule - yModule) / mean : 1.0;
            report.AxialNonuniformityGrade = GradeAxialNonuniformity(report.AxialNonuniformity);

            report.OverallGrade = (CodeQualityGrade)Math.Min(
                Math.Min((int)report.DecodeGrade, (int)report.SymbolContrastGrade),
                Math.Min(Math.Min((int)report.ModulationGrade, (int)report.FixedPatternDamageGrade),
                    (int)report.AxialNonuniformityGrade));
            return report;
        }

        // ── bbox 기반 corner 재추정 ──
        // 1) bbox sub-image 추출 → adaptive threshold → morph close → 최대 contour
        // 2) minAreaRect → 4 corners (회전 보존)
        // 3) 원본 이미지 좌표계로 평행이동 + 시계방향 TL 시작 정렬
        private static Point2f[]? TryFindCornersInBbox(Mat gray, Rect bbox)
        {
            int padX = bbox.Width / 10;
            int padY = bbox.Height / 10;
            int x = Math.Max(0, bbox.X - padX);
            int y = Math.Max(0, bbox.Y - padY);
            int w = Math.Min(gray.Width - x, bbox.Width + 2 * padX);
            int h = Math.Min(gray.Height - y, bbox.Height + 2 * padY);
            if (w <= 8 || h <= 8) return null;
            var paddedBbox = new Rect(x, y, w, h);

            using var sub = new Mat(gray, paddedBbox);
            int shortSide = Math.Min(sub.Width, sub.Height);
            int blockSize = Math.Max(11, (shortSide / 20) | 1);

            using var bin = new Mat();
            Cv2.AdaptiveThreshold(sub, bin, 255, AdaptiveThresholdTypes.MeanC,
                ThresholdTypes.BinaryInv, blockSize, 5);

            using var kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(3, 3));
            using var closed = new Mat();
            Cv2.MorphologyEx(bin, closed, MorphTypes.Close, kernel, iterations: 2);

            Cv2.FindContours(closed, out Point[][] contours, out _,
                RetrievalModes.External, ContourApproximationModes.ApproxSimple);
            if (contours.Length == 0) return null;

            // 가장 큰 contour 선택 — DM은 close 후 단일 큰 blob이 되어야 함
            Point[]? best = null;
            double bestArea = 0;
            int minArea = sub.Width * sub.Height / 16; // bbox의 1/16 이상
            foreach (var c in contours)
            {
                double a = Cv2.ContourArea(c);
                if (a < minArea) continue;
                if (a > bestArea) { bestArea = a; best = c; }
            }
            if (best == null) return null;

            var minRect = Cv2.MinAreaRect(best);
            Point2f[] boxPts = minRect.Points();

            // 원본 이미지 좌표계로 평행이동
            for (int i = 0; i < boxPts.Length; i++)
                boxPts[i] = new Point2f(boxPts[i].X + paddedBbox.X, boxPts[i].Y + paddedBbox.Y);

            return SortClockwise(boxPts);
        }

        // ── ZXing points fallback ──
        private static Point2f[]? NormalizeCornersFromPoints(Point2f[] pts)
        {
            Point2f[] four;
            if (pts.Length == 3)
            {
                // ZXing DM은 보통 (bottom-left, top-left, top-right) L-finder 3점
                var bl = pts[0]; var tl = pts[1]; var tr = pts[2];
                var br = new Point2f(tr.X + bl.X - tl.X, tr.Y + bl.Y - tl.Y);
                four = new[] { tl, tr, br, bl };
            }
            else if (pts.Length >= 4) four = pts.Take(4).ToArray();
            else return null;
            return SortClockwise(four);
        }

        private static Point2f[] SortClockwise(Point2f[] pts)
        {
            float cx = pts.Average(p => p.X);
            float cy = pts.Average(p => p.Y);
            var sorted = pts.OrderBy(p => Math.Atan2(p.Y - cy, p.X - cx)).ToArray();
            int startIdx = 0;
            float bestSum = float.MaxValue;
            for (int i = 0; i < 4; i++)
            {
                var sum = sorted[i].X + sorted[i].Y;
                if (sum < bestSum) { bestSum = sum; startIdx = i; }
            }
            return new[] { sorted[startIdx], sorted[(startIdx + 1) % 4],
                           sorted[(startIdx + 2) % 4], sorted[(startIdx + 3) % 4] };
        }

        private static Mat WarpToNormalized(Mat gray, Point2f[] corners)
        {
            var dst = new[]
            {
                new Point2f(0, 0),
                new Point2f(NormalizedSize - 1, 0),
                new Point2f(NormalizedSize - 1, NormalizedSize - 1),
                new Point2f(0, NormalizedSize - 1)
            };
            using var H = Cv2.GetPerspectiveTransform(corners, dst);
            var result = new Mat();
            Cv2.WarpPerspective(gray, result, H, new Size(NormalizedSize, NormalizedSize),
                InterpolationFlags.Linear, BorderTypes.Replicate);
            return result;
        }

        private static Mat ApplyRotation(Mat src, int rotationCase)
        {
            // 케이스 매핑 — DetectLFinderRotation 반환값과 동기화:
            // 0: L at left+bottom (BL) — 그대로
            // 1: L at right+bottom (BR) → 90° CW (BR→BL)
            // 2: L at top+right (TR)    → 180° (TR→BL)
            // 3: L at top+left (TL)     → 90° CCW (TL→BL)
            rotationCase &= 3;
            if (rotationCase == 0) return src.Clone();
            var dst = new Mat();
            var flag = rotationCase switch
            {
                1 => RotateFlags.Rotate90Clockwise,
                2 => RotateFlags.Rotate180,
                _ => RotateFlags.Rotate90Counterclockwise
            };
            Cv2.Rotate(src, dst, flag);
            return dst;
        }

        // ── 후보 (회전, N) 점수 ──
        // Otsu 임계값으로 모듈 binarize 후 L-finder(좌측+하단) all-dark × 클럭트랙(상단+우측) 교번
        // 두 패턴 적합도의 곱. ComputeMetrics가 4 회전 × SquareSizes 전체에서 최댓값 탐색.
        private static double ScoreCandidateSize(byte[,] modules, int n)
        {
            int otsu = ComputeOtsuThreshold(modules, n);

            // L-finder: 좌측 컬럼 + 하단 행 모두 dark여야 함
            int lfCorrect = 0;
            for (int y = 0; y < n; y++) if (modules[y, 0] < otsu) lfCorrect++;
            for (int x = 0; x < n; x++) if (modules[n - 1, x] < otsu) lfCorrect++;
            double lfScore = (double)lfCorrect / (2 * n);

            // 클럭트랙: 상단 행 (짝수 dark) + 우측 컬럼 (홀수 dark)
            int ctCorrect = 0;
            for (int x = 0; x < n; x++)
            {
                bool expectDark = (x % 2 == 0);
                if ((modules[0, x] < otsu) == expectDark) ctCorrect++;
            }
            for (int y = 0; y < n; y++)
            {
                bool expectDark = (y % 2 == 1);
                if ((modules[y, n - 1] < otsu) == expectDark) ctCorrect++;
            }
            double ctScore = (double)ctCorrect / (2 * n);

            // 곱셈 — 두 패턴 모두 좋아야 높은 점수 (둘 중 하나라도 망가지면 0에 가까움)
            return lfScore * ctScore;
        }

        private static int ComputeOtsuThreshold(byte[,] modules, int n)
        {
            var hist = new int[256];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                    hist[modules[y, x]]++;
            int total = n * n;
            double sum = 0;
            for (int i = 0; i < 256; i++) sum += i * hist[i];
            double sumB = 0;
            int wB = 0;
            double maxVar = 0;
            int threshold = 127;
            for (int t = 0; t < 256; t++)
            {
                wB += hist[t];
                if (wB == 0) continue;
                int wF = total - wB;
                if (wF == 0) break;
                sumB += t * hist[t];
                double mB = sumB / wB;
                double mF = (sum - sumB) / wF;
                double v = (double)wB * wF * (mB - mF) * (mB - mF);
                if (v > maxVar) { maxVar = v; threshold = t; }
            }
            return threshold;
        }

        // 모듈 중앙 ±18% 영역의 평균 픽셀값을 샘플링
        private static byte[,] SampleModules(Mat normalized, int n)
        {
            var arr = new byte[n, n];
            double cell = (double)NormalizedSize / n;
            int half = Math.Max(1, (int)(cell * 0.18));

            unsafe
            {
                byte* data = (byte*)normalized.Data;
                long stride = normalized.Step();
                for (int y = 0; y < n; y++)
                {
                    int cy = (int)((y + 0.5) * cell);
                    int y0 = Math.Max(0, cy - half);
                    int y1 = Math.Min(NormalizedSize - 1, cy + half);
                    for (int x = 0; x < n; x++)
                    {
                        int cx = (int)((x + 0.5) * cell);
                        int x0 = Math.Max(0, cx - half);
                        int x1 = Math.Min(NormalizedSize - 1, cx + half);
                        int sum = 0, count = 0;
                        for (int yy = y0; yy <= y1; yy++)
                            for (int xx = x0; xx <= x1; xx++)
                            { sum += data[yy * stride + xx]; count++; }
                        arr[y, x] = (byte)(sum / Math.Max(1, count));
                    }
                }
            }
            return arr;
        }

        // L-finder(좌측+하단) all dark + 클럭 트랙(상단+우측) alternating
        // 정상 비율 (1.0이 최상)
        private static double ComputeFixedPatternDamage(byte[,] modules, int n, double globalThreshold)
        {
            int correct = 0, total = 0;
            for (int y = 0; y < n; y++)
            {
                if (modules[y, 0] < globalThreshold) correct++; total++;          // 좌측
                bool topRight = (y % 2 == 1);
                if ((modules[y, n - 1] < globalThreshold) == topRight) correct++; total++;
            }
            for (int x = 0; x < n; x++)
            {
                if (modules[n - 1, x] < globalThreshold) correct++; total++;       // 하단
                bool topAlt = (x % 2 == 0);
                if ((modules[0, x] < globalThreshold) == topAlt) correct++; total++;
            }
            return (double)correct / total;
        }

        // ── ISO/IEC 15415 등급 임계값 (단순화) ──
        private static CodeQualityGrade GradeSymbolContrast(double sc) =>
            sc >= 0.70 ? CodeQualityGrade.A :
            sc >= 0.55 ? CodeQualityGrade.B :
            sc >= 0.40 ? CodeQualityGrade.C :
            sc >= 0.20 ? CodeQualityGrade.D : CodeQualityGrade.F;

        private static CodeQualityGrade GradeModulation(double mod) =>
            mod >= 0.50 ? CodeQualityGrade.A :
            mod >= 0.40 ? CodeQualityGrade.B :
            mod >= 0.30 ? CodeQualityGrade.C :
            mod >= 0.20 ? CodeQualityGrade.D : CodeQualityGrade.F;

        private static CodeQualityGrade GradeFixedPatternDamage(double fpd) =>
            fpd >= 0.95 ? CodeQualityGrade.A :
            fpd >= 0.90 ? CodeQualityGrade.B :
            fpd >= 0.85 ? CodeQualityGrade.C :
            fpd >= 0.75 ? CodeQualityGrade.D : CodeQualityGrade.F;

        private static CodeQualityGrade GradeAxialNonuniformity(double an) =>
            an <= 0.06 ? CodeQualityGrade.A :
            an <= 0.08 ? CodeQualityGrade.B :
            an <= 0.10 ? CodeQualityGrade.C :
            an <= 0.12 ? CodeQualityGrade.D : CodeQualityGrade.F;

        private static double Distance(Point2f a, Point2f b)
        {
            double dx = a.X - b.X, dy = a.Y - b.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }
    }
}
