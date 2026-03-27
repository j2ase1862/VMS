using System;

namespace VMS.PLC.Models.Sequence
{
    /// <summary>
    /// 시퀀스 노드 간 연결선 설정 (직렬화용)
    /// </summary>
    public class SequenceEdgeConfig
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string SourceNodeId { get; set; } = string.Empty;
        public string TargetNodeId { get; set; } = string.Empty;

        /// <summary>연결 라벨 ("True", "False", "Next")</summary>
        public string? Label { get; set; }
    }
}
