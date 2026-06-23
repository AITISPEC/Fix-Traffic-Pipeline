using System;
using System.Threading.Tasks;
using PlatformLauncher.Domain.Models;
using PlatformLauncher.Core.Interfaces;

namespace PlatformLauncher.Infrastructure.Services
{
    public class GameInstallService : IGameInstallService
    {
        private readonly IUpdateService _updateService;
        private readonly IPortsManager _portsManager;
        private readonly ILogger _logger;

        public GameInstallService(IUpdateService updateService, IPortsManager portsManager, ILogger logger)
        {
            _updateService = updateService;
            _portsManager = portsManager;
            _logger = logger;
        }

        public async Task<(bool Success, string Error)> InstallGameAsync(GamePreset preset, IProgress<string> progress)
        {
            try
            {
                progress?.Report($"⏳ Установка {preset.Name}...");
                var (success, error) = await _updateService.InstallGameAsync(preset);
                if (!success)
                    return (false, error);

                progress?.Report($"✅ Конфиг скачан");

                var config = _updateService.LoadGameConfig(preset.Id);
                if (config?.Ports != null)
                {
                    progress?.Report("📌 Установка правил портов...");
                    var (portOk, portError) = await _portsManager.AddRulesAsync(
                        config.Ports.Tcp,
                        config.Ports.Udp,
                        preset.Id,
                        progress.Report);
                    if (portOk)
                        progress?.Report("✅ Правила портов добавлены");
                    else
                        progress?.Report($"❌ Ошибка добавления правил портов: {portError}");
                }
                else
                {
                    progress?.Report("ℹ️ В конфиге нет портов");
                }

                return (true, null);
            }
            catch (Exception ex)
            {
                _logger.Error($"Ошибка установки: {ex}");
                return (false, ex.Message);
            }
        }

        public async Task<bool> UninstallGameAsync(GamePreset preset, IProgress<string> progress)
        {
            progress?.Report("📌 Удаление правил портов...");
            var (removed, removeError) = await _portsManager.RemoveAllRulesAsync(preset.Id, progress.Report);
            if (removed)
                progress?.Report("✅ Правила портов удалены");
            else
                progress?.Report($"❌ Ошибка удаления правил портов: {removeError}");

            _updateService.UninstallGame(preset.Id);
            return true;
        }
    }
}