using System.Collections.Generic;
using System.Threading.Tasks;
using PlatformLauncher.Domain.Models;

namespace PlatformLauncher.Core.Interfaces
{
    public interface IUpdateService
    {
        Task<bool> SyncFromGitHubAsync();
        List<GamePreset> LoadPresets();
        void SavePresetsFile(PresetsFile presets);
        Task<(bool Success, string ErrorMessage)> InstallGameAsync(GamePreset preset);
        void UninstallGame(string gameId);
        GameConfig LoadGameConfig(string gameId);
    }
}