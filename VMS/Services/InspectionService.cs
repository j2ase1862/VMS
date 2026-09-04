using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using VMS.Core.Interfaces;
using VMS.Core.Models;
using VMS.Core.Models.ParameterSync;
using VMS.Core.Security;
using VMS.Core.Services;
using VMS.Interfaces;
using VMS.Models;
using VMS.VisionSetup.Interfaces;
using VsToolConfig = VMS.VisionSetup.Models.ToolConfig;
using VsConnectionType = VMS.VisionSetup.Models.ConnectionType;
using VisionToolBase = VMS.VisionSetup.Models.VisionToolBase;
using VisionResult = VMS.VisionSetup.Models.VisionResult;
using VMS.VisionSetup.VisionTools.Result;
using VMS.VisionSetup.Services;
using VMS.VisionSetup.VisionTools.Measurement;
using VMS.VisionSetup.VisionTools.PatternMatching;

namespace VMS.Services
{
    /// <summary>
    /// VMS.VisionSetup의 도구 실행 엔진을 활용하여 레시피 기반 검사를 실행하는 서비스.
    /// 도구 인스턴스를 Step ID별로 캐싱하여 Fixture 기준 좌표(FixtureRef)가
    /// 세션 내에서 유지되도록 함.
    /// </summary>
    public class InspectionService : IInspectionService
    {
        private static InspectionService? _instance;
        public static InspectionService Instance => _instance ??= new InspectionService();

        private readonly Dictionary<string, StepExecutionContext> _stepContexts = new();
        private readonly object _contextLock = new();

        /// <summary>Web 파라미터 동기화 서비스 (외부 주입, nullable)</summary>
        public static IParameterSyncService? ParameterSyncService { get; set; }

        /// <summary>Web 파라미터 적용 서비스 (외부 주입, nullable)</summary>
        public static IParameterApplyService? ParameterApplyService { get; set; }

        /// <summary>
        /// 현재 로컬 레시피 이름 공급자 (외부 주입, nullable). 단독 모드나 Web 미연동
        /// 레시피에서도 Recent Inspections 에 레시피 이름을 남기기 위해 사용 —
        /// Web 연동 레시피면 Web 레시피 이름이 우선한다.
        /// </summary>
        public static Func<string?>? CurrentRecipeNameProvider { get; set; }

        /// <summary>
        /// 로컬 검사 이력 영구 저장소 (외부 주입, nullable — inspectionHistory.enabled=false 면 null).
        /// Recent Inspections 와 같은 지점에서 사이클/검사 1건씩 기록. 큐 push 만 하므로 택트 무관.
        /// </summary>
        public static VMS.Services.LocalHistory.ILocalInspectionHistoryStore? HistoryStore { get; set; }

        // ─── "1사이클 = 1개" 사이클 누적 (AUTO RUN, 2026-08-18) ───
        // 활성 시 검사별 Web 업로드/로컬 이력 push 를 버퍼에 모았다가
        // FlushCycleResultAsync 에서 사이클당 1건으로 업로드한다.
        private static readonly object _cycleLock = new();
        private static bool _cycleAccumulating;
        private static readonly List<ParameterResultDto> _cycleParamResults = new();
        private static readonly List<string> _cycleFailedTools = new();
        private static readonly List<VMS.Services.LocalHistory.LocalToolResult> _cycleToolResults = new();
        private static double _cycleTotalMs;
        private static InspectionFeatureMetrics? _cycleFeatureMetrics;
        private static string? _cycleCorrelationKey;

        /// <summary>AUTO RUN 시작/중지 시 AutoProcessService 가 토글. 전환 시 버퍼는 비운다.</summary>
        public static void SetCycleAccumulation(bool enabled)
        {
            lock (_cycleLock)
            {
                _cycleAccumulating = enabled;
                _cycleParamResults.Clear();
                _cycleFailedTools.Clear();
                _cycleToolResults.Clear();
                _cycleTotalMs = 0;
                _cycleFeatureMetrics = null;
                _cycleCorrelationKey = null;
            }
        }

        /// <summary>
        /// 검사 상관 키 발급. 사이클 누적 모드에서는 **사이클 내 모든 검사가 같은 키를 공유**
        /// — Web 이력이 사이클당 1행이므로, 검사별 고유 키를 쓰면 마지막 검사 외의 이미지가
        /// 전부 매칭 실패(409)로 폐기되고 "사이클 NG + 마지막 검사 OK" 조합에서 NG 이미지가
        /// 구조적으로 소실됐다 (2026-08-19 현장). 키는 FlushCycleResultAsync 가 사이클
        /// 경계에서 초기화하므로 사이클마다 새로 발급된다.
        /// </summary>
        internal static string CreateCorrelationKey()
        {
            lock (_cycleLock)
            {
                if (!_cycleAccumulating)
                    return Guid.NewGuid().ToString("N");
                return _cycleCorrelationKey ??= Guid.NewGuid().ToString("N");
            }
        }

