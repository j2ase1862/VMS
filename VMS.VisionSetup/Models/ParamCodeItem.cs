namespace VMS.VisionSetup.Models
{
    /// <summary>
    /// Web 파라미터 코드 항목 (ParamCode ComboBox용)
    /// </summary>
    public class ParamCodeItem
    {
        /// <summary>null이면 "None" (연동 해제)</summary>
        public int? ParamCode { get; set; }
        public string DisplayText { get; set; } = string.Empty;
        public double Value { get; set; }
        public string Unit { get; set; } = string.Empty;

        public override string ToString() => DisplayText;

        public override bool Equals(object? obj)
            => obj is ParamCodeItem other && ParamCode == other.ParamCode;

        public override int GetHashCode()
            => ParamCode?.GetHashCode() ?? 0;
    }
}
