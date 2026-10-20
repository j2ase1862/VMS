using System;
using VMS.VisionSetup.Models;

namespace VMS.VisionSetup.VisionTools.PatternMatching
{
    /// <summary>
    /// 얼라인 도구의 로봇 변환 · 판정 설정 — Match Align 과 Multi-Step Align 이 같은 이름 · 같은 뜻으로 가진다.
    /// </summary>
    public interface IAlignOutputSettings
    {
        bool EnableRobotTransform { get; }
        double RobotM11 { get; }
        double RobotM12 { get; }
        double RobotM21 { get; }
        double RobotM22 { get; }
        double RobotThetaSign { get; }
        double MaxDeltaXY { get; }
        double MaxDeltaTheta { get; }
    }

    /// <summary>
    /// 2점 강체 정합 결과 — 기준 두 점(O1·O2)과 현재 두 점(P1·P2)으로 구한 회전 · 중심 이동.
    /// </summary>
    public readonly record struct TwoPointAlignment(
        double RefAngleDeg, double CurrentAngleDeg, double DeltaTheta,
        double RefCenterX, double RefCenterY, double CurrentCenterX, double CurrentCenterY,
        double RefLength, double CurrentLength)
    {
        public double DeltaX => CurrentCenterX - RefCenterX;
        public double DeltaY => CurrentCenterY - RefCenterY;

        /// <summary>현재 두 점 거리 / 기준 두 점 거리 — 1.0 근처가 정상, 벗어나면 오검출 · 배율 변화 의심.</summary>
        public double ScaleRatio => CurrentLength / RefLength;
    }

    /// <summary>
    /// Match Align · Multi-Step Align 공용 계산. 두 도구가 같은 식을 따로 들고 있다가 한쪽만 고쳐지는 일을 막는다.
    /// 결과 키(RobotDX/DY/DTheta · JudgmentRadial/Pass)와 판정 메시지 형식은 두 도구 공통이다.
    /// </summary>
    public static class AlignMath
    {
        /// <summary>
        /// 2점 강체 정합 — Δθ = 두 점을 잇는 벡터의 회전(매칭 각도보다 기저선이 길어 정밀), ΔX/ΔY = 두 점 중심의 이동.
        /// 기준 두 점이 겹치는지는 호출 쪽이 <see cref="TwoPointAlignment.RefLength"/> 로 판단한다.
        /// </summary>
        public static TwoPointAlignment TwoPoint(
            double o1x, double o1y, double o2x, double o2y,
            double p1x, double p1y, double p2x, double p2y)
        {
            double refLen = Math.Sqrt((o2x - o1x) * (o2x - o1x) + (o2y - o1y) * (o2y - o1y));
            double curLen = Math.Sqrt((p2x - p1x) * (p2x - p1x) + (p2y - p1y) * (p2y - p1y));

            double angO = Math.Atan2(o2y - o1y, o2x - o1x) * 180.0 / Math.PI;
            double angP = Math.Atan2(p2y - p1y, p2x - p1x) * 180.0 / Math.PI;

            return new TwoPointAlignment(
                angO, angP, FeatureMatchTool.NormalizeAngle(angP - angO),
                (o1x + o2x) / 2.0, (o1y + o2y) / 2.0,
                (p1x + p2x) / 2.0, (p1y + p2y) / 2.0,
                refLen, curLen);
        }

        /// <summary>
        /// 로봇/스테이지 좌표 변위 — 핸드아이 행렬 상위 2x2 를 Δ 에 적용(변위는 벡터라 병진 성분이 소거된다).
        /// 꺼져 있으면 아무 키도 쓰지 않는다.
        /// </summary>
        public static void WriteRobotDelta(VisionResult result, IAlignOutputSettings s,
            double dx, double dy, double dTheta)
        {
            if (!s.EnableRobotTransform) return;
            result.Data["RobotDX"] = s.RobotM11 * dx + s.RobotM12 * dy;
            result.Data["RobotDY"] = s.RobotM21 * dx + s.RobotM22 * dy;
            result.Data["RobotDTheta"] = s.RobotThetaSign * dTheta;
        }

        /// <summary>
        /// 위치(√(jx²+jy²) ≤ MaxDeltaXY) · 각도(|Δθ| ≤ MaxDeltaTheta) 판정. 불합격이면 Success 를 내리고
        /// 메시지에 " · 판정 NG: …", 합격이면 " · 판정 OK" 를 붙인다. 판정을 켤지는 호출 쪽이 정한다.
        /// </summary>
        /// <param name="thetaDecimals">메시지의 각도 소수 자릿수 (Match Align 2 · Multi-Step Align 3).</param>
        public static void Judge(VisionResult result, IAlignOutputSettings s,
            double jx, double jy, double dTheta, string unit, int thetaDecimals)
        {
            double radial = Math.Sqrt(jx * jx + jy * jy);
            bool posOk = radial <= s.MaxDeltaXY;
            bool angOk = Math.Abs(dTheta) <= s.MaxDeltaTheta;
            result.Data["JudgmentRadial"] = radial;
            result.Data["JudgmentPass"] = posOk && angOk;

            if (!posOk || !angOk)
            {
                result.Success = false;
                string theta = dTheta.ToString("F" + thetaDecimals);
                result.Message += $" · 판정 NG: {(posOk ? "" : $"위치 {radial:F3}{unit} > {s.MaxDeltaXY}{unit} ")}" +
                                  $"{(angOk ? "" : $"각도 |{theta}°| > {s.MaxDeltaTheta}°")}".TrimEnd();
            }
            else
            {
                result.Message += " · 판정 OK";
            }
        }
    }
}
