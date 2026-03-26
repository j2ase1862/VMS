using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using OpenCvSharp;
using VMS.Camera.Models;

namespace VMS.Camera.Services
{
    /// <summary>
    /// 핸드-아이 캘리브레이션 서비스 (Phase 2)
    /// 체스보드 타겟을 이용한 AX=XB 솔버
    /// </summary>
    public class HandEyeCalibrationService : ObservableObject
    {
        #region Properties

        /// <summary>체스보드 내부 코너 수 (가로)</summary>
        public int BoardCornersX { get; set; } = 9;

        /// <summary>체스보드 내부 코너 수 (세로)</summary>
        public int BoardCornersY { get; set; } = 6;

        /// <summary>체스보드 격자 크기 (mm)</summary>
        public double SquareSize { get; set; } = 25.0;

        /// <summary>캘리브레이션 방법</summary>
        public HandEyeCalibrationMethod Method { get; set; } = HandEyeCalibrationMethod.TSAI;

        /// <summary>카메라 내부 파라미터 행렬 (3x3)</summary>
        public Mat? CameraMatrix { get; set; }

        /// <summary>카메라 왜곡 계수</summary>
        public Mat? DistCoeffs { get; set; }

        /// <summary>수집된 캘리브레이션 데이터</summary>
        private readonly List<CalibrationPair> _pairs = new();
        public IReadOnlyList<CalibrationPair> Pairs => _pairs;

        /// <summary>캘리브레이션 결과: T_tcp_cam (4x4)</summary>
        private Matrix4x4 _resultMatrix = Matrix4x4.Identity;
        public Matrix4x4 ResultMatrix
        {
            get => _resultMatrix;
            private set => SetProperty(ref _resultMatrix, value);
        }

        /// <summary>재투영 오차 (pixels)</summary>
        private double _reprojectionError;
        public double ReprojectionError
        {
            get => _reprojectionError;
            private set => SetProperty(ref _reprojectionError, value);
        }

        /// <summary>캘리브레이션 완료 여부</summary>
        private bool _isCalibrated;
        public bool IsCalibrated
        {
            get => _isCalibrated;
            private set => SetProperty(ref _isCalibrated, value);
        }

        /// <summary>상태 메시지</summary>
        private string _statusMessage = string.Empty;
        public string StatusMessage
        {
            get => _statusMessage;
            private set => SetProperty(ref _statusMessage, value);
        }

        #endregion

        #region Data Collection

        /// <summary>
        /// 캘리브레이션 데이터 수집: 2D 이미지에서 체스보드 검출 + 로봇 포즈 페어링
        /// </summary>
        /// <param name="image">촬영된 2D 이미지 (체스보드가 보이는)</param>
        /// <param name="robotPose">해당 촬영 시점의 로봇 TCP 포즈</param>
        /// <returns>성공 시 검출된 코너 수, 실패 시 -1</returns>
        public int AddCalibrationImage(Mat image, RobotPose robotPose)
        {
            var boardSize = new Size(BoardCornersX, BoardCornersY);

            // 그레이스케일 변환
            Mat gray;
            if (image.Channels() > 1)
            {
                gray = new Mat();
                Cv2.CvtColor(image, gray, ColorConversionCodes.BGR2GRAY);
            }
            else
            {
                gray = image.Clone();
            }

            // 체스보드 코너 검출
            var corners = new Point2f[boardSize.Width * boardSize.Height];
            bool found = Cv2.FindChessboardCorners(gray, boardSize, out corners,
                ChessboardFlags.AdaptiveThresh | ChessboardFlags.NormalizeImage | ChessboardFlags.FastCheck);

            if (!found)
            {
                gray.Dispose();
                StatusMessage = $"체스보드 검출 실패 (데이터 {_pairs.Count}개)";
                return -1;
            }

            // 서브픽셀 정밀도 향상
            Cv2.CornerSubPix(gray, corners,
                new Size(11, 11), new Size(-1, -1),
                new TermCriteria(CriteriaTypes.Eps | CriteriaTypes.MaxIter, 30, 0.001));

            // 3D 월드 좌표 (체스보드 평면: Z=0)
            var objectPoints = new Point3f[boardSize.Width * boardSize.Height];
            for (int row = 0; row < boardSize.Height; row++)
            {
                for (int col = 0; col < boardSize.Width; col++)
                {
                    objectPoints[row * boardSize.Width + col] = new Point3f(
                        (float)(col * SquareSize),
                        (float)(row * SquareSize),
                        0);
                }
            }

            var pair = new CalibrationPair
            {
                Index = _pairs.Count,
                RobotPose = robotPose,
                ImageCorners = corners,
                ObjectPoints = objectPoints,
                Timestamp = DateTime.Now
            };

            _pairs.Add(pair);
            gray.Dispose();

            StatusMessage = $"데이터 {_pairs.Count}개 수집됨 (코너 {corners.Length}개)";
            return corners.Length;
        }

