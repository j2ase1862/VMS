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
