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

            int expected = patternCols * patternRows;
            var centers = DetectCircleGrid(gray, patternSize, flags, expected);

            if (centers == null)
            {
                var layout = asymmetric ? "비대칭(엇갈린)" : "대칭";
                return Fail(
                    $"원형 그리드를 찾지 못했습니다 ({patternCols}×{patternRows}, {layout}). " +
                    "① 행·열 개수와 배열 종류(대칭/비대칭)를 확인하세요 — 비대칭 배열은 열 개수를 " +
                    "두 행 합쳐 세는 것이 OpenCV 관례입니다. " +
                    "② 사진이 너무 어둡거나 과노출이면 원을 못 찾습니다 — 원과 배경이 눈에 또렷이 " +
                    "구분되고 흰 부분이 하얗게 타지 않도록 노출을 맞춰 다시 찍으세요.");
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

                // 같은 원인(평면 한 장 = 미결정)이라도 예외가 아니라 **말도 안 되는 값**으로 끝나는
                // 경우가 있다 — 실제로 RMS 가 159억 px 로 나왔다. 그대로 두면 화면에 그 숫자가 뜨고
                // 사용자는 무슨 뜻인지 알 수 없다. 물리적으로 불가능한 값이면 같은 안내로 돌린다.
                if (double.IsNaN(rms) || double.IsInfinity(rms) || rms > ImplausibleRmsPx)
                {
                    error = AdviceFor(imagePoints.Count);
                    return false;
                }
                return true;
            }
            catch (OpenCVException)
            {
                rms = 0;
                error = AdviceFor(imagePoints.Count);
                return false;
            }
        }

        /// <summary>정상적인 캘리브레이션이 넘을 수 없는 재투영 오차(px). 넘으면 계산이 발산한 것.</summary>
        private const double ImplausibleRmsPx = 50.0;

        private static string AdviceFor(int viewCount) => viewCount < 2
            ? "한 장만으로는 계산할 수 없습니다 — 평면 타겟을 정면에서 한 장 찍으면 " +
              "렌즈 값이 수학적으로 정해지지 않습니다. [Accumulate Multi-View] 를 켜고 " +
              "타겟의 각도·위치를 바꿔 가며 10~20장을 모은 뒤 다시 실행하세요."
            : "계산이 수렴하지 않았습니다 — 모은 사진들이 서로 너무 비슷하면(같은 각도·같은 위치) " +
              "렌즈 값을 정할 수 없습니다. 타겟을 기울이고 화면의 여러 구석으로 옮겨 가며 다시 모으세요.";

        /// <summary>
        /// 원형 그리드용 blob 검출기. 면적 상·하한을 <b>화면 크기에 비례</b>시킨다.
        ///
        /// <para><b>왜 기본 검출기를 못 쓰는가 (2026-09-15 실증 PC).</b> OpenCV 기본 SimpleBlobDetector 는
        /// 면적 <c>25~5000 px²</c> 만 blob 으로 본다. 그런데 산업용 카메라(2448×2048)로 타겟을 화면에
        /// 크게 담아 찍으면 원 하나가 <b>1만 px² 을 훌쩍 넘어</b> 전부 걸러진다 — 사람 눈에는 아주
        /// 또렷한 원인데 "원을 못 찾았다" 가 된다. 실촬영에서 기본 검출기는 큰 원 20개를 하나도 잡지
        /// 못했다(작은 표식만 9~12개).</para>
        ///
        /// <para>상한을 화면의 2% 로 둔다 — 지름이 화면 폭의 16% 쯤 되는 원까지 받으면서, 배경(종이·
        /// 보드 전체)이 통째로 blob 으로 잡히는 것은 막는다. 원형도·관성비는 비스듬히 찍혀 타원이 된
        /// 원을 받아들일 만큼 느슨하게 두되, 잡음을 거를 정도는 유지한다.</para>
        /// </summary>
        /// <param name="minThreshold">이진화 스윕 시작 밝기. OpenCV 기본은 50.</param>
        /// <param name="maxThreshold">이진화 스윕 끝 밝기. OpenCV 기본은 220.</param>
        /// <param name="thresholdStep">스윕 간격. OpenCV 기본은 10.</param>
        private static SimpleBlobDetector CreateCircleDetector(
            int width, int height,
            float minThreshold = 50f, float maxThreshold = 220f, float thresholdStep = 10f)
        {
            double area = (double)width * height;
            return SimpleBlobDetector.Create(new SimpleBlobDetector.Params
            {
                FilterByArea = true,
                MinArea = (float)Math.Max(30.0, area * 5e-6),
                MaxArea = (float)(area * 0.02),
                FilterByCircularity = true,
                MinCircularity = 0.6f,
                FilterByConvexity = true,
                MinConvexity = 0.8f,
                FilterByInertia = true,
                MinInertiaRatio = 0.25f,
                MinThreshold = minThreshold,
                MaxThreshold = maxThreshold,
                ThresholdStep = thresholdStep,
            });
        }

        /// <summary>
        /// 원형 그리드 검출 — 조명이 바뀌어도 찾도록 <b>단계적으로</b> 시도한다.
        ///
        /// <para><b>왜 단계를 두는가 (2026-09-22 실증).</b> SimpleBlobDetector 는 밝기를
        /// <c>50~220</c> 구간에서 10 씩 훑어 이진화하고, 같은 자리에 반복해 나타나는 덩어리만
        /// blob 으로 인정한다(OpenCV 기본값). 조명이 어두워지면 원과 배경 밝기가 통째로 이 구간
        /// 아래로 밀려나 <b>대비는 충분한데도</b> blob 이 몇 개씩 빠지고, 원 20 개 중 하나라도
        /// 빠지면 <c>FindCirclesGrid</c> 는 격자를 세우지 못한다. 실촬영 타겟을 밝기만 바꿔 가며
        /// 26 조건으로 돌렸을 때 기본값은 13 건만 성공했다(평균 밝기 87 미만·215 초과에서 실패).</para>
        ///
        /// <para>그래서 ① 기본 구간으로 먼저 보고(정상 조명에서 가장 빠르다) ② 실패하면 구간을
        /// <c>10~250</c> 으로 넓혀 촘촘히(step 5) ③ 그래도 실패하면 명암을 펴서(min-max 정규화)
        /// 다시 본다. 같은 26 조건에서 25 건으로 올라가고, 잘 되던 조명에서는 1 단계에서 끝나
        /// 속도가 그대로다(약 300ms). 남는 실패는 포화·흑색 클리핑처럼 원본 정보가 날아간 경우라
        /// 재촬영이 답이다.</para>
        ///
        /// <para>검출된 중심 좌표는 어느 단계에서 찾아도 같다(실측 355.6~358.6px, 스케일 동일) —
        /// 단계는 "찾느냐 못 찾느냐" 만 가르고 측정값을 바꾸지 않는다.</para>
        /// </summary>
        private static Point2f[]? DetectCircleGrid(
            Mat gray, Size patternSize, FindCirclesGridFlags flags, int expected)
        {
            // 1·2 단계 — 원본 밝기 그대로, 임계 구간만 달리한다.
            foreach (var (minTh, maxTh, step) in CircleThresholdStages)
            {
                using var detector = CreateCircleDetector(gray.Width, gray.Height, minTh, maxTh, step);
                var found = TryBothPolarities(gray, patternSize, flags, expected, detector);
                if (found != null)
                    return found;
            }

            // 3 단계 — 명암을 전체 범위로 펴고 넓은 구간으로 한 번 더.
            using var stretched = new Mat();
            Cv2.Normalize(gray, stretched, 0, 255, NormTypes.MinMax);
            var (wideMin, wideMax, wideStep) = CircleThresholdStages[^1];
            using var wideDetector = CreateCircleDetector(gray.Width, gray.Height, wideMin, wideMax, wideStep);
            return TryBothPolarities(stretched, patternSize, flags, expected, wideDetector);
        }

        /// <summary>밝기 임계 스윕 단계 — (시작, 끝, 간격). 첫 단계는 OpenCV 기본값이다.</summary>
        private static readonly (float Min, float Max, float Step)[] CircleThresholdStages =
        {
            (50f, 220f, 10f),
            (10f, 250f, 5f),
        };

        /// <summary>
        /// 밝은 배경의 검은 원이 기본. 반대(어두운 배경의 밝은 원)면 반전해 한 번 더 시도한다 —
        /// 현장 타겟은 둘 다 쓰이고, 사용자가 그 차이를 알 이유가 없다.
        /// </summary>
        private static Point2f[]? TryBothPolarities(
            Mat gray, Size patternSize, FindCirclesGridFlags flags, int expected, Feature2D detector)
        {
            var centers = TryFindCircles(gray, patternSize, flags, expected, detector);
            if (centers != null)
                return centers;

            using var inverted = new Mat();
            Cv2.BitwiseNot(gray, inverted);
            return TryFindCircles(inverted, patternSize, flags, expected, detector);
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
        private static Point2f[]? TryFindCircles(
            Mat image, Size patternSize, FindCirclesGridFlags flags, int expected, Feature2D blobDetector)
        {
            try
            {
                if (!Cv2.FindCirclesGrid(image, patternSize, out var centers, flags, blobDetector))
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
