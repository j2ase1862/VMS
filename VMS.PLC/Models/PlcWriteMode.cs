namespace VMS.PLC.Models
{
    /// <summary>
    /// PLC result write mode for inspection data
    /// </summary>
    public enum PlcWriteMode
    {
        /// <summary>Write result once when inspection completes</summary>
        SingleShot,

        /// <summary>Write result and wait for PLC acknowledgment before proceeding</summary>
        Handshake
    }
}
