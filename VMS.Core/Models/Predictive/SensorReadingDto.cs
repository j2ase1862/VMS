using System;

namespace VMS.Core.Models.Predictive
{
    /// <summary>
    /// Predictive_DefectRate_Plan §5.2 — POST /api/sensors/readings 요청 본문.
    /// 모든 측정값 nullable — 일부 센서만 연결된 현장에서도 호출 가능.
    /// 모든 값이 null 이면 Web 측이 400 으로 reject 하므로 호출자가 사전 skip 권장.
    /// </summary>
    public class SensorReadingDto
    {
        public int ClientIndex { get; set; }

        /// <summary>VMS 측 UTC 측정 시각. 미지정 시 서버 수신 시각.</summary>
        public DateTime? Timestamp { get; set; }

        public double? TemperatureC { get; set; }
        public double? HumidityPct { get; set; }
        public double? VibrationRms { get; set; }
        public double? PressurePsi { get; set; }

        /// <summary>모든 측정값이 null 이면 의미 없는 reading → 송신 skip.</summary>
        public bool HasAnyReading =>
            TemperatureC.HasValue
            || HumidityPct.HasValue
            || VibrationRms.HasValue
            || PressurePsi.HasValue;
    }
}
