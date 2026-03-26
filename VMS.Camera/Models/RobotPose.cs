using System.Numerics;
using System.Text.Json.Serialization;

namespace VMS.Camera.Models
{
    /// <summary>
    /// 로봇 TCP 포즈 데이터 (6축: X, Y, Z, Rx, Ry, Rz)
    /// </summary>
    public class RobotPose
    {
        /// <summary>위치 X (mm)</summary>
        public double X { get; set; }

        /// <summary>위치 Y (mm)</summary>
        public double Y { get; set; }

        /// <summary>위치 Z (mm)</summary>
        public double Z { get; set; }

        /// <summary>
        /// 회전 1 (Rx / W / a): 의미는 EulerConvention에 따라 다름
        /// UR: rotation vector x (rad), Doosan: a (deg), Jaka: rx (deg), Fanuc: W (deg)
        /// </summary>
        public double Rx { get; set; }

        /// <summary>
        /// 회전 2 (Ry / P / b)
        /// </summary>
        public double Ry { get; set; }

        /// <summary>
        /// 회전 3 (Rz / R / c)
        /// </summary>
        public double Rz { get; set; }

        /// <summary>
        /// ABB 쿼터니언 전용 4번째 성분 (q4/w)
        /// </summary>
        public double Q4 { get; set; }

        /// <summary>
        /// 회전 표현 방식
        /// </summary>
        public EulerConvention Convention { get; set; } = EulerConvention.UR_RotationVector;

        /// <summary>
        /// 포즈 데이터를 4x4 동차 변환 행렬로 변환
        /// </summary>
        public Matrix4x4 ToMatrix4x4()
        {
            var rotation = ComputeRotationMatrix();
            return new Matrix4x4(
                rotation.M11, rotation.M12, rotation.M13, 0,
                rotation.M21, rotation.M22, rotation.M23, 0,
                rotation.M31, rotation.M32, rotation.M33, 0,
                (float)X, (float)Y, (float)Z, 1
            );
        }

        /// <summary>
        /// 회전 성분만 3x3 → Matrix4x4 형태로 반환
        /// </summary>
        private Matrix4x4 ComputeRotationMatrix()
        {
            return Convention switch
            {
                EulerConvention.UR_RotationVector => RotationFromRotationVector(Rx, Ry, Rz),
                EulerConvention.Doosan_ZYX => RotationFromEulerZYX(Rx, Ry, Rz),
                EulerConvention.Jaka_XYZ => RotationFromEulerXYZ(Rx, Ry, Rz),
                EulerConvention.ABB_Quaternion => RotationFromQuaternion(Rx, Ry, Rz, Q4),
                EulerConvention.Fanuc_WPR => RotationFromFanucWPR(Rx, Ry, Rz),
                _ => Matrix4x4.Identity
            };
        }

        /// <summary>
        /// UR: Rotation Vector → 3x3 Rotation Matrix (Rodrigues 공식)
        /// 입력은 라디안 단위의 회전 벡터 (rx, ry, rz)
        /// 벡터의 크기 = 회전 각도(θ), 방향 = 회전축
        /// </summary>
        private static Matrix4x4 RotationFromRotationVector(double rx, double ry, double rz)
        {
            double theta = Math.Sqrt(rx * rx + ry * ry + rz * rz);

            if (theta < 1e-10)
                return Matrix4x4.Identity;

            // 단위 벡터
            double kx = rx / theta;
            double ky = ry / theta;
            double kz = rz / theta;

            double c = Math.Cos(theta);
            double s = Math.Sin(theta);
            double v = 1.0 - c;

            // Rodrigues' rotation formula
            float r11 = (float)(kx * kx * v + c);
            float r12 = (float)(kx * ky * v - kz * s);
            float r13 = (float)(kx * kz * v + ky * s);

            float r21 = (float)(ky * kx * v + kz * s);
            float r22 = (float)(ky * ky * v + c);
            float r23 = (float)(ky * kz * v - kx * s);

            float r31 = (float)(kz * kx * v - ky * s);
            float r32 = (float)(kz * ky * v + kx * s);
            float r33 = (float)(kz * kz * v + c);

            return new Matrix4x4(
                r11, r12, r13, 0,
                r21, r22, r23, 0,
                r31, r32, r33, 0,
                0, 0, 0, 1
            );
        }

