using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using VMS.Core.Interfaces;
using VMS.Core.Models;
using VMS.Core.Models.ParameterSync;
using VMS.Core.Services;
using VMS.Interfaces;
using VMS.Models;
using VMS.VisionSetup.Interfaces;
using VsToolConfig = VMS.VisionSetup.Models.ToolConfig;
using VsConnectionType = VMS.VisionSetup.Models.ConnectionType;
using VisionToolBase = VMS.VisionSetup.Models.VisionToolBase;
using VisionResult = VMS.VisionSetup.Models.VisionResult;
using VMS.VisionSetup.VisionTools.Result;

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

        private InspectionService() { }

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
            var result = new StepInspectionResult();

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

                        var toolResult = tool.Execute(toolInput);
                        tool.LastResult = toolResult;
                        resultMap[tool.Id] = toolResult;

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

                // Web 파라미터 결과 수집 및 업로드 (피처 동봉)
                CollectAndUploadParameterResults(ctx, resultMap, featureMetrics);
                return result;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"Inspection error: {ex.Message}";
                if (sw.IsRunning) sw.Stop();
                result.ExecutionTimeMs = sw.Elapsed.TotalMilliseconds;
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

        private static void CollectAndUploadParameterResults(
            StepExecutionContext ctx,
            Dictionary<string, VisionResult> resultMap,
            InspectionFeatureMetrics? featureMetrics = null)
        {
            var syncService = ParameterSyncService;
            if (syncService == null || syncService.CurrentRecipeId <= 0)
                return;

            var paramResults = new List<ParameterResultDto>();

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

            if (paramResults.Count > 0)
            {
                // D8: VMS 자체 히스토리 — Web 끊겨도 작업자가 사이드 패널에서 즉시 확인.
                // 업로드 전에 푸시 — 네트워크 상태와 무관하게 항상 기록.
                var isPass = paramResults.All(r => r.Judgment == "OK");
                var ngCodes = paramResults.Where(r => r.Judgment == "NG")
                                          .Select(r => r.ParamCode.ToString())
                                          .Distinct()
                                          .ToList();
                var recipeName = syncService.Recipes
                    .FirstOrDefault(r => r.Id == syncService.CurrentRecipeId)?.Name;
                RecentInspectionsService.Instance.Add(new InspectionRecord
                {
                    IsPass = isPass,
                    NgCodes = ngCodes,
                    RecipeId = syncService.CurrentRecipeId,
                    RecipeName = recipeName,
                    WorkOrderId = syncService.WorkOrderId,
                    LotId = syncService.LotId,
                    SerialNumber = syncService.SerialNumber
                });

                _ = Task.Run(async () =>
                {
                    try
                    {
                        await syncService.UploadResultsAsync(syncService.CurrentRecipeId, paramResults, featureMetrics);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[InspectionService] Parameter result upload error: {ex.Message}");
                    }
                });
            }
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
