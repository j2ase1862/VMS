namespace VMS.Interfaces
{
    /// <summary>
    /// AutoProcess state machine states for PLC-triggered inspection
    /// </summary>
    public enum AutoProcessState
    {
        Idle,
        WaitTrigger,
        Grabbing,
        Inspecting,
        WritingResult,
        WaitAck,
        Error
    }

    /// <summary>
    /// Event args for AutoProcess state changes
    /// </summary>
    public class AutoProcessStateChangedEventArgs : EventArgs
    {
        public string CameraId { get; }
        public AutoProcessState OldState { get; }
        public AutoProcessState NewState { get; }

        public AutoProcessStateChangedEventArgs(string cameraId, AutoProcessState oldState, AutoProcessState newState)
        {
            CameraId = cameraId;
            OldState = oldState;
            NewState = newState;
        }
    }

    /// <summary>
    /// Service for PLC trigger-based automatic inspection process.
    /// Manages per-camera state machines that respond to PLC trigger signals.
    /// </summary>
    public interface IAutoProcessService
    {
        /// <summary>Whether the auto process is currently running</summary>
        bool IsRunning { get; }

        /// <summary>Fired when a camera channel changes state</summary>
        event EventHandler<AutoProcessStateChangedEventArgs>? StateChanged;

        /// <summary>Start the auto process loop for all configured cameras</summary>
        Task StartAsync(CancellationToken cancellationToken = default);

        /// <summary>Stop the auto process and clear all output signals</summary>
        Task StopAsync();

        /// <summary>Get the current state of a specific camera channel</summary>
        AutoProcessState GetCameraState(string cameraId);
    }
}