        /// <summary>
        /// Doosan: Euler Angles Z-Y-X intrinsic (degrees)
        /// R = Rz(c) · Ry(b) · Rx(a)  where a=Rx, b=Ry, c=Rz
        /// </summary>
        private static Matrix4x4 RotationFromEulerZYX(double a, double b, double c)
        {
            double aRad = a * Math.PI / 180.0;
            double bRad = b * Math.PI / 180.0;
            double cRad = c * Math.PI / 180.0;

            var rz = Matrix4x4.CreateRotationZ((float)cRad);
            var ry = Matrix4x4.CreateRotationY((float)bRad);
            var rx = Matrix4x4.CreateRotationX((float)aRad);

            return rx * ry * rz; // 행 우선(row-major) System.Numerics 순서
        }

        /// <summary>
        /// Jaka: Euler Angles X-Y-Z intrinsic (degrees)
        /// R = Rx(rx) · Ry(ry) · Rz(rz)
        /// </summary>
        private static Matrix4x4 RotationFromEulerXYZ(double rx, double ry, double rz)
        {
            double rxRad = rx * Math.PI / 180.0;
            double ryRad = ry * Math.PI / 180.0;
            double rzRad = rz * Math.PI / 180.0;

            var mrx = Matrix4x4.CreateRotationX((float)rxRad);
            var mry = Matrix4x4.CreateRotationY((float)ryRad);
            var mrz = Matrix4x4.CreateRotationZ((float)rzRad);

            return mrz * mry * mrx;
        }

        /// <summary>
        /// ABB: Quaternion (q1, q2, q3, q4) where q4 = w (scalar part)
        /// </summary>
        private static Matrix4x4 RotationFromQuaternion(double q1, double q2, double q3, double q4)
        {
            var q = Quaternion.Normalize(new Quaternion((float)q1, (float)q2, (float)q3, (float)q4));
            return Matrix4x4.CreateFromQuaternion(q);
        }

        /// <summary>
        /// Fanuc: W-P-R (degrees), Z-Y-X extrinsic = X-Y-Z intrinsic
        /// R = Rx(R) · Ry(P) · Rz(W)  where W=Rx, P=Ry, R=Rz
        /// </summary>
        private static Matrix4x4 RotationFromFanucWPR(double w, double p, double r)
        {
            double wRad = w * Math.PI / 180.0;
            double pRad = p * Math.PI / 180.0;
            double rRad = r * Math.PI / 180.0;

            var mrz = Matrix4x4.CreateRotationZ((float)wRad);
            var mry = Matrix4x4.CreateRotationY((float)pRad);
            var mrx = Matrix4x4.CreateRotationX((float)rRad);

            return mrz * mry * mrx;
        }

        /// <summary>
        /// 문자열 파싱 (로봇에서 수신한 CSV 형태)
        /// 형식: "x, y, z, rx, ry, rz" 또는 ABB: "x, y, z, q1, q2, q3, q4"
        /// </summary>
        public static RobotPose Parse(string data, EulerConvention convention)
        {
            var parts = data.Split(',', StringSplitOptions.TrimEntries);

            var pose = new RobotPose
            {
                Convention = convention,
                X = double.Parse(parts[0]),
                Y = double.Parse(parts[1]),
                Z = double.Parse(parts[2]),
                Rx = double.Parse(parts[3]),
                Ry = double.Parse(parts[4]),
                Rz = double.Parse(parts[5])
            };

            if (convention == EulerConvention.ABB_Quaternion && parts.Length >= 7)
            {
                pose.Q4 = double.Parse(parts[6]);
            }

            return pose;
        }

        public override string ToString()
            => $"Pose({X:F2}, {Y:F2}, {Z:F2}, {Rx:F4}, {Ry:F4}, {Rz:F4})";
    }
}
