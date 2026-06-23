using System;
using System.IO;
using System.Linq;
using PlatformLauncher.Core.Interfaces;

namespace PlatformLauncher.Infrastructure.Logging
{
    public class Logger : ILogger
    {
        private readonly string _logDirectory = "logs";
        private readonly string _logFileName = "launcher.log";
        private readonly int _maxFileSize = 1024 * 1024;
        private readonly int _maxBackupFiles = 5;
        private readonly object _lock = new object();

        public Logger()
        {
            Directory.CreateDirectory(_logDirectory);
        }

        private string GetLogFilePath()
        {
            string date = DateTime.Now.ToString("yyyy-MM-dd");
            return Path.Combine(_logDirectory, $"{_logFileName}_{date}.log");
        }

        private void RotateLogs()
        {
            var files = Directory.GetFiles(_logDirectory, $"{_logFileName}_*.log")
                                 .OrderBy(f => f)
                                 .ToList();
            while (files.Count > _maxBackupFiles)
            {
                try { File.Delete(files[0]); } catch { }
                files.RemoveAt(0);
            }
        }

        private void WriteLog(string level, string message)
        {
            try
            {
                lock (_lock)
                {
                    string path = GetLogFilePath();
                    string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {message}";
                    File.AppendAllText(path, line + Environment.NewLine);
                    var fi = new FileInfo(path);
                    if (fi.Length > _maxFileSize)
                    {
                        string backup = Path.Combine(_logDirectory, $"{_logFileName}_{DateTime.Now:yyyy-MM-dd}_{Guid.NewGuid()}.log");
                        File.Move(path, backup);
                    }
                    RotateLogs();
                }
            }
            catch { }
        }

        public void Info(string message) => WriteLog("INFO", message);
        public void Warning(string message) => WriteLog("WARN", message);
        public void Error(string message) => WriteLog("ERROR", message);
    }
}