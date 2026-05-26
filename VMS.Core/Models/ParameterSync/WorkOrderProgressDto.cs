namespace VMS.Core.Models.ParameterSync
{
    /// <summary>
    /// Web 의 /api/parameters/results 응답에 포함된 작업지시 진행률 스냅샷.
    /// Stage 3 — 헤더 WO 칩 실시간 갱신 + 계획 수량 도달 알람용.
    /// </summary>
    public class WorkOrderProgressDto
    {
        public int Id { get; set; }
        public string OrderNo { get; set; } = "";
        public int PlannedQuantity { get; set; }
        public int ProducedQuantity { get; set; }
        public int PassQuantity { get; set; }
        public int NgQuantity { get; set; }
        public string Status { get; set; } = "";

        /// <summary>이 업로드로 PlannedQuantity 에 도달해 막 Completed 전이된 경우 true.</summary>
        public bool Completed { get; set; }

        public double Progress => PlannedQuantity > 0
            ? System.Math.Round((double)ProducedQuantity / PlannedQuantity * 100, 1)
            : 0;

        public double PassRate => ProducedQuantity > 0
            ? System.Math.Round((double)PassQuantity / ProducedQuantity * 100, 1)
            : 0;
    }
}
