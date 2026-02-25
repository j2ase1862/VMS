using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using VMS.Interfaces;
using VMS.Models;
using VsToolConfig = VMS.VisionSetup.Models.ToolConfig;
using VsConnectionType = VMS.VisionSetup.Models.ConnectionType;
using VisionToolBase = VMS.VisionSetup.Models.VisionToolBase;
using VisionResult = VMS.VisionSetup.Models.VisionResult;

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

                    // Result 연결 확인
                    if (ShouldSkipByResultConnection(tool, ctx.Connections, resultMap))
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
                                PlcAddress = m.PlcAddress,
                                DataType = m.DataType
                            }).ToList()
                        });
                        continue;
                    }

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

                result.Success = allSuccess;
                result.Message = allSuccess ? "All tools passed" : "One or more tools failed";
                result.OverlayImage = compositeOverlay;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"Inspection error: {ex.Message}";
            }

            sw.Stop();
            result.ExecutionTimeMs = sw.Elapsed.TotalMilliseconds;
            return result;
        }

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
                    }).ToList() ?? new List<VMS.VisionSetup.Models.ToolConnectionConfig>()
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
