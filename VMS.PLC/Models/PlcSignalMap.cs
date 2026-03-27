namespace VMS.PLC.Models
{
    /// <summary>
    /// AutoProcess signal address mapping for a single camera channel.
    /// Maps PLC addresses for handshake signals between VMS and PLC.
    /// </summary>
    public class PlcSignalMap
    {
        /// <summary>Camera identifier</summary>
        public string CameraId { get; set; } = string.Empty;

        /// <summary>Step index for multi-step inspection</summary>
        public int StepIndex { get; set; }

        // PLC -> VMS signals (read by VMS)

        /// <summary>Trigger bit: PLC sets ON to request inspection</summary>
        public string TriggerAddress { get; set; } = string.Empty;

        /// <summary>Ack bit: PLC sets ON to acknowledge result received</summary>
        public string AckAddress { get; set; } = string.Empty;

        // VMS -> PLC signals (written by VMS)

        /// <summary>Busy bit: VMS sets ON while processing</summary>
        public string BusyAddress { get; set; } = string.Empty;

        /// <summary>Complete bit: VMS sets ON when inspection is done</summary>
        public string CompleteAddress { get; set; } = string.Empty;

        /// <summary>Result OK bit: VMS sets ON if inspection passed</summary>
        public string ResultOkAddress { get; set; } = string.Empty;

        /// <summary>Result NG bit: VMS sets ON if inspection failed (optional)</summary>
        public string ResultNgAddress { get; set; } = string.Empty;

        /// <summary>Error bit: VMS sets ON if an error occurred</summary>
        public string ErrorAddress { get; set; } = string.Empty;

        /// <summary>Heartbeat word: VMS increments periodically (optional)</summary>
        public string HeartbeatAddress { get; set; } = string.Empty;

        /// <summary>Measurement result data addresses (optional, VMS -> PLC)</summary>
        public List<string> ResultDataAddresses { get; set; } = new();
    }
}
