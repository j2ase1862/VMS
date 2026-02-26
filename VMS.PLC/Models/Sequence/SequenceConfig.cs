using System;
using System.Collections.Generic;

namespace VMS.PLC.Models.Sequence
{
    /// <summary>
    /// 시퀀스 컨테이너 - Recipe에 저장되는 통합 프로세스 시퀀스.
    /// 개별 Inspection 노드의 CameraId로 카메라별 검사를 수행한다.
    /// </summary>
    public class SequenceConfig
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = "Process Sequence";

        public List<SequenceNodeConfig> Nodes { get; set; } = new();
        public List<SequenceEdgeConfig> Edges { get; set; } = new();
    }
}
