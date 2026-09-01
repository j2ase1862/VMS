using OpenCvSharp;
using System;
using System.Text.Json;
using VMS.VisionSetup.Models;
using VMS.VisionSetup.Services;
using VMS.VisionSetup.ViewModels.ToolSettings;
using VMS.VisionSetup.VisionTools.PatternMatching;
using Xunit;

namespace VMS.VisionSetup.Tests
{
    /// <summary>
    /// FeatureMatchTool 다중 인스턴스(MaxInstances) + 커버리지 판정(MinCoverage) +
    /// 투표 스케일 축 회귀 테스트.
    /// - 같은 각도로 놓인 동일 객체 여러 개가 전부 검출되는지 (각도당 다중 피크)
    /// - 반쪽(부분) 매칭이 커버리지 판정으로 기각되는지 (실증 PC 중심 이탈 보고 대응)
    /// - 넓은 스케일 범위에서 축소된 객체가 검출되는지 (투표 스케일 축)
    /// - MaxInstances=1 기본값에서 기존 단일 매칭 동작·키가 유지되는지
    /// </summary>
    public class FeatureMatchMultiInstanceTests
    {
        private static Mat Black(int w, int h) => new Mat(h, w, MatType.CV_8UC1, Scalar.Black);

        /// <summary>64×64 템플릿 중앙의 32×32 정사각형을 학습한 툴.</summary>
        private static FeatureMatchTool TrainSquareTool()
        {
            var tool = new FeatureMatchTool();
            using var pattern = Black(64, 64);
            Cv2.Rectangle(pattern, new Rect(16, 16, 32, 32), Scalar.White, -1);
            Assert.True(tool.TrainPattern(pattern), "합성 패턴 학습이 실패하면 테스트 전제가 깨짐");
            return tool;
        }

        [Fact]
        public void Execute_ThreeInstancesSameAngle_FindsAll()
        {
            var tool = TrainSquareTool();
            tool.MaxInstances = 5;

            using var search = Black(240, 240);
            var centers = new[] { new Point(50, 50), new Point(150, 60), new Point(70, 160) };
            foreach (var c in centers)
                Cv2.Rectangle(search, new Rect(c.X - 16, c.Y - 16, 32, 32), Scalar.White, -1);

            var result = tool.Execute(search);

            Assert.True(result.Success, result.Message);
            Assert.Equal(3, (int)result.Data["MatchCount"]);

            for (int i = 0; i < 3; i++)
            {
                double mx = (double)result.Data[$"Match{i}_CenterX"];
                double my = (double)result.Data[$"Match{i}_CenterY"];
                Assert.Contains(centers, c => Math.Abs(c.X - mx) <= 4 && Math.Abs(c.Y - my) <= 4);
            }

            // NMS — 세 인스턴스가 서로 다른 위치여야 함 (중복 채택 없음)
            for (int i = 0; i < 3; i++)
                for (int j = i + 1; j < 3; j++)
                {
                    double dx = (double)result.Data[$"Match{i}_CenterX"] - (double)result.Data[$"Match{j}_CenterX"];
                    double dy = (double)result.Data[$"Match{i}_CenterY"] - (double)result.Data[$"Match{j}_CenterY"];
                    Assert.True(Math.Sqrt(dx * dx + dy * dy) > 16,
                        $"인스턴스 {i}/{j}가 같은 위치에 중복 채택됨");
                }

            // 대표(기존) 키는 최고 점수 인스턴스와 일치해야 함 (Fixture/Align 호환)
            Assert.Equal((double)result.Data["Match0_Score"], (double)result.Data["Score"], 9);

            result.ReleaseMats();
        }

        [Fact]
        public void Execute_MaxInstancesLimitsCount()
        {
            var tool = TrainSquareTool();
            tool.MaxInstances = 2;

            using var search = Black(240, 240);
            foreach (var c in new[] { new Point(50, 50), new Point(150, 60), new Point(70, 160) })
                Cv2.Rectangle(search, new Rect(c.X - 16, c.Y - 16, 32, 32), Scalar.White, -1);

            var result = tool.Execute(search);

            Assert.True(result.Success, result.Message);
            Assert.Equal(2, (int)result.Data["MatchCount"]);
            Assert.False(result.Data.ContainsKey("Match2_Score"));
            result.ReleaseMats();
        }

        [Fact]
        public void Execute_DefaultSingleInstance_KeepsLegacyKeys()
        {
            var tool = TrainSquareTool();   // MaxInstances 기본값 1

            using var search = Black(200, 200);
            Cv2.Rectangle(search, new Rect(84, 84, 32, 32), Scalar.White, -1);

            var result = tool.Execute(search);

            Assert.True(result.Success, result.Message);
            Assert.Equal(1, (int)result.Data["MatchCount"]);
            Assert.False(result.Data.ContainsKey("Match0_Score"));   // 단일 모드엔 인덱스 키 없음
            Assert.InRange((double)result.Data["CenterX"], 96, 104); // 중심 (100,100)
            Assert.InRange((double)result.Data["CenterY"], 96, 104);
            Assert.InRange((double)result.Data["Coverage"], 0.6, 1.0);
            result.ReleaseMats();
        }

