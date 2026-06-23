using System;
using System.IO;

namespace PlatformLauncher
{
    public static class DebugLogger
    {
        private static readonly string LogFile = "debug.log";
        private static readonly object Lock = new object();

        public static void Write(string message)
        {
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
            Write($"{prefix}: {ex.GetType().Name} - {ex.Message}");
            if (ex.InnerException != null)
                Write($"Inner: {ex.InnerException.Message}");
        }

        public static void Info(string msg) => Write($"INFO: {msg}");
        public static void Warn(string msg) => Write($"WARN: {msg}");
        public static void Error(string msg) => Write($"ERROR: {msg}");
    }
}