using System;
using System.Collections.Generic;
using System.Linq;
using VMS.PLC.Models.Sequence;

namespace VMS.PLC.Services
{
    /// <summary>
    /// 시퀀스의 "화면에 그려진 연결선(Edges)" 과 "엔진이 따라가는 참조(NextNodeId 등)" 를 맞춘다.
    ///
    /// 실행 엔진은 노드의 참조만 따라가고 편집기는 연결선을 그린다. 둘이 어긋나면 화면에는
    /// Wait Trigger → Inspection 선이 보이는데 운전은 트리거 직후 시퀀스가 끝나, 트리거마다
    /// 검사 없이 처음으로 돌아간다 — 오류도 로그도 없다 (2026-09-23 실증 PC).
    ///
    /// 규칙: 참조가 비어 있거나 그려지지 않은 곳을 가리키는데 같은 라벨의 선이 정확히 하나면
    /// 그 선으로 참조를 채운다(보이는 선 = 실행 경로). 참조를 지우지는 않는다 — 기본 시퀀스는
    /// Repeat → End 처럼 선 없이 참조만 둔 연결을 쓴다. 같은 라벨의 선이 여럿이면 고르지 않고 알린다.
    /// </summary>
    public static class SequenceLinkReconciler
    {
        public sealed record Result(IReadOnlyList<string> Repairs, IReadOnlyList<string> Problems)
        {
            public bool HasChanges => Repairs.Count > 0;
        }

        public static Result Reconcile(SequenceConfig config)
        {
            var repairs = new List<string>();
            var problems = new List<string>();
            var nodes = config.Nodes.Where(n => !string.IsNullOrEmpty(n.Id))
                                    .GroupBy(n => n.Id).ToDictionary(g => g.Key, g => g.First());

            foreach (var node in config.Nodes)
            {
                foreach (var slot in SlotsFor(node))
                {
                    var drawn = config.Edges
                        .Where(e => e.SourceNodeId == node.Id
                                    && string.Equals(NormalizeLabel(node, e.Label), slot.Label, StringComparison.Ordinal)
                                    && nodes.ContainsKey(e.TargetNodeId))
                        .Select(e => e.TargetNodeId)
                        .Distinct()
                        .ToList();
                    if (drawn.Count == 0) continue;

                    var current = slot.Get();
                    if (current != null && drawn.Contains(current)) continue;   // 일치

                    if (drawn.Count > 1)
                    {
                        problems.Add($"'{node.Name}' 에서 나가는 {slot.Label} 연결선이 {drawn.Count}개입니다 — " +
                                     "하나만 남기고 지운 뒤 다시 저장하세요.");
                        continue;
                    }

                    // 참조가 비었거나, 없는 노드/그려지지 않은 노드를 가리킨다 → 보이는 선으로 맞춘다.
                    var target = drawn[0];
                    slot.Set(target);
                    repairs.Add($"'{node.Name}' → '{nodes[target].Name}' ({slot.Label}) 연결을 화면의 선과 맞췄습니다.");
                }
            }

            problems.AddRange(FindDeadEnds(config, nodes));
            return new Result(repairs, problems);
        }

        /// <summary>Start 에서 닿는 노드 중 End 가 아닌데 다음 노드가 없는 곳 — 시퀀스가 거기서 끝난다.</summary>
        private static IEnumerable<string> FindDeadEnds(SequenceConfig config, Dictionary<string, SequenceNodeConfig> nodes)
        {
            var start = config.Nodes.FirstOrDefault(n => n.NodeType == SequenceNodeType.Start);
            if (start == null) yield break;

            var seen = new HashSet<string>();
            var queue = new Queue<SequenceNodeConfig>();
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                var node = queue.Dequeue();
                if (!seen.Add(node.Id)) continue;
                if (node.NodeType == SequenceNodeType.End) continue;

                var nexts = node.NodeType switch
                {
                    SequenceNodeType.Branch => new[] { node.TrueBranchNodeId, node.FalseBranchNodeId },
                    SequenceNodeType.Repeat => new[] { node.RepeatTargetNodeId ?? node.NextNodeId },
                    _ => new[] { node.NextNodeId },
                };

                foreach (var next in nexts)
                {
                    if (next != null && nodes.TryGetValue(next, out var nextNode))
                        queue.Enqueue(nextNode);
                    else
                        yield return $"'{node.Name}' 다음에 연결된 노드가 없어 시퀀스가 여기서 끝납니다" +
                                     (node.NodeType == SequenceNodeType.Branch ? " (True/False 중 빈 쪽)" : "") + ".";
                }
            }
        }

        private sealed record Slot(string Label, Func<string?> Get, Action<string> Set);

        private static IEnumerable<Slot> SlotsFor(SequenceNodeConfig n)
        {
            switch (n.NodeType)
            {
                case SequenceNodeType.End:
                    yield break;
                case SequenceNodeType.Branch:
                    yield return new Slot("True", () => n.TrueBranchNodeId, v => n.TrueBranchNodeId = v);
                    yield return new Slot("False", () => n.FalseBranchNodeId, v => n.FalseBranchNodeId = v);
                    yield break;
                case SequenceNodeType.Repeat:
                    yield return new Slot("Repeat", () => n.RepeatTargetNodeId, v => n.RepeatTargetNodeId = v);
                    yield return new Slot("Next", () => n.NextNodeId, v => n.NextNodeId = v);
                    yield break;
                default:
                    yield return new Slot("Next", () => n.NextNodeId, v => n.NextNodeId = v);
                    yield break;
            }
        }

        // 라벨 없는 옛 선은 일반 노드에서 Next 로 본다.
        private static string NormalizeLabel(SequenceNodeConfig source, string? label)
            => string.IsNullOrEmpty(label) && source.NodeType is not (SequenceNodeType.Branch or SequenceNodeType.Repeat)
                ? "Next"
                : label ?? string.Empty;
    }
}
