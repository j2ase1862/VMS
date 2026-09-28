using VMS.VisionSetup.Models;

namespace VMS.VisionSetup.VisionTools.Measurement
{
    /// <summary>
    /// 3D 측정 도구 공통 공차 판정 — 값이 [low, high] 안이면 합격.
    /// 결과 키·문구는 <see cref="GeometryTool"/> 의 판정과 같다(JudgmentValue/Low/High/Pass,
    /// "판정 OK (… ∈ …)"). Success 에 반영하므로 Result 도구가 따로 손대지 않아도 OK/NG 가 집계된다.
    /// 3D 도구는 종전에 값만 내고 합불을 못 냈다 — 높이·각도·개수는 PLC 로 보내 판정해야 했다.
    /// </summary>
    public static class ToleranceJudgment
    {
        /// <param name="result">판정을 기록할 결과. 이미 실패한 결과에는 호출하지 않는다.</param>
        /// <param name="measured">판정할 값.</param>
        /// <param name="low">합격 하한 (포함).</param>
        /// <param name="high">합격 상한 (포함).</param>
        /// <param name="unit">표기 단위 ("mm", "°", "" 등).</param>
        /// <param name="label">메시지에 붙일 값 이름 (예: "평탄도").</param>
        /// <returns>합격 여부.</returns>
        public static bool ApplyRange(VisionResult result, double measured, double low, double high, string unit, string label)
        {
            bool pass = measured >= low && measured <= high;

            result.Data["JudgmentValue"] = measured;
            result.Data["JudgmentLow"] = low;
            result.Data["JudgmentHigh"] = high;
            result.Data["JudgmentPass"] = pass;

            result.Success = pass;
            result.Message += pass
                ? $" · 판정 OK ({label} {measured:F3}{unit} ∈ {low:F3}~{high:F3}{unit})"
                : $" · 판정 NG ({label} {measured:F3}{unit} ∉ {low:F3}~{high:F3}{unit})";
            return pass;
        }

        /// <summary>판정 결과 키 — 각 도구의 GetAvailableResultKeys 에 덧붙인다 (PLC 매핑 후보).</summary>
        public static readonly string[] ResultKeys = { "JudgmentValue", "JudgmentLow", "JudgmentHigh", "JudgmentPass" };
    }
}
