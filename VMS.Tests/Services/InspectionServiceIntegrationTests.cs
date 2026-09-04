using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using VMS.Models;
using VMS.Services;
using Xunit;

namespace VMS.Tests.Services
{
    /// <summary>
    /// InspectionService 의 도구 파이프라인 흐름을 격리된 인스턴스로 검증.
    /// 실제 ToolSerializer + VisionToolBase 경로를 그대로 통과하므로
    /// 외부 의존(ONNX 가중치, 카메라)이 없는 GrayscaleTool / ResultTool 만 사용.
    ///
    /// 각 테스트는 IDisposable 의 Dispose 에서 Mat 핸들 해제 + 캐시 초기화.
    /// </summary>
    // ExecuteStep 이 InspectionService 정적 사이클 버퍼·RecentInspections·HistoryStore 에 기록하므로
    // 같은 정적 상태를 검증하는 테스트들과 직렬 실행 (병렬 시 상대 테스트의 저장소에 행이 섞인다).
    [Collection(InspectionServiceStaticsCollection.Name)]
    public class InspectionServiceIntegrationTests : IDisposable
    {
        private readonly InspectionService _service;
        private readonly List<Mat> _matsToDispose = new();

        public InspectionServiceIntegrationTests()
        {
            // internal ctor — 운영 싱글톤(Instance) 과 분리된 테스트 전용 인스턴스.
            _service = new InspectionService();
        }

        public void Dispose()
        {
            foreach (var m in _matsToDispose)
            {
                try { m.Dispose(); } catch { /* 이미 해제됨 무시 */ }
            }
            _service.ClearCache();
        }

        // ─── Helpers ────────────────────────────────────────────────

        private Mat MakeTestImage(int w = 64, int h = 64)
        {
            // 단일 회색 BGR 이미지 — GrayscaleTool 의 BGR→GRAY 경로 통과용.
            var m = new Mat(h, w, MatType.CV_8UC3, new Scalar(128, 128, 128));
            _matsToDispose.Add(m);
            return m;
        }

        private static InspectionStep StepWithTools(params ToolConfig[] tools)
        {
            return new InspectionStep
            {
                Id = "step_" + Guid.NewGuid().ToString("N"),
                Name = "TestStep",
                Tools = tools.ToList()
            };
        }

        private static ToolConfig MakeGrayscaleToolConfig(string name = "Gray", bool enabled = true)
        {
            return new ToolConfig
            {
                Id = Guid.NewGuid().ToString(),
                ToolType = "GrayscaleTool",
                Name = name,
                IsEnabled = enabled
            };
        }

        private static ToolConfig MakeResultToolConfig(string name = "Result", bool enabled = true)
        {
            return new ToolConfig
            {
                Id = Guid.NewGuid().ToString(),
                ToolType = "ResultTool",
                Name = name,
                IsEnabled = enabled,
                Parameters = new Dictionary<string, object> { ["JudgmentMode"] = "AllPass" }
            };
        }

        // ─── Empty / Null tool list ────────────────────────────────

        [Fact]
        public async Task ExecuteStepAsync_EmptyTools_ReturnsSuccess()
        {
            var step = StepWithTools();
            var img = MakeTestImage();
            var result = await _service.ExecuteStepAsync(step, img);

            Assert.True(result.Success);
            Assert.Equal("No tools to execute", result.Message);
            Assert.Empty(result.ToolResults);
        }

        [Fact]
        public async Task ExecuteStepAsync_NullTools_ReturnsSuccess()
        {
            var step = new InspectionStep { Id = "null_tools_step", Tools = null! };
            var img = MakeTestImage();
            var result = await _service.ExecuteStepAsync(step, img);

            Assert.True(result.Success);
            Assert.Equal("No tools to execute", result.Message);
        }

        // ─── Invalid tool type ─────────────────────────────────────

        [Fact]
        public async Task ExecuteStepAsync_AllInvalidToolTypes_ReturnsFalse()
        {
            var step = StepWithTools(new ToolConfig
            {
                Id = Guid.NewGuid().ToString(),
                ToolType = "TotallyBogusTool",
                Name = "Bad",
                IsEnabled = true
            });
            var img = MakeTestImage();
            var result = await _service.ExecuteStepAsync(step, img);

            Assert.False(result.Success);
            Assert.Equal("No tools could be deserialized", result.Message);
        }

