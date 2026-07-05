/// <summary>
/// Устанавливает, удаляет и скачивает конфигурационные файлы игр (presets) для интеграции с zapret.
/// Узкое место: сериализация YAML через DeserializerBuilder — не использует кэш, каждый запрос десериализует заново.
/// </summary>

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using PlatformLauncher.Domain.Models;
using PlatformLauncher.Core.Interfaces;

namespace PlatformLauncher.Infrastructure.Services
{
    /// <summary>Установка игры состоит из 3 шагов: 1) скачать YAML конфиг → zapret/lists/, 2) добавить TCP/UDP правила портов, 3) записать Installed=true в presets.yaml.</summary>
    /// <remarks>Поток обработки: MainViewModel.InstallCommand → InstallGameUseCase.ExecuteInstallAsync() → IGameInstallService.InstallGameAsync().</remarks>
    public class GameInstallService : IGameInstallService
    {
        private readonly IUpdateService _updateService;
        private readonly IPortsManager _portsManager;
        private readonly ILogger _logger;

        /// <summary>Конструктор через DI. Вводит зависимости (сложность 3 класса).</summary>
        public GameInstallService(IUpdateService updateService, IPortsManager portsManager, ILogger logger)
        {
            _updateService = updateService;
            _portsManager = portsManager;
            _logger = logger;
        }

        /// <summary>Скачивает конфиг из remote.ConfigUrl в local/data/configs/{presetId}.yaml. Возвращает (Success, Error).</summary>
        public async Task<(bool Success, string Error)> DownloadConfigAsync(GamePreset preset, IProgress<string> progress)
        {
            try
            {
                // Прогресс-отчёт через Report(IProgress) — работает асинхронно без блокировки UI.
                progress?.Report($"⏳ Скачивание конфига {preset.Name}...");

                var (success, error) = await _updateService.InstallGameAsync(preset);
                if (!success)
                    return (false, error); // early return — не влезает в catch

                progress?.Report($"✅ Конфиг скачан");
                return (true, null);
            }
            catch (Exception ex)
            {
                // Не скрываем стектрейс, чтобы отладка была быстрой.
                _logger.Error($"Ошибка скачивания конфига: {ex}");
                return (false, ex.Message);
            }
        }

        /// <summary>Установка пресета: 1) поиск config в data/configs/{presetId}.yaml, 2) установка TCP/UDP правил портов, 3) запись Installed=true.</summary>
        public async Task<(bool Success, string Error, GamePreset UpdatedPreset)> InstallGameAsync(GamePreset preset, IProgress<string> progress)
        {
            try
            {
                progress?.Report($"⏳ Установка {preset.Name}...");

                // Скачиваем конфиг (включая monitor.yaml)
                var config = _updateService.LoadGameConfig(preset.Id);
                if (config == null)
                {
                    // Конфиг отсутствует или невалиден — скачиваем
                    var (downloadSuccess, downloadError) = await _updateService.InstallGameAsync(preset);
                    if (!downloadSuccess)
                        return (false, downloadError, null);

                    // Перезагружаем конфиг после скачивания
                    config = _updateService.LoadGameConfig(preset.Id);
                    if (config == null)
                        throw new Exception("Конфигурация не найдена после скачивания");

                    progress?.Report($"✅ Конфиг {preset.Name} скачан");
                }

                // Для monitor.yaml установка правил портов не требуется
                if (preset.Id == "monitor")
                {
                    progress?.Report("✅ Мониторинг готов к запуску (конфигурация найдена)");

                    // Обновляем статус в presets.yaml
                    var updatedPresets = _updateService.LoadPresets();
                    foreach (var p in updatedPresets)
                    {
                        if (p.Id == preset.Id)
                        {
                            p.Installed = true;
                            p.ConfigDownloaded = true;
                            break;
                        }
                    }
                    _updateService.SavePresetsFile(new PresetsFile { Games = updatedPresets });

                    return (true, null, preset);
                }

                // Установка правил портов для обычных игр
                if (config.Ports != null)
                {
                    progress?.Report("📌 Установка правил портов...");
                    var (portOk, portError) = await _portsManager.AddRulesAsync(
                        config.Ports.Tcp,
                        config.Ports.Udp,
                        preset.Id,
                        msg => progress.Report(msg));

                    if (portOk)
                        progress?.Report("✅ Правила портов добавлены");
                    else
                        progress?.Report($"❌ Ошибка добавления правил портов: {portError}");
                }
                else
                {
                    progress?.Report("ℹ️ В конфиге нет портов");
                }

                // Обновляем статус пресета
                var presets = _updateService.LoadPresets();
                foreach (var p in presets)
                {
                    if (p.Id == preset.Id)
                    {
                        p.Installed = true;
                        p.ConfigDownloaded = true;
                        break;
                    }
                }
                _updateService.SavePresetsFile(new PresetsFile { Games = presets });

                return (true, null, preset);
            }
            catch (Exception ex)
            {
                _logger.Error($"Ошибка установки: {ex}");
                return (false, ex.Message, null);
            }
        }

        /// <summary>Удаляет TCP/UDP правила портов и ставит Installed=false в presets.yaml.</summary>
        public async Task<bool> UninstallGameAsync(GamePreset preset, IProgress<string> progress)
        {
            // Прогресс отчёт через Report(IProgress) — не блокирует UI.
            progress?.Report("📌 Удаление правил портов...");

            var (removed, removeError) = await _portsManager.RemoveAllRulesAsync(preset.Id, msg => progress.Report(msg));
            if (removed)
                progress?.Report("✅ Правила портов удалены");
            else
                progress?.Report($"❌ Ошибка удаления правил портов: {removeError}");

            // Убираем Installed=false из presets.yaml — UI сразу отобразит "не установлен".
            _updateService.UninstallGame(preset.Id);

            return true;
        }
    }
}