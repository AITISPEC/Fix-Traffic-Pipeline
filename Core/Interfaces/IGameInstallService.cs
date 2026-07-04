using System;
using System.Threading.Tasks;
using PlatformLauncher.Domain.Models;

namespace PlatformLauncher.Core.Interfaces
{
    public interface IGameInstallService
    {
        Task<(bool Success, string Error)> DownloadConfigAsync(GamePreset preset, IProgress<string> progress);
        Task<(bool Success, string Error, GamePreset UpdatedPreset)> InstallGameAsync(GamePreset preset, IProgress<string> progress);
        Task<bool> UninstallGameAsync(GamePreset preset, IProgress<string> progress);
    }
}