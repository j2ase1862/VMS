namespace VMS.PLC.Models
{
    /// <summary>
    /// Container for all PLC signal mappings used by AutoProcess.
    /// </summary>
    public class PlcSignalConfiguration
    {
        /// <summary>Signal maps for each camera channel</summary>
        public List<PlcSignalMap> SignalMaps { get; set; } = new();

        /// <summary>Heartbeat interval in milliseconds</summary>
        public int HeartbeatIntervalMs { get; set; } = 1000;

        /// <summary>Trigger polling interval in milliseconds</summary>
        public int TriggerPollingIntervalMs { get; set; } = 10;
    }
}
