using System;
using System.IO;
using System.Linq;

namespace PlatformLauncher.Services
{
    public static class LauncherLogger
    {
        private static readonly string LogDirectory = "logs";
        private static readonly string LogFilePath = Path.Combine(LogDirectory, "launcher.log");
        private static readonly int MaxFileSize = 1024 * 1024; // 1 MB
        private static readonly int MaxBackupFiles = 5;

        static LauncherLogger()
        {
            try
            {
                Directory.CreateDirectory(LogDirectory);
                RotateLogs();
            }
            catch { }
        }

        private static void RotateLogs()
        {
            if (!File.Exists(LogFilePath)) return;
            var fi = new FileInfo(LogFilePath);
            if (fi.Length < MaxFileSize) return;

            for (int i = MaxBackupFiles - 1; i >= 1; i--)
            {
                string backup = Path.Combine(LogDirectory, $"launcher.log.{i}");
                string nextBackup = Path.Combine(LogDirectory, $"launcher.log.{i + 1}");
                if (File.Exists(backup))
                {
                    if (File.Exists(nextBackup)) File.Delete(nextBackup);
                    File.Move(backup, nextBackup);
                }
            }
            File.Move(LogFilePath, Path.Combine(LogDirectory, "launcher.log.1"));
        }

        public static void Info(string message) => WriteLog("INFO", message);
        public static void Warning(string message) => WriteLog("WARN", message);
        public static void Error(string message) => WriteLog("ERROR", message);

        private static void WriteLog(string level, string message)
        {
            try
            {
                string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {message}";
                File.AppendAllText(LogFilePath, line + Environment.NewLine);
                if (new FileInfo(LogFilePath).Length > MaxFileSize)
                    RotateLogs();
            }
            catch { }
        }
    }
}