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
