using System;
using System.Collections.Generic;
using System.Linq;
using VMS.PLC.Models.Sequence;

namespace VMS.PLC.Services
{
    /// <summary>
    /// 시퀀스의 "WO 완료 시" 분기 규칙 — 실행 엔진과 연결 점검(편집기 저장·운전 시작)이 같은 규칙을 쓴다.
    ///
    /// WO 완료는 Web 서버가 판정해 업로드 응답·SignalR 로 알려 오므로, 사이클 안의 Branch 로는 알 수 없고
    /// 그 소식이 오면 자동 운전은 사이클 경계에서 멈춘다. 그래서 설비에 완료 신호를 내는 노드는 운전 흐름이
    /// 아니라 별도 진입점(<see cref="SequenceNodeType.WorkOrderComplete"/>)에 매달고, 정지 직후 한 번만 실행한다.
    /// 정지 중에 실행되므로 신호 대기·검사처럼 멈출 수 있는 노드는 받지 않고, 한 줄로 이어진 출력·지연만 받는다.
    /// </summary>
    public static class WorkOrderCompleteBranch
    {
        /// <summary>분기에 둘 수 있는 노드 — 출력·지연·끝.</summary>
        public static bool IsAllowed(SequenceNodeType type)
            => type is SequenceNodeType.OutputAction or SequenceNodeType.Delay or SequenceNodeType.End;

        public static SequenceNodeConfig? FindEntry(SequenceConfig config)
            => config.Nodes.FirstOrDefault(n => n.NodeType == SequenceNodeType.WorkOrderComplete);

        /// <summary>
        /// 진입점 다음부터 실행할 노드를 순서대로 모은다. 규칙에 어긋나면 <paramref name="problem"/> 에 사유를 담고
        /// null — 일부만 내보내고 멈추는 일이 없도록 실행 전에 전체를 확인한다. 진입점이 없으면 빈 목록.
        /// </summary>
        public static IReadOnlyList<SequenceNodeConfig>? Resolve(SequenceConfig config, out string? problem)
        {
            problem = null;
            var entries = config.Nodes.Where(n => n.NodeType == SequenceNodeType.WorkOrderComplete).ToList();
            if (entries.Count == 0) return Array.Empty<SequenceNodeConfig>();
            if (entries.Count > 1)
            {
                problem = $"'WO 완료 시' 노드가 {entries.Count}개입니다 — 하나만 남기세요.";
                return null;
            }

            var nodes = config.Nodes.Where(n => !string.IsNullOrEmpty(n.Id))
                                    .GroupBy(n => n.Id).ToDictionary(g => g.Key, g => g.First());
            var chain = new List<SequenceNodeConfig>();
            var seen = new HashSet<string>();
            var id = entries[0].NextNodeId;
            while (id != null && nodes.TryGetValue(id, out var node))
            {
                if (!seen.Add(id))
                {
                    problem = $"'WO 완료 시' 분기의 '{node.Name}' 에서 앞 노드로 되돌아가는 연결이 있습니다 — 한 줄로 이어 주세요.";
                    return null;
                }
                if (!IsAllowed(node.NodeType))
                {
                    problem = $"'WO 완료 시' 분기에는 Output Action · Delay · End 만 쓸 수 있습니다 — '{node.Name}'({node.NodeType}).";
                    return null;
                }
                if (node.NodeType == SequenceNodeType.End) break;
                chain.Add(node);
                id = node.NextNodeId;
            }
            return chain;
        }
    }
}
