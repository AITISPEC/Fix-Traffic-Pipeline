using System;
using System.IO;

namespace PlatformLauncher
{
    public static class DebugLogger
    {
        private static readonly string LogFile = "debug.log";
        private static readonly object Lock = new object();
        private static bool _enabled = false;

        public static void SetEnabled(bool enabled)
        {
            _enabled = enabled;
        }

        public static bool IsEnabled => _enabled;

        public static void Write(string message)
        {
            bool isError = message.StartsWith("ERROR:", StringComparison.OrdinalIgnoreCase);
            if (!_enabled && !isError) return;

            try
            {
                lock (Lock)
                {
                    File.AppendAllText(LogFile, $"{DateTime.Now:HH:mm:ss.fff} [{Environment.CurrentManagedThreadId}] {message}{Environment.NewLine}");
                }
            }
            catch { }
        }

        public static void WriteException(string prefix, Exception ex)
        {
            if (!_enabled) return;
            if (ex == null)
            {
                Write($"ERROR: {prefix}: exception is null");
                return;
            }
            Write($"ERROR: {prefix}: {ex.GetType().Name} - {ex.Message}");
            Write($"  StackTrace: {ex.StackTrace}");
            if (ex.InnerException != null)
                WriteException("  Inner", ex.InnerException);
        }

        public static void Info(string msg) => Write($"INFO: {msg}");
        public static void Warn(string msg) => Write($"WARN: {msg}");
        public static void Error(string msg) => Write($"ERROR: {msg}");
        public static void Debug(string msg) => Write($"DEBUG: {msg}");
    }
}