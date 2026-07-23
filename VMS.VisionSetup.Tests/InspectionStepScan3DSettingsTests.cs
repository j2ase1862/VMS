using System.Text.Json;
using System.Text.Json.Serialization;
using VMS.Camera.Models;
using VMS.VisionSetup.Models;
using Xunit;

namespace VMS.VisionSetup.Tests
{
    /// <summary>
    /// 스텝별 3D 카메라 파라미터(후처리 프리셋/뎁스 범위) 레시피 직렬화 회귀 테스트.
    /// RecipeService 와 동일한 JsonSerializerOptions 로 round-trip 을 검증한다.
    /// </summary>
    public class InspectionStepScan3DSettingsTests
    {
        // RecipeService.JsonOptions 와 동일 구성
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        [Fact]
        public void RoundTrip_Preserves3DCameraParameters()
        {
            var step = new InspectionStep
            {
                Name = "Top Scan",
                Exposure = 12000,
                Gain = 2.5,
                PointCloudPostProcess = PointCloudPostProcessPreset.Strong,
                UseDepthRange = true,
                DepthRangeMinMm = 780,
                DepthRangeMaxMm = 1180
            };

            var json = JsonSerializer.Serialize(step, JsonOptions);
            var restored = JsonSerializer.Deserialize<InspectionStep>(json, JsonOptions)!;

            Assert.Equal(PointCloudPostProcessPreset.Strong, restored.PointCloudPostProcess);
            Assert.True(restored.UseDepthRange);
            Assert.Equal(780, restored.DepthRangeMinMm);
            Assert.Equal(1180, restored.DepthRangeMaxMm);
        }

        [Fact]
        public void Serialize_WritesPresetAsReadableString()
        {
            var step = new InspectionStep { PointCloudPostProcess = PointCloudPostProcessPreset.Normal };

            var json = JsonSerializer.Serialize(step, JsonOptions);

            // 레시피 JSON 가독성 — 숫자가 아니라 enum 이름으로 기록
            Assert.Contains("\"pointCloudPostProcess\": \"Normal\"", json);
        }

        [Fact]
        public void Deserialize_LegacyRecipeWithoutNewFields_UsesSafeDefaults()
        {
            // 구버전 레시피 (신규 필드 없음) — 카메라 설정을 건드리지 않는 기본값이어야 함
            var legacyJson = """
            {
              "name": "Legacy Step",
              "exposure": 5000,
              "gain": 1.0
            }
            """;

            var restored = JsonSerializer.Deserialize<InspectionStep>(legacyJson, JsonOptions)!;

            Assert.Equal(PointCloudPostProcessPreset.CameraDefault, restored.PointCloudPostProcess);
            Assert.False(restored.UseDepthRange);
            // 2D 노출/게인도 카메라 설정 유지가 기본 — 구레시피 로드 시 카메라 튜닝값 보존
            Assert.True(restored.Use2DCameraDefault);
        }

        [Fact]
        public void RoundTrip_PreservesManual2DExposureMode()
        {
            // 유지 모드를 해제하고 노출을 명시한 스텝 — 재로드 후에도 push 모드 유지
            var step = new InspectionStep
            {
                Name = "Manual Exposure",
                Use2DCameraDefault = false,
                Exposure = 12000,
                Gain = 2.5
            };

            var json = JsonSerializer.Serialize(step, JsonOptions);
            var restored = JsonSerializer.Deserialize<InspectionStep>(json, JsonOptions)!;

            Assert.False(restored.Use2DCameraDefault);
            Assert.Equal(12000, restored.Exposure);
            Assert.Equal(2.5, restored.Gain);
        }

        [Fact]
        public void ExposureMs_IsUiUnitProxy_JsonStaysMicroseconds()
        {
            // UI 는 Mech-Eye Viewer 와 동일한 ms 단위, 레시피 JSON 은 µs 유지 (기존 호환)
            var step = new InspectionStep { ExposureMs = 200 };   // 200ms 입력

            Assert.Equal(200_000, step.Exposure);                 // 내부 µs
            Assert.Equal(200, step.ExposureMs);

            var json = JsonSerializer.Serialize(step, JsonOptions);
            Assert.Contains("\"exposure\": 200000", json);        // µs 로 저장
            Assert.DoesNotContain("exposureMs", json);            // UI 프록시는 직렬화 제외
        }

        [Fact]
        public void Scan3DSettings_RecordEquality_EnablesApplyCache()
        {
            // MechMindCameraAcquisition 이 동일 설정 재적용을 값 동등성으로 스킵하는 전제 검증
            var a = new Scan3DSettings
            {
                PostProcessPreset = PointCloudPostProcessPreset.Normal,
                UseDepthRange = true,
                DepthRangeMinMm = 780,
                DepthRangeMaxMm = 1180
            };
            var b = a with { };
            var c = a with { DepthRangeMaxMm = 1200 };

            Assert.Equal(a, b);
            Assert.NotEqual(a, c);
        }
    }
}
