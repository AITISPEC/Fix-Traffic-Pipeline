using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace PlatformLauncher.Services
{
    public class BackupManager
    {
        private readonly string _backupRoot;
        private readonly string _gameId;
        private readonly int _maxBackups;

        public BackupManager(string backupRoot, string gameId, int maxBackups = 10)
        {
            _backupRoot = Path.Combine(backupRoot, gameId);
            _gameId = gameId;
            _maxBackups = maxBackups;
            Directory.CreateDirectory(_backupRoot);
        }

        public async Task<string> CreateBackupAsync(string sourceListsPath)
        {
            if (!Directory.Exists(sourceListsPath))
                throw new DirectoryNotFoundException($"Source lists folder not found: {sourceListsPath}");

            string timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
            string backupDir = Path.Combine(_backupRoot, $"lists_{timestamp}");
            Directory.CreateDirectory(backupDir);
            await Task.Run(() => CopyDirectorySafe(sourceListsPath, backupDir));
            PruneBackups();
            return backupDir;
        }

        public async Task<bool> RestoreBackupAsync(string targetListsPath, string backupDir)
        {
            if (!Directory.Exists(backupDir))
                return false;

            // Если бэкап уже восстановлен – пропускаем
            if (File.Exists(Path.Combine(backupDir, ".restored")))
            {
                LauncherLogger.Info($"Бэкап {backupDir} уже восстановлен, повторный вызов игнорируется");
                return true;
            }

            // Если пользователь отметил "не восстанавливать" – пропускаем
            if (File.Exists(Path.Combine(backupDir, ".norestored")))
            {
                LauncherLogger.Info($"Бэкап {backupDir} помечен как невосстанавливаемый");
                return true;
            }

            if (Directory.Exists(targetListsPath))
            {
                try
                {
                    Directory.Delete(targetListsPath, true);
                }
                catch (Exception ex)
                {
                    LauncherLogger.Error($"Не удалось удалить {targetListsPath}: {ex.Message}");
                    return false;
                }
            }
            Directory.CreateDirectory(targetListsPath);

            await Task.Run(() => CopyDirectorySafe(backupDir, targetListsPath));

            File.WriteAllText(Path.Combine(backupDir, ".restored"), "");
            return true;
        }

        public List<string> GetUnrestoredBackups()
        {
            var dirs = GetBackupDirs();
            var result = new List<string>();
            foreach (var dir in dirs)
            {
                bool hasRestored = File.Exists(Path.Combine(dir, ".restored"));
                bool hasNoRestored = File.Exists(Path.Combine(dir, ".norestored"));
                if (!hasRestored && !hasNoRestored)
                    result.Add(dir);
            }
            return result;
        }

        public void MarkAsNoRestore(string backupDir)
        {
            File.WriteAllText(Path.Combine(backupDir, ".norestored"), "");
        }

        public string GetLatestUnrestoredBackup()
        {
            var backups = GetBackupDirs();
            foreach (string dir in backups.OrderByDescending(d => d))
            {
                if (!IsBackupRestored(dir) && !IsBackupNoRestored(dir))
                    return dir;
            }
            return null;
        }

        private List<string> GetBackupDirs()
        {
            return Directory.GetDirectories(_backupRoot, "lists_*")
                .Where(d => Directory.Exists(d))
                .ToList();
        }

        private bool IsBackupRestored(string backupDir)
        {
            return File.Exists(Path.Combine(backupDir, ".restored"));
        }

        private bool IsBackupNoRestored(string backupDir)
        {
            return File.Exists(Path.Combine(backupDir, ".norestored"));
        }

        private void PruneBackups()
        {
            var dirs = GetBackupDirs().OrderBy(d => d).ToList();
            while (dirs.Count > _maxBackups)
            {
                try { Directory.Delete(dirs[0], true); } catch { }
                dirs.RemoveAt(0);
            }
        }

        private void CopyDirectorySafe(string source, string destination)
        {
            foreach (string dirPath in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(source, dirPath);
                string newDir = Path.Combine(destination, relative);
                Directory.CreateDirectory(newDir);
            }

            foreach (string filePath in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            {
                if (File.Exists(filePath))
                {
                    string relative = Path.GetRelativePath(source, filePath);
                    string newFile = Path.Combine(destination, relative);
                    try
                    {
                        string dir = Path.GetDirectoryName(newFile);
                        if (!string.IsNullOrEmpty(dir))
                            Directory.CreateDirectory(dir);
                        File.Copy(filePath, newFile, true);
                    }
                    catch (Exception ex)
                    {
                        LauncherLogger.Warning($"Не удалось скопировать {filePath}: {ex.Message}");
                    }
                }
            }
        }
    }
}