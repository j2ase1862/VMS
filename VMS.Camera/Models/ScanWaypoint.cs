using System.Numerics;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace VMS.Camera.Models
{
    /// <summary>
    /// 스캔 웨이포인트: 3D 공간에서 카메라 촬영 위치 및 방향 정의
    /// UI에서 배치 → RobotPose로 변환 → 로봇에 전송
    /// </summary>
    public class ScanWaypoint : ObservableObject
    {
        private int _index;
        /// <summary>촬영 순서 (0부터)</summary>
        public int Index
        {
            get => _index;
            set => SetProperty(ref _index, value);
        }

        private string _name = string.Empty;
        /// <summary>웨이포인트 이름 (예: "Top", "Front-Left")</summary>
        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }

        private Vector3 _cameraPosition;
        /// <summary>카메라 위치 (월드 좌표, mm)</summary>
        public Vector3 CameraPosition
        {
            get => _cameraPosition;
            set => SetProperty(ref _cameraPosition, value);
        }

        private Vector3 _lookAtTarget;
        /// <summary>카메라가 바라보는 대상 점 (월드 좌표, mm)</summary>
        public Vector3 LookAtTarget
        {
            get => _lookAtTarget;
            set => SetProperty(ref _lookAtTarget, value);
        }

        private bool _isCompleted;
        /// <summary>촬영 완료 여부</summary>
        [JsonIgnore]
        public bool IsCompleted
        {
            get => _isCompleted;
            set => SetProperty(ref _isCompleted, value);
        }

        /// <summary>카메라 방향 벡터 (Position → LookAt 정규화)</summary>
        [JsonIgnore]
        public Vector3 Direction
        {
            get
            {
                var dir = LookAtTarget - CameraPosition;
                float len = dir.Length();
                return len > 1e-6f ? dir / len : -Vector3.UnitZ;
            }
        }

        /// <summary>카메라-대상 간 거리 (mm)</summary>
        [JsonIgnore]
        public float Distance => Vector3.Distance(CameraPosition, LookAtTarget);

        /// <summary>
        /// 카메라 포즈를 로봇 TCP 포즈로 변환
        /// T_tcp = T_base_target × Inverse(T_tcp_cam)
        /// </summary>
        /// <param name="handEyeInverse">핸드-아이 역행렬 Inverse(T_tcp_cam)</param>
        /// <param name="convention">로봇 오일러 표현 방식</param>
        public RobotPose ToRobotPose(Matrix4x4 handEyeInverse, EulerConvention convention)
        {
            // 카메라 포즈 행렬 구성: LookAt → 회전 행렬
            var cameraMatrix = CreateLookAtMatrix(CameraPosition, LookAtTarget);

            // T_tcp = T_cam × Inverse(T_tcp_cam)
            var tcpMatrix = cameraMatrix * handEyeInverse;

            // Matrix4x4에서 위치 추출
            var pose = new RobotPose
            {
                Convention = convention,
                X = tcpMatrix.M41,
                Y = tcpMatrix.M42,
                Z = tcpMatrix.M43
            };

            // 회전 행렬에서 Convention에 맞는 회전값 추출
            ExtractRotation(tcpMatrix, convention, pose);

            return pose;
        }

        /// <summary>
        /// 핸드-아이 행렬 없이 카메라 위치를 직접 TCP 포즈로 사용
        /// (카메라가 TCP와 동일 좌표계인 경우, Eye-in-Hand가 아닌 경우)
        /// </summary>
        public RobotPose ToDirectPose(EulerConvention convention)
        {
            return ToRobotPose(Matrix4x4.Identity, convention);
        }

        /// <summary>LookAt 카메라 행렬 생성 (OpenGL 스타일)</summary>
        private static Matrix4x4 CreateLookAtMatrix(Vector3 eye, Vector3 target)
        {
            var forward = Vector3.Normalize(target - eye);

            // Up 힌트: forward가 거의 수직이면 Y축, 아니면 Z축
            var upHint = MathF.Abs(Vector3.Dot(forward, Vector3.UnitZ)) > 0.95f
                ? Vector3.UnitY
                : Vector3.UnitZ;

            var right = Vector3.Normalize(Vector3.Cross(forward, upHint));
            var up = Vector3.Cross(right, forward);

            return new Matrix4x4(
                right.X, right.Y, right.Z, 0,
                up.X, up.Y, up.Z, 0,
                -forward.X, -forward.Y, -forward.Z, 0,
                eye.X, eye.Y, eye.Z, 1
            );
        }

        /// <summary>회전 행렬에서 Convention별 회전값 추출</summary>
        private static void ExtractRotation(Matrix4x4 m, EulerConvention convention, RobotPose pose)
        {
            switch (convention)
            {
                case EulerConvention.UR_RotationVector:
                    // 회전 행렬 → Rotation Vector (Rodrigues 역변환)
                    ExtractRotationVector(m, out double rvx, out double rvy, out double rvz);
                    pose.Rx = rvx;
                    pose.Ry = rvy;
                    pose.Rz = rvz;
                    break;

                case EulerConvention.Doosan_ZYX:
                    // R = Rz(c) · Ry(b) · Rx(a) → a,b,c 추출 (degrees)
                    pose.Ry = Math.Asin(-Clamp(m.M13, -1, 1)) * 180.0 / Math.PI;
                    pose.Rx = Math.Atan2(m.M23, m.M33) * 180.0 / Math.PI;
                    pose.Rz = Math.Atan2(m.M12, m.M11) * 180.0 / Math.PI;
                    break;

                case EulerConvention.Jaka_XYZ:
                    // R = Rx(rx) · Ry(ry) · Rz(rz) → rx,ry,rz 추출 (degrees)
                    pose.Ry = Math.Asin(Clamp(m.M31, -1, 1)) * 180.0 / Math.PI;
                    pose.Rx = Math.Atan2(-m.M32, m.M33) * 180.0 / Math.PI;
                    pose.Rz = Math.Atan2(-m.M21, m.M11) * 180.0 / Math.PI;
                    break;

                case EulerConvention.ABB_Quaternion:
                    var q = Quaternion.CreateFromRotationMatrix(m);
                    pose.Rx = q.X; pose.Ry = q.Y; pose.Rz = q.Z; pose.Q4 = q.W;
                    break;

                case EulerConvention.Fanuc_WPR:
                    // Same as ZYX extrinsic → XYZ intrinsic (degrees)
                    pose.Ry = Math.Asin(-Clamp(m.M13, -1, 1)) * 180.0 / Math.PI;
                    pose.Rz = Math.Atan2(m.M23, m.M33) * 180.0 / Math.PI;
                    pose.Rx = Math.Atan2(m.M12, m.M11) * 180.0 / Math.PI;
                    break;
            }
        }

        /// <summary>회전 행렬 → Rotation Vector (Rodrigues 역변환)</summary>
        private static void ExtractRotationVector(Matrix4x4 m, out double rx, out double ry, out double rz)
        {
            double trace = m.M11 + m.M22 + m.M33;
            double theta = Math.Acos(Math.Clamp((trace - 1.0) / 2.0, -1.0, 1.0));

            if (theta < 1e-6)
            {
                rx = ry = rz = 0;
                return;
            }

            double sinTheta = 2.0 * Math.Sin(theta);
            if (Math.Abs(sinTheta) < 1e-10)
            {
                rx = ry = rz = 0;
                return;
            }

            rx = (m.M32 - m.M23) / sinTheta * theta;
            ry = (m.M13 - m.M31) / sinTheta * theta;
            rz = (m.M21 - m.M12) / sinTheta * theta;
        }

        private static double Clamp(double v, double min, double max)
            => v < min ? min : v > max ? max : v;

        public override string ToString()
            => $"[{Index}] {Name} Pos({CameraPosition.X:F0},{CameraPosition.Y:F0},{CameraPosition.Z:F0})";
    }

    /// <summary>
    /// 웨이포인트 자동 생성 패턴
    /// </summary>
    public enum WaypointPattern
    {
        /// <summary>대상 주위 수평 링 (N개 등간격)</summary>
        Ring,
        /// <summary>상면 1개 + 수평 링 N개</summary>
        TopPlusRing,
        /// <summary>반구형 배치 (위경도 그리드)</summary>
        Hemisphere,
        /// <summary>사용자 수동 배치</summary>
        Custom
    }
}
