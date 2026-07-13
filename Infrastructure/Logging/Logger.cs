using System;
using System.IO;
using PlatformLauncher.Core.Interfaces;

namespace PlatformLauncher.Infrastructure.Logging
{
    public class Logger : ILogger
    {
        private readonly string _logPath;

        public Logger()
        {
            try
            {
                Directory.CreateDirectory("logs");
                _logPath = Path.Combine("logs", $"launcher_{DateTime.Now:yyyy-MM-dd}.log");
                // Пишем в debug.log, что логгер создан
                DebugLogger.Info($"Logger создан, файл: {_logPath}");
            }
            catch (Exception ex)
            {
                DebugLogger.Error($"Logger: ошибка создания: {ex.Message}");
                _logPath = Path.Combine(Path.GetTempPath(), $"launcher_{DateTime.Now:yyyy-MM-dd}.log");
                DebugLogger.Info($"Logger использует временный файл: {_logPath}");
            }
        }

        public void Info(string message) => Write("INFO", message);
        public void Warning(string message) => Write("WARN", message);
        public void Error(string message) => Write("ERROR", message);

        public void Error(string message, Exception ex)
        {
            Write("ERROR", $"{message}: {ex.GetType().Name} - {ex.Message}");
            Write("ERROR", $"  StackTrace: {ex.StackTrace}");
            if (ex.InnerException != null)
                Error("  Inner", ex.InnerException);
        }

        private void Write(string level, string message)
        {
            try
            {
                string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] [T:{Environment.CurrentManagedThreadId}] {message}";
                File.AppendAllText(_logPath, line + Environment.NewLine);
            }
            catch (Exception ex)
            {
                DebugLogger.Error($"Logger: запись не удалась: {ex.Message}");
            }
        }
    }
}