namespace VMS.Camera.Models
{
    /// <summary>
    /// 뎁스 카메라 내부 파라미터 (핀홀 모델, 초점거리·주점은 픽셀 단위).
    /// organized 점군의 X/Y(뎁스맵 픽셀 좌표)를 mm로 환산할 때 사용:
    /// 깊이 Z(mm)에서의 mm/px = Z / Fx (가로), Z / Fy (세로).
    /// </summary>
    public class DepthIntrinsics
    {
        public double Fx { get; init; }
        public double Fy { get; init; }
        public double Cx { get; init; }
        public double Cy { get; init; }

        public bool IsValid => Fx > 0 && Fy > 0;
    }
}