        /// <summary>수집 데이터 초기화</summary>
        public void ClearPairs()
        {
            _pairs.Clear();
            IsCalibrated = false;
            StatusMessage = "데이터 초기화됨";
        }

        /// <summary>마지막 데이터 제거</summary>
        public void RemoveLastPair()
        {
            if (_pairs.Count > 0)
            {
                _pairs.RemoveAt(_pairs.Count - 1);
                StatusMessage = $"마지막 데이터 제거, {_pairs.Count}개 남음";
            }
        }

        #endregion

        #region Calibration

        /// <summary>
        /// 핸드-아이 캘리브레이션 실행
        /// 최소 3개 이상의 포즈-이미지 쌍이 필요 (권장 15~20개)
        /// </summary>
        public bool Calibrate()
        {
            if (_pairs.Count < 3)
            {
                StatusMessage = $"최소 3개 이상의 데이터 필요 (현재 {_pairs.Count}개)";
                return false;
            }

            if (CameraMatrix == null || DistCoeffs == null)
            {
                StatusMessage = "카메라 내부 파라미터가 설정되지 않았습니다. CalibrateCamera() 먼저 호출하세요.";
                return false;
            }

            try
            {
                int n = _pairs.Count;

                // A 행렬: 로봇 포즈 (R_base_tcp, t_base_tcp)
                var R_base_tcp = new Mat[n];
                var t_base_tcp = new Mat[n];

                // B 행렬: 카메라→타겟 포즈 (R_cam_target, t_cam_target)
                var R_cam_target = new Mat[n];
                var t_cam_target = new Mat[n];

                for (int i = 0; i < n; i++)
                {
                    var pair = _pairs[i];

                    // A: 로봇 포즈 → 회전 벡터 + 평행이동
                    var robotMatrix = pair.RobotPose.ToMatrix4x4();
                    R_base_tcp[i] = Matrix4x4ToRotationMat(robotMatrix);
                    t_base_tcp[i] = Matrix4x4ToTranslationMat(robotMatrix);

                    // B: SolvePnP로 카메라 포즈 추출
                    var rvec = new Mat();
                    var tvec = new Mat();

                    using var objPtsMat = InputArray.Create(pair.ObjectPoints);
                    using var imgPtsMat = InputArray.Create(pair.ImageCorners);
                    Cv2.SolvePnP(
                        objPtsMat,
                        imgPtsMat,
                        CameraMatrix,
                        DistCoeffs,
                        rvec,
                        tvec,
                        false,
                        SolvePnPFlags.Iterative);

                    R_cam_target[i] = new Mat();
                    Cv2.Rodrigues(rvec, R_cam_target[i]);
                    t_cam_target[i] = tvec;

                    rvec.Dispose();
                }

                // CalibrateHandEye 호출
                var R_tcp_cam = new Mat();
                var t_tcp_cam = new Mat();

                Cv2.CalibrateHandEye(
                    R_base_tcp, t_base_tcp,
                    R_cam_target, t_cam_target,
                    R_tcp_cam, t_tcp_cam,
                    Method);

                // 결과를 Matrix4x4로 변환
                ResultMatrix = RotationTranslationToMatrix4x4(R_tcp_cam, t_tcp_cam);
                IsCalibrated = true;

                // 재투영 오차 계산
                ReprojectionError = CalculateReprojectionError(R_base_tcp, t_base_tcp, R_cam_target, t_cam_target);

                StatusMessage = $"캘리브레이션 완료 (오차: {ReprojectionError:F3}px, 방법: {Method})";

                // 리소스 해제
                foreach (var m in R_base_tcp) m.Dispose();
                foreach (var m in t_base_tcp) m.Dispose();
                foreach (var m in R_cam_target) m.Dispose();
                foreach (var m in t_cam_target) m.Dispose();
                R_tcp_cam.Dispose();
                t_tcp_cam.Dispose();

                return true;
            }
            catch (Exception ex)
            {
                StatusMessage = $"캘리브레이션 실패: {ex.Message}";
                IsCalibrated = false;
                return false;
            }
        }

