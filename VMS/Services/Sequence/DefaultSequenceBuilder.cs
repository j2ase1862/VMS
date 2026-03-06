using VMS.PLC.Models;
using VMS.PLC.Models.Sequence;

namespace VMS.Services.Sequence
{
    /// <summary>
    /// PlcSignalConfiguration → SequenceConfig 변환.
    /// 전체 프로세스 시퀀스를 생성한다 (단일 시퀀스에서 모든 카메라를 순차 검사).
    ///
    /// Start
    ///   → RecipeChange (Signal + Index 주소로 레시피 변경 체크)
    ///   → InputCheck (TriggerAddress, BitOn)
    ///   → StepChange (Signal + Index 주소로 스텝 변경 체크)
    ///   → OutputAction (BusyAddress = true)
    ///   → Inspection (Camera1)
    ///   → Inspection (Camera2)
    ///   → ...
    ///   → Branch (AllInspectionsOk?)
    ///       True  → OutputAction (ResultOk = true per camera)
    ///       False → OutputAction (ResultNg = true per camera)
    ///   → OutputAction (CompleteAddress = true)
    ///   → InputCheck (TriggerAddress, BitOff, 30s)
    ///   → Clear all outputs
    ///   → Repeat (→ RecipeChange)
    /// </summary>
    public static class DefaultSequenceBuilder
    {
        private const double NodeX = 200;
        private const double NodeStartY = 50;
        private const double NodeSpacingY = 110;
        private const int AckTimeoutMs = 30000;

