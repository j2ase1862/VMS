using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using VMS.Interfaces;

namespace VMS.Services
{
    public class SystemLogService : ISystemLogService
    {
        private static readonly Lazy<SystemLogService> _instance = new(() => new SystemLogService());
        public static SystemLogService Instance => _instance.Value;

        private const int MaxEntries = 200;
        private readonly Dispatcher _dispatcher;

        public ObservableCollection<LogEntry> LogEntries { get; } = new();

        private SystemLogService()
        {
            _dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        }

        public void Log(string message, LogLevel level = LogLevel.Info, string source = "")
        {
            var entry = new LogEntry
            {
                Timestamp = DateTime.Now,
                Level = level,
                Message = message,
                Source = source
            };

            if (_dispatcher.CheckAccess())
            {
                AddEntry(entry);
            }
            else
            {
                _dispatcher.Invoke(() => AddEntry(entry));
            }
        }

        public void Clear()
        {
            if (_dispatcher.CheckAccess())
            {
                LogEntries.Clear();
            }
            else
            {
                _dispatcher.Invoke(() => LogEntries.Clear());
            }
        }

        private void AddEntry(LogEntry entry)
        {
            LogEntries.Add(entry);
            while (LogEntries.Count > MaxEntries)
            {
                LogEntries.RemoveAt(0);
            }
        }
    }
}
