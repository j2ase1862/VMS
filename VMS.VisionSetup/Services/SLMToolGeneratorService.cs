using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.Json;
using VMS.VisionSetup.Models;
using VMS.VisionSetup.ViewModels;

namespace VMS.VisionSetup.Services
{
    public class SLMToolGeneratorService
    {
        private static readonly HashSet<string> ImageToolTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            "GrayscaleTool", "BlurTool", "ThresholdTool", "EdgeDetectionTool",
            "MorphologyTool", "HistogramTool", "HeightSlicerTool"
        };

        private static readonly HashSet<string> CoordinatesToolTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            "FeatureMatchTool", "CaliperTool", "BlobTool", "LineFitTool", "CircleFitTool"
        };

        // Result: everything else (CodeReaderTool, OCRTool, DetectionTool, etc.)

        private static readonly Dictionary<string, string> ToolDisplayNames = new(StringComparer.OrdinalIgnoreCase)
        {
            { "GrayscaleTool", "그레이스케일 변환" },
            { "BlurTool", "블러 (노이즈 제거)" },
            { "ThresholdTool", "이진화" },
            { "EdgeDetectionTool", "에지 검출" },
            { "MorphologyTool", "모폴로지 연산" },
            { "HistogramTool", "히스토그램 분석" },
            { "HeightSlicerTool", "높이맵 슬라이싱" },
            { "FeatureMatchTool", "패턴 매칭 (위치 보정)" },
            { "BlobTool", "객체 검출 (Blob)" },
            { "CaliperTool", "에지 측정 (Caliper)" },
            { "LineFitTool", "직선 피팅" },
            { "CircleFitTool", "원 피팅" },
            { "GeometryTool", "기하학 계산" },
            { "Geometry3DTool", "3D 기하학 계산" },
            { "PlaneFitTool", "평면 피팅" },
            { "CodeReaderTool", "바코드/QR 읽기" },
            { "OCRTool", "문자 인식 (OCR)" },
            { "DetectionTool", "딥러닝 검출 (YOLO)" },
            { "ClassifyTool", "딥러닝 분류" },
            { "AnomalyTool", "이상 탐지" },
            { "ResultTool", "최종 판정 (Pass/Fail)" },
        };

        private static readonly Dictionary<string, string> ConnectionTypeDisplayNames = new(StringComparer.OrdinalIgnoreCase)
        {
            { "Image", "이미지" },
            { "Coordinates", "좌표" },
            { "Result", "결과" },
        };

        /// <summary>
        /// SLMActionModel을 사람이 읽을 수 있는 미리보기 텍스트로 변환
        /// </summary>
        public string BuildPreviewText(SLMActionModel action)
        {
            var sb = new StringBuilder();

            if (action.Action.Equals("Modify", StringComparison.OrdinalIgnoreCase))
            {
                sb.AppendLine("[수정 미리보기]");
                sb.AppendLine();

                if (action.Changes == null || action.Changes.Count == 0)
                {
                    sb.AppendLine("(변경 사항 없음)");
                    return sb.ToString();
                }

                for (int i = 0; i < action.Changes.Count; i++)
                {
                    var c = action.Changes[i];
                    string icon = c.Operation switch
                    {
                        "AddTool" => "+",
                        "RemoveTool" => "-",
                        "AddConnection" => ">>",
                        "RemoveConnection" => "xx",
                        _ => "?"
                    };

                    sb.Append($"  {icon} ");

                    switch (c.Operation)
                    {
                        case "AddTool":
                            string addName = GetToolDisplayName(c.ToolType ?? "");
                            sb.AppendLine($"도구 추가: {c.ToolName} ({addName})");
                            if (!string.IsNullOrEmpty(c.InputSource) &&
                                !c.InputSource!.Equals("Camera", StringComparison.OrdinalIgnoreCase))
                                sb.AppendLine($"     입력: {c.InputSource}");
                            break;

                        case "RemoveTool":
                            sb.AppendLine($"도구 제거: {c.ToolName}");
                            break;

                        case "AddConnection":
                            string connType = GetConnectionTypeDisplayName(c.ConnectionType);
                            sb.AppendLine($"연결 추가: {c.SourceTool} -> {c.TargetTool} ({connType})");
                            break;

                        case "RemoveConnection":
                            sb.AppendLine($"연결 제거: {c.SourceTool} -> {c.TargetTool}");
                            break;

                        default:
                            sb.AppendLine($"{c.Operation}");
                            break;
                    }
                }
            }
            else
            {
                // Create action
                sb.AppendLine($"[레시피 미리보기] {action.RecipeName ?? ""}");
                sb.AppendLine();

                if (action.Sequence == null || action.Sequence.Count == 0)
                {
                    sb.AppendLine("(도구 없음)");
                    return sb.ToString();
                }

                // Build connection map: ToolName -> InputSource
                var inputMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var item in action.Sequence)
                {
                    if (!string.IsNullOrEmpty(item.InputSource))
                        inputMap[item.ToolName] = item.InputSource;
                }

                for (int i = 0; i < action.Sequence.Count; i++)
                {
                    var item = action.Sequence[i];
                    string displayName = GetToolDisplayName(item.ToolType);
                    string num = $"{i + 1}";

                    sb.AppendLine($"  {num}. {item.ToolName}");
                    sb.AppendLine($"     [{displayName}]");

                    if (!string.IsNullOrEmpty(item.Description))
                        sb.AppendLine($"     {item.Description}");

                    // Show connection arrow
                    if (!string.IsNullOrEmpty(item.InputSource) &&
                        !item.InputSource.Equals("Camera", StringComparison.OrdinalIgnoreCase))
                    {
                        // Find source tool type for connection type
                        var sourceItem = action.Sequence.FirstOrDefault(s =>
                            s.ToolName.Equals(item.InputSource, StringComparison.OrdinalIgnoreCase));
                        string connTypeName = "이미지";
                        if (sourceItem != null)
                        {
                            var connType = DetermineConnectionType(sourceItem.ToolType, item.ToolType);
                            connTypeName = GetConnectionTypeDisplayName(connType.ToString());
                        }
                        sb.AppendLine($"     << {item.InputSource} ({connTypeName} 연결)");
                    }
                    else
                    {
                        sb.AppendLine($"     << 카메라 (직접 입력)");
                    }

                    if (i < action.Sequence.Count - 1)
                        sb.AppendLine();
                }

                // Show flow diagram
                sb.AppendLine();
                sb.AppendLine("[파이프라인 흐름]");
                sb.Append("  ");

                // Build a simple flow: group by InputSource chains
                var roots = action.Sequence
                    .Where(s => string.IsNullOrEmpty(s.InputSource) ||
                                s.InputSource.Equals("Camera", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var root in roots)
                {
                    if (visited.Contains(root.ToolName)) continue;
                    AppendFlowChain(sb, root.ToolName, action.Sequence, visited, "  ");
                }
            }

            sb.AppendLine();
            sb.AppendLine("'Apply Recipe' 버튼을 누르면 위 구성이 워크스페이스에 적용됩니다.");
            return sb.ToString();
        }

        private void AppendFlowChain(StringBuilder sb, string toolName, List<SLMToolItem> sequence,
            HashSet<string> visited, string indent)
        {
            if (visited.Contains(toolName)) return;
            visited.Add(toolName);

            var item = sequence.FirstOrDefault(s => s.ToolName.Equals(toolName, StringComparison.OrdinalIgnoreCase));
            if (item == null) return;

            string displayName = GetToolDisplayName(item.ToolType);
            sb.AppendLine($"{indent}[{item.ToolName}] ({displayName})");

            // Find children
            var children = sequence
                .Where(s => s.InputSource != null &&
                            s.InputSource.Equals(toolName, StringComparison.OrdinalIgnoreCase))
                .ToList();

            for (int i = 0; i < children.Count; i++)
            {
                string connector = children.Count > 1 ? (i < children.Count - 1 ? "├─> " : "└─> ") : "└─> ";
                string childIndent = children.Count > 1 && i < children.Count - 1
                    ? indent + "│   "
                    : indent + "    ";

                sb.Append($"{indent}{connector}");
                // Inline child name
                string childDisplay = GetToolDisplayName(children[i].ToolType);
                sb.AppendLine($"[{children[i].ToolName}] ({childDisplay})");

                // Recurse for grandchildren
                visited.Add(children[i].ToolName);
                var grandChildren = sequence
                    .Where(s => s.InputSource != null &&
                                s.InputSource.Equals(children[i].ToolName, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                foreach (var gc in grandChildren)
                {
                    AppendFlowChain(sb, gc.ToolName, sequence, visited, childIndent);
                }
            }
        }

        private static string GetToolDisplayName(string toolType)
        {
            return ToolDisplayNames.TryGetValue(toolType, out var name) ? name : toolType;
        }

        private static string GetConnectionTypeDisplayName(string? connType)
        {
            if (connType != null && ConnectionTypeDisplayNames.TryGetValue(connType, out var name))
                return name;
            return connType ?? "결과";
        }

        /// <summary>
        /// SLM JSON 출력을 SLMActionModel로 파싱
        /// </summary>
        public SLMActionModel? ParseActionJson(string slmOutput)
        {
            slmOutput = SLMChatService.StripMarkdownCodeBlock(slmOutput);

            int firstBrace = slmOutput.IndexOf('{');
            int lastBrace = slmOutput.LastIndexOf('}');

            if (firstBrace < 0 || lastBrace < 0 || lastBrace <= firstBrace)
            {
                Debug.WriteLine("[SLMToolGenerator] No valid JSON found in SLM output.");
                return null;
            }

            string jsonStr = slmOutput.Substring(firstBrace, lastBrace - firstBrace + 1);

            try
            {
                var model = JsonSerializer.Deserialize<SLMActionModel>(jsonStr, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (model != null && string.IsNullOrEmpty(model.Action))
                    model.Action = "Create";

                return model;
            }
            catch (JsonException ex)
            {
                Debug.WriteLine($"[SLMToolGenerator] Failed to parse action JSON: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Create 액션 - 도구 시퀀스를 워크스페이스에 적용
        /// </summary>
        public (int created, List<string> errors) ApplyRecipe(MainViewModel mainViewModel, SLMActionModel action)
        {
            var errors = new List<string>();

            if (action.Sequence == null || action.Sequence.Count == 0)
            {
                errors.Add("Recipe contains no tools.");
                return (0, errors);
            }

            var createdTools = new Dictionary<string, ToolItem>(StringComparer.OrdinalIgnoreCase);
            double startX = 400;
            double startY = 100;
            double yOffset = 0;
            const double verticalSpacing = 120;

            // Phase 1: Create all tools
            foreach (var item in action.Sequence)
            {
                var visionTool = VisionService.CreateTool(item.ToolType);
                if (visionTool == null)
                {
                    errors.Add($"Unknown tool type: {item.ToolType} ('{item.ToolName}')");
                    continue;
                }

                var toolItem = mainViewModel.CreateDroppedTool(
                    new ToolItem { Name = item.ToolName, ToolType = item.ToolType },
                    startX, startY + yOffset);

                if (toolItem != null)
                {
                    // Rename to SLM-specified name
                    toolItem.Name = item.ToolName;
                    if (toolItem.VisionTool != null)
                        toolItem.VisionTool.Name = item.ToolName;

                    createdTools[item.ToolName] = toolItem;
                    Debug.WriteLine($"[SLMToolGenerator] Created: {item.ToolType} as '{item.ToolName}'");
                }
                else
                {
                    errors.Add($"Failed to create tool: {item.ToolType} ('{item.ToolName}')");
                }

                yOffset += verticalSpacing;
            }

            // Phase 2: Create connections based on InputSource
            foreach (var item in action.Sequence)
            {
                if (string.IsNullOrWhiteSpace(item.InputSource) ||
                    item.InputSource.Equals("Camera", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (createdTools.TryGetValue(item.InputSource, out var source) &&
                    createdTools.TryGetValue(item.ToolName, out var target))
                {
                    var sourceItem = action.Sequence.FirstOrDefault(s =>
                        s.ToolName.Equals(item.InputSource, StringComparison.OrdinalIgnoreCase));
                    string sourceType = sourceItem?.ToolType ?? item.ToolType;
                    var connectionType = DetermineConnectionType(sourceType, item.ToolType);

                    mainViewModel.AddConnection(source, target, connectionType);
                    Debug.WriteLine($"[SLMToolGenerator] Connected: {item.InputSource} -> {item.ToolName} ({connectionType})");
                }
                else
                {
                    errors.Add($"Could not find tools for connection: {item.InputSource} -> {item.ToolName}");
                }
            }

            return (createdTools.Count, errors);
        }

        /// <summary>
        /// Modify 액션 - 기존 워크스페이스 수정
        /// </summary>
        public (int applied, List<string> errors) ApplyModifications(MainViewModel mainViewModel, SLMActionModel action)
        {
            var errors = new List<string>();
            int appliedCount = 0;

            if (action.Changes == null || action.Changes.Count == 0)
            {
                errors.Add("No modification changes found.");
                return (0, errors);
            }

            foreach (var change in action.Changes)
            {
                try
                {
                    switch (change.Operation)
                    {
                        case "RemoveConnection":
                            {
                                var source = FindToolByName(mainViewModel, change.SourceTool);
                                var target = FindToolByName(mainViewModel, change.TargetTool);
                                if (source == null || target == null)
                                {
                                    errors.Add($"RemoveConnection: tool not found (source='{change.SourceTool}', target='{change.TargetTool}')");
                                    break;
                                }

                                var conn = mainViewModel.Connections.FirstOrDefault(c =>
                                    c.SourceToolItem?.Id == source.Id && c.TargetToolItem?.Id == target.Id);
                                if (conn != null)
                                {
                                    mainViewModel.Connections.Remove(conn);
                                    if (source.VisionTool != null && target.VisionTool != null)
                                        mainViewModel.RemoveConnectionsForTool(source);
                                    appliedCount++;
                                }
                                else
                                {
                                    errors.Add($"RemoveConnection: connection not found ({change.SourceTool} -> {change.TargetTool})");
                                }
                            }
                            break;

                        case "AddConnection":
                            {
                                var source = FindToolByName(mainViewModel, change.SourceTool);
                                var target = FindToolByName(mainViewModel, change.TargetTool);
                                if (source == null || target == null)
                                {
                                    errors.Add($"AddConnection: tool not found (source='{change.SourceTool}', target='{change.TargetTool}')");
                                    break;
                                }
                                var connType = ParseConnectionType(change.ConnectionType);
                                mainViewModel.AddConnection(source, target, connType);
                                appliedCount++;
                            }
                            break;

                        case "AddTool":
                            {
                                if (string.IsNullOrEmpty(change.ToolType))
                                {
                                    errors.Add("AddTool: missing ToolType");
                                    break;
                                }

                                var position = CalculateNewToolPosition(mainViewModel);
                                var toolItem = mainViewModel.CreateDroppedTool(
                                    new ToolItem { Name = change.ToolName ?? change.ToolType, ToolType = change.ToolType },
                                    position.x, position.y);

                                if (toolItem != null)
                                {
                                    if (!string.IsNullOrEmpty(change.ToolName))
                                    {
                                        toolItem.Name = change.ToolName;
                                        if (toolItem.VisionTool != null)
                                            toolItem.VisionTool.Name = change.ToolName;
                                    }

                                    // Create connection from InputSource
                                    if (!string.IsNullOrEmpty(change.InputSource) &&
                                        !change.InputSource.Equals("Camera", StringComparison.OrdinalIgnoreCase))
                                    {
                                        var sourceTool = FindToolByName(mainViewModel, change.InputSource);
                                        if (sourceTool != null)
                                        {
                                            var connType = DetermineConnectionType(
                                                sourceTool.ToolType, change.ToolType);
                                            mainViewModel.AddConnection(sourceTool, toolItem, connType);
                                        }
                                    }
                                    appliedCount++;
                                }
                                else
                                {
                                    errors.Add($"AddTool failed: {change.ToolType} ('{change.ToolName}')");
                                }
                            }
                            break;

                        case "RemoveTool":
                            {
                                var tool = FindToolByName(mainViewModel, change.ToolName);
                                if (tool == null)
                                {
                                    errors.Add($"RemoveTool: tool not found '{change.ToolName}'");
                                    break;
                                }
                                mainViewModel.RemoveConnectionsForTool(tool);
                                mainViewModel.DroppedTools.Remove(tool);
                                if (tool.VisionTool != null)
                                    mainViewModel.ExecutionQueue.Remove(tool.VisionTool);
                                appliedCount++;
                            }
                            break;

                        default:
                            errors.Add($"Unknown operation: {change.Operation}");
                            break;
                    }
                }
                catch (Exception ex)
                {
                    errors.Add($"{change.Operation} error: {ex.Message}");
                }
            }

            return (appliedCount, errors);
        }

        /// <summary>
        /// 현재 캔버스 상태를 텍스트로 직렬화 (SLM 컨텍스트용)
        /// </summary>
        public string? BuildCanvasContext(MainViewModel vm)
        {
            if (vm.DroppedTools.Count == 0)
                return null;

            var lines = new List<string> { "Tools:" };
            foreach (var t in vm.DroppedTools)
                lines.Add($"- \"{t.Name}\" (Type: {t.ToolType})");

            lines.Add("Connections:");
            foreach (var c in vm.Connections)
                lines.Add($"- \"{c.SourceToolItem?.Name}\" -> \"{c.TargetToolItem?.Name}\" ({c.Type})");

            return string.Join("\n", lines);
        }

        private ConnectionType DetermineConnectionType(string sourceToolType, string targetToolType)
        {
            if (ImageToolTypes.Contains(sourceToolType))
                return ConnectionType.Image;

            if (CoordinatesToolTypes.Contains(sourceToolType))
            {
                // ResultTool로 가는 연결은 Result 타입
                if (targetToolType.Equals("ResultTool", StringComparison.OrdinalIgnoreCase))
                    return ConnectionType.Result;
                return ConnectionType.Coordinates;
            }

            return ConnectionType.Result;
        }

        private static ToolItem? FindToolByName(MainViewModel vm, string? toolName)
        {
            if (string.IsNullOrEmpty(toolName))
                return null;

            return vm.DroppedTools.FirstOrDefault(t =>
                t.Name.Equals(toolName, StringComparison.OrdinalIgnoreCase));
        }

        private static ConnectionType ParseConnectionType(string? typeStr)
        {
            return typeStr?.ToLower() switch
            {
                "image" => ConnectionType.Image,
                "coordinates" => ConnectionType.Coordinates,
                _ => ConnectionType.Result
            };
        }

        private static (double x, double y) CalculateNewToolPosition(MainViewModel vm)
        {
            if (vm.DroppedTools.Count == 0)
                return (400, 100);

            double maxY = 0;
            double avgX = 0;
            foreach (var tool in vm.DroppedTools)
            {
                if (tool.Y > maxY) maxY = tool.Y;
                avgX += tool.X;
            }
            avgX /= vm.DroppedTools.Count;

            return (avgX, maxY + 120);
        }
    }
}