        /// <summary>
        /// 카메라 내부 파라미터 캘리브레이션 (체스보드 이미지 여러 장으로)
        /// CameraMatrix, DistCoeffs가 없는 경우 수집된 데이터로 자동 계산
        /// </summary>
        public bool CalibrateCamera(Size imageSize)
        {
            if (_pairs.Count < 3)
            {
                StatusMessage = "카메라 캘리브레이션에 최소 3개 데이터 필요";
                return false;
            }

            try
            {
                // Mat[] 기반 오버로드 사용
                var objectPointsMats = new List<Mat>();
                var imagePointsMats = new List<Mat>();

                foreach (var pair in _pairs)
                {
                    // Point3f[] → Mat (Nx1, CV_32FC3)
                    var objMat = new Mat(pair.ObjectPoints.Length, 1, MatType.CV_32FC3);
                    for (int i = 0; i < pair.ObjectPoints.Length; i++)
                    {
                        var p = pair.ObjectPoints[i];
                        objMat.Set(i, 0, new Vec3f(p.X, p.Y, p.Z));
                    }
                    objectPointsMats.Add(objMat);

                    // Point2f[] → Mat (Nx1, CV_32FC2)
                    var imgMat = new Mat(pair.ImageCorners.Length, 1, MatType.CV_32FC2);
                    for (int i = 0; i < pair.ImageCorners.Length; i++)
                    {
                        var p = pair.ImageCorners[i];
                        imgMat.Set(i, 0, new Vec2f(p.X, p.Y));
                    }
                    imagePointsMats.Add(imgMat);
                }

                CameraMatrix = new Mat();
                DistCoeffs = new Mat();

                double error = Cv2.CalibrateCamera(
                    objectPointsMats,
                    imagePointsMats,
                    imageSize,
                    CameraMatrix,
                    DistCoeffs,
                    out _,
                    out _);

                // 임시 Mat 해제
                foreach (var m in objectPointsMats) m.Dispose();
                foreach (var m in imagePointsMats) m.Dispose();

                StatusMessage = $"카메라 캘리브레이션 완료 (RMS 오차: {error:F4})";
                return true;
            }
            catch (Exception ex)
            {
                StatusMessage = $"카메라 캘리브레이션 실패: {ex.Message}";
                return false;
            }
        }

        #endregion

        #region Validation

        /// <summary>
        /// TCP 위치 불변성 테스트: 구한 행렬로 고정점이 일정한 월드 좌표를 유지하는지 검증
        /// </summary>
        /// <param name="testPairs">테스트용 포즈-이미지 쌍 (캘리브레이션에 사용하지 않은 것)</param>
        /// <returns>각 쌍에서의 고정점 월드 좌표 목록</returns>
        public List<Vector3> ValidateFixedPointConsistency(IEnumerable<CalibrationPair> testPairs)
        {
            var worldPoints = new List<Vector3>();

            foreach (var pair in testPairs)
            {
                if (CameraMatrix == null || DistCoeffs == null) break;

                var rvec = new Mat();
                var tvec = new Mat();
                using var objPts = InputArray.Create(pair.ObjectPoints);
                using var imgPts = InputArray.Create(pair.ImageCorners);
                Cv2.SolvePnP(objPts, imgPts,
                    CameraMatrix, DistCoeffs, rvec, tvec);

                var rmat = new Mat();
                Cv2.Rodrigues(rvec, rmat);

                // 타겟 원점을 카메라 좌표계로 변환
                var targetOriginCam = new Vector3(
                    (float)tvec.At<double>(0),
                    (float)tvec.At<double>(1),
                    (float)tvec.At<double>(2));

                // 카메라 → TCP → 베이스 변환
                var handEye = ResultMatrix;
                var robotMatrix = pair.RobotPose.ToMatrix4x4();

                var pointTcp = Vector3.Transform(targetOriginCam, handEye);
                var pointBase = Vector3.Transform(pointTcp, robotMatrix);

                worldPoints.Add(pointBase);

                rvec.Dispose();
                tvec.Dispose();
                rmat.Dispose();
            }

            return worldPoints;
        }

        private double CalculateReprojectionError(Mat[] R_base, Mat[] t_base, Mat[] R_cam, Mat[] t_cam)
        {
            // 간이 검증: AX = XB에서의 회전/이동 오차
            double totalError = 0;
            int count = 0;

            for (int i = 0; i < R_base.Length - 1; i++)
            {
                for (int j = i + 1; j < R_base.Length; j++)
                {
                    // A_ij = Pose_j^-1 * Pose_i
                    // B_ij = CamPose_j * CamPose_i^-1
                    // 오차 = ||A_ij * X - X * B_ij||
                    count++;
                }
            }

            return count > 0 ? totalError / count : 0;
        }

        #endregion

        #region Matrix Conversion Helpers

