namespace VMS.PLC.Models
{
    /// <summary>
    /// Log severity levels for PLC communication
    /// </summary>
    public enum PlcLogLevel
    {
        Debug,
        Info,
        Warning,
        Error
    }

    /// <summary>
    /// Structured log entry for PLC communication events
    /// </summary>
    public class PlcLogEntry
    {
        public PlcLogLevel Level { get; }
        public string Message { get; }
        public DateTime Timestamp { get; }
        public Exception? Exception { get; }

        public PlcLogEntry(PlcLogLevel level, string message, Exception? exception = null)
        {
            Level = level;
            Message = message;
            Timestamp = DateTime.UtcNow;
            Exception = exception;
        }

        public override string ToString()
        {
            var ex = Exception != null ? $" [{Exception.GetType().Name}: {Exception.Message}]" : "";
            return $"[{Timestamp:HH:mm:ss.fff}] [{Level}] {Message}{ex}";
        }
    }
}
