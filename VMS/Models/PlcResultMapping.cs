using VMS.PLC.Models;

namespace VMS.Models
{
    /// <summary>
    /// PLC 결과 매핑 항목 (런타임 전용 POCO)
    /// </summary>
    public class PlcResultMapping
    {
        public string ResultKey { get; set; } = "Success";
        public string PlcAddress { get; set; } = string.Empty;
        public PlcDataType DataType { get; set; } = PlcDataType.Bit;
    }
}
