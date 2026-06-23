using System.Collections.Generic;
using System.Threading.Tasks;

namespace PlatformLauncher.Core.Interfaces
{
    public interface IBackupManager
    {
        Task<string> CreateBackupAsync(string sourceListsPath, string gameId);
        Task<bool> RestoreBackupAsync(string targetListsPath, string backupDir);
        List<string> GetUnrestoredBackups();
        void MarkAsNoRestore(string backupDir);
        void MarkAsSaved(string backupDir);
        string GetLatestUnrestoredBackup();
    }
}