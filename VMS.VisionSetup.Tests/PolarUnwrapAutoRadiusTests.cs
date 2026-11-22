using System.Linq;
using OpenCvSharp;
using VMS.VisionSetup.Models;
using VMS.VisionSetup.Services;
using VMS.VisionSetup.VisionTools.ImageProcessing;
using Xunit;

namespace VMS.VisionSetup.Tests
{
    /// <summary>
    /// Polar Unwrap 에 Circle Fit 을 Coordinates 로 연결했을 때의 반지름 자동 설정(매뉴얼 §6.1).
    ///
    /// <para>새로 놓아 반지름을 넣지 않은 도구만 원 반지름의 0.6 배 · 1.1 배로 채운다.
    /// 반지름을 직접 넣었거나 레시피에서 불러온 도구는 값이 기본값(50 / 200)과 같더라도 건드리지 않는다 —
    /// 기존 레시피의 결과가 달라지면 안 된다.</para>
    /// </summary>
    [Collection("VisionServiceSingleton")]
    public class PolarUnwrapAutoRadiusTests
    {
        /// <summary>중심 · 반지름을 내보내는 Circle Fit 대역.</summary>
        private sealed class CircleSource : VisionToolBase
        {
            public double Radius { get; set; } = 100.0;

            public CircleSource()
            {
                Name = "Circle";
                ToolType = "CircleSource";
            }

            public override VisionResult Execute(Mat inputImage)
            {
                var result = new VisionResult { Success = true, Message = "ok" };
                result.Data["CenterX"] = 200.0;
                result.Data["CenterY"] = 200.0;
                result.Data["Radius"] = Radius;
                return result;
            }

            public override VisionToolBase Clone() => new CircleSource { Radius = Radius };
        }

        private static VisionService Prepare(PolarUnwrapTool polar, CircleSource source)
        {
            var service = VisionService.Instance;
            service.ClearTools();
            using var image = new Mat(400, 400, MatType.CV_8UC1, new Scalar(64));
            service.SetImage(image);
            service.AddTool(source);
            service.AddTool(polar);
            service.AddConnection(source, polar, ConnectionType.Coordinates);
            return service;
        }

        [Fact]
        public void 새_도구는_Circle_Fit_반지름의_0_6배_1_1배로_자동_설정된다()
        {
            var polar = new PolarUnwrapTool();
            var service = Prepare(polar, new CircleSource { Radius = 100.0 });
            try
            {
                Assert.True(polar.AutoRadiusPending);

                service.ExecuteAll();

                Assert.Equal(200.0, polar.CenterX, 6);
                Assert.Equal(200.0, polar.CenterY, 6);
                Assert.Equal(60.0, polar.InnerRadius, 6);
                Assert.Equal(110.0, polar.OuterRadius, 6);
                Assert.False(polar.AutoRadiusPending);
            }
            finally
            {
                service.ClearTools();
            }
        }

        [Fact]
        public void 자동_설정은_한_번뿐이다_다음_실행에서_원이_달라져도_반지름은_그대로()
        {
            var polar = new PolarUnwrapTool();
            var source = new CircleSource { Radius = 100.0 };
            var service = Prepare(polar, source);
            try
            {
                service.ExecuteAll();
                source.Radius = 150.0;
                service.ExecuteAll();

                Assert.Equal(60.0, polar.InnerRadius, 6);
                Assert.Equal(110.0, polar.OuterRadius, 6);
            }
            finally
            {
                service.ClearTools();
            }
        }

        [Fact]
        public void 반지름을_직접_넣은_도구는_건드리지_않는다()
        {
            // 기본값과 같은 값을 넣어도 "직접 넣은 것" 이다.
            var polar = new PolarUnwrapTool { InnerRadius = 50, OuterRadius = 200 };
            var service = Prepare(polar, new CircleSource { Radius = 100.0 });
            try
            {
                service.ExecuteAll();

                Assert.Equal(50.0, polar.InnerRadius, 6);
                Assert.Equal(200.0, polar.OuterRadius, 6);
                // 중심은 종전대로 넘어온다.
                Assert.Equal(200.0, polar.CenterX, 6);
            }
            finally
            {
                service.ClearTools();
            }
        }

