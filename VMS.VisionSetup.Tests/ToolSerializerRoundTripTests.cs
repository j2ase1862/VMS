using System.Collections.Generic;
using System.Text.Json;
using VMS.VisionSetup.Models;
using VMS.VisionSetup.Services;
using Xunit;

namespace VMS.VisionSetup.Tests
{
    /// <summary>
    /// ToolSerializer 회귀 테스트.
    /// VisionService.GetAvailableTools에 등록된 모든 도구가 Serialize → JSON → Deserialize 후
    /// 베이스 프로퍼티(ROI, Name, IsEnabled, 위치)를 유지하는지 자동 검증.
    /// 새 도구 추가 시 ToolSerializer 케이스가 누락되면 즉시 실패.
    /// </summary>
    public class ToolSerializerRoundTripTests
    {
        /// <summary>
        /// 등록된 모든 ToolType 이름을 Theory 데이터로 노출.
        /// 각 ToolType에 대해 별도 테스트 케이스가 생성되므로,
        /// 실패 시 어느 도구가 깨졌는지 즉시 식별 가능.
        /// </summary>
        public static IEnumerable<object[]> AllRegisteredToolTypes()
        {
            foreach (var kv in VisionService.GetAvailableTools())
                foreach (var toolType in kv.Value)
                    yield return new object[] { toolType };
        }

        [Theory]
        [MemberData(nameof(AllRegisteredToolTypes))]
        public void CreateTool_ReturnsNonNull(string toolType)
        {
            var tool = VisionService.CreateTool(toolType);
            Assert.NotNull(tool);
            Assert.Equal(toolType, tool.ToolType);
        }

        [Theory]
        [MemberData(nameof(AllRegisteredToolTypes))]
        public void RoundTrip_PreservesBaseProperties(string toolType)
        {
            var original = VisionService.CreateTool(toolType);
            Assert.NotNull(original);

            // 알려진 값으로 베이스 프로퍼티 변경
            original.Name = "TestTool_" + toolType;
            original.IsEnabled = false;
            original.X = 100;
            original.Y = 200;
            original.UseROI = true;
            original.ROIX = 42;
            original.ROIY = 23;
            original.ROIWidth = 100;
            original.ROIHeight = 50;
            original.ROIAngle = 15.5;
            original.ROICenterX = 92;
            original.ROICenterY = 73;

            var config = ToolSerializer.SerializeTool(original);
            Assert.NotNull(config);
            Assert.Equal(toolType, config.ToolType);

            // JSON 왕복 — 실제 레시피 저장/로드 환경 시뮬레이션
            var jsonOpts = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
            var json = JsonSerializer.Serialize(config, jsonOpts);
            var configFromJson = JsonSerializer.Deserialize<ToolConfig>(json, jsonOpts);
            Assert.NotNull(configFromJson);

            var restored = ToolSerializer.DeserializeTool(configFromJson);
            Assert.NotNull(restored);
            Assert.Equal(original.ToolType, restored.ToolType);
            Assert.Equal(original.Name, restored.Name);
            Assert.Equal(original.IsEnabled, restored.IsEnabled);
            Assert.Equal(original.X, restored.X);
            Assert.Equal(original.Y, restored.Y);
            Assert.Equal(original.UseROI, restored.UseROI);
            Assert.Equal(original.ROIX, restored.ROIX);
            Assert.Equal(original.ROIY, restored.ROIY);
            Assert.Equal(original.ROIWidth, restored.ROIWidth);
            Assert.Equal(original.ROIHeight, restored.ROIHeight);
            Assert.Equal(original.ROIAngle, restored.ROIAngle);
            Assert.Equal(original.ROICenterX, restored.ROICenterX);
            Assert.Equal(original.ROICenterY, restored.ROICenterY);
        }

