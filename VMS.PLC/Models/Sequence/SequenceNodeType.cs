namespace VMS.PLC.Models.Sequence
{
    /// <summary>
    /// 시퀀스 노드 타입
    /// </summary>
    public enum SequenceNodeType
    {
        Start,
        End,
        InputCheck,
        OutputAction,
        Inspection,
        Branch,
        Delay,
        Repeat,
        RecipeChange,
        StepChange,

        /// <summary>
        /// "WO 완료 시" 진입점 — 작업지시 완료로 자동 운전이 사이클 경계에서 멈춘 직후 한 번만
        /// 이 노드부터 실행한다. 이어지는 노드는 OutputAction · Delay · End 만 허용 (설비에 완료 신호).
        /// 수동 정지·오류 정지에서는 실행하지 않는다.
        /// </summary>
        WorkOrderComplete
    }
}
