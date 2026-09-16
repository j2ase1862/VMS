using System;
using OpenCvSharp;
using VMS.VisionSetup.Models;
using VMS.VisionSetup.Services;
using Xunit;

namespace VMS.VisionSetup.Tests
{
    /// <summary>
    /// Fixture(Coordinates) 연결의 <b>각도</b> 전달 검증.
    ///
    /// <para>FeatureMatch 가 좌표를 넘기면 하위 도구의 ROI 가 "부품을 따라간다"는 것이
    /// 이 연결의 컨셉이다. 이동만 따라가고 <b>기울기는 안 따라가면</b>, 부품이 돌아간 순간
    /// ROI 는 제자리 크기로 남아 부품의 다른 부분을 본다 — 판정이 조용히 틀어진다.</para>
    ///
    /// <para>2026-09-16 현장 보고: "각도 전달이 안 되는 것 같다". 실제로 예전 구현은
    /// ROI <b>중심만</b> deltaAngle 만큼 회전 이동시키고 ROI 자체는 축 정렬 Rect 로 두어
    /// <c>ROIAngle</c> 을 한 번도 쓰지 않았다.</para>
    /// </summary>
    [Collection("VisionServiceSingleton")]
    public class FixtureAnglePropagationTests
    {
        /// <summary>CenterX/CenterY/Angle 을 내보내는 FeatureMatch 대역 소스.</summary>
        private sealed class FixtureSourceTool : VisionToolBase
        {
            public double CenterX { get; set; }
            public double CenterY { get; set; }
            public double Angle { get; set; }

            public FixtureSourceTool(string name = "FixtureSource")
            {
                Name = name;
                ToolType = "FixtureSourceTool";
            }

            public override VisionResult Execute(Mat inputImage)
            {
                var result = new VisionResult { Success = true, Message = "ok" };
                result.Data["CenterX"] = CenterX;
                result.Data["CenterY"] = CenterY;
                result.Data["Angle"] = Angle;
                return result;
            }

            public override VisionToolBase Clone() => new FixtureSourceTool(Name);
        }

        /// <summary>ROI 만 받는 최소 타겟.</summary>
        private sealed class RoiTargetTool : VisionToolBase
        {
            public RoiTargetTool(string name = "RoiTarget")
            {
                Name = name;
                ToolType = "RoiTargetTool";
            }

            public override VisionResult Execute(Mat inputImage)
                => new VisionResult { Success = true, Message = "ok", OutputImage = inputImage.Clone() };

            public override VisionToolBase Clone() => new RoiTargetTool(Name);
        }

        private static VisionService PrepareService(int size = 400)
        {
            var service = VisionService.Instance;
            service.ClearTools();
            using var image = new Mat(size, size, MatType.CV_8UC3, new Scalar(64, 64, 64));
            service.SetImage(image);
            return service;
        }

