using System;
using System.Threading.Tasks;
using PlatformLauncher.Models;

namespace PlatformLauncher.Services
{
    public static class GameInstallService
    {
        public static async Task<(bool Success, string Error)> InstallGameAsync(GamePreset preset, IProgress<string> progress)
        {
            try
            {
                progress?.Report($"⏳ Установка {preset.Name}...");
                var (success, error) = await UpdateService.InstallGameAsync(preset);
                if (!success)
                    return (false, error);

                progress?.Report($"✅ Конфиг скачан");

                var config = UpdateService.LoadGameConfig(preset.Id);
                if (config?.Ports != null)
                {
                    progress?.Report("📌 Установка правил портов...");
                    var pm = new PortsManager(preset.Id);
                    var (portOk, portError) = await pm.AddRulesAsync(config.Ports.Tcp, config.Ports.Udp);
                    if (portOk)
                        progress?.Report("✅ Правила портов добавлены");
                    else
                        progress?.Report($"❌ Ошибка добавления правил портов: {portError}");
                }
                else
                {
                    progress?.Report("ℹ️ В конфиге нет портов");
                }

                // Обновить пресеты
                var freshPresets = UpdateService.LoadPresets();
                // (обновление UI делается снаружи)

                return (true, null);
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        public static async Task<bool> UninstallGameAsync(GamePreset preset, IProgress<string> progress)
        {
            progress?.Report("📌 Удаление правил портов...");
            var pm = new PortsManager(preset.Id);
            var (removed, removeError) = await pm.RemoveAllRulesAsync();
            if (removed)
                progress?.Report("✅ Правила портов удалены");
            else
                progress?.Report($"❌ Ошибка удаления правил портов: {removeError}");

            UpdateService.UninstallGame(preset.Id);
            return true;
        }
    }
}