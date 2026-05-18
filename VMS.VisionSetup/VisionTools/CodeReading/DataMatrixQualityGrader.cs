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
    /// 4개 꼭짓점(또는 L-finder 3점 + 보완)을 받아 심볼을 정규화 → 모듈 그리드 샘플링 → 등급 산출.
    /// 전체 사양 준수가 아닌 산업 현장 추세 모니터링용 근사치.
    /// </summary>
    public static class DataMatrixQualityGrader
    {
        // DataMatrix 정사각형 표준 크기 (n × n 모듈, 클럭트랙/L-finder 포함)
        private static readonly int[] SquareSizes =
            { 10, 12, 14, 16, 18, 20, 22, 24, 26, 32, 36, 40, 44, 48, 52, 64, 72, 80, 88, 96, 104, 120, 132, 144 };

        private const int NormalizedSize = 256;

        /// <summary>
        /// 등급 산출. points는 3 또는 4개 (이미지 원본 좌표계).
        /// 디코딩 실패 시 decoded=false로 호출하면 DecodeGrade=F + 부분 측정만 수행.
        /// </summary>
        public static DataMatrixQualityReport Grade(Mat grayImage, Point2f[] points, bool decoded)
        {
            var report = new DataMatrixQualityReport
            {
                DecodeGrade = decoded ? CodeQualityGrade.A : CodeQualityGrade.F
            };

            if (grayImage == null || grayImage.Empty() || points == null || points.Length < 3)
                return report;

            // 1) 4개 꼭짓점 정규화 (시계방향 TL/TR/BR/BL)
            var corners = NormalizeCorners(points);
            if (corners == null) return report;

            // 2) PPM (원본 픽셀 기준 한 변 길이의 평균)
            double sidePixelsAvg = (Distance(corners[0], corners[1]) + Distance(corners[1], corners[2])
                                  + Distance(corners[2], corners[3]) + Distance(corners[3], corners[0])) / 4.0;

            // 3) Warp perspective → 정규화 정사각형 이미지
            using var normalized = WarpToNormalized(grayImage, corners);

            // 4) 심볼 모듈 크기 추정 (clock track 무결성 기준으로 best fit)
            int symbolSize = EstimateSymbolSize(normalized);
            report.SymbolSize = symbolSize;
            report.PixelsPerModule = sidePixelsAvg / symbolSize;

            // 5) 모듈별 평균 reflectance 샘플링
            byte[,] moduleValues = SampleModules(normalized, symbolSize);

            // 6) Symbol Contrast
            byte rMin = 255, rMax = 0;
            for (int y = 0; y < symbolSize; y++)
                for (int x = 0; x < symbolSize; x++)
                {
                    var v = moduleValues[y, x];
                    if (v < rMin) rMin = v;
                    if (v > rMax) rMax = v;
                }
            report.SymbolContrast = (rMax - rMin) / 255.0;
            report.SymbolContrastGrade = GradeSymbolContrast(report.SymbolContrast);

            // 7) Modulation — 각 모듈의 평균 대비 임계값과의 거리
            double globalThreshold = (rMax + rMin) / 2.0;
            double contrast = Math.Max(1, rMax - rMin);
            double minMod = 1.0;
            for (int y = 0; y < symbolSize; y++)
                for (int x = 0; x < symbolSize; x++)
                {
                    double mod = 2.0 * Math.Abs(moduleValues[y, x] - globalThreshold) / contrast;
                    if (mod < minMod) minMod = mod;
                }
            report.Modulation = minMod;
            report.ModulationGrade = GradeModulation(minMod);

            // 8) Fixed Pattern Damage — L-finder + 클럭 트랙 무결성
            report.FixedPatternDamage = ComputeFixedPatternDamage(moduleValues, symbolSize, globalThreshold);
            report.FixedPatternDamageGrade = GradeFixedPatternDamage(report.FixedPatternDamage);

            // 9) Axial Non-uniformity — 가로/세로 모듈 폭 차이
            double xModule = (Distance(corners[0], corners[1]) + Distance(corners[3], corners[2])) / 2.0 / symbolSize;
            double yModule = (Distance(corners[0], corners[3]) + Distance(corners[1], corners[2])) / 2.0 / symbolSize;
            double mean = (xModule + yModule) / 2.0;
            report.AxialNonuniformity = mean > 0 ? Math.Abs(xModule - yModule) / mean : 1.0;
            report.AxialNonuniformityGrade = GradeAxialNonuniformity(report.AxialNonuniformity);

            // 10) Overall — 모든 등급의 최소값
            report.OverallGrade = (CodeQualityGrade)Math.Min(
                Math.Min((int)report.DecodeGrade, (int)report.SymbolContrastGrade),
                Math.Min(Math.Min((int)report.ModulationGrade, (int)report.FixedPatternDamageGrade),
                    (int)report.AxialNonuniformityGrade));

            return report;
        }

        // ── 꼭짓점 정규화: 3점 입력 시 평행사변형으로 4번째 점 보완 → TL/TR/BR/BL 정렬 ──
        private static Point2f[]? NormalizeCorners(Point2f[] pts)
        {
            Point2f[] four;
            if (pts.Length == 3)
            {
                // ZXing DataMatrix 검출은 보통 (bottom-left, top-left, top-right) L-finder 3점
                // 4번째 = topRight + (bottomLeft - topLeft)
                var bl = pts[0]; var tl = pts[1]; var tr = pts[2];
                var br = new Point2f(tr.X + bl.X - tl.X, tr.Y + bl.Y - tl.Y);
                four = new[] { tl, tr, br, bl };
            }
            else if (pts.Length >= 4)
            {
                four = pts.Take(4).ToArray();
            }
            else
            {
                return null;
            }
            return SortClockwise(four);
        }

        private static Point2f[] SortClockwise(Point2f[] pts)
        {
            // 중심 기준 각도로 정렬, TL부터 시계방향
            float cx = pts.Average(p => p.X);
            float cy = pts.Average(p => p.Y);
            var sorted = pts.OrderBy(p => Math.Atan2(p.Y - cy, p.X - cx)).ToArray();
            // 가장 좌상단 (x+y 최소)을 시작으로 회전
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
            var src = corners;
            var dst = new[]
            {
                new Point2f(0, 0),
                new Point2f(NormalizedSize - 1, 0),
                new Point2f(NormalizedSize - 1, NormalizedSize - 1),
                new Point2f(0, NormalizedSize - 1)
            };
            using var H = Cv2.GetPerspectiveTransform(src, dst);
            var result = new Mat();
            Cv2.WarpPerspective(gray, result, H, new Size(NormalizedSize, NormalizedSize),
                InterpolationFlags.Linear, BorderTypes.Replicate);
            return result;
        }

        // ── 심볼 크기 추정: 후보 크기마다 clock track 적합도 점수 → 최댓값 선택 ──
        private static int EstimateSymbolSize(Mat normalized)
        {
            int best = 16;
            double bestScore = double.MinValue;
            foreach (var n in SquareSizes)
            {
                var modules = SampleModules(normalized, n);
                double score = ScoreClockTrack(modules, n);
                if (score > bestScore) { bestScore = score; best = n; }
            }
            return best;
        }

        // 각 모듈 중앙 ±10% 영역의 평균 픽셀값을 샘플링.
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

        // L-finder(좌측+하단)는 모두 dark, 클럭 트랙(상단+우측)은 교번. 합산 점수.
        private static double ScoreClockTrack(byte[,] modules, int n)
        {
            // 전역 임계값으로 binarize
            int sum = 0;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++) sum += modules[y, x];
            double th = (double)sum / (n * n);

            int correct = 0, total = 0;
            // 좌측 컬럼 (x=0): all dark
            for (int y = 0; y < n; y++) { if (modules[y, 0] < th) correct++; total++; }
            // 하단 행 (y=n-1): all dark
            for (int x = 0; x < n; x++) { if (modules[n - 1, x] < th) correct++; total++; }
            // 상단 행 (y=0): 교번 (짝수 x: dark, 홀수 x: light)
            for (int x = 0; x < n; x++)
            {
                bool isDark = (x % 2 == 0);
                bool isActuallyDark = modules[0, x] < th;
                if (isDark == isActuallyDark) correct++;
                total++;
            }
            // 우측 컬럼 (x=n-1): 교번
            for (int y = 0; y < n; y++)
            {
                bool isDark = (y % 2 == 1);
                bool isActuallyDark = modules[y, n - 1] < th;
                if (isDark == isActuallyDark) correct++;
                total++;
            }
            return (double)correct / total;
        }

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
            // 정상 비율 (1.0이 최상)
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
