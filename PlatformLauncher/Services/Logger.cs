using System;
using System.IO;
using System.Linq;

public static class LauncherLogger
{
    private static readonly string LogDirectory = "logs";
    private static readonly string LogFileName = "launcher.log";
    private static readonly int MaxFileSize = 1024 * 1024;
    private static readonly int MaxBackupFiles = 5;
    private static readonly object _lock = new object();

    static LauncherLogger()
    {
        Directory.CreateDirectory(LogDirectory);
    }

    private static string GetLogFilePath()
    {
        // Используем дату в имени файла для ежедневной ротации
        string date = DateTime.Now.ToString("yyyy-MM-dd");
        return Path.Combine(LogDirectory, $"{LogFileName}_{date}.log");
    }

    private static void RotateLogs()
    {
        // удаляем старые файлы, если их больше MaxBackupFiles
        var files = Directory.GetFiles(LogDirectory, $"{LogFileName}_*.log")
                             .OrderBy(f => f)
                             .ToList();
        while (files.Count > MaxBackupFiles)
        {
            try { File.Delete(files[0]); } catch { }
            files.RemoveAt(0);
        }
    }

    public static void Info(string message) => WriteLog("INFO", message);
    public static void Warning(string message) => WriteLog("WARN", message);
    public static void Error(string message) => WriteLog("ERROR", message);

    private static void WriteLog(string level, string message)
    {
        try
        {
            lock (_lock)
            {
                string path = GetLogFilePath();
                string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {message}";
                File.AppendAllText(path, line + Environment.NewLine);
                var fi = new FileInfo(path);
                if (fi.Length > MaxFileSize)
                {
                    // переименовываем текущий файл, создаём новый
                    string backup = Path.Combine(LogDirectory, $"{LogFileName}_{DateTime.Now:yyyy-MM-dd}_{Guid.NewGuid()}.log");
                    File.Move(path, backup);
                }
                RotateLogs();
            }
        }
        catch { }
    }
}