        [Fact]
        public async Task ExecuteStepAsync_MixedValidAndInvalid_OnlyValidRuns()
        {
            // ToolSerializer 가 모르는 타입은 null 반환 → ctx.Tools 에서 누락.
            var gray = MakeGrayscaleToolConfig();
            var bogus = new ToolConfig
            {
                Id = Guid.NewGuid().ToString(),
                ToolType = "TotallyBogusTool",
                IsEnabled = true
            };
            var step = StepWithTools(gray, bogus);
            var img = MakeTestImage();

            var result = await _service.ExecuteStepAsync(step, img);
            Assert.True(result.Success);
            Assert.Single(result.ToolResults);
            Assert.Equal("GrayscaleTool", result.ToolResults[0].ToolType);
        }

        // ─── Single tool execution ────────────────────────────────

        [Fact]
        public async Task ExecuteStepAsync_SingleGrayscaleTool_ReturnsSuccess()
        {
            var step = StepWithTools(MakeGrayscaleToolConfig());
            var img = MakeTestImage();
            var result = await _service.ExecuteStepAsync(step, img);

            Assert.True(result.Success);
            Assert.Single(result.ToolResults);
            Assert.Equal("GrayscaleTool", result.ToolResults[0].ToolType);
            Assert.True(result.ToolResults[0].Success);
        }

        [Fact]
        public async Task ExecuteStepAsync_DisabledTool_SkipsExecution()
        {
            // IsEnabled=false 도구는 ctx.Tools 에는 있으나 실행 루프에서 continue 로 건너뜀.
            var step = StepWithTools(MakeGrayscaleToolConfig(enabled: false));
            var img = MakeTestImage();
            var result = await _service.ExecuteStepAsync(step, img);

            Assert.True(result.Success);          // 실패 발생 X → allSuccess=true
            Assert.Empty(result.ToolResults);     // 실행 건너뛰었으므로 결과 없음
        }

        // ─── ResultTool aggregation ───────────────────────────────

        [Fact]
        public async Task ExecuteStepAsync_ResultToolNoSources_AllPass_ReturnsFalse()
        {
            // AllPass 모드: total>0 필요. 연결된 소스 0개 → NG.
            var step = StepWithTools(MakeResultToolConfig());
            var img = MakeTestImage();
            var result = await _service.ExecuteStepAsync(step, img);

            Assert.False(result.Success);
            Assert.Single(result.ToolResults);
            Assert.Equal("ResultTool", result.ToolResults[0].ToolType);
        }

        [Fact]
        public async Task ExecuteStepAsync_ResultTool_AggregatesGrayscaleSource_AllPass()
        {
            // Gray (성공) → Result (AllPass) → 1/1 통과 → finalSuccess=true.
            var gray = MakeGrayscaleToolConfig("UpstreamGray");
            var resultCfg = MakeResultToolConfig("Aggregator");
            resultCfg.Connections.Add(new ToolConnectionConfig
            {
                SourceToolId = gray.Id,
                ConnectionType = "Result"
            });

            var step = StepWithTools(gray, resultCfg);
            var img = MakeTestImage();
            var stepResult = await _service.ExecuteStepAsync(step, img);

            Assert.True(stepResult.Success);
            Assert.Equal(2, stepResult.ToolResults.Count);

            var rt = stepResult.ToolResults.Single(t => t.ToolType == "ResultTool");
            Assert.True(rt.Success);
            Assert.Contains("1/1", rt.Message);
        }

        // ─── Image chain + 사이클 Mat 해제 회귀 ────────────────────

        [Fact]
        public async Task ExecuteStepAsync_ImageChain_WorksAndRepeats()
        {
            // Gray --(Image)--> Threshold 체인. 사이클 종료 시 ReleaseMats 가
            // 결과 Mat 을 해제하는데(메모리 톱니 수정, 2026-08-29), 해제가
            // 루프 도중으로 앞당겨지면 하류 도구가 죽은 Mat 을 받아 실패하고,
            // LastResult 이미지 무효화가 다음 사이클을 깨면 2회차가 실패한다.
            var gray = MakeGrayscaleToolConfig("ChainGray");
            var threshold = new ToolConfig
            {
                Id = Guid.NewGuid().ToString(),
                ToolType = "ThresholdTool",
                Name = "ChainThreshold",
                IsEnabled = true
            };
            threshold.Connections.Add(new ToolConnectionConfig
            {
                SourceToolId = gray.Id,
                ConnectionType = "Image"
            });

            var step = StepWithTools(gray, threshold);
            var img = MakeTestImage();

            var first = await _service.ExecuteStepAsync(step, img);
            Assert.True(first.Success);
            Assert.Equal(2, first.ToolResults.Count);
            Assert.All(first.ToolResults, t => Assert.True(t.Success));

            var second = await _service.ExecuteStepAsync(step, img);
            Assert.True(second.Success);
            Assert.All(second.ToolResults, t => Assert.True(t.Success));
        }

