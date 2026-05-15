using System;
using OpenCvSharp;

namespace VMS.VisionSetup.Models
{
    /// <summary>
    /// 캘리브레이션 모드.
    /// </summary>
    public enum CalibrationMode
    {
        /// <summary>체커보드 패턴 → 내부 파라미터 + 왜곡 계수 + 픽셀 크기</summary>
        Checkerboard,
        /// <summary>픽셀 좌표 ↔ 실측 mm 좌표 N개 매칭 → 평면 호모그래피</summary>
        NPointToNPoint,
        /// <summary>알려진 길이의 직선 1개 → 픽셀당 mm 비율만</summary>
        SinglePointScale
    }

    /// <summary>
    /// 카메라 캘리브레이션 메타데이터.
    /// CalibrationTool이 생성하여 VisionService.CurrentCalibrationMetadata 또는 Recipe.Calibration에 보관.
    /// 측정 도구(픽셀→mm)와 ImageRectifyTool(왜곡 보정)이 소비.
    /// JSON 직렬화 친화적: Mat 대신 jagged array 사용.
    /// </summary>
    public class CalibrationMetadata
    {
        /// <summary>
        /// 카메라 내부 파라미터 행렬 (3x3, [[fx,0,cx],[0,fy,cy],[0,0,1]]).
        /// Checkerboard 모드에서만 채워짐.
        /// </summary>
        public double[][]? CameraMatrix { get; set; }

        /// <summary>
        /// 렌즈 왜곡 계수 (k1, k2, p1, p2, k3 [, k4, k5, k6]).
        /// Checkerboard 모드에서만 채워짐.
        /// </summary>
        public double[]? DistortionCoeffs { get; set; }

        /// <summary>
        /// 1픽셀당 mm (등방 가정). 모든 모드에서 채워짐.
        /// </summary>
        public double PixelSizeMm { get; set; }

        /// <summary>
        /// 픽셀 좌표 → 실측 mm 좌표 변환 호모그래피 (3x3).
        /// NPointToNPoint 모드에서만 채워짐.
        /// </summary>
        public double[][]? HomographyPx2Mm { get; set; }

        /// <summary>
        /// 캘리브레이션 RMS 재투영 오차 (픽셀 단위, &lt; 1.0 권장).
        /// </summary>
        public double ReprojectionError { get; set; }

        /// <summary>
        /// 캘리브레이션 수행 시각.
        /// </summary>
        public DateTime CalibratedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// 캘리브레이션을 생성한 도구의 Name (디버깅용).
        /// </summary>
        public string SourceToolName { get; set; } = string.Empty;

        /// <summary>
        /// 캘리브레이션 수행 시점 이미지 해상도. 런타임 해상도와 다르면 재사용 부적합.
        /// </summary>
        public int ImageWidth { get; set; }
        public int ImageHeight { get; set; }

        /// <summary>
        /// 캘리브레이션 모드.
        /// </summary>
        public CalibrationMode Mode { get; set; }

        /// <summary>
        /// CameraMatrix를 OpenCV Mat (CV_64FC1 3x3)로 변환. 호출자가 Dispose 책임.
        /// </summary>
        public Mat? ToCameraMatrixMat()
        {
            if (CameraMatrix == null || CameraMatrix.Length != 3) return null;
            var mat = new Mat(3, 3, MatType.CV_64FC1);
            for (int r = 0; r < 3; r++)
                for (int c = 0; c < 3; c++)
                    mat.Set(r, c, CameraMatrix[r][c]);
            return mat;
        }

        /// <summary>
        /// DistortionCoeffs를 OpenCV Mat (CV_64FC1 Nx1)로 변환. 호출자가 Dispose 책임.
        /// </summary>
        public Mat? ToDistortionMat()
        {
            if (DistortionCoeffs == null || DistortionCoeffs.Length == 0) return null;
            var mat = new Mat(DistortionCoeffs.Length, 1, MatType.CV_64FC1);
            for (int i = 0; i < DistortionCoeffs.Length; i++)
                mat.Set(i, 0, DistortionCoeffs[i]);
            return mat;
        }

        /// <summary>
        /// HomographyPx2Mm를 OpenCV Mat (CV_64FC1 3x3)로 변환. 호출자가 Dispose 책임.
        /// </summary>
        public Mat? ToHomographyMat()
        {
            if (HomographyPx2Mm == null || HomographyPx2Mm.Length != 3) return null;
            var mat = new Mat(3, 3, MatType.CV_64FC1);
            for (int r = 0; r < 3; r++)
                for (int c = 0; c < 3; c++)
                    mat.Set(r, c, HomographyPx2Mm[r][c]);
            return mat;
        }

        /// <summary>
        /// 픽셀 좌표를 mm 좌표로 변환.
        /// Homography가 있으면 사용 (NPoint 모드), 없으면 PixelSizeMm 등방 스케일링.
        /// </summary>
        public (double XMm, double YMm) PixelToMm(double pixelX, double pixelY)
        {
            if (HomographyPx2Mm != null && HomographyPx2Mm.Length == 3)
            {
                var h = HomographyPx2Mm;
                double w = h[2][0] * pixelX + h[2][1] * pixelY + h[2][2];
                if (Math.Abs(w) >= 1e-12)
                {
                    double x = (h[0][0] * pixelX + h[0][1] * pixelY + h[0][2]) / w;
                    double y = (h[1][0] * pixelX + h[1][1] * pixelY + h[1][2]) / w;
                    return (x, y);
                }
            }
            return (pixelX * PixelSizeMm, pixelY * PixelSizeMm);
        }

        /// <summary>
        /// 픽셀 길이(스칼라)를 mm 길이로 변환. Homography는 직선 길이에 위치-종속이므로 무시.
        /// </summary>
        public double LengthToMm(double pixelLength) => pixelLength * PixelSizeMm;

        /// <summary>
        /// 캘리브레이션 결과의 기본 유효성 검사.
        /// </summary>
        public bool IsValid()
        {
            if (PixelSizeMm <= 0 || double.IsNaN(PixelSizeMm)) return false;
            return Mode switch
            {
                CalibrationMode.Checkerboard =>
                    CameraMatrix != null && CameraMatrix.Length == 3 && DistortionCoeffs != null,
                CalibrationMode.NPointToNPoint =>
                    HomographyPx2Mm != null && HomographyPx2Mm.Length == 3,
                CalibrationMode.SinglePointScale => true,
                _ => false
            };
        }

        /// <summary>
        /// 2D 배열을 jagged 배열로 변환 (직렬화용).
        /// </summary>
        public static double[][] To2DJagged(double[,] src)
        {
            int rows = src.GetLength(0), cols = src.GetLength(1);
            var result = new double[rows][];
            for (int r = 0; r < rows; r++)
            {
                result[r] = new double[cols];
                for (int c = 0; c < cols; c++)
                    result[r][c] = src[r, c];
            }
            return result;
        }
    }
}
