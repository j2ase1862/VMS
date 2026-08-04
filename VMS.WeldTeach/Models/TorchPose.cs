namespace VMS.WeldTeach.Models;

/// <summary>
/// 로봇 전달용 6-DoF 토치 포즈. 각도는 도(deg), ZYX(yaw-pitch-roll) 오일러 규약.
/// Z축 = 진행 방향(접선), X축 = 토치 이등분 방향, Y = Z × X.
/// 실제 로봇 벤더 연동 시 해당 벤더의 오일러 규약으로 변환 필요 (PoC 범위 외).
/// </summary>
public record TorchPose(
    double X, double Y, double Z,
    double RollDeg, double PitchDeg, double YawDeg);
