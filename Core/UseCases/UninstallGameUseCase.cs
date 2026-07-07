using System;
using System.Threading.Tasks;
using PlatformLauncher.Domain.Models;
using PlatformLauncher.Core.Interfaces;

namespace PlatformLauncher.Core.UseCases
{
    public class UninstallGameUseCase
    {
        private readonly IGameInstallService _gameInstallService;
        private readonly IUpdateService _updateService;
        private readonly ILogger _logger;

        public UninstallGameUseCase(IGameInstallService gameInstallService, IUpdateService updateService, ILogger logger)
        {
            _gameInstallService = gameInstallService;
            _updateService = updateService;
            _logger = logger;
        }

        public async Task<(bool Success, string ErrorMessage)> ExecuteAsync(GamePreset preset, IProgress<string> progress)
        {
            try
            {
                var result = await _gameInstallService.UninstallGameAsync(preset, progress);
                if (result)
                {
                    var freshPresets = _updateService.LoadPresets();
                    var updated = freshPresets.Find(p => p.Id == preset.Id);
                    if (updated != null)
                        updated.Installed = false;
                    _updateService.SavePresetsFile(new PresetsFile { Games = freshPresets });
                }
                return (result, string.Empty);
            }
            catch (Exception ex)
            {
                _logger.Error($"Ошибка в UseCase удаления: {ex}");
                return (false, ex.Message);
            }
        }
    }
}