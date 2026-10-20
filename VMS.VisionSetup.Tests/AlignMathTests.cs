using System;
using VMS.VisionSetup.Models;
using VMS.VisionSetup.VisionTools.PatternMatching;
using Xunit;

namespace VMS.VisionSetup.Tests
{
    /// <summary>
    /// AlignMath — Match Align · Multi-Step Align 공용 계산(2점 정합 · 로봇 변환 · 판정) 검증.
    /// 두 도구의 결과 키 · 메시지 형식이 이 한 곳에서 나온다.
    /// </summary>
    public class AlignMathTests
    {
        private sealed class Settings : IAlignOutputSettings
        {
            public bool EnableRobotTransform { get; init; }
            public double RobotM11 { get; init; } = 1;
            public double RobotM12 { get; init; }
            public double RobotM21 { get; init; }
            public double RobotM22 { get; init; } = 1;
            public double RobotThetaSign { get; init; } = 1;
            public double MaxDeltaXY { get; init; } = 1;
            public double MaxDeltaTheta { get; init; } = 1;
        }

        [Fact]
        public void TwoPoint_rotation_and_centre_shift()
        {
            // 기준: (0,0)–(100,0). 현재: 중심 (10,5) 이동 + 90° 회전 → (60,−45)–(60,55)
            var a = AlignMath.TwoPoint(0, 0, 100, 0, 60, -45, 60, 55);

            Assert.Equal(90.0, a.DeltaTheta, 9);
            Assert.Equal(10.0, a.DeltaX, 9);
            Assert.Equal(5.0, a.DeltaY, 9);
            Assert.Equal(1.0, a.ScaleRatio, 9);
            Assert.Equal(0.0, a.RefAngleDeg, 9);
            Assert.Equal(90.0, a.CurrentAngleDeg, 9);
        }

        [Fact]
        public void TwoPoint_angle_wraps_across_180()
        {
            // 기준 179°, 현재 −179° → Δθ = +2° (−358° 가 아니다)
            double r = Math.PI / 180;
            var a = AlignMath.TwoPoint(0, 0, Math.Cos(179 * r), Math.Sin(179 * r),
                                       0, 0, Math.Cos(-179 * r), Math.Sin(-179 * r));

            Assert.Equal(2.0, a.DeltaTheta, 6);
        }

        [Fact]
        public void TwoPoint_reports_zero_reference_length_for_identical_points()
        {
            var a = AlignMath.TwoPoint(5, 5, 5, 5, 1, 2, 3, 4);

            Assert.Equal(0.0, a.RefLength);
        }

        [Fact]
        public void WriteRobotDelta_applies_matrix_and_sign_only_when_enabled()
        {
            var off = new VisionResult();
            AlignMath.WriteRobotDelta(off, new Settings(), 1, 2, 3);
            Assert.False(off.Data.ContainsKey("RobotDX"));

            var on = new VisionResult();
            AlignMath.WriteRobotDelta(on, new Settings
            {
                EnableRobotTransform = true, RobotM11 = 0, RobotM12 = -1, RobotM21 = 1, RobotM22 = 0, RobotThetaSign = -1
            }, 2, 5, 1.5);

            Assert.Equal(-5.0, (double)on.Data["RobotDX"], 9);
            Assert.Equal(2.0, (double)on.Data["RobotDY"], 9);
            Assert.Equal(-1.5, (double)on.Data["RobotDTheta"], 9);
        }

        [Fact]
        public void Judge_pass_appends_ok_and_keeps_success()
        {
            var r = new VisionResult { Success = true, Message = "Δ" };

            AlignMath.Judge(r, new Settings { MaxDeltaXY = 1, MaxDeltaTheta = 1 }, 0.6, 0.8, 0.5, "mm", 2);

            Assert.True(r.Success);
            Assert.Equal("Δ · 판정 OK", r.Message);
            Assert.Equal(1.0, (double)r.Data["JudgmentRadial"], 9);   // 경계값은 합격
            Assert.True((bool)r.Data["JudgmentPass"]);
        }

        [Fact]
        public void Judge_fail_names_position_and_angle_with_requested_decimals()
        {
            var s = new Settings { MaxDeltaXY = 0.5, MaxDeltaTheta = 1 };

            var both = new VisionResult { Success = true, Message = "Δ" };
            AlignMath.Judge(both, s, 3, 4, -1.23456, "px", 3);
            Assert.False(both.Success);
            Assert.False((bool)both.Data["JudgmentPass"]);
            Assert.Equal("Δ · 판정 NG: 위치 5.000px > 0.5px 각도 |-1.235°| > 1°", both.Message);

            var angleOnly = new VisionResult { Success = true, Message = "Δ" };
            AlignMath.Judge(angleOnly, s, 0, 0, 2.5, "mm", 2);
            Assert.Equal("Δ · 판정 NG: 각도 |2.50°| > 1°", angleOnly.Message);

            var positionOnly = new VisionResult { Success = true, Message = "Δ" };
            AlignMath.Judge(positionOnly, s, 1, 0, 0, "mm", 2);
            // 위치만 NG 면 끝에 공백 하나가 남는다(TrimEnd 가 각도 부분에만 걸림) — 추출 전 두 도구와 같은 출력.
            Assert.Equal("Δ · 판정 NG: 위치 1.000mm > 0.5mm ", positionOnly.Message);
        }
    }
}
