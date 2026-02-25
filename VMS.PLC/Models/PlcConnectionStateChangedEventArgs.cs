namespace VMS.PLC.Models
{
    /// <summary>
    /// Event args for PLC connection state transitions
    /// </summary>
    public class PlcConnectionStateChangedEventArgs : EventArgs
    {
        public PlcConnectionState OldState { get; }
        public PlcConnectionState NewState { get; }
        public string? Reason { get; }

        public PlcConnectionStateChangedEventArgs(
            PlcConnectionState oldState,
            PlcConnectionState newState,
            string? reason = null)
        {
            OldState = oldState;
            NewState = newState;
            Reason = reason;
        }
    }
}