        /// <summary>
        /// 전체 등록 도구를 한 번에 verify — Serialize/Deserialize switch에 누락된 케이스가
        /// 있는지 한눈에 보고. 개별 Theory 테스트 실패와 별개로 종합 진단.
        /// </summary>
        [Fact]
        public void AllRegisteredToolTypes_CanSerializeAndDeserialize()
        {
            var allTypes = new List<string>();
            foreach (var kv in VisionService.GetAvailableTools())
                allTypes.AddRange(kv.Value);

            var failures = new List<string>();
            foreach (var tt in allTypes)
            {
                var tool = VisionService.CreateTool(tt);
                if (tool == null)
                {
                    failures.Add($"{tt}: CreateTool returned null");
                    continue;
                }
                ToolConfig? config;
                try { config = ToolSerializer.SerializeTool(tool); }
                catch (System.Exception ex) { failures.Add($"{tt}: SerializeTool threw {ex.GetType().Name}: {ex.Message}"); continue; }
                if (config == null) { failures.Add($"{tt}: SerializeTool returned null"); continue; }

                var restored = ToolSerializer.DeserializeTool(config);
                if (restored == null)
                    failures.Add($"{tt}: DeserializeTool returned null (case missing in switch?)");
            }

            Assert.True(failures.Count == 0,
                "다음 도구의 직렬화가 실패했습니다:\n" + string.Join("\n", failures));
        }

        /// <summary>
        /// BlobTool Web 파라미터 연동 정책: 판정 기준값(ExpectedArea/Tolerance/ExpectedCount)
        /// 링크는 왕복 보존, 레거시 필터 링크(MinArea/MaxArea)는 로드 시 제거.
        /// </summary>
        [Fact]
        public void BlobTool_RoundTrip_PreservesJudgmentLinks_AndStripsLegacyAreaFilterLinks()
        {
            var original = VisionService.CreateTool("BlobTool");
            Assert.NotNull(original);

            original.LinkedParamCodes = new Dictionary<string, int>
            {
                ["ExpectedArea"] = 101,
                ["AreaTolerancePlus"] = 102,
                ["AreaToleranceMinus"] = 103,
                ["ExpectedCount"] = 104,
                ["ThresholdValue"] = 105,
                // 과거 UI 에서 걸 수 있었던 레거시 필터 링크 — 로드 시 제거돼야 함
                ["MinArea"] = 201,
                ["MaxArea"] = 202
            };

            var config = ToolSerializer.SerializeTool(original);
            var jsonOpts = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
            var json = JsonSerializer.Serialize(config, jsonOpts);
            var configFromJson = JsonSerializer.Deserialize<ToolConfig>(json, jsonOpts);
            Assert.NotNull(configFromJson);

            var restored = ToolSerializer.DeserializeTool(configFromJson);
            Assert.NotNull(restored);

            Assert.Equal(101, restored.LinkedParamCodes["ExpectedArea"]);
            Assert.Equal(102, restored.LinkedParamCodes["AreaTolerancePlus"]);
            Assert.Equal(103, restored.LinkedParamCodes["AreaToleranceMinus"]);
            Assert.Equal(104, restored.LinkedParamCodes["ExpectedCount"]);
            Assert.Equal(105, restored.LinkedParamCodes["ThresholdValue"]);
            Assert.False(restored.LinkedParamCodes.ContainsKey("MinArea"));
            Assert.False(restored.LinkedParamCodes.ContainsKey("MaxArea"));
        }

        /// <summary>
        /// 레거시 링크 제거는 BlobTool 한정 — 다른 도구의 동명 프로퍼티 링크는 건드리지 않는다.
        /// </summary>
        [Fact]
        public void NonBlobTool_RoundTrip_KeepsAllLinks()
        {
            var original = VisionService.CreateTool("ThresholdTool");
            Assert.NotNull(original);

            original.LinkedParamCodes = new Dictionary<string, int>
            {
                ["ThresholdValue"] = 105,
                ["MinArea"] = 201    // 가상의 링크 — BlobTool 이 아니므로 보존돼야 함
            };

            var config = ToolSerializer.SerializeTool(original);
            var restored = ToolSerializer.DeserializeTool(config);
            Assert.NotNull(restored);

            Assert.Equal(105, restored.LinkedParamCodes["ThresholdValue"]);
            Assert.Equal(201, restored.LinkedParamCodes["MinArea"]);
        }
    }
}