        /// <summary>
        /// 소스가 회전하면 타겟 ROI 도 <b>같은 각도로 기울어져야</b> 한다.
        /// 중심 이동만으로는 부족하다 — 그래야 GetAlignedROIImage·Blob·Caliper 가
        /// 부품과 같은 방향으로 본다.
        /// </summary>
        [Fact]
        public void CoordinatesConnection_PropagatesAngleToTargetRoi()
        {
            var service = PrepareService();
            var source = new FixtureSourceTool { CenterX = 200, CenterY = 200, Angle = 0 };
            var target = new RoiTargetTool { UseROI = true, ROI = new Rect(230, 190, 40, 20) };

            service.AddTool(source);
            service.AddTool(target);
            service.AddConnection(source, target, ConnectionType.Coordinates);

            try
            {
                // ① 학습 자세 — 각도 0, 기준 스냅샷이 잡힌다
                service.ExecuteAll();
                Assert.Equal(0, target.ROIAngle, 3);

                // ② 부품이 30° 돌았다 (위치는 그대로)
                source.Angle = 30;
                service.ExecuteAll();

                Assert.Equal(30, target.ROIAngle, 3);

                // 회전 중심도 함께 실려야 한다 — GetAlignedROIImage 가 이 점을 축으로 편다.
                // ROI 사각형은 정수라 반올림되지만 ROICenter 는 정밀값을 들고 있어야 한다:
                // 기준(200,200) 기준 상대위치 (50,0) 을 30° 회전 → (+43.30, +25.00)
                Assert.Equal(200 + 50 * Math.Cos(Math.PI / 6), target.ROICenterX, 3);
                Assert.Equal(200 + 50 * Math.Sin(Math.PI / 6), target.ROICenterY, 3);

                // 사각형 중심도 같은 자리 (정수 반올림 오차 이내)
                double cx = target.ROI.X + target.ROI.Width / 2.0;
                double cy = target.ROI.Y + target.ROI.Height / 2.0;
                Assert.True(Math.Abs(cx - target.ROICenterX) <= 1.0, $"cx={cx}");
                Assert.True(Math.Abs(cy - target.ROICenterY) <= 1.0, $"cy={cy}");

                // 크기는 유지 — 회전은 각도로 표현하지 바운딩 박스를 키우지 않는다
                Assert.Equal(40, target.ROI.Width);
                Assert.Equal(20, target.ROI.Height);
            }
            finally
            {
                service.ClearTools();
            }
        }

        /// <summary>
        /// 사용자가 <b>이미 기울여 그린</b> ROI 가 기준이면, 전달 각도는 그 위에 더해져야 한다.
        /// 기준 각도를 0 으로 보면 학습 자세에서부터 ROI 가 수평으로 펴져 버린다.
        /// </summary>
        [Fact]
        public void CoordinatesConnection_AddsDeltaOnTopOfUserDrawnRoiAngle()
        {
            var service = PrepareService();
            var source = new FixtureSourceTool { CenterX = 200, CenterY = 200, Angle = 10 };
            var target = new RoiTargetTool
            {
                UseROI = true,
                ROI = new Rect(230, 190, 40, 20),
                ROIAngle = 15          // 사용자가 기울여 그린 ROI
            };

            service.AddTool(source);
            service.AddTool(target);
            service.AddConnection(source, target, ConnectionType.Coordinates);

            try
            {
                // 학습 자세(소스 10°) — 사용자가 그린 15° 가 그대로 유지돼야 한다
                service.ExecuteAll();
                Assert.Equal(15, target.ROIAngle, 3);

                // 소스가 10° → 40° (델타 +30°) → 15 + 30 = 45°
                source.Angle = 40;
                service.ExecuteAll();
                Assert.Equal(45, target.ROIAngle, 3);
            }
            finally
            {
                service.ClearTools();
            }
        }

        /// <summary>
        /// 각도가 없는(회전을 안 내보내는) 소스면 ROI 기울기는 건드리지 않는다 —
        /// 기존 이동 전용 레시피가 갑자기 돌아가면 안 된다.
        /// </summary>
        [Fact]
        public void CoordinatesConnection_WithoutAngle_LeavesRoiAngleUntouched()
        {
            var service = PrepareService();
            var source = new CenterOnlyTool();
            var target = new RoiTargetTool
            {
                UseROI = true,
                ROI = new Rect(230, 190, 40, 20),
                ROIAngle = 15
            };

            service.AddTool(source);
            service.AddTool(target);
            service.AddConnection(source, target, ConnectionType.Coordinates);

            try
            {
                service.ExecuteAll();
                source.CenterX = 260;   // 이동만
                service.ExecuteAll();

                Assert.Equal(15, target.ROIAngle, 3);
                Assert.Equal(310, target.ROI.X + target.ROI.Width / 2.0, 0);   // 250 + (260-200)
            }
            finally
            {
                service.ClearTools();
            }
        }

