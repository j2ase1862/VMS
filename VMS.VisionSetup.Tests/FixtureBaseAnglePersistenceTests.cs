using OpenCvSharp;
using VMS.VisionSetup.Services;
using VMS.VisionSetup.VisionTools.Measurement;
using Xunit;

namespace VMS.VisionSetup.Tests
{
    /// <summary>
    /// Fixture 기준 기울기(FixtureBaseROIAngle)의 저장·복원 검증 —
    /// 현장 확인(2026-09-22): 이 값만 레시피에 저장되지 않아, 재로드 후 첫 실행에서
    /// <c>ROIAngle = 0 + delta</c> 가 되어 회전 ROI 가 축정렬로 펴졌다.
    /// 176° 로 세팅한 캘리퍼가 0° 근처가 되면 탐색 방향이 뒤집혀 다른 엣지를 잡는다.
    /// </summary>
    public class FixtureBaseAnglePersistenceTests
    {
        private static CaliperTool MakeTool(double roiAngle, double baseAngle)
        {
            var tool = new CaliperTool
            {
                Name = "Caliper B",
                UseROI = true,
                ROI = new Rect(1492, 916, 235, 66),
                ROIAngle = roiAngle,
                // Fixture 가 한 번이라도 적용된 상태
                HasFixtureBaseROI = true,
                FixtureBaseROI = new Rect(1492, 916, 235, 66),
                FixtureRefX = 1368.6,
                FixtureRefY = 888.7,
                FixtureRefAngle = -2.835,
                FixtureBaseROIAngle = baseAngle,
            };
            return tool;
        }

        [Fact]
        public void 저장_후_로드하면_기준_기울기가_유지된다()
        {
            var config = ToolSerializer.SerializeTool(MakeTool(roiAngle: 176.9267, baseAngle: 176.9267));

            var restored = ToolSerializer.DeserializeTool(config);

            Assert.NotNull(restored);
            Assert.Equal(176.9267, restored!.FixtureBaseROIAngle, 4);
            Assert.Equal(176.9267, restored.ROIAngle, 4);
        }

        [Fact]
        public void 기준_기울기가_실행_각도와_다를_때도_그대로_복원된다()
        {
            // 직전 실행에서 ROIAngle 이 base+delta 로 갱신된 상태로 저장된 경우
            var config = ToolSerializer.SerializeTool(MakeTool(roiAngle: 181.85, baseAngle: 176.9267));

            var restored = ToolSerializer.DeserializeTool(config);

            Assert.Equal(176.9267, restored!.FixtureBaseROIAngle, 4);
        }

        [Fact]
        public void 키가_없는_구버전_레시피는_저장된_각도를_기준으로_쓴다()
        {
            // 이 키가 생기기 전에 저장된 레시피 — 0 으로 두면 회전 ROI 가 펴진다.
            var config = ToolSerializer.SerializeTool(MakeTool(roiAngle: 176.9267, baseAngle: 176.9267));
            config.Parameters.Remove("_FixtureBaseROIAngle");

            var restored = ToolSerializer.DeserializeTool(config);

            Assert.Equal(176.9267, restored!.FixtureBaseROIAngle, 4);
        }

        [Fact]
        public void Fixture를_쓰지_않는_도구는_기준_기울기를_저장하지_않는다()
        {
            var tool = new CaliperTool { UseROI = true, ROI = new Rect(10, 20, 30, 40), ROIAngle = 12.5 };
            Assert.False(tool.HasFixtureBaseROI);

            var config = ToolSerializer.SerializeTool(tool);

            Assert.DoesNotContain("_FixtureBaseROIAngle", config.Parameters.Keys);
        }
    }
}