        /// <summary>
        /// 사이클 완료 시 호출 — 로컬 최근 검사 이력에 사이클 1건을 **항상** 남기고,
        /// 버퍼에 쌓인 파라미터 결과(없으면 판정만)를 Web 에 1건으로 업로드한다.
        /// Web 미연동(단독 모드 또는 CurrentRecipeId==0)이면 로컬 이력만 남기고 false 반환
        /// — WO 집계는 Web 연동 레시피 전제.
        /// 로컬 이력 push 가 Web 가드 안쪽에 있어 단독 모드에서 Recent Inspections 가
        /// 항상 비어 있던 문제 수정 (2026-09-04).
        /// </summary>
        public static async Task<bool> FlushCycleResultAsync(bool overallPass)
        {
            List<ParameterResultDto> results;
            List<string> failedTools;
            List<VMS.Services.LocalHistory.LocalToolResult> toolResults;
            double totalMs;
            InspectionFeatureMetrics? metrics;
            string? corrKey;
            lock (_cycleLock)
            {
                results = new List<ParameterResultDto>(_cycleParamResults);
                failedTools = new List<string>(_cycleFailedTools);
                toolResults = new List<VMS.Services.LocalHistory.LocalToolResult>(_cycleToolResults);
                totalMs = _cycleTotalMs;
                metrics = _cycleFeatureMetrics;
                corrKey = _cycleCorrelationKey;
                _cycleParamResults.Clear();
                _cycleFailedTools.Clear();
                _cycleToolResults.Clear();
                _cycleTotalMs = 0;
                _cycleFeatureMetrics = null;
                _cycleCorrelationKey = null;
            }

            var isPass = overallPass && results.All(r => r.Judgment == "OK");
            RecordLocalInspection(isPass, results, failedTools,
                correlationKey: corrKey,
                toolResults: toolResults,
                cycleTimeMs: totalMs > 0 ? (int)Math.Round(totalMs) : null,
                mode: VMS.Services.LocalHistory.LocalInspectionMode.Cycle);

            var syncService = ParameterSyncService;
            if (syncService == null || syncService.CurrentRecipeId <= 0)
            {
                Debug.WriteLine("[InspectionService] cycle upload skipped — Web 연동 레시피 아님 (로컬 이력만 기록)");
                return false;
            }

            try
            {
                return await syncService.UploadResultsAsync(
                    syncService.CurrentRecipeId, results, metrics, corrKey, overallPass);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[InspectionService] cycle result upload error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// D8: VMS 자체 히스토리(Recent Inspections) 1건 push — Web 연동 여부와 무관하게
        /// 항상 기록. NG 코드는 Web 파라미터 NG 코드가 있으면 그것을, 없으면(단독 모드·
        /// 파라미터 미연결 레시피) 실패한 도구 이름을 남긴다. 레시피 이름은 Web 레시피 →
        /// 로컬 레시피(<see cref="CurrentRecipeNameProvider"/>) 순으로 보강.
        /// </summary>
        internal static InspectionRecord RecordLocalInspection(
            bool isPass,
            IReadOnlyList<ParameterResultDto> paramResults,
            IReadOnlyList<string> failedTools,
            string? correlationKey = null,
            IReadOnlyList<VMS.Services.LocalHistory.LocalToolResult>? toolResults = null,
            int? cycleTimeMs = null,
            VMS.Services.LocalHistory.LocalInspectionMode mode = VMS.Services.LocalHistory.LocalInspectionMode.Manual)
        {
            var ngCodes = paramResults.Where(r => r.Judgment == "NG")
                                      .Select(r => r.ParamCode.ToString())
                                      .Distinct()
                                      .ToList();
            if (ngCodes.Count == 0 && !isPass)
                ngCodes = failedTools.Distinct().ToList();

            var syncService = ParameterSyncService;
            var webLinked = syncService != null && syncService.CurrentRecipeId > 0;
            string? recipeName = null;
            if (webLinked)
                recipeName = syncService!.Recipes
                    .FirstOrDefault(r => r.Id == syncService.CurrentRecipeId)?.Name;
            if (string.IsNullOrEmpty(recipeName))
            {
                try { recipeName = CurrentRecipeNameProvider?.Invoke(); }
                catch (Exception ex) { Debug.WriteLine($"[InspectionService] recipe name provider error: {ex.Message}"); }
            }

            var record = new InspectionRecord
            {
                IsPass = isPass,
                NgCodes = ngCodes,
                RecipeId = webLinked ? syncService!.CurrentRecipeId : 0,
                RecipeName = recipeName,
                WorkOrderId = webLinked ? syncService!.WorkOrderId : null,
                LotId = webLinked ? syncService!.LotId : null,
                SerialNumber = webLinked ? syncService!.SerialNumber : null
            };
            RecentInspectionsService.Instance.Add(record);

            // 영구 로컬 이력 — 큐 push 만 (택트 무관). 실패는 검사 흐름과 무관하게 삼킨다.
            var store = HistoryStore;
            if (store != null)
            {
                try
                {
                    store.Record(new VMS.Services.LocalHistory.LocalInspectionEntry
                    {
                        InspectedAtUtc = record.Timestamp.ToUniversalTime(),
                        IsPass = record.IsPass,
                        RecipeId = record.RecipeId,
                        RecipeName = record.RecipeName,
                        NgCodes = new List<string>(record.NgCodes),
                        ToolResults = toolResults != null
                            ? new List<VMS.Services.LocalHistory.LocalToolResult>(toolResults)
                            : new List<VMS.Services.LocalHistory.LocalToolResult>(),
                        CorrelationKey = correlationKey,
                        WorkOrderId = record.WorkOrderId,
                        LotId = record.LotId,
                        SerialNumber = record.SerialNumber,
                        CycleTimeMs = cycleTimeMs,
                        Mode = mode
                    });
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[InspectionService] local history record error: {ex.Message}");
                }
            }
            return record;
        }

        /// <summary>도구 결과 → 로컬 이력용 요약 (Data 사전은 제외, 실패 시에만 메시지 보존).</summary>
        private static List<VMS.Services.LocalHistory.LocalToolResult> ToLocalToolResults(
            IEnumerable<ToolInspectionResult> toolResults)
        {
            var list = new List<VMS.Services.LocalHistory.LocalToolResult>();
            foreach (var t in toolResults)
            {
                list.Add(new VMS.Services.LocalHistory.LocalToolResult
                {
                    ToolName = t.ToolName,
                    ToolType = t.ToolType,
                    Success = t.Success,
                    ExecutionTimeMs = Math.Round(t.ExecutionTimeMs, 1),
                    Message = t.Success || string.IsNullOrEmpty(t.Message) ? null : t.Message
                });
            }
            return list;
        }

        /// <summary>
        /// internal — VMS.Tests 통합 테스트에서 격리된 인스턴스를 만들 때만 호출.
        /// 운영 코드는 반드시 <see cref="Instance"/> 싱글톤 사용.
        /// </summary>
        internal InspectionService() { }

        /// <summary>
        /// Step ID별 캐싱된 도구/연결/정렬 데이터
        /// </summary>
        private class StepExecutionContext
        {
            public List<VisionToolBase> Tools { get; set; } = new();
            public Dictionary<string, VisionToolBase> ToolById { get; set; } = new();
            public List<ConnectionInfo> Connections { get; set; } = new();
            public List<VisionToolBase> SortedTools { get; set; } = new();
        }

        public void ClearCache()
        {
            lock (_contextLock)
            {
                _stepContexts.Clear();
            }
        }

        /// <summary>
        /// Step에 대한 도구 컨텍스트를 캐시에서 가져오거나 새로 생성.
        /// 캐싱을 통해 도구 인스턴스가 유지되므로 FixtureRef 기준점이
        /// 첫 실행에서 설정된 후 이후 실행에서 delta 오프셋이 올바르게 적용됨.
        /// </summary>
        private StepExecutionContext GetOrCreateContext(InspectionStep step)
        {
            lock (_contextLock)
            {
                if (_stepContexts.TryGetValue(step.Id, out var existing))
                    return existing;

                var vsConfigs = ConvertToolConfigs(step.Tools);

                var tools = new List<VisionToolBase>();
                var toolById = new Dictionary<string, VisionToolBase>();

                foreach (var config in vsConfigs)
                {
                    var tool = VMS.VisionSetup.Services.ToolSerializer.DeserializeTool(config);
                    if (tool != null)
                    {
                        tools.Add(tool);
                        toolById[tool.Id] = tool;
                    }
                }

                var connections = BuildConnections(step.Tools, toolById);
                var sorted = TopologicalSort(tools, connections);

                var ctx = new StepExecutionContext
                {
                    Tools = tools,
                    ToolById = toolById,
                    Connections = connections,
                    SortedTools = sorted
                };

                _stepContexts[step.Id] = ctx;
                return ctx;
            }
        }

        public async Task<StepInspectionResult> ExecuteStepAsync(InspectionStep step, Mat inputImage)
        {
            return await Task.Run(() => ExecuteStep(step, inputImage));
        }

        private StepInspectionResult ExecuteStep(InspectionStep step, Mat inputImage)
        {
            var sw = Stopwatch.StartNew();
            var result = new StepInspectionResult
            {
                // 결과 업로드와 이미지 업로드가 공유할 상관 키.
                CorrelationKey = CreateCorrelationKey()
            };

            try
            {
                if (step.Tools == null || step.Tools.Count == 0)
                {
                    result.Success = true;
                    result.Message = "No tools to execute";
                    sw.Stop();
                    result.ExecutionTimeMs = sw.Elapsed.TotalMilliseconds;
                    return result;
                }

                // 캐싱된 도구 컨텍스트 사용 (도구 인스턴스 재활용 → FixtureRef 유지)
                var ctx = GetOrCreateContext(step);

                if (ctx.Tools.Count == 0)
                {
                    result.Success = false;
                    result.Message = "No tools could be deserialized";
                    sw.Stop();
                    result.ExecutionTimeMs = sw.Elapsed.TotalMilliseconds;
                    return result;
                }

                // 실행
                var resultMap = new Dictionary<string, VisionResult>();
                bool allSuccess = true;
                Mat? compositeOverlay = null;

                foreach (var tool in ctx.SortedTools)
                {
                    if (!tool.IsEnabled)
                        continue;

                    var toolSw = Stopwatch.StartNew();

                    // Result 연결 확인 (ResultTool은 실패 정보를 수집해야 하므로 스킵 우회)
                    if (tool is not ResultTool && ShouldSkipByResultConnection(tool, ctx.Connections, resultMap))
                    {
                        var skipResult = new VisionResult
                        {
                            Success = false,
                            Message = $"연결된 도구의 결과가 실패하여 건너뜀: {tool.Name}"
                        };
                        resultMap[tool.Id] = skipResult;
                        allSuccess = false;

                        toolSw.Stop();
                        result.ToolResults.Add(new ToolInspectionResult
                        {
                            ToolName = tool.Name,
                            ToolType = tool.ToolType,
                            Success = false,
                            Message = skipResult.Message,
                            ExecutionTimeMs = toolSw.Elapsed.TotalMilliseconds,
                            PlcMappings = tool.PlcMappings.Select(m => new VMS.Models.PlcResultMapping
                            {
                                ResultKey = m.ResultKey,
                                DeviceId = string.IsNullOrWhiteSpace(m.DeviceId) ? "MainPLC" : m.DeviceId,
                                PlcAddress = m.PlcAddress,
                                DataType = m.DataType
                            }).ToList()
                        });
                        continue;
                    }

                    // Web 파라미터 적용 (LinkedParamCodes → 도구 프로퍼티)
                    ParameterApplyService?.ApplyParameters(tool);

                    // Coordinates 연결 적용 (Fixture offset)
                    ApplyCoordinatesConnection(tool, ctx.Connections, resultMap);

                    // Image 연결 해소
                    Mat toolInput;
                    var connectedImage = GetConnectedInputImage(tool, ctx.Connections, resultMap);
                    bool usesBaseImage = connectedImage == null;
                    if (connectedImage != null)
                        toolInput = connectedImage;
                    else
                        toolInput = inputImage.Clone();

                    // 오버레이 베이스 이미지 주입
                    tool.OverlayBaseImage = inputImage;

                    try
                    {
                        // ResultTool: Execute 전에 연결된 소스 결과 주입
                        if (tool is ResultTool rt)
                        {
                            rt.SourceResults.Clear();
                            foreach (var conn in ctx.Connections
                                .Where(c => c.TargetId == tool.Id && c.Type == VsConnectionType.Result))
                            {
                                if (resultMap.TryGetValue(conn.SourceId, out var srcResult))
                                {
                                    var srcTool = ctx.SortedTools.FirstOrDefault(t => t.Id == conn.SourceId);
                                    rt.SourceResults.Add(new SourceToolResult
                                    {
                                        ToolId = conn.SourceId,
                                        ToolName = srcTool?.Name ?? conn.SourceId,
                                        Success = srcResult.Success,
                                        Message = srcResult.Message
                                    });
                                }
                            }
                        }

                        // 소스 소비 도구 주입 — VisionSetup(VisionService.ExecuteAll)과 동일 규칙.
                        // 이 엔진에는 ResultTool 주입만 있었고 Geometry/MatchAlign 이 누락돼
                        // AUTO RUN 에서 소스가 비는 채로 실행됐다 (2026-08-27 발견).
                        if (tool is GeometryTool geometryTool)
                            ToolSourceInjector.InjectGeometry(
                                geometryTool, EnumerateResultSources(tool, ctx, resultMap));
                        if (tool is MatchAlignTool matchAlignTool)
                            ToolSourceInjector.InjectMatchAlign(
                                matchAlignTool, EnumerateResultSources(tool, ctx, resultMap));

                        var toolResult = tool.Execute(toolInput);
                        tool.LastResult = toolResult;
                        resultMap[tool.Id] = toolResult;

                        // 다중 스텝 얼라인용 포즈 기록 — MultiStepAlignTool 이
                        // (스텝 Id, 툴 Id) 키로 다른 스텝의 매칭 포즈를 참조한다
                        StepPoseStore.Record(step.Id, tool, toolResult);

                        if (!toolResult.Success)
                            allSuccess = false;

                        // 오버레이 합성
                        if (toolResult.OverlayImage != null && !toolResult.OverlayImage.Empty())
                        {
                            if (compositeOverlay == null)
                            {
                                compositeOverlay = toolResult.OverlayImage.Clone();
                                if (compositeOverlay.Channels() == 1)
                                    Cv2.CvtColor(compositeOverlay, compositeOverlay, ColorConversionCodes.GRAY2BGR);
                            }
                            else
                            {
                                MergeOverlayGraphics(toolResult.OverlayImage, inputImage, compositeOverlay);
                            }
                        }

                        toolSw.Stop();
                        result.ToolResults.Add(new ToolInspectionResult
                        {
                            ToolName = tool.Name,
                            ToolType = tool.ToolType,
                            Success = toolResult.Success,
                            Message = toolResult.Message,
                            ExecutionTimeMs = toolSw.Elapsed.TotalMilliseconds,
                            Data = toolResult.Data ?? new Dictionary<string, object>(),
                            PlcMappings = tool.PlcMappings.Select(m => new VMS.Models.PlcResultMapping
                            {
                                ResultKey = m.ResultKey,
                                DeviceId = string.IsNullOrWhiteSpace(m.DeviceId) ? "MainPLC" : m.DeviceId,
                                PlcAddress = m.PlcAddress,
                                DataType = m.DataType
                            }).ToList()
                        });
                    }
                    finally
                    {
                        tool.OverlayBaseImage = null;
                        if (usesBaseImage)
                            toolInput.Dispose();
                    }
                }

                // ResultTool이 존재하면 최종 판정은 ResultTool의 Success로 결정
                var resultToolInstance = ctx.SortedTools.OfType<ResultTool>().FirstOrDefault();
                bool finalSuccess = resultToolInstance != null
                    ? resultMap.TryGetValue(resultToolInstance.Id, out var rtResult) && rtResult.Success
                    : allSuccess;

                result.Success = finalSuccess;
                result.Message = finalSuccess ? "All tools passed" : "One or more tools failed";
                result.OverlayImage = compositeOverlay;

                // 실패한 도구 이름 — 감사 로그 + 로컬 이력(NG 코드 대체)에 사용.
                var failedTools = result.ToolResults
                    .Where(t => !t.Success)
                    .Select(t => t.ToolName)
                    .ToArray();

                // 감사 로그 — NG (검사 실패) 만 기록. OK 는 빈도가 높아 jsonl 폭증을 막기 위해 제외.
                // 실패한 도구 이름을 함께 기록하여 사후 추적 가능.
                if (!finalSuccess)
                {
                    AuditLogger.Instance.Log(
                        AuditCategory.Inspection, "InspectionNG", AuditOutcome.Failure,
                        source: nameof(InspectionService),
                        details: $"StepId={step.Id}, FailedTools=[{string.Join(", ", failedTools)}]");
                }

                // Predictive_DefectRate_Plan §5.1 — 예측 모델용 피처 산출.
                // CycleTime 은 ToolResults 합산 후/업로드 직전에 캡처해야 의미가 있음
                // (도구 실행 시간을 모두 포함해야 함).
                sw.Stop();
                result.ExecutionTimeMs = sw.Elapsed.TotalMilliseconds;

                var imgMetrics = VMS.Core.Services.ImageQualityMetrics.Compute(inputImage);
                var (dlConfidence, dlModelVersion) = ExtractDlSignal(ctx, resultMap);
                var featureMetrics = new InspectionFeatureMetrics
                {
                    CycleTimeMs = (int)Math.Round(result.ExecutionTimeMs),
                    Brightness = imgMetrics.Brightness,
                    ContrastStd = imgMetrics.ContrastStd,
                    FocusScore = imgMetrics.FocusScore,
                    BlobCount = imgMetrics.BlobCount,
                    MaxBlobAreaPx = imgMetrics.MaxBlobAreaPx,
                    DlConfidence = dlConfidence,
                    DlModelVersion = dlModelVersion
                };

                // 로컬 이력 기록 + Web 파라미터 결과 수집·업로드 (피처 + 이미지와 공유할 상관 키 동봉)
                var paramResults = CollectParameterResults(ctx, resultMap);
                RecordInspectionOutcome(finalSuccess, failedTools, paramResults, featureMetrics, result.CorrelationKey,
                    ToLocalToolResults(result.ToolResults));

                // 사이클 임시 Mat 즉시 해제 — 툴 결과의 OutputImage/OverlayImage 는 프레임
                // 크기 네이티브 메모리라 GC 통계에 잡히지 않고 파이널라이저까지 떠 있는다.
                // 방치하면 AUTO RUN 사이클마다 수십 MB 씩 쌓였다 지연 회수되는 톱니형
                // 증가가 되고(실증 PC 2026-08-29), LastResult 로 캐시 툴에 남는 직전
                // 사이클 1세트는 상시 상주했다. 표시용 오버레이는 위에서 합성본
                // (compositeOverlay 클론)으로 분리됐고, 이 엔진은 사이클 간에 결과
                // 이미지를 참조하지 않으므로(판정/Data 만 사용) 여기서 안전하다.
                // ReleaseMats 는 idempotent 이며 Data/Message 는 보존한다.
                foreach (var vr in resultMap.Values)
                    vr.ReleaseMats();

                return result;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"Inspection error: {ex.Message}";
                if (sw.IsRunning) sw.Stop();
                result.ExecutionTimeMs = sw.Elapsed.TotalMilliseconds;
                AuditLogger.Instance.Log(
                    AuditCategory.Inspection, "InspectionException", AuditOutcome.Failure,
                    source: nameof(InspectionService),
                    details: $"StepId={step.Id}, {ex.GetType().Name}: {ex.Message}");
                return result;
            }
        }

        /// <summary>
        /// Predictive_DefectRate_Plan §5.1 (V3) — DL 도구의 신뢰도와 모델 버전을 추출.
        /// 의미 통일: DlConfidence ∈ [0,1], **높을수록 OK 일 확신**.
        /// 도구별 매핑:
        ///   • ClassifyTool : Data["Confidence"] 직접
        ///   • DetectionTool: Data["Det{i}_Confidence"] 의 **최소값** (가장 위험한 검출)
        ///   • YoloSegTool  : Data["Inst{i}_Score"] 의 최소값
        ///   • AnomalyTool  : Data["AnomalyScore"] 는 **반대 의미**(높을수록 NG) →
        ///                    pseudo = clamp(1 - score / threshold, 0, 1) 로 변환
        ///   • 그 외        : 없음 (null)
        /// 복수 DL 도구가 있으면 전체에서 최소 confidence 를 픽 → 그 도구의 ModelPath 파일명을 함께 반환.
        /// </summary>
        private static (double? Confidence, string? ModelVersion) ExtractDlSignal(
            StepExecutionContext ctx, Dictionary<string, VisionResult> resultMap)
        {
            double? minConfidence = null;
            string? minConfidenceModel = null;

            foreach (var tool in ctx.SortedTools)
            {
                if (!resultMap.TryGetValue(tool.Id, out var vr) || vr.Data == null)
                    continue;

                var c = ExtractToolConfidence(tool.ToolType, vr.Data);
                if (!c.HasValue) continue;

                if (!minConfidence.HasValue || c.Value < minConfidence.Value)
                {
                    minConfidence = c.Value;
                    minConfidenceModel = TryGetModelVersion(tool);
                }
            }

            return (minConfidence, minConfidenceModel);
        }

        private static double? ExtractToolConfidence(string toolType, Dictionary<string, object> data)
        {
            switch (toolType)
            {
                case "ClassifyTool":
                    return TryToDouble(data, "Confidence");

                case "DetectionTool":
                {
                    double? min = null;
                    int i = 0;
                    while (data.ContainsKey($"Det{i}_Confidence"))
                    {
                        var v = TryToDouble(data, $"Det{i}_Confidence");
                        if (v.HasValue && (!min.HasValue || v.Value < min.Value))
                            min = v.Value;
                        i++;
                    }
                    return min;
                }

                case "YoloSegTool":
                {
                    double? min = null;
                    int i = 0;
                    while (data.ContainsKey($"Inst{i}_Score"))
                    {
                        var v = TryToDouble(data, $"Inst{i}_Score");
                        if (v.HasValue && (!min.HasValue || v.Value < min.Value))
                            min = v.Value;
                        i++;
                    }
                    return min;
                }

                case "AnomalyTool":
                case "EnsembleTool":
                {
                    // AnomalyScore 는 높을수록 NG. Threshold(있다면)를 기준으로 의사 신뢰도 산출.
                    var score = TryToDouble(data, "AnomalyScore");
                    if (!score.HasValue) return null;
                    var threshold = TryToDouble(data, "Threshold") ?? 1.0;
                    if (threshold <= 0) threshold = 1.0;
                    var pseudo = 1.0 - (score.Value / threshold);
                    return Math.Clamp(pseudo, 0.0, 1.0);
                }

                default:
                    return null; // 비 DL 도구
            }
        }

        private static double? TryToDouble(Dictionary<string, object> data, string key)
        {
            if (!data.TryGetValue(key, out var obj) || obj == null) return null;
            try { return Convert.ToDouble(obj); }
            catch { return null; }
        }

        /// <summary>
        /// 모든 DL 도구는 public string ModelPath 속성을 가지므로 reflection 으로 일관 추출.
        /// (전용 interface 신설은 V3 범위 밖 — 추후 IDlTool 도입 시 교체.)
        /// 반환 형식: "ToolType:파일명.onnx" — 학습-운영 간 모델 분포 추적 키로 충분.
        /// </summary>
        private static string? TryGetModelVersion(VisionToolBase tool)
        {
            try
            {
                var prop = tool.GetType().GetProperty("ModelPath");
                var path = prop?.GetValue(tool) as string;
                if (string.IsNullOrWhiteSpace(path)) return null;
                return $"{tool.ToolType}:{System.IO.Path.GetFileName(path)}";
            }
            catch
            {
                return null;
            }
        }

        #region Web Parameter Result Collection

        /// <summary>
        /// Web 파라미터에 연결된 도구 결과를 측정값 목록으로 수집. Web 미연동(단독 모드 또는
        /// CurrentRecipeId==0)이면 빈 목록 — 로컬 이력은 그래도 남아야 하므로 호출자가
        /// <see cref="RecordInspectionOutcome"/> 로 이어간다.
        /// </summary>
        private static List<ParameterResultDto> CollectParameterResults(
            StepExecutionContext ctx,
            Dictionary<string, VisionResult> resultMap)
        {
            var paramResults = new List<ParameterResultDto>();

            var syncService = ParameterSyncService;
            if (syncService == null || syncService.CurrentRecipeId <= 0)
                return paramResults;

            foreach (var tool in ctx.SortedTools)
            {
                if (tool.LinkedParamCodes == null || tool.LinkedParamCodes.Count == 0)
                    continue;

                if (!resultMap.TryGetValue(tool.Id, out var toolResult) || toolResult.Data == null)
                    continue;

                foreach (var (propertyName, paramCode) in tool.LinkedParamCodes)
                {
                    // 결과 데이터에서 해당 프로퍼티 이름의 측정값 탐색
                    if (toolResult.Data.TryGetValue(propertyName, out var measuredObj))
                    {
                        try
                        {
                            var measuredValue = Convert.ToDouble(measuredObj);
                            paramResults.Add(new ParameterResultDto
                            {
                                ParamCode = paramCode,
                                MeasuredValue = measuredValue,
                                Judgment = toolResult.Success ? "OK" : "NG",
                                Timestamp = DateTime.UtcNow
                            });
                        }
                        catch
                        {
                            // 숫자 변환 실패 시 스킵
                        }
                    }
                }
            }

            return paramResults;
        }

        /// <summary>
        /// 검사 1건의 판정을 처리 — 사이클 누적 모드면 버퍼에 모으고(사이클 완료 시
        /// <see cref="FlushCycleResultAsync"/> 가 1건으로 처리, "1사이클 = 1개"), 아니면
        /// 로컬 이력에 즉시 push 하고 Web 연동 레시피면 파라미터 결과를 업로드한다.
        /// 로컬 이력은 단독 모드에서도 남는다 (D8: Web 끊겨도 사이드 패널에서 즉시 확인).
        /// </summary>
        internal static void RecordInspectionOutcome(
            bool finalSuccess,
            IReadOnlyList<string> failedTools,
            List<ParameterResultDto> paramResults,
            InspectionFeatureMetrics? featureMetrics = null,
            string? correlationKey = null,
            IReadOnlyList<VMS.Services.LocalHistory.LocalToolResult>? toolResults = null)
        {
            lock (_cycleLock)
            {
                if (_cycleAccumulating)
                {
                    _cycleParamResults.AddRange(paramResults);
                    _cycleFailedTools.AddRange(failedTools);
                    if (toolResults != null) _cycleToolResults.AddRange(toolResults);
                    if (featureMetrics?.CycleTimeMs is int stepMs) _cycleTotalMs += stepMs;
                    if (featureMetrics != null) _cycleFeatureMetrics = featureMetrics;
                    if (correlationKey != null) _cycleCorrelationKey = correlationKey;
                    return;
                }
            }

            // 업로드 전에 푸시 — 네트워크 상태와 무관하게 항상 기록.
            var isPass = finalSuccess && paramResults.All(r => r.Judgment == "OK");
            RecordLocalInspection(isPass, paramResults, failedTools,
                correlationKey: correlationKey,
                toolResults: toolResults,
                cycleTimeMs: featureMetrics?.CycleTimeMs,
                mode: VMS.Services.LocalHistory.LocalInspectionMode.Manual);

            var syncService = ParameterSyncService;
            if (syncService == null || syncService.CurrentRecipeId <= 0 || paramResults.Count == 0)
                return;

            _ = Task.Run(async () =>
            {
                try
                {
                    await syncService.UploadResultsAsync(syncService.CurrentRecipeId, paramResults, featureMetrics, correlationKey);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[InspectionService] Parameter result upload error: {ex.Message}");
                }
            });
        }

        #endregion

        #region Tool Config Conversion

        private static List<VsToolConfig> ConvertToolConfigs(List<ToolConfig> vmsConfigs)
        {
            var result = new List<VsToolConfig>();

            foreach (var src in vmsConfigs)
            {
                var dst = new VsToolConfig
                {
                    Id = src.Id,
                    ToolType = src.ToolType,
                    Name = src.Name,
                    Sequence = src.Sequence,
                    IsEnabled = src.IsEnabled,
                    UseROI = src.UseROI,
                    ROIX = src.ROIX,
                    ROIY = src.ROIY,
                    ROIWidth = src.ROIWidth,
                    ROIHeight = src.ROIHeight,
                    Parameters = src.Parameters ?? new Dictionary<string, object>(),
                    PlcMappings = src.PlcMappings?.Select(m => new VMS.VisionSetup.Models.PlcResultMapping
                    {
                        ResultKey = m.ResultKey,
                        PlcAddress = m.PlcAddress,
                        DataType = m.DataType
                    }).ToList() ?? new List<VMS.VisionSetup.Models.PlcResultMapping>(),
                    // 레거시 호환 (ToolSerializer에서 마이그레이션 처리)
                    ResultPlcAddress = src.ResultPlcAddress,
                    ResultDataType = src.ResultDataType,
                    ResultDataKey = src.ResultDataKey,
                    Connections = src.Connections?.Select(c => new VMS.VisionSetup.Models.ToolConnectionConfig
                    {
                        SourceToolId = c.SourceToolId,
                        ConnectionType = c.ConnectionType
                    }).ToList() ?? new List<VMS.VisionSetup.Models.ToolConnectionConfig>(),
                    LinkedParamCodes = src.LinkedParamCodes != null && src.LinkedParamCodes.Count > 0
                        ? new Dictionary<string, int>(src.LinkedParamCodes)
                        : new Dictionary<string, int>()
                };
                result.Add(dst);
            }

            return result;
        }

        #endregion

        #region Connection Management

        private class ConnectionInfo
        {
            public string SourceId { get; set; } = string.Empty;
            public string TargetId { get; set; } = string.Empty;
            public VsConnectionType Type { get; set; }
        }

        /// <summary>대상 도구를 향한 Result 연결 소스를 연결 순서대로 열거 (주입 공용 로직용).</summary>
        private static IEnumerable<ToolSourceInjector.ResultSource> EnumerateResultSources(
            VisionToolBase tool, StepExecutionContext ctx, Dictionary<string, VisionResult> resultMap)
        {
            foreach (var conn in ctx.Connections
                .Where(c => c.TargetId == tool.Id && c.Type == VsConnectionType.Result))
            {
                if (resultMap.TryGetValue(conn.SourceId, out var srcResult))
                {
                    ctx.ToolById.TryGetValue(conn.SourceId, out var srcTool);
                    yield return new ToolSourceInjector.ResultSource(
                        conn.SourceId, srcResult,
                        srcTool?.Name ?? conn.SourceId, srcTool?.ToolType ?? string.Empty);
                }
            }
        }

        private static List<ConnectionInfo> BuildConnections(
            List<ToolConfig> vmsConfigs, Dictionary<string, VisionToolBase> toolById)
        {
            var connections = new List<ConnectionInfo>();

            foreach (var config in vmsConfigs)
            {
                if (config.Connections == null) continue;

                foreach (var conn in config.Connections)
                {
                    if (string.IsNullOrEmpty(conn.SourceToolId)) continue;
                    if (!toolById.ContainsKey(conn.SourceToolId)) continue;
                    if (!toolById.ContainsKey(config.Id)) continue;

                    var connType = conn.ConnectionType switch
                    {
                        "Image" => VsConnectionType.Image,
                        "Coordinates" => VsConnectionType.Coordinates,
                        "Result" => VsConnectionType.Result,
                        _ => VsConnectionType.Image
                    };

                    connections.Add(new ConnectionInfo
                    {
                        SourceId = conn.SourceToolId,
                        TargetId = config.Id,
                        Type = connType
                    });
                }
            }

            return connections;
        }

        private static Mat? GetConnectedInputImage(
            VisionToolBase tool, List<ConnectionInfo> connections, Dictionary<string, VisionResult> resultMap)
        {
            var imageConnection = connections
                .FirstOrDefault(c => c.TargetId == tool.Id && c.Type == VsConnectionType.Image);

            if (imageConnection != null && resultMap.TryGetValue(imageConnection.SourceId, out var sourceResult))
            {
                if (sourceResult.OutputImage != null && !sourceResult.OutputImage.Empty())
                    return sourceResult.OutputImage;
            }

            return null;
        }

        private static bool ShouldSkipByResultConnection(
            VisionToolBase tool, List<ConnectionInfo> connections, Dictionary<string, VisionResult> resultMap)
        {
            var resultConnections = connections
                .Where(c => c.TargetId == tool.Id && c.Type == VsConnectionType.Result)
                .ToList();

            foreach (var conn in resultConnections)
            {
                if (resultMap.TryGetValue(conn.SourceId, out var sourceResult))
                {
                    if (!sourceResult.Success)
                        return true;
                }
            }

            return false;
        }

        private static void ApplyCoordinatesConnection(
            VisionToolBase tool, List<ConnectionInfo> connections, Dictionary<string, VisionResult> resultMap)
        {
            var coordConnections = connections
                .Where(c => c.TargetId == tool.Id && c.Type == VsConnectionType.Coordinates)
                .ToList();

            tool.IsFixtureTransformActive = true;
            try
            {
                foreach (var conn in coordConnections)
                {
                    if (!resultMap.TryGetValue(conn.SourceId, out var sourceResult) || sourceResult.Data == null)
                        continue;

                    if (sourceResult.Data.TryGetValue("CenterX", out var cx) &&
                        sourceResult.Data.TryGetValue("CenterY", out var cy))
                    {
                        if (!tool.HasFixtureBaseROI)
                        {
                            double refCX = Convert.ToDouble(cx);
                            double refCY = Convert.ToDouble(cy);

                            if (tool.UseROI && tool.ROI.Width > 0 && tool.ROI.Height > 0)
                            {
                                tool.FixtureBaseROI = tool.ROI;
                            }
                            else
                            {
                                int defaultW = tool.ROI.Width > 0 ? tool.ROI.Width : 200;
                                int defaultH = tool.ROI.Height > 0 ? tool.ROI.Height : 200;
                                tool.FixtureBaseROI = new Rect(
                                    (int)(refCX - defaultW / 2.0),
                                    (int)(refCY - defaultH / 2.0),
                                    defaultW, defaultH);
                            }

                            tool.HasFixtureBaseROI = true;
                            tool.FixtureRefX = refCX;
                            tool.FixtureRefY = refCY;
                            tool.FixtureRefAngle = sourceResult.Data.TryGetValue("Angle", out var initAngle)
                                ? Convert.ToDouble(initAngle) : 0;
                        }

                        double foundX = Convert.ToDouble(cx);
                        double foundY = Convert.ToDouble(cy);
                        double refX = tool.FixtureRefX;
                        double refY = tool.FixtureRefY;

                        double baseCX = tool.FixtureBaseROI.X + tool.FixtureBaseROI.Width / 2.0;
                        double baseCY = tool.FixtureBaseROI.Y + tool.FixtureBaseROI.Height / 2.0;

                        double currentAngle = 0;
                        if (sourceResult.Data.TryGetValue("Angle", out var angleObj))
                            currentAngle = Convert.ToDouble(angleObj);
                        double deltaAngle = currentAngle - tool.FixtureRefAngle;

                        double newCX, newCY;
                        if (Math.Abs(deltaAngle) > 0.01)
                        {
                            double relX = baseCX - refX;
                            double relY = baseCY - refY;
                            double rad = deltaAngle * Math.PI / 180.0;
                            newCX = foundX + relX * Math.Cos(rad) - relY * Math.Sin(rad);
                            newCY = foundY + relX * Math.Sin(rad) + relY * Math.Cos(rad);
                        }
                        else
                        {
                            newCX = baseCX + (foundX - refX);
                            newCY = baseCY + (foundY - refY);
                        }

                        int w = tool.FixtureBaseROI.Width > 0 ? tool.FixtureBaseROI.Width : 100;
                        int h = tool.FixtureBaseROI.Height > 0 ? tool.FixtureBaseROI.Height : 100;
                        tool.ROI = new Rect((int)(newCX - w / 2.0), (int)(newCY - h / 2.0), w, h);
                        tool.UseROI = true;
                    }
                    else if (sourceResult.Data.TryGetValue("BoundingRect", out var rectObj) && rectObj is Rect boundingRect)
                    {
                        tool.ROI = boundingRect;
                        tool.UseROI = true;
                    }
                }
            }
            finally
            {
                tool.IsFixtureTransformActive = false;
            }
        }

        #endregion

        #region Topological Sort

        private static List<VisionToolBase> TopologicalSort(
            List<VisionToolBase> tools, List<ConnectionInfo> connections)
        {
            var toolById = tools.ToDictionary(t => t.Id);
            var dependents = new Dictionary<string, List<string>>();
            var inDegree = new Dictionary<string, int>();

            foreach (var t in tools)
            {
                dependents[t.Id] = new List<string>();
                inDegree[t.Id] = 0;
            }

            foreach (var conn in connections)
            {
                if (toolById.ContainsKey(conn.SourceId) && toolById.ContainsKey(conn.TargetId))
                {
                    dependents[conn.SourceId].Add(conn.TargetId);
                    inDegree[conn.TargetId]++;
                }
            }

            var queue = new Queue<string>();
            foreach (var t in tools)
                if (inDegree[t.Id] == 0)
                    queue.Enqueue(t.Id);

            var sorted = new List<VisionToolBase>();
            while (queue.Count > 0)
            {
                string id = queue.Dequeue();
                sorted.Add(toolById[id]);
                foreach (var depId in dependents[id])
                {
                    inDegree[depId]--;
                    if (inDegree[depId] == 0)
                        queue.Enqueue(depId);
                }
            }

            if (sorted.Count < tools.Count)
            {
                var sortedIds = new HashSet<string>(sorted.Select(t => t.Id));
                foreach (var t in tools)
                    if (!sortedIds.Contains(t.Id))
                        sorted.Add(t);
            }

            return sorted;
        }

        #endregion

        #region Overlay Merging

        private static void MergeOverlayGraphics(Mat overlay, Mat baseInput, Mat composite)
        {
            Mat overlayBGR = overlay;
            Mat inputBGR = baseInput;
            bool disposeOverlay = false, disposeInput = false;

            if (overlay.Channels() == 1)
            {
                overlayBGR = new Mat();
                Cv2.CvtColor(overlay, overlayBGR, ColorConversionCodes.GRAY2BGR);
                disposeOverlay = true;
            }
            if (baseInput.Channels() == 1)
            {
                inputBGR = new Mat();
                Cv2.CvtColor(baseInput, inputBGR, ColorConversionCodes.GRAY2BGR);
                disposeInput = true;
            }

            try
            {
                if (overlayBGR.Size() != composite.Size()) return;

                using var diff = new Mat();
                Cv2.Absdiff(overlayBGR, inputBGR, diff);
                using var grayDiff = new Mat();
                Cv2.CvtColor(diff, grayDiff, ColorConversionCodes.BGR2GRAY);
                using var mask = new Mat();
                Cv2.Threshold(grayDiff, mask, 1, 255, ThresholdTypes.Binary);
                overlayBGR.CopyTo(composite, mask);
            }
            finally
            {
                if (disposeOverlay) overlayBGR.Dispose();
                if (disposeInput) inputBGR.Dispose();
            }
        }

        #endregion
    }
}
