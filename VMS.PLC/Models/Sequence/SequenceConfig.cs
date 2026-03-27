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

        /// <summary>
        /// Reset 신호 PLC 주소.
        /// 시퀀스 실행 중 이 주소를 병렬로 모니터링하여 조건 충족 시 즉시 Start 노드로 복귀.
        /// null 또는 빈 문자열이면 Reset 기능 비활성.
        /// </summary>
        public string? ResetSignalAddress { get; set; }

        /// <summary>Reset 신호 체크 모드 (BitOn/BitOff/WordEquals 등)</summary>
        public InputCheckMode ResetSignalCheckMode { get; set; } = InputCheckMode.BitOn;

        /// <summary>Reset 신호 Word 비교값 (WordEquals, WordGreaterThan, WordLessThan용)</summary>
        public int? ResetSignalCompareValue { get; set; }

        public List<SequenceNodeConfig> Nodes { get; set; } = new();
        public List<SequenceEdgeConfig> Edges { get; set; } = new();
    }
}
