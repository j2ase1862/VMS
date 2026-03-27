namespace VMS.PLC.Models
{
    /// <summary>
    /// PLC connection state enumeration
    /// </summary>
    public enum PlcConnectionState
    {
        Disconnected,
        Connecting,
        Connected,
        Error,
        Reconnecting
    }
}
