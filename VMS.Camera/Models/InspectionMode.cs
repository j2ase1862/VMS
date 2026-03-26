namespace VMS.Camera.Models
{
    /// <summary>
    /// 로봇 제조사별 회전 표현 방식
    /// </summary>
    public enum EulerConvention
    {
        /// <summary>
        /// UR (Universal Robots): Rotation Vector (rx, ry, rz) in radians.
        /// 벡터의 방향 = 회전축, 크기 = 회전 각도. Rodrigues 변환 사용.
        /// </summary>
        UR_RotationVector,

        /// <summary>
        /// Doosan Robotics: Euler Angles (a, b, c) in degrees, Z-Y-X intrinsic order.
        /// R = Rz(c) · Ry(b) · Rx(a)
        /// </summary>
        Doosan_ZYX,

        /// <summary>
        /// Jaka Robotics: Euler Angles (rx, ry, rz) in degrees, X-Y-Z intrinsic order.
        /// R = Rx(rx) · Ry(ry) · Rz(rz)
        /// </summary>
        Jaka_XYZ,

        /// <summary>
        /// ABB: Quaternion (q1, q2, q3, q4) — rx,ry,rz는 q1,q2,q3이고 별도 q4 필드 사용
        /// </summary>
        ABB_Quaternion,

        /// <summary>
        /// Fanuc: Euler Angles (W, P, R) in degrees, Z-Y-X extrinsic order.
        /// R = Rx(R) · Ry(P) · Rz(W)
        /// </summary>
        Fanuc_WPR
    }

    /// <summary>
    /// 멀티뷰 정합 전략
    /// </summary>
    public enum RegistrationStrategy
    {
        /// <summary>
        /// 로봇 포즈 기반 변환만 적용 (캘리브레이션 행렬 사용)
        /// </summary>
        PoseOnly,

        /// <summary>
        /// 로봇 포즈 기반 초기 정렬 + ICP 미세 정합
        /// </summary>
        PoseWithICP
    }
}
