using System;
using System.IO;
using System.Linq;
using PlatformLauncher.Core.Interfaces;

namespace PlatformLauncher.Infrastructure.Logging
{
    /// <summary>
    /// Централизованная логирование для всей инфраструктуры — многопоточный ввод в log-файлы с автоматическим ротацией по размеру/времени.
    /// Узкое место: lock(_lock) на критической секции WriteLog() — блокирует все записи между строкой и записью на диск, при высокой нагрузке может стать узким местом.
    /// </summary>
    public class Logger : ILogger
    {
        private readonly string _logDirectory = "logs";
        private readonly string _logFileName = "launcher.log";
        private readonly int _maxFileSize = 1024 * 1024;
        private readonly int _maxBackupFiles = 5;
        private readonly object _lock = new object();

        /// <summary>Конструктор без DI — сложность 1 класса.</summary>
        public Logger()
        {
            Directory.CreateDirectory(_logDirectory);
        }

        /// <summary>Генерирует путь к файлу логи за день (например, logs/launcher.log_2024-01-15.log).</summary>
        private string GetLogFilePath()
        {
            string date = DateTime.Now.ToString("yyyy-MM-dd");
            return Path.Combine(_logDirectory, $"{_logFileName}_{date}.log");
        }

        /// <summary>Ротация логов: оставляет только _maxBackupFiles последних файлов.</summary>
        private void RotateLogs()
        {
            var files = Directory.GetFiles(_logDirectory, $"{_logFileName}_*.log")
                                 .OrderBy(f => f)
                                 .ToList();
            while (files.Count > _maxBackupFiles)
            {
                try { File.Delete(files[0]); }
                catch (Exception ex)
                {
                    DebugLogger.Warn($"Failed to delete old log {files[0]}: {ex.Message}");
                }
                files.RemoveAt(0);
            }
        }

        /// <summary>Поток обработки: Info/Warning/Error → WriteLog() → lock + AppendAllText + RotateLogs.</summary>
        private void WriteLog(string level, string message)
        {
            try
            {
                lock (_lock)
                {
                    // Критическая секция: блокирует запись на диск — узкое место при высокой нагрузке.
                    string path = GetLogFilePath();
                    int threadId = Environment.CurrentManagedThreadId;
                    string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] [T:{threadId}] {message}";
                    File.AppendAllText(path, line + Environment.NewLine);

                    // Ротация по размеру — каждый файл не превышает _maxFileSize (1 МБ).
                    var fi = new FileInfo(path);
                    if (fi.Length > _maxFileSize)
                    {
                        string backup = Path.Combine(_logDirectory, $"{_logFileName}_{DateTime.Now:yyyy-MM-dd}_{Guid.NewGuid()}.log");
                        File.Move(path, backup);
                    }
                    RotateLogs();
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Error($"Logger.WriteLog failed: {ex.Message}");
            }
        }

        public void Info(string message) => WriteLog("INFO", message);
        public void Warning(string message) => WriteLog("WARN", message);
        public void Error(string message) => WriteLog("ERROR", message);

        /// <summary>Глубокая рекурсия по стеку исключений — может вызвать StackOverflowException при очень глубоких стеках.</summary>
        public void Error(string message, Exception ex)
        {
            if (ex == null)
            {
                WriteLog("ERROR", $"{message}: exception is null");
                return;
            }
            WriteLog("ERROR", $"{message}: {ex.GetType().Name} - {ex.Message}");
            WriteLog("ERROR", $"  StackTrace: {ex.StackTrace}");
            if (ex.InnerException != null)
                Error("  Inner", ex.InnerException);
        }
    }
}