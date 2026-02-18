using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;

namespace MCUScope.Services
{
    public enum LogLevel
    {
        Debug,
        Info,
        Warn,
        Error
    }

    public static class LogService
    {
        private static readonly object _lock = new();
        private static string _logFilePath = string.Empty;
        private static LogLevel _minLevel = LogLevel.Debug;

        public static event Action<LogLevel, string>? LogReceived;

        public static ObservableCollection<string> LogEntries { get; } = new();
        private const int MaxLogEntries = 500;

        public static void Initialize(string logDirectory)
        {
            if (!Directory.Exists(logDirectory))
                Directory.CreateDirectory(logDirectory);

            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            _logFilePath = Path.Combine(logDirectory, $"mcuscope_{timestamp}.log");
        }

        public static void SetMinLevel(LogLevel level) => _minLevel = level;

        public static void Debug(string message) => Log(LogLevel.Debug, message);
        public static void Info(string message) => Log(LogLevel.Info, message);
        public static void Warn(string message) => Log(LogLevel.Warn, message);
        public static void Error(string message) => Log(LogLevel.Error, message);
        public static void Error(string message, Exception ex) => Log(LogLevel.Error, $"{message}: {ex.Message}");

        public static void ClearLogEntries()
        {
            if (Application.Current != null)
                Application.Current.Dispatcher.Invoke(() => LogEntries.Clear());
            else
                LogEntries.Clear();
        }

        private static void Log(LogLevel level, string message)
        {
            if (level < _minLevel) return;

            string timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
            string line = $"[{timestamp}] [{level}] {message}";

            System.Diagnostics.Debug.WriteLine(line);
            LogReceived?.Invoke(level, line);

            // Add to UI-bound collection via dispatcher
            if (Application.Current != null)
            {
                Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    LogEntries.Add(line);
                    while (LogEntries.Count > MaxLogEntries)
                        LogEntries.RemoveAt(0);
                }));
            }

            if (!string.IsNullOrEmpty(_logFilePath))
            {
                lock (_lock)
                {
                    try
                    {
                        File.AppendAllText(_logFilePath, line + Environment.NewLine);
                    }
                    catch
                    {
                        // Cannot log failure to log
                    }
                }
            }
        }
    }
}