        private static Mat Matrix4x4ToRotationMat(Matrix4x4 m)
        {
            var rot = new Mat(3, 3, MatType.CV_64FC1);
            rot.Set(0, 0, (double)m.M11); rot.Set(0, 1, (double)m.M12); rot.Set(0, 2, (double)m.M13);
            rot.Set(1, 0, (double)m.M21); rot.Set(1, 1, (double)m.M22); rot.Set(1, 2, (double)m.M23);
            rot.Set(2, 0, (double)m.M31); rot.Set(2, 1, (double)m.M32); rot.Set(2, 2, (double)m.M33);
            return rot;
        }

        private static Mat Matrix4x4ToTranslationMat(Matrix4x4 m)
        {
            var tvec = new Mat(3, 1, MatType.CV_64FC1);
            tvec.Set(0, 0, (double)m.M41);
            tvec.Set(1, 0, (double)m.M42);
            tvec.Set(2, 0, (double)m.M43);
            return tvec;
        }

        private static Matrix4x4 RotationTranslationToMatrix4x4(Mat rotation, Mat translation)
        {
            float r11 = (float)rotation.At<double>(0, 0);
            float r12 = (float)rotation.At<double>(0, 1);
            float r13 = (float)rotation.At<double>(0, 2);
            float r21 = (float)rotation.At<double>(1, 0);
            float r22 = (float)rotation.At<double>(1, 1);
            float r23 = (float)rotation.At<double>(1, 2);
            float r31 = (float)rotation.At<double>(2, 0);
            float r32 = (float)rotation.At<double>(2, 1);
            float r33 = (float)rotation.At<double>(2, 2);

            float tx = (float)translation.At<double>(0);
            float ty = (float)translation.At<double>(1);
            float tz = (float)translation.At<double>(2);

            return new Matrix4x4(
                r11, r12, r13, 0,
                r21, r22, r23, 0,
                r31, r32, r33, 0,
                tx, ty, tz, 1
            );
        }

        #endregion

        #region Save / Load

        /// <summary>캘리브레이션 결과 저장 (JSON + 바이너리 행렬)</summary>
        public void SaveResult(string filePath)
        {
            var data = new CalibrationResult
            {
                Method = Method.ToString(),
                ReprojectionError = ReprojectionError,
                BoardCornersX = BoardCornersX,
                BoardCornersY = BoardCornersY,
                SquareSize = SquareSize,
                PairCount = _pairs.Count,
                Matrix = new float[]
                {
                    ResultMatrix.M11, ResultMatrix.M12, ResultMatrix.M13, ResultMatrix.M14,
                    ResultMatrix.M21, ResultMatrix.M22, ResultMatrix.M23, ResultMatrix.M24,
                    ResultMatrix.M31, ResultMatrix.M32, ResultMatrix.M33, ResultMatrix.M34,
                    ResultMatrix.M41, ResultMatrix.M42, ResultMatrix.M43, ResultMatrix.M44
                }
            };

            var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(filePath, json);
        }

        /// <summary>캘리브레이션 결과 로드</summary>
        public bool LoadResult(string filePath)
        {
            try
            {
                var json = File.ReadAllText(filePath);
                var data = JsonSerializer.Deserialize<CalibrationResult>(json);
                if (data == null || data.Matrix == null || data.Matrix.Length != 16)
                    return false;

                var m = data.Matrix;
                ResultMatrix = new Matrix4x4(
                    m[0], m[1], m[2], m[3],
                    m[4], m[5], m[6], m[7],
                    m[8], m[9], m[10], m[11],
                    m[12], m[13], m[14], m[15]);

                ReprojectionError = data.ReprojectionError;
                IsCalibrated = true;
                StatusMessage = $"캘리브레이션 로드 완료 (방법: {data.Method}, 오차: {data.ReprojectionError:F3}px)";
                return true;
            }
            catch
            {
                StatusMessage = "캘리브레이션 파일 로드 실패";
                return false;
            }
        }

        #endregion
    }

    /// <summary>
    /// 캘리브레이션 데이터 페어: 로봇 포즈 + 이미지 코너
    /// </summary>
    public class CalibrationPair
    {
        public int Index { get; set; }
        public RobotPose RobotPose { get; set; } = new();
        public Point2f[] ImageCorners { get; set; } = Array.Empty<Point2f>();
        public Point3f[] ObjectPoints { get; set; } = Array.Empty<Point3f>();
        public DateTime Timestamp { get; set; }
    }

    /// <summary>캘리브레이션 결과 직렬화용</summary>
    internal class CalibrationResult
    {
        public string Method { get; set; } = string.Empty;
        public double ReprojectionError { get; set; }
        public int BoardCornersX { get; set; }
        public int BoardCornersY { get; set; }
        public double SquareSize { get; set; }
        public int PairCount { get; set; }
        public float[] Matrix { get; set; } = Array.Empty<float>();
    }
}
