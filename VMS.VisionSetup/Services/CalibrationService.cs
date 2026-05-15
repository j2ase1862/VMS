using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using VMS.VisionSetup.Models;

namespace VMS.VisionSetup.Services
{
    /// <summary>
    /// 카메라 캘리브레이션 서비스.
    /// CalibrationManagerWindow가 인스턴스를 보유하여 다중 장 누적 상태를 유지.
    /// 결과 메타데이터는 호출자가 VisionService.CurrentCalibrationMetadata / Recipe.Calibration에 적용.
    /// </summary>
    public class CalibrationService
    {
        private readonly List<Point2f[]> _accumulatedCorners = new();
        private Size _accumulatedImageSize;

        public int AccumulatedViewCount => _accumulatedCorners.Count;

        /// <summary>
        /// 누적된 다중 장 데이터를 초기화.
        /// </summary>
        public void ResetAccumulated()
        {
            _accumulatedCorners.Clear();
            _accumulatedImageSize = default;
        }

        /// <summary>
        /// 체커보드 캘리브레이션 실행.
        /// accumulate=true이면 검출된 코너를 누적 후 calibrateCamera에 전체 사용. false면 단일 장 단독 캘리브레이션.
        /// </summary>
        public CalibrationResult RunCheckerboard(
            Mat image, int patternCols, int patternRows, double squareSizeMm, bool accumulate)
        {
            if (image == null || image.Empty())
                return Fail("Input image is empty");

            using var gray = image.Channels() > 1
                ? image.CvtColor(ColorConversionCodes.BGR2GRAY)
                : image.Clone();

            var patternSize = new Size(patternCols, patternRows);

            if (!Cv2.FindChessboardCorners(gray, patternSize, out var corners,
                    ChessboardFlags.AdaptiveThresh | ChessboardFlags.NormalizeImage))
                return Fail("Chessboard corners not found");

            var criteria = new TermCriteria(CriteriaTypes.Eps | CriteriaTypes.MaxIter, 30, 0.001);
            var refined = Cv2.CornerSubPix(gray, corners, new Size(11, 11), new Size(-1, -1), criteria);

            if (accumulate)
            {
                if (_accumulatedCorners.Count == 0)
                    _accumulatedImageSize = gray.Size();
                else if (gray.Size() != _accumulatedImageSize)
                    return Fail($"Image size mismatch (accumulated {_accumulatedImageSize.Width}x{_accumulatedImageSize.Height}). Reset before adding different resolution.");

                _accumulatedCorners.Add(refined);
            }

            var imagePoints = accumulate
                ? _accumulatedCorners
                : new List<Point2f[]> { refined };

            var objectPoints = BuildObjectPoints(patternSize, (float)squareSizeMm);
            var objectPointsList = Enumerable.Repeat(objectPoints, imagePoints.Count).ToList();

            var cameraMatrix = new double[3, 3];
            var distCoeffs = new double[5];
            double rms = Cv2.CalibrateCamera(
                objectPointsList,
                imagePoints,
                gray.Size(),
                cameraMatrix,
                distCoeffs,
                out _, out _);

            double pixelSizeMm = EstimatePixelSizeMm(refined, patternSize, squareSizeMm);

            var meta = new CalibrationMetadata
            {
                Mode = CalibrationMode.Checkerboard,
                CameraMatrix = CalibrationMetadata.To2DJagged(cameraMatrix),
                DistortionCoeffs = distCoeffs,
                PixelSizeMm = pixelSizeMm,
                ReprojectionError = rms,
                ImageWidth = image.Width,
                ImageHeight = image.Height,
                CalibratedAt = DateTime.UtcNow,
                SourceToolName = "CalibrationManager"
            };

            var overlay = DrawCornersOverlay(image, refined, patternSize);

            return new CalibrationResult
            {
                Success = rms < 1.0,
                Message = $"RMS={rms:F3}px, PixelSize={pixelSizeMm:F4}mm/px, Views={imagePoints.Count}",
                Metadata = meta,
                Overlay = overlay,
                ViewCount = imagePoints.Count
            };
        }

