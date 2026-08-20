namespace VMS.Models
{
    /// <summary>
    /// 헤더 Lot 콤보 항목 — WO 의 Open Lot 1개.
    /// ToString 이 Id 만 반환하는 이유: 편집형 ComboBox 에서 항목 선택 시 WPF 가
    /// Text 에 ToString() 을 밀어넣는데, Text 는 LotIdText(정수 파싱 → 업로드 LotId)에
    /// 바인딩되어 있어 표시 문자열이 들어가면 파싱이 깨진다. 표시는 DisplayText 로.
    /// </summary>
    public sealed class LotComboItem
    {
        public int Id { get; init; }
        public string LotNumber { get; init; } = string.Empty;

        /// <summary>드롭다운 표시용 (예: "#12 — 20260820-WO-001-003").</summary>
        public string DisplayText => $"#{Id} — {LotNumber}";

        public override string ToString() => Id.ToString();
    }
}
