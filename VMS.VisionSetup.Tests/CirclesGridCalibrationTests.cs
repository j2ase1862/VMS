using System;
using OpenCvSharp;
using VMS.VisionSetup.Models;
using VMS.VisionSetup.Services;
using Xunit;

namespace VMS.VisionSetup.Tests
{
    /// <summary>
    /// 원형 그리드(도트) 캘리브레이션.
    ///
    /// <para><b>왜 필요한가 (2026-09-15).</b> 현장이 가진 캘리브레이션 타겟이 체커보드가 아니라
    /// 원형 그리드(예: <c>CGB-020 5x4-20mm</c>)여서, 체커보드만 지원하던 동안에는 그 타겟으로
    /// 아예 캘리브레이션을 할 수 없었다 — Run 을 누르면 "Chessboard corners not found" 만 났다.</para>
    ///
    /// <para>실물 사진 대신 <b>합성 패턴</b>으로 검증한다. 간격·배율을 아는 상태로 만들 수 있어
    /// PixelSize 가 맞게 나오는지까지 확인할 수 있고, CI 에서 이미지 파일에 기대지 않는다.</para>
    /// </summary>
    public class CirclesGridCalibrationTests
    {
        /// <summary>
        /// 비대칭(엇갈린) 원형 그리드 이미지를 만든다. OpenCV 관례와 같은 좌표계 —
        /// x = (2·열 + 행%2)·spacing, y = 행·spacing.
        /// </summary>
        private static Mat MakeAsymmetricGrid(
            int cols, int rows, double spacingMm, double pxPerMm, int radiusPx, int marginPx)
        {
            double wMm = (2 * (cols - 1) + 1) * spacingMm;
            double hMm = (rows - 1) * spacingMm;
            int width = (int)Math.Round(wMm * pxPerMm) + marginPx * 2;
            int height = (int)Math.Round(hMm * pxPerMm) + marginPx * 2;

            var img = new Mat(new Size(width, height), MatType.CV_8UC1, Scalar.All(255));
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    double xMm = (2 * c + r % 2) * spacingMm;
                    double yMm = r * spacingMm;
                    var center = new Point(
                        (int)Math.Round(xMm * pxPerMm) + marginPx,
                        (int)Math.Round(yMm * pxPerMm) + marginPx);
                    Cv2.Circle(img, center, radiusPx, Scalar.All(0), -1, LineTypes.AntiAlias);
                }
            }
            return img;
        }

        private static Mat MakeSymmetricGrid(
            int cols, int rows, double spacingMm, double pxPerMm, int radiusPx, int marginPx)
        {
            int width = (int)Math.Round((cols - 1) * spacingMm * pxPerMm) + marginPx * 2;
            int height = (int)Math.Round((rows - 1) * spacingMm * pxPerMm) + marginPx * 2;

            var img = new Mat(new Size(width, height), MatType.CV_8UC1, Scalar.All(255));
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                    Cv2.Circle(img,
                        new Point((int)Math.Round(c * spacingMm * pxPerMm) + marginPx,
                                  (int)Math.Round(r * spacingMm * pxPerMm) + marginPx),
                        radiusPx, Scalar.All(0), -1, LineTypes.AntiAlias);
            return img;
        }

        /// <summary>타겟을 살짝 기울여 찍은 것처럼 원근 변형을 준다 (0 이면 정면).</summary>
        private static Mat Tilt(Mat src, double amount)
        {
            if (Math.Abs(amount) < 1e-9) return src.Clone();

            float w = src.Width, h = src.Height;
            float dx = (float)(w * amount);
            var from = new[]
            {
                new Point2f(0, 0), new Point2f(w, 0), new Point2f(w, h), new Point2f(0, h)
            };
            var to = new[]
            {
                new Point2f(dx, 0), new Point2f(w - dx, 0),
                new Point2f(w, h), new Point2f(0, h)
            };
            using var m = Cv2.GetPerspectiveTransform(from, to);
            var dst = new Mat();
            Cv2.WarpPerspective(src, dst, m, src.Size(), InterpolationFlags.Linear,
                BorderTypes.Constant, Scalar.All(255));
            return dst;
        }

        [Fact]
        public void Asymmetric_grid_is_detected_and_calibrates()
        {
            // 현장 타겟과 같은 모양 — 한 행에 원 4개, 5행, 중심 간 20mm
            using var img = MakeAsymmetricGrid(
                cols: 4, rows: 5, spacingMm: 20.0, pxPerMm: 8.0, radiusPx: 26, marginPx: 90);

            var svc = new CalibrationService();
            var result = svc.RunCirclesGrid(img, 4, 5, 20.0, asymmetric: true, accumulate: false);

            Assert.True(result.Success, $"검출/캘리브레이션 실패: {result.Message}");
            Assert.NotNull(result.Metadata);
            Assert.Equal(CalibrationMode.CirclesGrid, result.Metadata!.Mode);
            Assert.NotNull(result.Metadata.CameraMatrix);
            Assert.NotNull(result.Metadata.DistortionCoeffs);
        }

        /// <summary>
        /// 비대칭 배열에서 같은 행의 이웃 원까지는 2×spacing 이다 — 이걸 틀리면 mm 환산이
        /// 정확히 2배로 어긋난다(왜곡 보정은 멀쩡해서 알아채기 어렵다).
        /// </summary>
        [Fact]
        public void Asymmetric_pixel_size_accounts_for_double_step()
        {
            const double pxPerMm = 8.0;          // 1px = 0.125mm
            using var img = MakeAsymmetricGrid(
                cols: 4, rows: 5, spacingMm: 20.0, pxPerMm: pxPerMm, radiusPx: 26, marginPx: 90);

            var svc = new CalibrationService();
            var result = svc.RunCirclesGrid(img, 4, 5, 20.0, asymmetric: true, accumulate: false);

            Assert.True(result.Success, result.Message);
            Assert.InRange(result.Metadata!.PixelSizeMm, 1.0 / pxPerMm * 0.95, 1.0 / pxPerMm * 1.05);
        }

        /// <summary>
        /// 기울여 찍은 여러 장을 모으는 것이 실제 사용법이고, 평면 타겟은 그래야만 렌즈 값이 정해진다.
        /// </summary>
        [Fact]
        public void Symmetric_grid_is_detected_across_accumulated_views()
        {
            var svc = new CalibrationService();
            CalibrationResult? last = null;

            // 합성 원은 많이 기울이면 타원이 되어 검출기가 거르므로 완만한 각도를 쓴다
            // (실물 타겟은 더 큰 각도까지 잡힌다). 5장이면 RMS 가 0.1 아래로 떨어진다.
            foreach (var tilt in new[] { 0.0, 0.02, 0.03, 0.04, 0.05 })
            {
                using var flat = MakeSymmetricGrid(
                    cols: 5, rows: 4, spacingMm: 15.0, pxPerMm: 8.0, radiusPx: 22, marginPx: 90);
                using var view = Tilt(flat, tilt);
                last = svc.RunCirclesGrid(view, 5, 4, 15.0, asymmetric: false, accumulate: true);
            }

            Assert.NotNull(last);
            Assert.True(last!.Success, $"검출/캘리브레이션 실패: {last.Message}");
            Assert.Equal(CalibrationMode.CirclesGrid, last.Metadata!.Mode);
            Assert.Equal(5, last.ViewCount);
        }

        /// <summary>
        /// 평면 타겟을 <b>정면에서 한 장만</b> 찍으면 내부 파라미터가 정해지지 않아 OpenCV 가
        /// 예외를 던진다(순수 OpenCV 에서도 동일). 창이 죽는 대신 <b>무엇을 하라고 알려 주는지</b>.
        /// </summary>
        [Fact]
        public void Single_frontal_view_fails_with_advice_instead_of_crashing()
        {
            using var img = MakeSymmetricGrid(
                cols: 5, rows: 4, spacingMm: 15.0, pxPerMm: 8.0, radiusPx: 22, marginPx: 80);

            var svc = new CalibrationService();
            var result = svc.RunCirclesGrid(img, 5, 4, 15.0, asymmetric: false, accumulate: false);

            Assert.False(result.Success);
            Assert.Contains("Accumulate Multi-View", result.Message);
        }

        /// <summary>
        /// 어두운 배경에 밝은 원인 타겟도 그대로 된다 — 현장 타겟은 둘 다 쓰이고,
        /// 사용자가 그 차이를 알 이유가 없어 반전 재시도를 넣었다.
        /// </summary>
        [Fact]
        public void Inverted_target_light_circles_on_dark_also_works()
        {
            using var normal = MakeAsymmetricGrid(
                cols: 4, rows: 5, spacingMm: 20.0, pxPerMm: 8.0, radiusPx: 26, marginPx: 90);
            using var inverted = new Mat();
            Cv2.BitwiseNot(normal, inverted);

            var svc = new CalibrationService();
            var result = svc.RunCirclesGrid(inverted, 4, 5, 20.0, asymmetric: true, accumulate: false);

            Assert.True(result.Success, $"반전 타겟 검출 실패: {result.Message}");
        }

        /// <summary>배열 종류를 잘못 고르면 개수가 맞아도 못 찾는다 — 안내 문구가 그 점을 짚어야 한다.</summary>
        [Fact]
        public void Wrong_layout_fails_with_a_message_that_names_the_layout()
        {
            using var img = MakeAsymmetricGrid(
                cols: 4, rows: 5, spacingMm: 20.0, pxPerMm: 8.0, radiusPx: 26, marginPx: 90);

            var svc = new CalibrationService();
            var result = svc.RunCirclesGrid(img, 4, 5, 20.0, asymmetric: false, accumulate: false);

            Assert.False(result.Success);
            Assert.Contains("대칭", result.Message);
        }

        /// <summary>
        /// 2026-09-15 실증 PC: 산업용 카메라(2448×2048)로 타겟을 크게 담아 찍으니 원 하나가
        /// 1만 px² 을 넘어, OpenCV 기본 blob 검출기의 상한(5000 px²)에 전부 걸러졌다 —
        /// 사람 눈에는 또렷한 원인데 "원을 못 찾았다" 가 났다. 면적 상한을 화면 크기에 비례시켜 해결.
        ///
        /// <para>여기서는 <b>기본 검출기라면 확실히 걸러질 크기</b>(반지름 60px → 약 11,300 px²)로
        /// 만들어, 검출기 설정이 되돌아가면 바로 깨지게 한다.</para>
        /// </summary>
        [Fact]
        public void Large_circles_beyond_default_detector_limit_are_found()
        {
            const int radius = 60;                       // 면적 ≈ 11,300 px² (기본 상한 5000 초과)
            using var img = MakeAsymmetricGrid(
                cols: 4, rows: 5, spacingMm: 20.0, pxPerMm: 12.0, radiusPx: radius, marginPx: 160);

            var svc = new CalibrationService();
            var result = svc.RunCirclesGrid(img, 4, 5, 20.0, asymmetric: true, accumulate: false);

            // 한 장이라 계산 단계에서는 막힌다 — 여기서 보려는 것은 그 **앞 단계**인 검출이다.
            // "못 찾았습니다" 가 아니라 "여러 장을 모으라" 는 안내가 나와야 원을 찾았다는 뜻이다.
            Assert.False(result.Success);
            Assert.DoesNotContain("찾지 못했습니다", result.Message);
            Assert.Contains("Accumulate Multi-View", result.Message);
        }

        [Fact]
        public void Empty_image_is_rejected()
        {
            var svc = new CalibrationService();
            var result = svc.RunCirclesGrid(new Mat(), 4, 5, 20.0, true, false);

            Assert.False(result.Success);
            Assert.Contains("empty", result.Message, StringComparison.OrdinalIgnoreCase);
        }
    }
}
