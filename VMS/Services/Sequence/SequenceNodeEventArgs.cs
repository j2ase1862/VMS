using VMS.PLC.Models.Sequence;

namespace VMS.Services.Sequence
{
    /// <summary>
    /// 시퀀스 노드 실행 이벤트 인자
    /// </summary>
    public class SequenceNodeEventArgs : EventArgs
    {
        public string NodeId { get; }
        public string NodeName { get; }
        public SequenceNodeType NodeType { get; }

        /// <summary>Inspection 노드인 경우 대상 카메라 ID</summary>
        public string? CameraId { get; }

        public SequenceNodeEventArgs(string nodeId, string nodeName, SequenceNodeType nodeType, string? cameraId = null)
        {
            NodeId = nodeId;
            NodeName = nodeName;
            NodeType = nodeType;
            CameraId = cameraId;
        }
    }

    /// <summary>
    /// 시퀀스 사이클 완료 이벤트 인자 — "1사이클 = 1개" 집계용.
    /// 사이클 = Repeat 노드 통과(또는 시퀀스 정상 종료)까지의 구간이며,
    /// 그 사이 Inspection 이 1회 이상 실행된 경우에만 발생한다.
    /// </summary>
    public class SequenceCycleCompletedEventArgs : EventArgs
    {
        /// <summary>사이클 동안 실행된 모든 Inspection 이 OK 였는지</summary>
        public bool AllInspectionsOk { get; }

        /// <summary>사이클 동안 실행된 Inspection 횟수 (grab 실패 포함)</summary>
        public int InspectionCount { get; }

        public SequenceCycleCompletedEventArgs(bool allInspectionsOk, int inspectionCount)
        {
            AllInspectionsOk = allInspectionsOk;
            InspectionCount = inspectionCount;
        }
    }

    /// <summary>
    /// 시퀀스 에러 이벤트 인자
    /// </summary>
    public class SequenceErrorEventArgs : EventArgs
    {
        public string NodeId { get; }
        public string NodeName { get; }
        public Exception Error { get; }

        public SequenceErrorEventArgs(string nodeId, string nodeName, Exception error)
        {
            NodeId = nodeId;
            NodeName = nodeName;
            Error = error;
        }
    }
}
