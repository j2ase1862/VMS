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
            // 평면 타겟 한 장은 수학적으로 풀리지 않아 OpenCV 가 예외를 던진다 — TryCalibrateCamera 참조.
            if (!TryCalibrateCamera(objectPointsList, imagePoints, gray.Size(),
                    cameraMatrix, distCoeffs, out double rms, out var calibError))
                return Fail(calibError!);

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
        /// 원형 그리드(도트) 캘리브레이션 — 체커보드와 같은 결과를 낸다(내부 파라미터 + 왜곡 계수).
        ///
        /// <para><b>왜 필요한가.</b> 산업용 캘리브레이션 타겟은 원형 그리드인 경우가 많고
        /// (예: CGB-020 5×4-20mm), 초점이 약간 흐려도 원의 무게중심은 안정적으로 잡혀 현장에서
        /// 유리하다. 예전에는 체커보드만 지원해 그런 타겟으로는 아예 캘리브레이션을 할 수 없었다.</para>
        ///
        /// <para><b>간격(spacing)의 뜻이 배치에 따라 다르다.</b>
        /// 대칭 배열은 이웃한 원의 <b>중심 간 거리</b> 그대로지만, 비대칭(엇갈린) 배열은 OpenCV 관례상
        /// 같은 행에서 한 칸 건너뛴 원까지가 2×spacing 이 되도록 좌표를 만든다 —
        /// 즉 <b>엇갈린 이웃 행까지의 가로 방향 거리</b>가 spacing 이다. 타겟 사양서의 값이
        /// 어느 쪽인지 확인해야 mm 환산이 맞는다.</para>
        /// </summary>
        /// <param name="patternCols">한 행의 원 개수 (비대칭이면 OpenCV 관례에 따라 두 행을 합친 열 수).</param>
        /// <param name="patternRows">행 개수.</param>
        /// <param name="spacingMm">원 중심 간 간격(mm) — 위 설명 참조.</param>
        /// <param name="asymmetric">행이 서로 엇갈린 배열이면 true.</param>
        public CalibrationResult RunCirclesGrid(
            Mat image, int patternCols, int patternRows, double spacingMm, bool asymmetric, bool accumulate)
        {
            if (image == null || image.Empty())
                return Fail("Input image is empty");

            using var gray = image.Channels() > 1
                ? image.CvtColor(ColorConversionCodes.BGR2GRAY)
                : image.Clone();

            var patternSize = new Size(patternCols, patternRows);
            var flags = asymmetric
                ? FindCirclesGridFlags.AsymmetricGrid
                : FindCirclesGridFlags.SymmetricGrid;

            // 밝은 배경의 검은 원이 기본. 반대(어두운 배경의 밝은 원)면 반전해 한 번 더 시도한다 —
            // 현장 타겟은 둘 다 쓰이고, 사용자가 그 차이를 알 이유가 없다.
            int expected = patternCols * patternRows;
            var centers = TryFindCircles(gray, patternSize, flags, expected);
            if (centers == null)
            {
                using var inverted = new Mat();
                Cv2.BitwiseNot(gray, inverted);
                centers = TryFindCircles(inverted, patternSize, flags, expected);
            }

            if (centers == null)
            {
                var layout = asymmetric ? "비대칭(엇갈린)" : "대칭";
                return Fail(
                    $"원형 그리드를 찾지 못했습니다 ({patternCols}×{patternRows}, {layout}). " +
                    "행·열 개수와 배열 종류(대칭/비대칭)를 확인하세요. " +
                    "비대칭 배열은 열 개수를 두 행 합쳐 세는 것이 OpenCV 관례입니다.");
            }

            if (accumulate)
            {
                if (_accumulatedCorners.Count == 0)
                    _accumulatedImageSize = gray.Size();
                else if (gray.Size() != _accumulatedImageSize)
                    return Fail($"Image size mismatch (accumulated {_accumulatedImageSize.Width}x{_accumulatedImageSize.Height}). Reset before adding different resolution.");

                _accumulatedCorners.Add(centers);
            }

            var imagePoints = accumulate
                ? _accumulatedCorners
                : new List<Point2f[]> { centers };

            var objectPoints = asymmetric
                ? BuildAsymmetricCircleObjectPoints(patternSize, (float)spacingMm)
                : BuildObjectPoints(patternSize, (float)spacingMm);
            var objectPointsList = Enumerable.Repeat(objectPoints, imagePoints.Count).ToList();

            var cameraMatrix = new double[3, 3];
            var distCoeffs = new double[5];
            if (!TryCalibrateCamera(objectPointsList, imagePoints, gray.Size(),
                    cameraMatrix, distCoeffs, out double rms, out var calibError))
                return Fail(calibError!);

            double pixelSizeMm = EstimateCirclePixelSizeMm(centers, patternSize, spacingMm, asymmetric);

            var meta = new CalibrationMetadata
            {
                Mode = CalibrationMode.CirclesGrid,
                CameraMatrix = CalibrationMetadata.To2DJagged(cameraMatrix),
                DistortionCoeffs = distCoeffs,
                PixelSizeMm = pixelSizeMm,
                ReprojectionError = rms,
                ImageWidth = image.Width,
                ImageHeight = image.Height,
                CalibratedAt = DateTime.UtcNow,
                SourceToolName = "CalibrationManager"
            };

            var overlay = DrawCornersOverlay(image, centers, patternSize);

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

        /// <summary>
        /// <c>CalibrateCamera</c> 호출을 감싼다. 실패하면 예외 대신 <c>CalibrationResult</c> 실패를 돌려준다.
        ///
        /// <para><b>왜 감싸는가.</b> 평면 타겟을 <b>정면에서 한 장만</b> 찍으면 내부 파라미터가
        /// 수학적으로 결정되지 않아 OpenCV 가 <c>"m.dims >= 2"</c> 같은 내부 단언으로 <b>예외를 던진다</b>
        /// (순수 OpenCV 에서도 동일 — 라이브러리 한계다). 현장에서 타겟을 카메라와 나란히 놓고
        /// 한 장 찍는 것은 아주 자연스러운 행동이라, 그대로 두면 [Run Calibration] 한 번에 창이 죽는다.
        /// 이제 무엇을 해야 하는지 알려 주고 멈춘다.</para>
        /// </summary>
        private static bool TryCalibrateCamera(
            IReadOnlyList<Point3f[]> objectPoints, IReadOnlyList<Point2f[]> imagePoints, Size imageSize,
            double[,] cameraMatrix, double[] distCoeffs, out double rms, out string? error)
        {
            error = null;
            try
            {
                rms = Cv2.CalibrateCamera(
                    objectPoints, imagePoints, imageSize, cameraMatrix, distCoeffs, out _, out _);
                return true;
            }
            catch (OpenCVException)
            {
                rms = 0;
                error = imagePoints.Count < 2
                    ? "한 장만으로는 계산할 수 없습니다 — 평면 타겟을 정면에서 한 장 찍으면 " +
                      "렌즈 값이 수학적으로 정해지지 않습니다. [Accumulate Multi-View] 를 켜고 " +
                      "타겟의 각도·위치를 바꿔 가며 10~20장을 모은 뒤 다시 실행하세요."
                    : "계산에 실패했습니다 — 모은 사진들이 서로 너무 비슷하면(같은 각도·같은 위치) " +
                      "렌즈 값을 정할 수 없습니다. 타겟을 기울이고 화면의 여러 구석으로 옮겨 가며 다시 모으세요.";
                return false;
            }
        }

        /// <summary>
        /// 원형 그리드 검출 1회. 찾으면 중심 배열을, 못 찾으면 null 을 돌려준다.
        ///
        /// <para><b>왜 감싸는가.</b> <c>FindCirclesGrid</c> 는 원을 하나도 못 찾으면 false 를
        /// 돌려주는 대신 <c>"samples is empty"</c> 예외를 던진다 — 빈 이미지나 배열 종류를 잘못
        /// 고른 경우에 그렇다. 그대로 두면 [Run Calibration] 한 번에 창이 죽는다.
        /// 개수가 기대와 다른 경우도 여기서 걸러낸다 — 그대로 넘기면 CalibrateCamera 가
        /// <c>"m.dims >= 2"</c> 로 터진다.</para>
        /// </summary>
        private static Point2f[]? TryFindCircles(Mat image, Size patternSize, FindCirclesGridFlags flags, int expected)
        {
            try
            {
                if (!Cv2.FindCirclesGrid(image, patternSize, out var centers, flags))
                    return null;
                if (centers == null || centers.Length != expected)
                    return null;
                return centers;
            }
            catch (OpenCVException)
            {
                // 검출 실패의 한 형태 — 호출부가 "못 찾았다" 로 처리한다.
                return null;
            }
        }

        /// <summary>
        /// 비대칭(엇갈린) 원형 그리드의 3D 좌표. OpenCV 관례 — 행마다 반 칸씩 밀리므로
        /// x = (2·열 + 행%2)·spacing, y = 행·spacing 이 된다.
        /// </summary>
        private static Point3f[] BuildAsymmetricCircleObjectPoints(Size patternSize, float spacingMm)
        {
            var pts = new Point3f[patternSize.Width * patternSize.Height];
            int i = 0;
            for (int r = 0; r < patternSize.Height; r++)
                for (int c = 0; c < patternSize.Width; c++)
                    pts[i++] = new Point3f((2 * c + r % 2) * spacingMm, r * spacingMm, 0f);
            return pts;
        }

        /// <summary>
        /// 원형 그리드의 픽셀당 mm. 한 행 안에서 이웃 중심 간 평균 픽셀 거리를 쓰되,
        /// 비대칭 배열은 그 거리가 2×spacing 에 해당하므로 그만큼 나눈다.
        /// </summary>
        private static double EstimateCirclePixelSizeMm(
            Point2f[] centers, Size patternSize, double spacingMm, bool asymmetric)
        {
            int cols = patternSize.Width;
            if (centers.Length < cols || cols < 2) return 0.0;

            double sumPx = 0;
            for (int c = 0; c < cols - 1; c++)
            {
                var dx = centers[c + 1].X - centers[c].X;
                var dy = centers[c + 1].Y - centers[c].Y;
                sumPx += Math.Sqrt(dx * dx + dy * dy);
            }
            double avgPx = sumPx / (cols - 1);
            if (avgPx <= 1e-6) return 0.0;

            double mmPerStep = asymmetric ? spacingMm * 2.0 : spacingMm;
            return mmPerStep / avgPx;
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