        /// <summary>
        /// 캔버스에 회전 ROI 도형이 떠 있어도 <b>전달받은 각도가 이긴다</b>.
        ///
        /// <para>도구들은 원래 "라이브 도형 각도 우선, 없으면 ROIAngle" 로 판단했다. 그대로 두면
        /// VisionSetup 화면에서는 전달된 각도가 도형에 가려 통째로 무시된다 — AUTO RUN 에서만
        /// 듣고 세팅 화면에서는 안 듣는, 가장 헷갈리는 형태가 된다.</para>
        /// </summary>
        [Fact]
        public void EffectiveRoiAngle_PrefersFixtureAngleOverLiveCanvasShape()
        {
            var service = PrepareService();
            var source = new FixtureSourceTool { CenterX = 200, CenterY = 200, Angle = 0 };
            var target = new RoiTargetTool { UseROI = true, ROI = new Rect(230, 190, 40, 20) };

            // 사용자가 캔버스에 20° 기울여 그려 둔 상태
            target.AssociatedROIShape = new RectangleAffineROI
            {
                CenterX = 250, CenterY = 200, Width = 40, Height = 20, Angle = 20
            };

            service.AddTool(source);
            service.AddTool(target);
            service.AddConnection(source, target, ConnectionType.Coordinates);

            try
            {
                // 학습 자세 — 기준은 캔버스 도형의 20° 여야 한다
                service.ExecuteAll();
                Assert.Equal(20, target.FixtureBaseROIAngle, 3);
                Assert.Equal(20, target.EffectiveROIAngle, 3);

                // 부품이 30° 더 돌았다 → 20 + 30 = 50°.
                // 캔버스 도형은 20° 그대로지만 실행은 50° 를 봐야 한다.
                source.Angle = 30;
                service.ExecuteAll();

                Assert.True(target.HasFixtureAngle);
                Assert.Equal(50, target.ROIAngle, 3);
                Assert.Equal(50, target.EffectiveROIAngle, 3);
                Assert.Equal(20, ((RectangleAffineROI)target.AssociatedROIShape!).Angle, 3); // 화면 도형은 그대로
            }
            finally
            {
                service.ClearTools();
            }
        }

        /// <summary>
        /// 사용자가 ROI 를 다시 그리면 전달받은 각도는 버려지고 기준을 다시 잡는다.
        /// </summary>
        [Fact]
        public void UserEditingRoi_DropsFixtureAngle()
        {
            var service = PrepareService();
            var source = new FixtureSourceTool { CenterX = 200, CenterY = 200, Angle = 0 };
            var target = new RoiTargetTool { UseROI = true, ROI = new Rect(230, 190, 40, 20) };

            service.AddTool(source);
            service.AddTool(target);
            service.AddConnection(source, target, ConnectionType.Coordinates);

            try
            {
                source.Angle = 30;
                service.ExecuteAll();
                Assert.True(target.HasFixtureAngle);

                // 사용자가 ROI 를 새로 그렸다 (Fixture 변환 중이 아닌 경로)
                target.ROI = new Rect(100, 100, 50, 50);

                Assert.False(target.HasFixtureAngle);
                Assert.False(target.HasFixtureBaseROI);
            }
            finally
            {
                service.ClearTools();
            }
        }

        /// <summary>Angle 키를 아예 내보내지 않는 소스 (Blob 등).</summary>
        private sealed class CenterOnlyTool : VisionToolBase
        {
            public double CenterX { get; set; } = 200;
            public double CenterY { get; set; } = 200;

            public CenterOnlyTool()
            {
                Name = "CenterOnly";
                ToolType = "CenterOnlyTool";
            }

            public override VisionResult Execute(Mat inputImage)
            {
                var result = new VisionResult { Success = true, Message = "ok" };
                result.Data["CenterX"] = CenterX;
                result.Data["CenterY"] = CenterY;
                return result;
            }

            public override VisionToolBase Clone() => new CenterOnlyTool();
        }
    }
}