        /// <summary>
        /// 전체 PlcSignalConfiguration에서 통합 프로세스 시퀀스 생성.
        /// 모든 카메라를 하나의 시퀀스에서 순차 검사한다.
        /// </summary>
        public static SequenceConfig BuildFromSignalConfiguration(PlcSignalConfiguration signalConfig)
        {
            var config = new SequenceConfig
            {
                Name = "Default Process Sequence",
                ResetSignalAddress = string.IsNullOrWhiteSpace(signalConfig.ResetSignalAddress)
                    ? null
                    : signalConfig.ResetSignalAddress,
                ResetSignalCheckMode = signalConfig.ResetSignalCheckMode,
                ResetSignalCompareValue = signalConfig.ResetSignalCompareValue
            };

            if (signalConfig.SignalMaps.Count == 0)
                return config;

            // 단일 카메라인 경우 기존 BuildFromSignalMap 호환
            if (signalConfig.SignalMaps.Count == 1)
            {
                var singleConfig = BuildFromSignalMap(signalConfig.SignalMaps[0]);
                singleConfig.ResetSignalAddress = config.ResetSignalAddress;
                singleConfig.ResetSignalCheckMode = config.ResetSignalCheckMode;
                singleConfig.ResetSignalCompareValue = config.ResetSignalCompareValue;
                return singleConfig;
            }

            var nodes = new List<SequenceNodeConfig>();
            var edges = new List<SequenceEdgeConfig>();
            int index = 0;

            // 첫 번째 signalMap에서 트리거 주소 사용 (공통 트리거)
            var primaryMap = signalConfig.SignalMaps[0];

            // --- Start ---
            var startNode = CreateNode(SequenceNodeType.Start, "Start", ref index);
            nodes.Add(startNode);

            // --- RecipeChange: 레시피 변경 체크 ---
            var recipeChange = CreateNode(SequenceNodeType.RecipeChange, "Recipe Change", ref index);
            nodes.Add(recipeChange);
            Link(startNode, recipeChange, edges, "Next");

            // --- InputCheck: TriggerAddress BitOn ---
            var waitTrigger = CreateNode(SequenceNodeType.InputCheck, "Wait Trigger", ref index);
            waitTrigger.PlcAddress = primaryMap.TriggerAddress;
            waitTrigger.CheckMode = InputCheckMode.BitOn;
            nodes.Add(waitTrigger);
            Link(recipeChange, waitTrigger, edges, "Next");

            // --- StepChange: 스텝 변경 체크 ---
            var stepChange = CreateNode(SequenceNodeType.StepChange, "Step Change", ref index);
            nodes.Add(stepChange);
            Link(waitTrigger, stepChange, edges, "Next");

            // --- Busy ON (각 카메라 채널) ---
            var prevNode = stepChange;
            foreach (var signalMap in signalConfig.SignalMaps)
            {
                if (!string.IsNullOrEmpty(signalMap.BusyAddress))
                {
                    var busyOn = CreateNode(SequenceNodeType.OutputAction, $"Busy ON ({signalMap.CameraId})", ref index);
                    busyOn.PlcAddress = signalMap.BusyAddress;
                    busyOn.OutputDataType = PlcDataType.Bit;
                    busyOn.BitValue = true;
                    nodes.Add(busyOn);
                    Link(prevNode, busyOn, edges, "Next");
                    prevNode = busyOn;
                }
            }

            // --- Inspection (각 카메라 순차 검사) ---
            foreach (var signalMap in signalConfig.SignalMaps)
            {
                var inspection = CreateNode(SequenceNodeType.Inspection, $"Inspection ({signalMap.CameraId})", ref index);
                inspection.CameraId = signalMap.CameraId;
                nodes.Add(inspection);
                Link(prevNode, inspection, edges, "Next");
                prevNode = inspection;
            }

            // --- Branch (AllInspectionsOk) ---
            var branch = CreateNode(SequenceNodeType.Branch, "Result Branch", ref index);
            branch.BranchOnAllCameras = true;
            nodes.Add(branch);
            Link(prevNode, branch, edges, "Next");

            // --- True 분기: 각 카메라 ResultOk ON ---
            SequenceNodeConfig? truePrev = null;
            foreach (var signalMap in signalConfig.SignalMaps)
            {
                if (!string.IsNullOrEmpty(signalMap.ResultOkAddress))
                {
                    var resultOk = CreateNode(SequenceNodeType.OutputAction, $"Result OK ({signalMap.CameraId})", ref index);
                    resultOk.PlcAddress = signalMap.ResultOkAddress;
                    resultOk.OutputDataType = PlcDataType.Bit;
                    resultOk.BitValue = true;
                    nodes.Add(resultOk);

                    if (truePrev == null)
                    {
                        branch.TrueBranchNodeId = resultOk.Id;
                        edges.Add(new SequenceEdgeConfig { SourceNodeId = branch.Id, TargetNodeId = resultOk.Id, Label = "True" });
                    }
                    else
                    {
                        Link(truePrev, resultOk, edges, "Next");
                    }
                    truePrev = resultOk;
                }
            }

            // True 분기가 비어있으면 더미 노드
            if (truePrev == null)
            {
                var skipOk = CreateNode(SequenceNodeType.Delay, "Skip (OK)", ref index);
                skipOk.DelayMs = 0;
                nodes.Add(skipOk);
                branch.TrueBranchNodeId = skipOk.Id;
                edges.Add(new SequenceEdgeConfig { SourceNodeId = branch.Id, TargetNodeId = skipOk.Id, Label = "True" });
                truePrev = skipOk;
            }

            // --- False 분기: 각 카메라 ResultNg ON ---
            SequenceNodeConfig? falsePrev = null;
            foreach (var signalMap in signalConfig.SignalMaps)
            {
                if (!string.IsNullOrEmpty(signalMap.ResultNgAddress))
                {
                    var resultNg = CreateNode(SequenceNodeType.OutputAction, $"Result NG ({signalMap.CameraId})", ref index);
                    resultNg.PlcAddress = signalMap.ResultNgAddress;
                    resultNg.OutputDataType = PlcDataType.Bit;
                    resultNg.BitValue = true;
                    nodes.Add(resultNg);

                    if (falsePrev == null)
                    {
                        branch.FalseBranchNodeId = resultNg.Id;
                        edges.Add(new SequenceEdgeConfig { SourceNodeId = branch.Id, TargetNodeId = resultNg.Id, Label = "False" });
                    }
                    else
                    {
                        Link(falsePrev, resultNg, edges, "Next");
                    }
                    falsePrev = resultNg;
                }
            }

            if (falsePrev == null)
            {
                var skipNg = CreateNode(SequenceNodeType.Delay, "Skip (NG)", ref index);
                skipNg.DelayMs = 0;
                nodes.Add(skipNg);
                branch.FalseBranchNodeId = skipNg.Id;
                edges.Add(new SequenceEdgeConfig { SourceNodeId = branch.Id, TargetNodeId = skipNg.Id, Label = "False" });
                falsePrev = skipNg;
            }

            // --- Complete ON (합류 지점) ---
            SequenceNodeConfig? mergeNode = null;
            foreach (var signalMap in signalConfig.SignalMaps)
            {
                if (!string.IsNullOrEmpty(signalMap.CompleteAddress))
                {
                    var completeOn = CreateNode(SequenceNodeType.OutputAction, $"Complete ON ({signalMap.CameraId})", ref index);
                    completeOn.PlcAddress = signalMap.CompleteAddress;
                    completeOn.OutputDataType = PlcDataType.Bit;
                    completeOn.BitValue = true;
                    nodes.Add(completeOn);

                    if (mergeNode == null)
                    {
                        // True/False 양쪽에서 합류
                        Link(truePrev, completeOn, edges, "Next");
                        Link(falsePrev, completeOn, edges, "Next");
                    }
                    else
                    {
                        Link(mergeNode, completeOn, edges, "Next");
                    }
                    mergeNode = completeOn;
                }
            }

            if (mergeNode == null)
            {
                mergeNode = CreateNode(SequenceNodeType.Delay, "Merge", ref index);
                ((SequenceNodeConfig)mergeNode).DelayMs = 0;
                nodes.Add(mergeNode);
                Link(truePrev, mergeNode, edges, "Next");
                Link(falsePrev, mergeNode, edges, "Next");
            }

            prevNode = mergeNode;

            // --- WaitAck: TriggerAddress BitOff ---
            var waitAck = CreateNode(SequenceNodeType.InputCheck, "Wait Ack", ref index);
            waitAck.PlcAddress = primaryMap.TriggerAddress;
            waitAck.CheckMode = InputCheckMode.BitOff;
            waitAck.TimeoutMs = AckTimeoutMs;
            nodes.Add(waitAck);
            Link(prevNode, waitAck, edges, "Next");
            prevNode = waitAck;

            // --- Clear outputs (모든 카메라) ---
            foreach (var signalMap in signalConfig.SignalMaps)
            {
                if (!string.IsNullOrEmpty(signalMap.BusyAddress))
                {
                    var busyOff = CreateNode(SequenceNodeType.OutputAction, $"Busy OFF ({signalMap.CameraId})", ref index);
                    busyOff.PlcAddress = signalMap.BusyAddress;
                    busyOff.OutputDataType = PlcDataType.Bit;
                    busyOff.BitValue = false;
                    nodes.Add(busyOff);
                    Link(prevNode, busyOff, edges, "Next");
                    prevNode = busyOff;
                }

                if (!string.IsNullOrEmpty(signalMap.CompleteAddress))
                {
                    var completeOff = CreateNode(SequenceNodeType.OutputAction, $"Complete OFF ({signalMap.CameraId})", ref index);
                    completeOff.PlcAddress = signalMap.CompleteAddress;
                    completeOff.OutputDataType = PlcDataType.Bit;
                    completeOff.BitValue = false;
                    nodes.Add(completeOff);
                    Link(prevNode, completeOff, edges, "Next");
                    prevNode = completeOff;
                }

                if (!string.IsNullOrEmpty(signalMap.ResultOkAddress))
                {
                    var resultOkOff = CreateNode(SequenceNodeType.OutputAction, $"Result OK OFF ({signalMap.CameraId})", ref index);
                    resultOkOff.PlcAddress = signalMap.ResultOkAddress;
                    resultOkOff.OutputDataType = PlcDataType.Bit;
                    resultOkOff.BitValue = false;
                    nodes.Add(resultOkOff);
                    Link(prevNode, resultOkOff, edges, "Next");
                    prevNode = resultOkOff;
                }

                if (!string.IsNullOrEmpty(signalMap.ResultNgAddress))
                {
                    var resultNgOff = CreateNode(SequenceNodeType.OutputAction, $"Result NG OFF ({signalMap.CameraId})", ref index);
                    resultNgOff.PlcAddress = signalMap.ResultNgAddress;
                    resultNgOff.OutputDataType = PlcDataType.Bit;
                    resultNgOff.BitValue = false;
                    nodes.Add(resultNgOff);
                    Link(prevNode, resultNgOff, edges, "Next");
                    prevNode = resultNgOff;
                }
            }

            // --- Repeat → RecipeChange ---
            var repeat = CreateNode(SequenceNodeType.Repeat, "Repeat", ref index);
            repeat.RepeatTargetNodeId = recipeChange.Id;
            repeat.RepeatCount = -1;
            nodes.Add(repeat);
            Link(prevNode, repeat, edges, "Next");

            // --- End ---
            var endNode = CreateNode(SequenceNodeType.End, "End", ref index);
            nodes.Add(endNode);
            repeat.NextNodeId = endNode.Id;

            config.Nodes = nodes;
            config.Edges = edges;
            return config;
        }

