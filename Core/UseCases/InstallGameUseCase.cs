using System;
using System.Threading.Tasks;
using PlatformLauncher.Domain.Models;
using PlatformLauncher.Core.Interfaces;

namespace PlatformLauncher.Core.UseCases
{
    public class InstallGameUseCase
    {
        private readonly IGameInstallService _gameInstallService;
        private readonly IUpdateService _updateService;
        private readonly ILogger _logger;

        public InstallGameUseCase(IGameInstallService gameInstallService, IUpdateService updateService, ILogger logger)
        {
            _gameInstallService = gameInstallService;
            _updateService = updateService;
            _logger = logger;
        }

        public async Task<(bool Success, string ErrorMessage, GamePreset UpdatedPreset)> ExecuteDownloadAsync(GamePreset preset, IProgress<string> progress)
        {
            try
            {
                var (success, error) = await _gameInstallService.DownloadConfigAsync(preset, progress);
                if (!success)
                    return (false, error, null);

                var freshPresets = _updateService.LoadPresets();
                var updated = freshPresets.Find(p => p.Id == preset.Id);
                if (updated != null)
                    updated.ConfigDownloaded = true;
                return (true, null, updated);
            }
            catch (Exception ex)
            {
                _logger.Error($"Ошибка в UseCase скачивания: {ex}");
                return (false, ex.Message, null);
            }
        }

        public async Task<(bool Success, string ErrorMessage, GamePreset UpdatedPreset)> ExecuteInstallAsync(GamePreset preset, IProgress<string> progress)
        {
            try
            {
                var result = await _gameInstallService.InstallGameAsync(preset, progress);
                if (!result.Success)
                    return (false, result.Error, null);

                var freshPresets = _updateService.LoadPresets();
                var updated = freshPresets.Find(p => p.Id == preset.Id);
                if (updated != null){
                    updated.Installed = true;
                    updated.ConfigDownloaded = true;
                }
                return (true, null, updated);
            }
            catch (Exception ex)
            {
                _logger.Error($"Ошибка в UseCase установки: {ex}");
                return (false, ex.Message, null);
            }
        }
    }
}