namespace VMS.PLC.Models
{
    /// <summary>
    /// Serial port parity options (mirrors System.IO.Ports.Parity without the dependency)
    /// </summary>
    public enum PlcSerialParity
    {
        None,
        Odd,
        Even,
        Mark,
        Space
    }

    /// <summary>
    /// Serial port stop bits (mirrors System.IO.Ports.StopBits without the dependency)
    /// </summary>
    public enum PlcSerialStopBits
    {
        One,
        OnePointFive,
        Two
    }
}