        /// <summary>
        /// 단일 PlcSignalMap에서 단일 카메라 시퀀스 생성 (하위 호환용).
        /// </summary>
        public static SequenceConfig BuildFromSignalMap(PlcSignalMap signalMap)
        {
            var config = new SequenceConfig
            {
                Name = $"Default Sequence ({signalMap.CameraId})"
            };

            var nodes = new List<SequenceNodeConfig>();
            var edges = new List<SequenceEdgeConfig>();
            int index = 0;

            // --- Start ---
            var startNode = CreateNode(SequenceNodeType.Start, "Start", ref index);
            nodes.Add(startNode);

            // --- RecipeChange: 레시피 변경 체크 ---
            var recipeChange = CreateNode(SequenceNodeType.RecipeChange, "Recipe Change", ref index);
            nodes.Add(recipeChange);
            Link(startNode, recipeChange, edges, "Next");

            // --- InputCheck: TriggerAddress BitOn (WaitTrigger) ---
            var waitTrigger = CreateNode(SequenceNodeType.InputCheck, "Wait Trigger", ref index);
            waitTrigger.PlcAddress = signalMap.TriggerAddress;
            waitTrigger.CheckMode = InputCheckMode.BitOn;
            nodes.Add(waitTrigger);
            Link(recipeChange, waitTrigger, edges, "Next");

            // --- StepChange: 스텝 변경 체크 ---
            var stepChange = CreateNode(SequenceNodeType.StepChange, "Step Change", ref index);
            nodes.Add(stepChange);
            Link(waitTrigger, stepChange, edges, "Next");

            // --- OutputAction: BusyAddress = true ---
            SequenceNodeConfig? busyOn = null;
            var prevNode = stepChange;
            if (!string.IsNullOrEmpty(signalMap.BusyAddress))
            {
                busyOn = CreateNode(SequenceNodeType.OutputAction, "Busy ON", ref index);
                busyOn.PlcAddress = signalMap.BusyAddress;
                busyOn.OutputDataType = PlcDataType.Bit;
                busyOn.BitValue = true;
                nodes.Add(busyOn);
                Link(prevNode, busyOn, edges, "Next");
                prevNode = busyOn;
            }

            // --- Inspection ---
            var inspection = CreateNode(SequenceNodeType.Inspection, "Inspection", ref index);
            inspection.CameraId = signalMap.CameraId;
            nodes.Add(inspection);
            Link(prevNode, inspection, edges, "Next");

            // --- Branch ---
            var branch = CreateNode(SequenceNodeType.Branch, "Result Branch", ref index);
            nodes.Add(branch);
            Link(inspection, branch, edges, "Next");

            // --- True: ResultOk = true ---
            SequenceNodeConfig trueTarget;
            if (!string.IsNullOrEmpty(signalMap.ResultOkAddress))
            {
                var resultOk = CreateNode(SequenceNodeType.OutputAction, "Result OK ON", ref index);
                resultOk.PlcAddress = signalMap.ResultOkAddress;
                resultOk.OutputDataType = PlcDataType.Bit;
                resultOk.BitValue = true;
                nodes.Add(resultOk);
                trueTarget = resultOk;
            }
            else
            {
                trueTarget = CreateNode(SequenceNodeType.Delay, "Skip (OK)", ref index);
                trueTarget.DelayMs = 0;
                nodes.Add(trueTarget);
            }

            // --- False: ResultNg = true ---
            SequenceNodeConfig falseTarget;
            if (!string.IsNullOrEmpty(signalMap.ResultNgAddress))
            {
                var resultNg = CreateNode(SequenceNodeType.OutputAction, "Result NG ON", ref index);
                resultNg.PlcAddress = signalMap.ResultNgAddress;
                resultNg.OutputDataType = PlcDataType.Bit;
                resultNg.BitValue = true;
                nodes.Add(resultNg);
                falseTarget = resultNg;
            }
            else
            {
                falseTarget = CreateNode(SequenceNodeType.Delay, "Skip (NG)", ref index);
                falseTarget.DelayMs = 0;
                nodes.Add(falseTarget);
            }

            // Branch 연결
            branch.TrueBranchNodeId = trueTarget.Id;
            branch.FalseBranchNodeId = falseTarget.Id;
            edges.Add(new SequenceEdgeConfig
            {
                SourceNodeId = branch.Id,
                TargetNodeId = trueTarget.Id,
                Label = "True"
            });
            edges.Add(new SequenceEdgeConfig
            {
                SourceNodeId = branch.Id,
                TargetNodeId = falseTarget.Id,
                Label = "False"
            });

            // --- CompleteAddress = true (True/False 모두 여기로 합류) ---
            SequenceNodeConfig? completeOn = null;
            if (!string.IsNullOrEmpty(signalMap.CompleteAddress))
            {
                completeOn = CreateNode(SequenceNodeType.OutputAction, "Complete ON", ref index);
                completeOn.PlcAddress = signalMap.CompleteAddress;
                completeOn.OutputDataType = PlcDataType.Bit;
                completeOn.BitValue = true;
                nodes.Add(completeOn);
                Link(trueTarget, completeOn, edges, "Next");
                Link(falseTarget, completeOn, edges, "Next");
                prevNode = completeOn;
            }
            else
            {
                // 합류용 Delay 0 노드
                var merge = CreateNode(SequenceNodeType.Delay, "Merge", ref index);
                merge.DelayMs = 0;
                nodes.Add(merge);
                Link(trueTarget, merge, edges, "Next");
                Link(falseTarget, merge, edges, "Next");
                prevNode = merge;
            }

            // --- InputCheck: TriggerAddress BitOff (WaitAck) ---
            var waitAck = CreateNode(SequenceNodeType.InputCheck, "Wait Ack", ref index);
            waitAck.PlcAddress = signalMap.TriggerAddress;
            waitAck.CheckMode = InputCheckMode.BitOff;
            waitAck.TimeoutMs = AckTimeoutMs;
            nodes.Add(waitAck);
            Link(prevNode, waitAck, edges, "Next");
            prevNode = waitAck;

            // --- Clear outputs ---
            if (!string.IsNullOrEmpty(signalMap.BusyAddress))
            {
                var busyOff = CreateNode(SequenceNodeType.OutputAction, "Busy OFF", ref index);
                busyOff.PlcAddress = signalMap.BusyAddress;
                busyOff.OutputDataType = PlcDataType.Bit;
                busyOff.BitValue = false;
                nodes.Add(busyOff);
                Link(prevNode, busyOff, edges, "Next");
                prevNode = busyOff;
            }

            if (!string.IsNullOrEmpty(signalMap.CompleteAddress))
            {
                var completeOff = CreateNode(SequenceNodeType.OutputAction, "Complete OFF", ref index);
                completeOff.PlcAddress = signalMap.CompleteAddress;
                completeOff.OutputDataType = PlcDataType.Bit;
                completeOff.BitValue = false;
                nodes.Add(completeOff);
                Link(prevNode, completeOff, edges, "Next");
                prevNode = completeOff;
            }

            if (!string.IsNullOrEmpty(signalMap.ResultOkAddress))
            {
                var resultOkOff = CreateNode(SequenceNodeType.OutputAction, "Result OK OFF", ref index);
                resultOkOff.PlcAddress = signalMap.ResultOkAddress;
                resultOkOff.OutputDataType = PlcDataType.Bit;
                resultOkOff.BitValue = false;
                nodes.Add(resultOkOff);
                Link(prevNode, resultOkOff, edges, "Next");
                prevNode = resultOkOff;
            }

            if (!string.IsNullOrEmpty(signalMap.ResultNgAddress))
            {
                var resultNgOff = CreateNode(SequenceNodeType.OutputAction, "Result NG OFF", ref index);
                resultNgOff.PlcAddress = signalMap.ResultNgAddress;
                resultNgOff.OutputDataType = PlcDataType.Bit;
                resultNgOff.BitValue = false;
                nodes.Add(resultNgOff);
                Link(prevNode, resultNgOff, edges, "Next");
                prevNode = resultNgOff;
            }

            // --- Repeat → RecipeChange (무한 반복) ---
            var repeat = CreateNode(SequenceNodeType.Repeat, "Repeat", ref index);
            repeat.RepeatTargetNodeId = recipeChange.Id;
            repeat.RepeatCount = -1;
            nodes.Add(repeat);
            Link(prevNode, repeat, edges, "Next");

            // --- End (Repeat가 무한이므로 도달하지 않지만 구조 완성용) ---
            var endNode = CreateNode(SequenceNodeType.End, "End", ref index);
            nodes.Add(endNode);
            repeat.NextNodeId = endNode.Id;

            config.Nodes = nodes;
            config.Edges = edges;
            return config;
        }

        private static SequenceNodeConfig CreateNode(SequenceNodeType type, string name, ref int index)
        {
            var node = new SequenceNodeConfig
            {
                NodeType = type,
                Name = name,
                X = NodeX,
                Y = NodeStartY + (index * NodeSpacingY)
            };
            index++;
            return node;
        }

        private static void Link(SequenceNodeConfig source, SequenceNodeConfig target, List<SequenceEdgeConfig> edges, string label)
        {
            source.NextNodeId = target.Id;
            edges.Add(new SequenceEdgeConfig
            {
                SourceNodeId = source.Id,
                TargetNodeId = target.Id,
                Label = label
            });
        }
    }
}