        [Fact]
        public void 한쪽_반지름만_넣어도_자동_설정하지_않는다()
        {
            var polar = new PolarUnwrapTool { OuterRadius = 180 };
            var service = Prepare(polar, new CircleSource { Radius = 100.0 });
            try
            {
                service.ExecuteAll();

                Assert.Equal(50.0, polar.InnerRadius, 6);
                Assert.Equal(180.0, polar.OuterRadius, 6);
            }
            finally
            {
                service.ClearTools();
            }
        }

        [Fact]
        public void 레시피에서_불러온_도구는_기본_반지름이어도_그대로다()
        {
            // 저장 → 불러오기 왕복. 반지름은 기본값(50 / 200) 그대로 저장돼 있다.
            var config = ToolSerializer.SerializeTool(new PolarUnwrapTool());
            var loaded = Assert.IsType<PolarUnwrapTool>(ToolSerializer.DeserializeTool(config));
            Assert.False(loaded.AutoRadiusPending);

            var service = Prepare(loaded, new CircleSource { Radius = 100.0 });
            try
            {
                service.ExecuteAll();

                Assert.Equal(50.0, loaded.InnerRadius, 6);
                Assert.Equal(200.0, loaded.OuterRadius, 6);
            }
            finally
            {
                service.ClearTools();
            }
        }

        [Fact]
        public void 반지름_키가_없는_레시피도_자동_설정하지_않는다()
        {
            var config = ToolSerializer.SerializeTool(new PolarUnwrapTool());
            config.Parameters.Remove("InnerRadius");
            config.Parameters.Remove("OuterRadius");

            var loaded = Assert.IsType<PolarUnwrapTool>(ToolSerializer.DeserializeTool(config));

            Assert.False(loaded.AutoRadiusPending);
            Assert.False(loaded.TryApplyAutoRadii(100.0));
            Assert.Equal(50.0, loaded.InnerRadius, 6);
            Assert.Equal(200.0, loaded.OuterRadius, 6);
        }

        [Fact]
        public void 복제한_도구는_원본의_반지름을_그대로_쓴다()
        {
            var clone = Assert.IsType<PolarUnwrapTool>(new PolarUnwrapTool().Clone());

            Assert.False(clone.TryApplyAutoRadii(100.0));
            Assert.Equal(50.0, clone.InnerRadius, 6);
            Assert.Equal(200.0, clone.OuterRadius, 6);
        }

        [Theory]
        [InlineData(0.0)]
        [InlineData(-5.0)]
        [InlineData(double.NaN)]
        [InlineData(double.PositiveInfinity)]
        public void 쓸_수_없는_원_반지름은_무시하고_대기_상태를_유지한다(double radius)
        {
            var polar = new PolarUnwrapTool();

            Assert.False(polar.TryApplyAutoRadii(radius));
            Assert.True(polar.AutoRadiusPending);
            Assert.Equal(50.0, polar.InnerRadius, 6);
            Assert.Equal(200.0, polar.OuterRadius, 6);
        }

        [Fact]
        public void 저장_형식은_그대로다_자동_설정_상태는_레시피에_남지_않는다()
        {
            var config = ToolSerializer.SerializeTool(new PolarUnwrapTool());

            Assert.Equal(
                new[] { "CenterX", "CenterY", "Direction", "InnerRadius", "OuterRadius", "OutputHeight", "OutputWidth", "StartAngleDeg" },
                config.Parameters.Keys
                    .Where(k => k is "CenterX" or "CenterY" or "Direction" or "InnerRadius" or "OuterRadius"
                        or "OutputHeight" or "OutputWidth" or "StartAngleDeg" || k.Contains("Auto") || k.Contains("Untouched"))
                    .OrderBy(k => k, System.StringComparer.Ordinal));
        }
    }
}
