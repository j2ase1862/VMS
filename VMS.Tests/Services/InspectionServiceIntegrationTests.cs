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

        // 실행 컨텍스트는 VisionService 싱글톤에 걸리므로 테스트 종료 시 원복한다.
        private readonly VMS.VisionSetup.Models.CalibrationMetadata? _savedCalibration;
        private readonly double _savedStepResolution;
        private readonly string? _savedStepId;

        public InspectionServiceIntegrationTests()
        {
            // internal ctor — 운영 싱글톤(Instance) 과 분리된 테스트 전용 인스턴스.
            _service = new InspectionService();

            var vs = VMS.VisionSetup.Services.VisionService.Instance;
            _savedCalibration = vs.CurrentCalibrationMetadata;
            _savedStepResolution = vs.CurrentStepResolutionMmPerPx;
            _savedStepId = vs.CurrentStepId;
        }

        public void Dispose()
        {
            foreach (var m in _matsToDispose)
            {
                try { m.Dispose(); } catch { /* 이미 해제됨 무시 */ }
            }
            _service.ClearCache();

            var vs = VMS.VisionSetup.Services.VisionService.Instance;
            vs.CurrentCalibrationMetadata = _savedCalibration;
            vs.CurrentStepResolutionMmPerPx = _savedStepResolution;
            vs.CurrentStepId = _savedStepId;
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
        // ─── 실행 컨텍스트 (mm 판정) ────────────────────────────────

        // 아래 4건은 레시피 필드가 VMS 실행 경로에서 유실되던 결함의 회귀 방지 —
        // 같은 레시피가 VisionSetup Run All 에서는 Pass, VMS Inspect 에서는 NG 였다
        // (2026-09-22 현장). 원인은 VMS 가 들고 있던 축소 모델 사본이었고, 수정은
        // 레시피 모델을 VMS.VisionSetup.Models 로 통일한 것이다.

        [Fact]
        public async Task ExecuteStepAsync_StepResolution_FeedsEffectiveCalibration()
        {
            // 스텝 Resolution(mm/px) 이 실행 엔진에 전달되지 않으면 JudgmentUnit=Mm 도구가
            // "mm 변환 불가" 로 무조건 NG 가 된다.
            var step = StepWithTools(MakeGrayscaleToolConfig());
            step.Resolution = 0.05;
            var img = MakeTestImage();

            await _service.ExecuteStepAsync(step, img);

            var cal = VMS.VisionSetup.Services.VisionService.Instance.EffectiveCalibration;
            Assert.NotNull(cal);
            Assert.Equal(0.05, cal!.PixelSizeMm, 6);
        }

        [Fact]
        public async Task ExecuteStepAsync_RecipeCalibration_WinsOverStepResolution()
        {
            // 정식 캘리브레이션이 있으면 수동 입력값(스텝 Resolution)이 덮지 않는다.
            var step = StepWithTools(MakeGrayscaleToolConfig());
            step.Resolution = 0.05;
            _service.SetRecipeContext(new Recipe
            {
                Calibration = new VMS.VisionSetup.Models.CalibrationMetadata
                {
                    Mode = VMS.VisionSetup.Models.CalibrationMode.SinglePointScale,
                    PixelSizeMm = 0.0112,
                    SourceToolName = "CalibrationTool"
                }
            });
            var img = MakeTestImage();

            await _service.ExecuteStepAsync(step, img);

            var cal = VMS.VisionSetup.Services.VisionService.Instance.EffectiveCalibration;
            Assert.NotNull(cal);
            Assert.Equal(0.0112, cal!.PixelSizeMm, 6);
            Assert.Equal("CalibrationTool", cal.SourceToolName);
        }

        [Fact]
        public async Task ExecuteStepAsync_SetsCurrentStepId_ForPoseStore()
        {
            // 다중 스텝 얼라인이 StepPoseStore 를 이 키로 조회한다.
            var step = StepWithTools(MakeGrayscaleToolConfig());
            var img = MakeTestImage();

            await _service.ExecuteStepAsync(step, img);

            Assert.Equal(step.Id, VMS.VisionSetup.Services.VisionService.Instance.CurrentStepId);
        }

        // ─── ToolConfig → 도구 전달 무손실 ─────────────────────────

        [Fact]
        public void GetTools_PreservesRotatedRoiAndDeviceId()
        {
            // 종전 ConvertToolConfigs 가 ROI 각도·중심과 PLC DeviceId 를 떨어뜨렸다.
            // 각도가 사라지면 회전 ROI 가 0° 로 실행돼 캘리퍼 탐색 방향이 뒤집힌다.
            var step = StepWithTools(new ToolConfig
            {
                Id = Guid.NewGuid().ToString(),
                ToolType = "GrayscaleTool",
                Name = "Rotated",
                UseROI = true,
                ROIX = 10,
                ROIY = 20,
                ROIWidth = 30,
                ROIHeight = 40,
                ROIAngle = 179.67,
                ROICenterX = 25.5,
                ROICenterY = 40.5,
                PlcMappings =
                {
                    new VMS.VisionSetup.Models.PlcResultMapping
                    {
                        ResultKey = "Success",
                        DeviceId = "ADLink_1",
                        PlcAddress = "3"
                    }
                }
            });

            var tool = Assert.Single(_service.GetToolsForTest(step));

            Assert.Equal(179.67, tool.ROIAngle, 6);
            Assert.Equal(25.5, tool.ROICenterX, 6);
            Assert.Equal(40.5, tool.ROICenterY, 6);
            // 캔버스 도형이 없는 실행 경로에서는 저장된 각도가 곧 실효 각도다.
            Assert.Equal(179.67, tool.EffectiveROIAngle, 6);
            Assert.Equal("ADLink_1", Assert.Single(tool.PlcMappings).DeviceId);
        }
        // ─── 현장 증상 재현: mm 판정이 VMS 에서만 NG 였다 ──────────────

        [Fact]
        public async Task ExecuteStepAsync_MmJudgment_PassesLikeVisionSetup()
        {
            // 2026-09-22 현장: 같은 레시피가 VisionSetup Run All 은 Pass, VMS Inspect 는 NG.
            // GeometryTool(JudgmentUnit=Mm) 이 스텝 Resolution 을 받지 못해 "mm 변환 불가"
            // 로 실패했기 때문이다. Blob 두 개(60px 간격) → 거리 60px × 0.05mm/px = 3.0mm.
            using var img = MakeBlobPairImage();

            var blobLeft = MakeBlobToolConfig("BlobLeft", roiX: 0);
            var blobRight = MakeBlobToolConfig("BlobRight", roiX: 64);
            var geometry = new ToolConfig
            {
                Id = Guid.NewGuid().ToString(),
                ToolType = "GeometryTool",
                Name = "Geometry",
                Parameters = new Dictionary<string, object>
                {
                    ["Operation"] = "PointPointDistance",
                    ["EnableJudgment"] = true,
                    ["JudgmentUnit"] = "Mm",
                    ["ExpectedValue"] = 3.0,
                    ["ToleranceMinus"] = 0.5,
                    ["TolerancePlus"] = 0.5
                },
                Connections =
                {
                    new ToolConnectionConfig { SourceToolId = blobLeft.Id, ConnectionType = "Result" },
                    new ToolConnectionConfig { SourceToolId = blobRight.Id, ConnectionType = "Result" }
                }
            };

            var step = StepWithTools(blobLeft, blobRight, geometry);
            step.Resolution = 0.05;

            var result = await _service.ExecuteStepAsync(step, img);

            var geoResult = Assert.Single(result.ToolResults, t => t.ToolName == "Geometry");
            Assert.DoesNotContain("mm 변환 불가", geoResult.Message);
            Assert.True(geoResult.Success, $"Geometry 판정 실패: {geoResult.Message}");
            Assert.True(result.Success, $"스텝 판정 실패: {result.Message}");
        }

        [Fact]
        public async Task ExecuteStepAsync_MmJudgment_WithoutResolution_FailsLoudly()
        {
            // Resolution 도 캘리브레이션도 없으면 px 값을 mm 기준과 비교해 오판하는 대신
            // 명확히 실패해야 한다 (도구의 의도된 동작 — 위 테스트의 대조군).
            using var img = MakeBlobPairImage();

            var blobLeft = MakeBlobToolConfig("BlobLeft", roiX: 0);
            var blobRight = MakeBlobToolConfig("BlobRight", roiX: 64);
            var geometry = new ToolConfig
            {
                Id = Guid.NewGuid().ToString(),
                ToolType = "GeometryTool",
                Name = "Geometry",
                Parameters = new Dictionary<string, object>
                {
                    ["Operation"] = "PointPointDistance",
                    ["EnableJudgment"] = true,
                    ["JudgmentUnit"] = "Mm",
                    ["ExpectedValue"] = 3.0,
                    ["ToleranceMinus"] = 0.5,
                    ["TolerancePlus"] = 0.5
                },
                Connections =
                {
                    new ToolConnectionConfig { SourceToolId = blobLeft.Id, ConnectionType = "Result" },
                    new ToolConnectionConfig { SourceToolId = blobRight.Id, ConnectionType = "Result" }
                }
            };

            var step = StepWithTools(blobLeft, blobRight, geometry);
            step.Resolution = 0;   // 미설정

            var result = await _service.ExecuteStepAsync(step, img);

            var geoResult = Assert.Single(result.ToolResults, t => t.ToolName == "Geometry");
            Assert.False(geoResult.Success);
            Assert.Contains("mm 변환 불가", geoResult.Message);
        }

        /// <summary>검정 바탕에 흰 사각형 두 개 — 중심 간 거리 60px (BlobTool 기본 설정으로 검출).</summary>
        private Mat MakeBlobPairImage()
        {
            var m = new Mat(128, 128, MatType.CV_8UC3, new Scalar(0, 0, 0));
            Cv2.Rectangle(m, new Rect(20, 54, 20, 20), new Scalar(255, 255, 255), -1);   // 중심 (30, 64)
            Cv2.Rectangle(m, new Rect(80, 54, 20, 20), new Scalar(255, 255, 255), -1);   // 중심 (90, 64)
            _matsToDispose.Add(m);
            return m;
        }

        /// <summary>절반 폭 ROI 로 사각형 하나만 보는 BlobTool — 첫 blob 이 그 사각형이 되게 한다.</summary>
        private static ToolConfig MakeBlobToolConfig(string name, int roiX)
        {
            return new ToolConfig
            {
                Id = Guid.NewGuid().ToString(),
                ToolType = "BlobTool",
                Name = name,
                UseROI = true,
                ROIX = roiX,
                ROIY = 0,
                ROIWidth = 64,
                ROIHeight = 128
            };
        }
        // ─── 두 실행 엔진의 규칙 정합 ────────────────────────────────

        [Fact]
        public async Task ExecuteStepAsync_EnsembleTool_ReceivesConnectedSources()
        {
            // 소스 주입 목록에 EnsembleTool 이 빠져 있어 VMS 에서는 늘 소스가 비었다
            // ("소스 도구가 연결되지 않았습니다") — VisionSetup 에서는 정상 동작.
            var gray = MakeGrayscaleToolConfig("Upstream");
            var ensemble = new ToolConfig
            {
                Id = Guid.NewGuid().ToString(),
                ToolType = "EnsembleTool",
                Name = "Ensemble",
                Parameters = new Dictionary<string, object> { ["Mode"] = "Or" },
                Connections =
                {
                    new ToolConnectionConfig { SourceToolId = gray.Id, ConnectionType = "Result" }
                }
            };

            var result = await _service.ExecuteStepAsync(StepWithTools(gray, ensemble), MakeTestImage());

            var ensembleResult = Assert.Single(result.ToolResults, t => t.ToolName == "Ensemble");
            Assert.DoesNotContain("연결되지 않았습니다", ensembleResult.Message);
            Assert.True(ensembleResult.Success, $"Ensemble 실패: {ensembleResult.Message}");
        }

        [Fact]
        public async Task ExecuteStepAsync_AggregatorTools_NotSkippedWhenSourceFails()
        {
            // 집계 도구는 실패 정보를 수집해야 하므로 소스가 실패해도 실행한다 —
            // 우회 목록이 VisionService.ExecuteAll 과 같아야 판정이 갈리지 않는다.
            // 소스 없는 EnsembleTool 은 반드시 실패하므로 실패 소스로 쓴다.
            var failing = new ToolConfig
            {
                Id = Guid.NewGuid().ToString(),
                ToolType = "EnsembleTool",
                Name = "FailingSource",
                Parameters = new Dictionary<string, object> { ["Mode"] = "Or" }
            };
            var geometry = new ToolConfig
            {
                Id = Guid.NewGuid().ToString(),
                ToolType = "GeometryTool",
                Name = "Geometry",
                Connections =
                {
                    new ToolConnectionConfig { SourceToolId = failing.Id, ConnectionType = "Result" }
                }
            };

            var result = await _service.ExecuteStepAsync(StepWithTools(failing, geometry), MakeTestImage());

            Assert.False(Assert.Single(result.ToolResults, t => t.ToolName == "FailingSource").Success);

            // Geometry 는 실행됐어야 한다 — 소스 부족으로 실패하더라도 "건너뜀" 은 아니다.
            var geoResult = Assert.Single(result.ToolResults, t => t.ToolName == "Geometry");
            Assert.DoesNotContain("건너뜀", geoResult.Message);
        }

        [Fact]
        public async Task ExecuteStepAsync_NonAggregatorTool_StillSkippedWhenSourceFails()
        {
            // 대조군 — 집계 도구가 아닌 일반 도구는 종전대로 건너뛴다.
            var failing = new ToolConfig
            {
                Id = Guid.NewGuid().ToString(),
                ToolType = "EnsembleTool",
                Name = "FailingSource",
                Parameters = new Dictionary<string, object> { ["Mode"] = "Or" }
            };
            var gray = MakeGrayscaleToolConfig("Downstream");
            gray.Connections.Add(new ToolConnectionConfig
            {
                SourceToolId = failing.Id,
                ConnectionType = "Result"
            });

            var result = await _service.ExecuteStepAsync(StepWithTools(failing, gray), MakeTestImage());

            var downstream = Assert.Single(result.ToolResults, t => t.ToolName == "Downstream");
            Assert.False(downstream.Success);
            Assert.Contains("건너뜀", downstream.Message);
        }
    }
}
