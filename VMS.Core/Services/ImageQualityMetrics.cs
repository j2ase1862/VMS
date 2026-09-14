using System;
using OpenCvSharp;

namespace VMS.Core.Services
{
    /// <summary>
    /// 검사 입력 이미지에서 예측 모델용 품질 피처를 산출.
    /// (Predictive_DefectRate_Plan.md D3/V1) Brightness/Contrast/Focus/Blob.
    /// 추론·학습 시 OK 안의 드리프트를 잡아내는 핵심 피처.
    /// </summary>
    public static class ImageQualityMetrics
    {
        public readonly struct Result
        {
            public double Brightness { get; init; }
            public double ContrastStd { get; init; }
            public double FocusScore { get; init; }
            public int BlobCount { get; init; }
            public double MaxBlobAreaPx { get; init; }
        }

        /// <summary>
        /// 메트릭 5종을 단일 패스로 계산. src 가 null/Empty 이면 모두 0.
        /// 호출자가 src 를 dispose 하므로 본 함수는 새 Mat 을 보유하지 않음.
        /// </summary>
        public static Result Compute(Mat? src)
        {
            if (src == null || src.Empty())
                return default;

            Mat? gray = null;
            bool grayOwned = false;
            try
            {
                if (src.Channels() == 1)
                {
                    gray = src;
                }
                else
                {
                    gray = new Mat();
                    Cv2.CvtColor(src, gray, ColorConversionCodes.BGR2GRAY);
                    grayOwned = true;
                }

                // 8bit 기준(0~255)으로 한 번 맞춘 뒤 모든 지표를 계산한다.
                //
                // 예전에는 입력 깊이를 그대로 썼다. 그래서 16bit·실수 이미지에서는
                // (a) Otsu 이진화가 8bit 만 지원해 예외 → catch 로 떨어지며 <b>지표 5종이 전부 0</b>
                //     이 되거나,
                // (b) 밝기 평균이 0~255 범위를 벗어나 Web 업로드 검증에 걸렸다. Web 은 400 을
                //     돌려주고 VMS 는 400 을 영구 거절로 분류하므로, 그 사이클의 측정값·판정·
                //     작업지시 수량까지 통째로 사라졌다 — 부가 지표 하나 때문에 생산 기록을 잃는다.
                //
                // 한 축으로 맞춰 두면 카메라가 바뀌어도 예측 피처가 같은 의미를 갖는다.
                if (gray.Depth() != MatType.CV_8U)
                {
                    var converted = new Mat();
                    gray.ConvertTo(converted, MatType.CV_8U, DepthScaleTo8Bit(gray));
                    if (grayOwned) gray.Dispose();
                    gray = converted;
                    grayOwned = true;
                }

                // Brightness + ContrastStd: 한 번의 MeanStdDev 호출로 동시 산출
                Cv2.MeanStdDev(gray, out var meanScalar, out var stdScalar);
                double brightness = meanScalar.Val0;
                double contrastStd = stdScalar.Val0;

                // FocusScore: Laplacian 분산 (높을수록 선명)
                double focus = ComputeLaplacianVariance(gray);

                // Blob 통계: Otsu 이진화 → connected components → 가장 큰 컴포넌트 면적
                var (blobCount, maxArea) = ComputeBlobStats(gray);

                return new Result
                {
                    Brightness = brightness,
                    ContrastStd = contrastStd,
                    FocusScore = focus,
                    BlobCount = blobCount,
                    MaxBlobAreaPx = maxArea
                };
            }
            catch
            {
                // 메트릭 산출 실패는 검사 자체를 막아선 안 됨 — 0 반환
                return default;
            }
            finally
            {
                if (grayOwned) gray?.Dispose();
            }
        }

        /// <summary>
        /// 입력 깊이를 8bit(0~255) 기준으로 옮기는 배율.
        /// 16bit 는 255/65535, 32/64bit 실수(0~1 규약)는 255 배, 그 외(8bit)는 그대로.
        /// </summary>
        private static double DepthScaleTo8Bit(Mat gray)
        {
            return gray.Depth() switch
            {
                MatType.CV_16U or MatType.CV_16S => 255.0 / 65535.0,
                MatType.CV_32F or MatType.CV_64F => 255.0,
                _ => 1.0
            };
        }

        private static double ComputeLaplacianVariance(Mat gray)
        {
            using var lap = new Mat();
            Cv2.Laplacian(gray, lap, MatType.CV_64F);
            Cv2.MeanStdDev(lap, out _, out var std);
            // Variance = std^2 — 표준 Focus 메트릭
            return std.Val0 * std.Val0;
        }

        private static (int Count, double MaxAreaPx) ComputeBlobStats(Mat gray)
        {
            using var binary = new Mat();
            // Otsu — 자동 임계로 환경 밝기에 강건
            Cv2.Threshold(gray, binary, 0, 255, ThresholdTypes.Binary | ThresholdTypes.Otsu);

            using var labels = new Mat();
            using var stats = new Mat();
            using var centroids = new Mat();
            int total = Cv2.ConnectedComponentsWithStats(
                binary, labels, stats, centroids, PixelConnectivity.Connectivity8, MatType.CV_32S);

            // 0번 라벨은 배경. 1번부터 실제 컴포넌트.
            int blobCount = Math.Max(0, total - 1);
            double maxArea = 0;
            for (int i = 1; i < total; i++)
            {
                int area = stats.At<int>(i, (int)ConnectedComponentsTypes.Area);
                if (area > maxArea) maxArea = area;
            }
            return (blobCount, maxArea);
        }
    }
}
