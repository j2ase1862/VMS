namespace VMS.PLC.Models
{
    /// <summary>
    /// Event args for PLC bit monitoring change detection
    /// </summary>
    public class PlcBitChangedEventArgs : EventArgs
    {
        public PlcAddress Address { get; }
        public bool NewValue { get; }
        public DateTime Timestamp { get; }

        public PlcBitChangedEventArgs(PlcAddress address, bool newValue)
        {
            Address = address;
            NewValue = newValue;
            Timestamp = DateTime.UtcNow;
        }
    }
}