        /// <summary>
        /// N-Point 캘리브레이션 — 픽셀↔mm 점쌍 4개 이상으로 평면 호모그래피 계산.
        /// 평면 객체(평탄한 작업물) 측정에 적합. 렌즈 왜곡 보정은 안 함 (Checkerboard 모드의 책임).
        /// </summary>
        public CalibrationResult RunNPoint(
            IList<(Point2d Pixel, Point2d WorldMm)> points, int imageWidth, int imageHeight)
        {
            if (points.Count < 4) return Fail($"NPoint needs at least 4 points (got {points.Count})");

            var src = points.Select(p => p.Pixel).ToArray();
            var dst = points.Select(p => p.WorldMm).ToArray();

            using var H = Cv2.FindHomography(src, dst, HomographyMethods.Ransac, 3);
            if (H == null || H.Empty()) return Fail("Homography computation failed");

            var matrix = new double[3, 3];
            for (int r = 0; r < 3; r++)
                for (int c = 0; c < 3; c++)
                    matrix[r, c] = H.At<double>(r, c);

            // 재투영 오차 (mm 단위) 계산
            double sumSq = 0;
            for (int i = 0; i < points.Count; i++)
            {
                var p = points[i].Pixel;
                double w = matrix[2, 0] * p.X + matrix[2, 1] * p.Y + matrix[2, 2];
                if (Math.Abs(w) < 1e-12) continue;
                double projX = (matrix[0, 0] * p.X + matrix[0, 1] * p.Y + matrix[0, 2]) / w;
                double projY = (matrix[1, 0] * p.X + matrix[1, 1] * p.Y + matrix[1, 2]) / w;
                double dx = projX - points[i].WorldMm.X;
                double dy = projY - points[i].WorldMm.Y;
                sumSq += dx * dx + dy * dy;
            }
            double rmsMm = Math.Sqrt(sumSq / points.Count);

            // PixelSizeMm 등방 추정 — 모든 점쌍의 평균 (mm 거리 / 픽셀 거리)
            double ratioSum = 0;
            int ratioCount = 0;
            for (int i = 0; i < points.Count - 1; i++)
            {
                for (int j = i + 1; j < points.Count; j++)
                {
                    double dxp = points[j].Pixel.X - points[i].Pixel.X;
                    double dyp = points[j].Pixel.Y - points[i].Pixel.Y;
                    double pxLen = Math.Sqrt(dxp * dxp + dyp * dyp);
                    if (pxLen < 1e-6) continue;
                    double dxm = points[j].WorldMm.X - points[i].WorldMm.X;
                    double dym = points[j].WorldMm.Y - points[i].WorldMm.Y;
                    double mmLen = Math.Sqrt(dxm * dxm + dym * dym);
                    ratioSum += mmLen / pxLen;
                    ratioCount++;
                }
            }
            double pixelSizeMm = ratioCount > 0 ? ratioSum / ratioCount : 0;

            var meta = new CalibrationMetadata
            {
                Mode = CalibrationMode.NPointToNPoint,
                HomographyPx2Mm = CalibrationMetadata.To2DJagged(matrix),
                PixelSizeMm = pixelSizeMm,
                ReprojectionError = rmsMm,
                ImageWidth = imageWidth,
                ImageHeight = imageHeight,
                CalibratedAt = DateTime.UtcNow,
                SourceToolName = "CalibrationManager"
            };

            return new CalibrationResult
            {
                Success = true,
                Message = $"NPoint: {points.Count} pts, RMS={rmsMm:F4}mm, PixelSize~{pixelSizeMm:F5}mm/px",
                Metadata = meta,
                ViewCount = points.Count
            };
        }

        /// <summary>
        /// 한 직선 위의 두 점 + 알려진 mm 길이 → PixelSizeMm 비율만 계산 (가장 단순 모드).
        /// 평면/왜곡 보정 정보는 없음. 등방 가정.
        /// </summary>
        public CalibrationResult RunSingleScale(
            Point2d p1, Point2d p2, double knownLengthMm, int imageWidth, int imageHeight)
        {
            double dx = p2.X - p1.X;
            double dy = p2.Y - p1.Y;
            double pxLen = Math.Sqrt(dx * dx + dy * dy);
            if (pxLen < 1e-6) return Fail("Two points are identical");
            if (knownLengthMm <= 0) return Fail("Known length must be positive");

            double pixelSizeMm = knownLengthMm / pxLen;

            var meta = new CalibrationMetadata
            {
                Mode = CalibrationMode.SinglePointScale,
                PixelSizeMm = pixelSizeMm,
                ReprojectionError = 0,
                ImageWidth = imageWidth,
                ImageHeight = imageHeight,
                CalibratedAt = DateTime.UtcNow,
                SourceToolName = "CalibrationManager"
            };

            return new CalibrationResult
            {
                Success = true,
                Message = $"Scale: {pxLen:F1}px ↔ {knownLengthMm}mm → {pixelSizeMm:F5} mm/px",
                Metadata = meta,
                ViewCount = 1
            };
        }

        private static Point3f[] BuildObjectPoints(Size patternSize, float squareSizeMm)
        {
            var pts = new Point3f[patternSize.Width * patternSize.Height];
            int i = 0;
            for (int r = 0; r < patternSize.Height; r++)
                for (int c = 0; c < patternSize.Width; c++)
                    pts[i++] = new Point3f(c * squareSizeMm, r * squareSizeMm, 0f);
            return pts;
        }

        private static double EstimatePixelSizeMm(Point2f[] corners, Size patternSize, double squareSizeMm)
        {
            int cols = patternSize.Width;
            if (corners.Length < cols) return 0.0;
            double sumPx = 0;
            for (int c = 0; c < cols - 1; c++)
            {
                var dx = corners[c + 1].X - corners[c].X;
                var dy = corners[c + 1].Y - corners[c].Y;
                sumPx += Math.Sqrt(dx * dx + dy * dy);
            }
            double avgPx = sumPx / (cols - 1);
            return avgPx > 1e-6 ? squareSizeMm / avgPx : 0.0;
        }

        private static Mat DrawCornersOverlay(Mat input, Point2f[] corners, Size patternSize)
        {
            var overlay = input.Channels() >= 3
                ? input.Clone()
                : input.CvtColor(ColorConversionCodes.GRAY2BGR);
            Cv2.DrawChessboardCorners(overlay, patternSize, corners, true);
            return overlay;
        }

        private static CalibrationResult Fail(string message) => new()
        {
            Success = false,
            Message = message
        };
    }

    /// <summary>
    /// CalibrationService 실행 결과.
    /// </summary>
    public class CalibrationResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public CalibrationMetadata? Metadata { get; set; }
        public Mat? Overlay { get; set; }
        public int ViewCount { get; set; }
    }
}
