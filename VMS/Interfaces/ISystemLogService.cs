using System.Collections.ObjectModel;

namespace VMS.Interfaces
{
    public enum LogLevel
    {
        Info,
        Warning,
        Error,
        Success
    }

    public class LogEntry
    {
        public DateTime Timestamp { get; set; }
        public LogLevel Level { get; set; }
        public string Message { get; set; } = string.Empty;
        public string Source { get; set; } = string.Empty;
    }

    public interface ISystemLogService
    {
        ObservableCollection<LogEntry> LogEntries { get; }
        void Log(string message, LogLevel level = LogLevel.Info, string source = "");
        void Clear();
    }
}
