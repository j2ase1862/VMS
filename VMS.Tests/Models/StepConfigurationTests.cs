using System.Text.Json;
using VMS.Models;
using Xunit;

namespace VMS.Tests.Models
{
    /// <summary>
    /// system_config.json 의 StepConfiguration 하위 호환 검증 —
    /// 구버전 파일(Use2DCameraDefault 필드 없음)은 "카메라 설정 유지" 로 로드되어
    /// 촬영 시 카메라 튜닝값을 덮어쓰지 않아야 한다.
    /// </summary>
    public class StepConfigurationTests
    {
        [Fact]
        public void Deserialize_LegacyStepWithoutUse2DCameraDefault_DefaultsToTrue()
        {
            var legacyJson = """
            {
              "StepNumber": 1,
              "Name": "Step 1",
              "Exposure": 8000,
              "Gain": 2.0
            }
            """;

            var restored = JsonSerializer.Deserialize<StepConfiguration>(legacyJson)!;

            Assert.True(restored.Use2DCameraDefault);
            Assert.Equal(8000, restored.Exposure);
            Assert.Equal(2.0, restored.Gain);
        }

        [Fact]
        public void RoundTrip_PreservesManualExposureMode()
        {
            var step = new StepConfiguration
            {
                StepNumber = 2,
                Name = "Step 2",
                Use2DCameraDefault = false,
                Exposure = 12000,
                Gain = 3.5
            };

            var json = JsonSerializer.Serialize(step);
            var restored = JsonSerializer.Deserialize<StepConfiguration>(json)!;

            Assert.False(restored.Use2DCameraDefault);
            Assert.Equal(12000, restored.Exposure);
            Assert.Equal(3.5, restored.Gain);
        }
    }
}