        [Fact]
        public void Execute_HalfObject_RejectedByCoverage()
        {
            var tool = TrainSquareTool();
            // 낮은 스코어 임계 = 반쪽 매칭이 스코어(≈0.5)로는 통과하는 현장 조건 재현
            tool.ScoreThreshold = 0.30;
            tool.MinCoverage = 0.80;

            using var half = Black(200, 200);
            Cv2.Rectangle(half, new Rect(84, 84, 16, 32), Scalar.White, -1);   // 절반만 존재

            var result = tool.Execute(half);
            Assert.False(result.Success, "반쪽 객체가 커버리지 판정을 통과하면 안 됨: " + result.Message);
            result.ReleaseMats();

            // 같은 설정에서 온전한 객체는 통과 — 판정이 과하게 엄격하지 않은지 확인
            using var full = Black(200, 200);
            Cv2.Rectangle(full, new Rect(84, 84, 32, 32), Scalar.White, -1);

            var ok = tool.Execute(full);
            Assert.True(ok.Success, ok.Message);
            Assert.InRange((double)ok.Data["Coverage"], 0.8, 1.0);
            ok.ReleaseMats();
        }

        [Fact]
        public void Execute_WideScaleRange_FindsShrunkObject()
        {
            var tool = TrainSquareTool();
            tool.MinScale = 0.5;
            tool.MaxScale = 1.0;

            using var search = Black(200, 200);
            // 학습 32px 정사각형의 약 0.66배 = 21px — 투표 스케일 축 없이는
            // 학습 스케일 고정 투표가 번져 후보 탈락이 잦던 케이스
            Cv2.Rectangle(search, new Rect(90, 90, 21, 21), Scalar.White, -1);

            var result = tool.Execute(search);

            Assert.True(result.Success, result.Message);
            Assert.InRange((double)result.Data["Scale"], 0.55, 0.78);
            Assert.InRange((double)result.Data["CenterX"], 96, 106);   // 중심 ≈ 100.5
            Assert.InRange((double)result.Data["CenterY"], 96, 106);
            result.ReleaseMats();
        }

        [Fact]
        public void Serializer_RoundTripsMultiInstanceParams()
        {
            var tool = new FeatureMatchTool
            {
                MaxInstances = 7,
                NmsDistanceFactor = 0.8,
                MinCoverage = 0.65
            };

            var config = ToolSerializer.SerializeTool(tool);
            // JSON 왕복 — 실제 레시피 저장/로드 환경 시뮬레이션
            var jsonOpts = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
            var json = JsonSerializer.Serialize(config, jsonOpts);
            var configFromJson = JsonSerializer.Deserialize<ToolConfig>(json, jsonOpts);
            Assert.NotNull(configFromJson);

            var restored = Assert.IsType<FeatureMatchTool>(ToolSerializer.DeserializeTool(configFromJson!));
            Assert.Equal(7, restored.MaxInstances);
            Assert.Equal(0.8, restored.NmsDistanceFactor, 6);
            Assert.Equal(0.65, restored.MinCoverage, 6);
        }

        [Fact]
        public void SettingsViewModel_WrapsMultiInstanceParams()
        {
            var tool = new FeatureMatchTool();
            var vm = new FeatureMatchToolSettingsViewModel(tool);

            vm.MaxInstances = 9;
            vm.MinCoverage = 0.7;
            vm.NmsDistanceFactor = 1.2;

            Assert.Equal(9, tool.MaxInstances);
            Assert.Equal(0.7, tool.MinCoverage, 6);
            Assert.Equal(1.2, tool.NmsDistanceFactor, 6);
            Assert.Equal(9, vm.MaxInstances);
        }

        [Fact]
        public void Parameters_AreClamped()
        {
            var tool = new FeatureMatchTool();
            tool.MaxInstances = 999;
            Assert.Equal(50, tool.MaxInstances);
            tool.MaxInstances = 0;
            Assert.Equal(1, tool.MaxInstances);
            tool.MinCoverage = 1.5;
            Assert.Equal(1.0, tool.MinCoverage, 6);
            tool.NmsDistanceFactor = 0;
            Assert.Equal(0.1, tool.NmsDistanceFactor, 6);
        }

        [Fact]
        public void ResultKeys_IncludeInstanceKeysOnlyInMultiMode()
        {
            var tool = new FeatureMatchTool();
            Assert.DoesNotContain("Match0_Score", tool.GetAvailableResultKeys());
            Assert.Contains("Coverage", tool.GetAvailableResultKeys());
            Assert.Contains("MatchCount", tool.GetAvailableResultKeys());

            tool.MaxInstances = 3;
            var keys = tool.GetAvailableResultKeys();
            Assert.Contains("Match0_Score", keys);
            Assert.Contains("Match2_CenterX", keys);
            Assert.Contains("Match3_Score", keys);   // 여분 슬롯 (ShapeMatch 규약)
        }

        [Fact]
        public void Clone_CopiesMultiInstanceParams()
        {
            var tool = new FeatureMatchTool
            {
                MaxInstances = 4,
                NmsDistanceFactor = 0.7,
                MinCoverage = 0.6
            };
            var clone = Assert.IsType<FeatureMatchTool>(tool.Clone());
            Assert.Equal(4, clone.MaxInstances);
            Assert.Equal(0.7, clone.NmsDistanceFactor, 6);
            Assert.Equal(0.6, clone.MinCoverage, 6);
        }

        [Fact]
        public void TrainPattern_FewFeaturePoints_SetsWarning()
        {
            var tool = new FeatureMatchTool();
            // 작은 저대비 패턴 → 특징점 소수 (10~59개 구간을 노림)
            using var tiny = Black(24, 24);
            Cv2.Rectangle(tiny, new Rect(8, 8, 8, 8), Scalar.White, -1);

            if (tool.TrainPattern(tiny))
            {
                var model = tool.SelectedModel!;
                if (model.ModelEdges.Count < 60)
                    Assert.NotNull(tool.LastTrainWarning);
                else
                    Assert.Null(tool.LastTrainWarning);
            }

            // 충분한 특징점의 큰 패턴은 경고 없음
            var tool2 = TrainSquareTool();
            Assert.Null(tool2.LastTrainWarning);
        }
    }
}
