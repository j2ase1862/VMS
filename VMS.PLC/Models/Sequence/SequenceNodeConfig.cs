using System;

namespace VMS.PLC.Models.Sequence
{
    /// <summary>
    /// 직렬화 가능한 시퀀스 노드 설정 (Flat POCO - 다형성 역직렬화 회피)
    /// </summary>
    public class SequenceNodeConfig
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public SequenceNodeType NodeType { get; set; }
        public string Name { get; set; } = string.Empty;

        /// <summary>캔버스 X 위치</summary>
        public double X { get; set; }

        /// <summary>캔버스 Y 위치</summary>
        public double Y { get; set; }

        // --- InputCheck 파라미터 ---

        /// <summary>체크할 PLC 주소 (InputCheck, OutputAction 공용)</summary>
        public string? PlcAddress { get; set; }

        /// <summary>입력 체크 모드</summary>
        public InputCheckMode CheckMode { get; set; } = InputCheckMode.BitOn;

        /// <summary>Word 비교값 (WordEquals, WordGreaterThan, WordLessThan용)</summary>
        public int? CompareValue { get; set; }

        /// <summary>타임아웃 (ms), -1 = 무제한</summary>
        public int TimeoutMs { get; set; } = -1;

        // --- OutputAction 파라미터 ---

        /// <summary>출력 데이터 타입</summary>
        public PlcDataType OutputDataType { get; set; } = PlcDataType.Bit;

        /// <summary>Bit 출력값</summary>
        public bool? BitValue { get; set; }

        /// <summary>Word 출력값 (Int16, Int32)</summary>
        public int? WordValue { get; set; }

        /// <summary>Float 출력값</summary>
        public float? FloatValue { get; set; }

        // --- Inspection 파라미터 ---

        /// <summary>검사 대상 카메라 ID</summary>
        public string? CameraId { get; set; }

        // --- RecipeChange 파라미터 ---

        /// <summary>레시피 변경 요청 신호 PLC 주소 (예: Y101, D200)</summary>
        public string? RecipeSignalAddress { get; set; }

        /// <summary>레시피 신호 체크 모드 (BitOn/BitOff/WordEquals 등)</summary>
        public InputCheckMode RecipeSignalCheckMode { get; set; } = InputCheckMode.BitOn;

        /// <summary>레시피 신호 Word 비교값 (WordEquals, WordGreaterThan, WordLessThan용)</summary>
        public int? RecipeSignalCompareValue { get; set; }

        /// <summary>레시피 인덱스를 읽을 PLC 주소 (Word, 예: D101)</summary>
        public string? RecipeIndexAddress { get; set; }

        /// <summary>변경할 레시피 ID (정적 모드 — PLC 주소 미설정 시 폴백)</summary>
        public string? RecipeId { get; set; }

        // --- StepChange 파라미터 ---

        /// <summary>스텝 변경 요청 신호 PLC 주소 (예: Y102, D300)</summary>
        public string? StepSignalAddress { get; set; }

        /// <summary>스텝 신호 체크 모드 (BitOn/BitOff/WordEquals 등)</summary>
        public InputCheckMode StepSignalCheckMode { get; set; } = InputCheckMode.BitOn;

        /// <summary>스텝 신호 Word 비교값 (WordEquals, WordGreaterThan, WordLessThan용)</summary>
        public int? StepSignalCompareValue { get; set; }

        /// <summary>스텝 인덱스를 읽을 PLC 주소 (Word)</summary>
        public string? StepIndexAddress { get; set; }

        // --- Delay 파라미터 ---

        /// <summary>지연 시간 (ms)</summary>
        public int DelayMs { get; set; }

        // --- Branch 파라미터 ---

        /// <summary>Branch 판정 모드: true=전체 카메라 결과(AllOk), false=직전 Inspection 결과(LastOk)</summary>
        public bool BranchOnAllCameras { get; set; } = true;

        // --- 흐름 제어 ---

        /// <summary>다음 노드 ID (일반 흐름)</summary>
        public string? NextNodeId { get; set; }

        /// <summary>조건 참일 때 이동할 노드 ID (Branch용)</summary>
        public string? TrueBranchNodeId { get; set; }

        /// <summary>조건 거짓일 때 이동할 노드 ID (Branch용)</summary>
        public string? FalseBranchNodeId { get; set; }

        // --- Repeat 파라미터 ---

        /// <summary>반복 대상 노드 ID</summary>
        public string? RepeatTargetNodeId { get; set; }

        /// <summary>반복 횟수 (-1 = 무한 반복)</summary>
        public int RepeatCount { get; set; } = -1;
    }
}
