using System;
using System.Collections.Generic;
using Debug = System.Diagnostics.Debug;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using PlatformLauncher.Core.Interfaces;


namespace PlatformLauncher.Infrastructure.Backup
{
    public class BackupManager : IBackupManager
    {
        private readonly ILogger _logger;
        private readonly string _backupRoot;
        private readonly int _maxBackups;

        /// <summary>Конструктор через DI — вводит зависимости (сложность 3 класса).</summary>
        public BackupManager(ILogger logger, string backupRoot, int maxBackups = 10)
        {
            _logger = logger;
            _backupRoot = backupRoot;
            _maxBackups = maxBackups;
        }

        /// <summary>Файл-маркер для UI — пользователь подтвердил корректность бэкапа.</summary>
        public void MarkAsSaved(string backupDir)
        {
            File.WriteAllText(Path.Combine(backupDir, ".saved"), "");
        }

        /// <summary>Асинхронное создание бэкапа: копирует папку lists в ./backup/{gameId}/lists_{timestamp}/.</summary>
        public async Task<string> CreateBackupAsync(string sourceListsPath, string gameId)
        {
            if (!Directory.Exists(sourceListsPath))
                throw new DirectoryNotFoundException($"Source lists folder not found: {sourceListsPath}");

            string gameBackupRoot = Path.Combine(_backupRoot, gameId);
            Directory.CreateDirectory(gameBackupRoot);

            string timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
            string backupDir = Path.Combine(gameBackupRoot, $"lists_{timestamp}");
            Directory.CreateDirectory(backupDir);

            await Task.Run(() => CopyDirectorySafe(sourceListsPath, backupDir));
            PruneBackups(gameBackupRoot);
            return backupDir;
        }

        /// <summary>Атомарное восстановление бэкапа: удаляет targetListsPath → создаёт новую папку → копирует содержимое.</summary>
        public async Task<bool> RestoreBackupAsync(string targetListsPath, string backupDir)
        {
            if (!Directory.Exists(backupDir))
                return false;

            if (File.Exists(Path.Combine(backupDir, ".restored")))
            {
                _logger.Info($"Бэкап {backupDir} уже восстановлен, повторный вызов игнорируется");
                return true;
            }

            if (File.Exists(Path.Combine(backupDir, ".norestored")))
            {
                _logger.Info($"Бэкап {backupDir} помечен .norestored");
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
                    _logger.Error($"Не удалось удалить {targetListsPath}: {ex.Message}");
                    return false;
                }
            }
            Directory.CreateDirectory(targetListsPath);

            await Task.Run(() => CopyDirectorySafe(backupDir, targetListsPath));

            File.WriteAllText(Path.Combine(backupDir, ".restored"), "");
            return true;
        }

        /// <summary>Возвращает все бэкапы из backupRoot, которые ещё не использованы.</summary>
        public List<string> GetUnrestoredBackups()
        {
            var dirs = GetBackupDirs();
            var result = new List<string>();
            foreach (var dir in dirs)
            {
                bool hasRestored = File.Exists(Path.Combine(dir, ".restored"));
                bool hasNoRestored = File.Exists(Path.Combine(dir, ".norestored"));
                bool hasSaved = File.Exists(Path.Combine(dir, ".saved"));
                if (!hasRestored && !hasNoRestored && !hasSaved)
                    result.Add(dir);
            }
            return result;
        }

        public void MarkAsNoRestore(string backupDir)
        {
            File.WriteAllText(Path.Combine(backupDir, ".norestored"), "");
        }

        /// <summary>Возвращает путь к первому (самому старому) свободному бэкапу — для автовосстановления при старте.</summary>
        public string GetLatestUnrestoredBackup()
        {
            var backups = GetBackupDirs();
            foreach (string dir in backups.OrderByDescending(d => d))
            {
                if (!IsBackupRestored(dir) && !IsBackupNoRestored(dir) && !IsBackupSaved(dir))
                    return dir;
            }
            return null;
        }

        private bool IsBackupSaved(string backupDir)
        {
            return File.Exists(Path.Combine(backupDir, ".saved"));
        }

        /// <summary>Находит все папки с бэкапами в формате lists_* внутри backupRoot.</summary>
        private List<string> GetBackupDirs()
        {
            try
            {
                if (!Directory.Exists(_backupRoot))
                {
                    DebugLogger.Debug($"Папка бэкапов не существует: {_backupRoot}");
                    return new List<string>();
                }

                return Directory.GetDirectories(_backupRoot, "lists_*", SearchOption.AllDirectories)
                    .Where(d => Directory.Exists(d))
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.Warning($"Ошибка получения списка бэкапов: {ex.Message}");
                return new List<string>();
            }
        }

        private bool IsBackupRestored(string backupDir)
        {
            return File.Exists(Path.Combine(backupDir, ".restored"));
        }

        private bool IsBackupNoRestored(string backupDir)
        {
            return File.Exists(Path.Combine(backupDir, ".norestored"));
        }

        /// <summary>Оставляет только _maxBackups последних — старые удаляет по возрастанию.</summary>
        private void PruneBackups(string backupRoot)
        {
            var dirs = Directory.GetDirectories(backupRoot, "lists_*")
                                .OrderBy(d => d)
                                .ToList();
            while (dirs.Count > _maxBackups)
            {
                try { Directory.Delete(dirs[0], true); }
                catch (Exception ex)
                {
                    _logger.Warning($"Не удалось удалить старый бэкап {dirs[0]}: {ex.Message}");
                }
                dirs.RemoveAt(0);
            }
        }

        /// <summary>Копирует файлы через Process.Start — работает на любом OS.</summary>
        private void CopyDirectorySafe(string source, string destination)
        {
            // Служебные файлы, которые не должны копироваться
            var excludedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".restored",
                ".norestored",
                ".saved"
            };

            foreach (string dirPath in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(source, dirPath);
                string newDir = Path.Combine(destination, relative);
                Directory.CreateDirectory(newDir);
            }

            foreach (string filePath in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            {
                string fileName = Path.GetFileName(filePath);

                // Пропускаем служебные файлы
                if (excludedFiles.Contains(fileName))
                    continue;

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
                        _logger.Warning($"Не удалось скопировать {filePath}: {ex.Message}");
                    }
                }
            }
        }
    }
}
