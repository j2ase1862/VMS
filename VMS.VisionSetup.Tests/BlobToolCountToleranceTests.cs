using System.Text.Json;
using OpenCvSharp;
using VMS.VisionSetup.Models;
using VMS.VisionSetup.Services;
using VMS.VisionSetup.ViewModels.ToolSettings;
using VMS.VisionSetup.VisionTools.BlobAnalysis;
using Xunit;

namespace VMS.VisionSetup.Tests
{
    /// <summary>
    /// BlobTool 개수 판정 Tolerance 모드 (기준 ± 공차, Web 연동용).
    /// 기존 Range(Min~Max) 모드는 그대로 두고 새 모드로 추가 — 호환 회귀 포함.
    /// </summary>
    public class BlobToolCountToleranceTests
    {
        // ── 판정 로직 ──

        private static Mat MakeBlobImage(int blobCount)
        {
            // 640x160 검정 배경에 흰 원 N개 (r=15 → 면적 ~707, 기본 MinArea=100 통과)
            var img = new Mat(160, 640, MatType.CV_8UC1, Scalar.Black);
            for (int i = 0; i < blobCount; i++)
                Cv2.Circle(img, new Point(50 + i * 60, 80), 15, Scalar.White, -1);
            return img;
        }

        [Theory]
        [InlineData(5, 5, 2, 2, true)]    // 기준 5±2, 검출 5 → 합격
        [InlineData(3, 5, 2, 2, true)]    // 하한 경계 (5−2=3) → 합격
        [InlineData(7, 5, 2, 2, true)]    // 상한 경계 (5+2=7) → 합격
        [InlineData(2, 5, 2, 2, false)]   // 하한 미달 → 불합격
        [InlineData(8, 5, 2, 2, false)]   // 상한 초과 → 불합격
        [InlineData(5, 5, 0, 0, true)]    // 공차 0 = Equal 동작
        [InlineData(4, 5, 0, 0, false)]
        public void ToleranceMode_JudgesCountWindow(
            int actualBlobs, int expected, int upperTol, int lowerTol, bool shouldPass)
        {
            using var img = MakeBlobImage(actualBlobs);
            var tool = new BlobTool
            {
                UseCountJudgment = true,
                CountMode = CountJudgmentMode.Tolerance,
                ExpectedCount = expected,
                CountUpperTol = upperTol,
                CountLowerTol = lowerTol
            };

            var result = tool.Execute(img);

            Assert.Equal(actualBlobs, result.Data["BlobCount"]);
            Assert.Equal(shouldPass, result.Success);
            Assert.Equal(shouldPass, (bool)result.Data["CountJudgment"]);
        }

        [Fact]
        public void RangeMode_Unchanged()
        {
            // 기존 Range(Min~Max) 모드 호환 회귀 — Tolerance 추가와 무관하게 동작 유지
            using var img = MakeBlobImage(4);
            var tool = new BlobTool
            {
                UseCountJudgment = true,
                CountMode = CountJudgmentMode.Range,
                ExpectedCount = 3,
                ExpectedCountMax = 5
            };

            var result = tool.Execute(img);
            Assert.True(result.Success);
        }

        [Fact]
        public void LowerTol_AbsorbsNegativeSign()
        {
            // Web은 하한 공차를 음수(−2)로 등록하는 관례 — 크기로 해석해야 함
            var tool = new BlobTool { CountLowerTol = -2, CountUpperTol = -3 };
            Assert.Equal(2, tool.CountLowerTol);
            Assert.Equal(3, tool.CountUpperTol);
        }

        // ── 직렬화 왕복 ──

        [Fact]
        public void Serializer_RoundTrips_ToleranceParams()
        {
            var original = new BlobTool
            {
                UseCountJudgment = true,
                CountMode = CountJudgmentMode.Tolerance,
                ExpectedCount = 5,
                CountUpperTol = 2,
                CountLowerTol = 1
            };

            var config = ToolSerializer.SerializeTool(original);
            var jsonOpts = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
            var json = JsonSerializer.Serialize(config, jsonOpts);
            var configFromJson = JsonSerializer.Deserialize<ToolConfig>(json, jsonOpts);
            Assert.NotNull(configFromJson);

            var restored = Assert.IsType<BlobTool>(ToolSerializer.DeserializeTool(configFromJson));
            Assert.Equal(CountJudgmentMode.Tolerance, restored.CountMode);
            Assert.Equal(5, restored.ExpectedCount);
            Assert.Equal(2, restored.CountUpperTol);
            Assert.Equal(1, restored.CountLowerTol);
        }

        [Fact]
        public void Serializer_LegacyConfigWithoutToleranceKeys_DefaultsToZero()
        {
            // 구버전 레시피(공차 키 없음) 로드 시 기본 0 — Range/Equal 동작 불변
            var legacy = new BlobTool { CountMode = CountJudgmentMode.Range, ExpectedCount = 3 };
            var config = ToolSerializer.SerializeTool(legacy);
            config.Parameters.Remove("CountUpperTol");
            config.Parameters.Remove("CountLowerTol");

            var restored = Assert.IsType<BlobTool>(ToolSerializer.DeserializeTool(config));
            Assert.Equal(0, restored.CountUpperTol);
            Assert.Equal(0, restored.CountLowerTol);
            Assert.Equal(CountJudgmentMode.Range, restored.CountMode);
        }

        [Fact]
        public void Clone_CopiesToleranceParams()
        {
            var tool = new BlobTool { CountUpperTol = 4, CountLowerTol = 3 };
            var clone = Assert.IsType<BlobTool>(tool.Clone());
            Assert.Equal(4, clone.CountUpperTol);
            Assert.Equal(3, clone.CountLowerTol);
        }

        // ── Settings VM 왕복 (VM 래퍼 누락 시 화면 공백+저장 불가 회귀 방지) ──

        [Fact]
        public void SettingsViewModel_RoundTrips_ToleranceParams()
        {
            var tool = new BlobTool();
            var vm = new BlobToolSettingsViewModel(tool);

            vm.CountMode = CountJudgmentMode.Tolerance;
            vm.CountUpperTol = 2;
            vm.CountLowerTol = 1;

            Assert.Equal(CountJudgmentMode.Tolerance, tool.CountMode);
            Assert.Equal(2, tool.CountUpperTol);
            Assert.Equal(1, tool.CountLowerTol);
            Assert.Equal(2, vm.CountUpperTol);
            Assert.Equal(1, vm.CountLowerTol);
        }
    }
}
