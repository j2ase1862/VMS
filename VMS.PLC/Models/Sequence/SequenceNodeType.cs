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
        StepChange
    }
}
