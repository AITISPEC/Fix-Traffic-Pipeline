using System;
using System.IO;
using System.Linq;

namespace PlatformLauncher.Services
{
    public static class BackupManager
    {
        public static string GetLatestBackupForAnyGame(string backupRoot)
        {
            if (!Directory.Exists(backupRoot)) return null;
            var dirs = Directory.GetDirectories(backupRoot, "lists_*", SearchOption.AllDirectories);
            var sorted = dirs.OrderByDescending(d => new DirectoryInfo(d).CreationTime);
            foreach (var dir in sorted)
            {
                string markerRestored = Path.Combine(dir, ".restored");
                string markerNotRestored = Path.Combine(dir, ".notrestored");
                if (!File.Exists(markerRestored) && !File.Exists(markerNotRestored))
                    return dir;
            }
            return null;
        }

        public static void MarkBackupAsNotRestored(string backupDir)
        {
            try
            {
                string marker = Path.Combine(backupDir, ".notrestored");
                File.WriteAllText(marker, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            }
            catch (Exception ex)
            {
                LauncherLogger.Error($"Не удалось создать маркер .notrestored: {ex.Message}");
                throw; // пробросим, чтобы увидеть в логе
            }
        }
        public static void RestoreBackup(string targetListsPath, string backupDir)
        {
            if (!Directory.Exists(backupDir)) throw new DirectoryNotFoundException($"Бэкап {backupDir} не найден");

            if (Directory.Exists(targetListsPath))
            {
                foreach (var file in Directory.GetFiles(targetListsPath))
                    File.Delete(file);
                foreach (var dir in Directory.GetDirectories(targetListsPath))
                    Directory.Delete(dir, true);
            }
            else
            {
                Directory.CreateDirectory(targetListsPath);
            }

            foreach (var file in Directory.GetFiles(backupDir))
            {
                string dest = Path.Combine(targetListsPath, Path.GetFileName(file));
                File.Copy(file, dest, true);
            }
            foreach (var dir in Directory.GetDirectories(backupDir))
            {
                string destDir = Path.Combine(targetListsPath, Path.GetFileName(dir));
                CopyDirectory(dir, destDir);
            }

            string marker = Path.Combine(backupDir, ".restored");
            File.WriteAllText(marker, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        }

        private static void CopyDirectory(string source, string dest)
        {
            Directory.CreateDirectory(dest);
            foreach (var file in Directory.GetFiles(source))
                File.Copy(file, Path.Combine(dest, Path.GetFileName(file)), true);
            foreach (var dir in Directory.GetDirectories(source))
                CopyDirectory(dir, Path.Combine(dest, Path.GetFileName(dir)));
        }
    }
}