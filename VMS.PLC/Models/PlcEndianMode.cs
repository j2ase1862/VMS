namespace VMS.PLC.Models
{
    /// <summary>
    /// Byte order for 16/32-bit PLC data words.
    /// Different PLC vendors use different endianness.
    /// </summary>
    public enum PlcEndianMode
    {
        /// <summary>Little-endian (LS, Mitsubishi default)</summary>
        LittleEndian,

        /// <summary>Big-endian (Siemens, Omron default)</summary>
        BigEndian
    }
}