        // ─── Topological order ────────────────────────────────────

        [Fact]
        public async Task ExecuteStepAsync_TopologicalSort_RunsSourceBeforeTarget()
        {
            // step.Tools 순서를 [Result, Gray] 로 역배치해도 토폴로지 정렬 후
            // Gray 가 Result 보다 먼저 실행되어 SourceResults 에 반영됨.
            var gray = MakeGrayscaleToolConfig("UpstreamGray");
            var resultCfg = MakeResultToolConfig("Aggregator");
            resultCfg.Connections.Add(new ToolConnectionConfig
            {
                SourceToolId = gray.Id,
                ConnectionType = "Result"
            });

            var step = StepWithTools(resultCfg, gray);  // 의도적으로 역순
            var img = MakeTestImage();
            var stepResult = await _service.ExecuteStepAsync(step, img);

            // Gray 가 먼저 실행되어야 SourceResults 가 채워지고 Result 가 1/1 OK.
            var rt = stepResult.ToolResults.Single(t => t.ToolType == "ResultTool");
            Assert.True(rt.Success);
        }

        // ─── ExecutionTimeMs ──────────────────────────────────────

        [Fact]
        public async Task ExecuteStepAsync_PopulatesExecutionTimeMs()
        {
            var step = StepWithTools(MakeGrayscaleToolConfig());
            var img = MakeTestImage();
            var result = await _service.ExecuteStepAsync(step, img);

            Assert.True(result.ExecutionTimeMs >= 0);
            Assert.True(result.ToolResults[0].ExecutionTimeMs >= 0);
        }

        // ─── Exception path ───────────────────────────────────────

        [Fact]
        public async Task ExecuteStepAsync_NullImage_ReturnsExceptionResult()
        {
            // inputImage.Clone() / GetROIImage 등이 null 에서 throw → outer catch.
            var step = StepWithTools(MakeGrayscaleToolConfig());
            var result = await _service.ExecuteStepAsync(step, null!);

            Assert.False(result.Success);
            Assert.StartsWith("Inspection error:", result.Message);
        }

        // ─── Cache behavior ───────────────────────────────────────

        [Fact]
        public async Task GetOrCreateContext_CacheHit_OnSameStepId()
        {
            // 같은 step.Id 로 두 번째 호출 시: 캐싱된 도구 인스턴스 재사용.
            // 검증 — ToolConfig.IsEnabled 를 호출 사이에 false 로 바꿔도
            // 캐시된 VisionToolBase.IsEnabled 는 여전히 true → 실행됨.
            var gray = MakeGrayscaleToolConfig(enabled: true);
            var step = StepWithTools(gray);
            var img = MakeTestImage();

            var first = await _service.ExecuteStepAsync(step, img);
            Assert.Single(first.ToolResults);

            gray.IsEnabled = false;  // 캐시 후 config 변경 — 영향 X.
            var second = await _service.ExecuteStepAsync(step, img);
            Assert.Single(second.ToolResults);
        }

        [Fact]
        public async Task ClearCache_ForcesContextRebuild()
        {
            // ClearCache 후 같은 step.Id 호출하면 config 가 다시 deserialize 되어
            // 변경된 IsEnabled 가 반영됨.
            var gray = MakeGrayscaleToolConfig(enabled: true);
            var step = StepWithTools(gray);
            var img = MakeTestImage();

            var first = await _service.ExecuteStepAsync(step, img);
            Assert.Single(first.ToolResults);

            _service.ClearCache();
            gray.IsEnabled = false;
            var second = await _service.ExecuteStepAsync(step, img);
            Assert.Empty(second.ToolResults);
        }

        [Fact]
        public void ClearCache_OnEmptyCache_NoThrow()
        {
            // 빈 캐시에서 ClearCache 호출도 안전해야 함.
            _service.ClearCache();
            _service.ClearCache();
        }

        // ─── Step ID isolation ────────────────────────────────────

        [Fact]
        public async Task ExecuteStepAsync_DifferentStepIds_IsolatedContexts()
        {
            // 서로 다른 step.Id → 독립된 캐시 슬롯 → 충돌 없음.
            var stepA = StepWithTools(MakeGrayscaleToolConfig());
            var stepB = StepWithTools(MakeGrayscaleToolConfig());
            var img = MakeTestImage();

            var rA = await _service.ExecuteStepAsync(stepA, img);
            var rB = await _service.ExecuteStepAsync(stepB, img);

            Assert.True(rA.Success);
            Assert.True(rB.Success);
            Assert.Single(rA.ToolResults);
            Assert.Single(rB.ToolResults);
        }
    }
}
