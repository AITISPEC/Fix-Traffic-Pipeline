using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using PlatformLauncher.Domain.Models;
using PlatformLauncher.Core.Interfaces;
using YamlDotNet.Serialization;

namespace PlatformLauncher.Infrastructure.Services
{
    public class UpdateService : IUpdateService
    {
        private readonly ILogger _logger;
        private readonly HttpClient _httpClient;
        private readonly string _presetsPath;
        private readonly string _configsFolder;
        private const string RemotePresetsUrl = "https://raw.githubusercontent.com/AITISPEC/Fix-Traffic-Pipeline/main/data/configs/presets.yaml";

        public UpdateService(ILogger logger)
        {
            _logger = logger;
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "PlatformLauncher/1.0");
            _presetsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "configs", "presets.yaml");
            _configsFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "configs");
        }

        public PresetsFile LoadPresetsFile()
        {
            if (!File.Exists(_presetsPath))
                return new PresetsFile { Games = new List<GamePreset>() };
            try
            {
                var yaml = File.ReadAllText(_presetsPath);
                var deserializer = new DeserializerBuilder().Build();
                return deserializer.Deserialize<PresetsFile>(yaml) ?? new PresetsFile();
            }
            catch (Exception ex)
            {
                _logger.Error($"Ошибка загрузки presets.yaml: {ex.Message}");
                return new PresetsFile();
            }
        }

        public void SavePresetsFile(PresetsFile presets)
        {
            try
            {
                var serializer = new SerializerBuilder().Build();
                var yaml = serializer.Serialize(presets);
                string dir = Path.GetDirectoryName(_presetsPath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(_presetsPath, yaml);
                _logger.Info($"presets.yaml сохранён ({presets.Games.Count} игр)");
            }
            catch (Exception ex)
            {
                _logger.Error($"Ошибка сохранения presets.yaml: {ex.Message}");
            }
        }

        public List<GamePreset> LoadPresets()
        {
            var allGames = new List<GamePreset>();
            var presetsFile = LoadPresetsFile();
            allGames.AddRange(presetsFile.Games);

            if (Directory.Exists(_configsFolder))
            {
                var localFiles = Directory.GetFiles(_configsFolder, "*.yaml")
                                           .Where(f => !f.EndsWith("presets.yaml"))
                                           .Select(Path.GetFileNameWithoutExtension)
                                           .Where(id => !string.IsNullOrEmpty(id));
                foreach (var id in localFiles)
                {
                    if (!allGames.Any(g => g.Id == id))
                    {
                        var config = LoadGameConfig(id);
                        if (config != null)
                        {
                            var preset = new GamePreset
                            {
                                Id = id,
                                Name = id,
                                ConfigUrl = null,
                                WarpSupported = config.WarpSupported ?? false,
                                Version = config.Version ?? 1,
                                Installed = true
                            };
                            allGames.Add(preset);
                        }
                    }
                }
            }
            return allGames;
        }

        public async Task<bool> SyncFromGitHubAsync()
        {
            try
            {
                _logger.Info("Начинаем синхронизацию пресетов (загрузка presets.yaml)...");
                using var response = await _httpClient.GetAsync(RemotePresetsUrl);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.Error($"Ошибка загрузки presets.yaml: {response.StatusCode}");
                    return false;
                }

                var remoteYaml = await response.Content.ReadAsStringAsync();
                var deserializer = new DeserializerBuilder().Build();
                PresetsFile remotePresets;
                try
                {
                    remotePresets = deserializer.Deserialize<PresetsFile>(remoteYaml);
                }
                catch (Exception ex)
                {
                    _logger.Error($"Ошибка десериализации удалённого presets.yaml: {ex.Message}");
                    return false;
                }

                if (remotePresets?.Games == null || remotePresets.Games.Count == 0)
                {
                    _logger.Warning("Удалённый presets.yaml не содержит списка игр или пуст. Локальные пресеты не изменены.");
                    return true;
                }

                var localPresets = LoadPresetsFile();
                var localDict = localPresets.Games.ToDictionary(g => g.Id);

                foreach (var remoteGame in remotePresets.Games)
                {
                    if (localDict.TryGetValue(remoteGame.Id, out var localGame))
                    {
                        bool wasInstalled = localGame.Installed;
                        localGame.Name = remoteGame.Name;
                        localGame.ConfigUrl = remoteGame.ConfigUrl;
                        localGame.WarpSupported = remoteGame.WarpSupported;
                        localGame.Version = remoteGame.Version;
                        localGame.Installed = wasInstalled;
                    }
                    else
                    {
                        remoteGame.Installed = false;
                        localPresets.Games.Add(remoteGame);
                    }
                }

                SavePresetsFile(localPresets);
                _logger.Info("Синхронизация завершена, presets.yaml обновлён");
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error($"Ошибка синхронизации: {ex.Message}");
                return false;
            }
        }

        public async Task<(bool Success, string ErrorMessage)> InstallGameAsync(GamePreset preset)
        {
            try
            {
                _logger.Info($"Начало установки {preset.Id}");
                string configDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "configs");
                if (!Directory.Exists(configDir)) Directory.CreateDirectory(configDir);
                string localConfigPath = Path.Combine(configDir, $"{preset.Id}.yaml");

                if (string.IsNullOrEmpty(preset.ConfigUrl))
                {
                    _logger.Warning($"ConfigUrl для {preset.Id} пуст, отмечаем как установленный без загрузки");
                    var presetsFile = LoadPresetsFile();
                    var localPreset = presetsFile.Games.Find(g => g.Id == preset.Id);
                    if (localPreset != null)
                    {
                        localPreset.Installed = true;
                        SavePresetsFile(presetsFile);
                    }
                    return (true, null);
                }

                _logger.Info($"Загрузка конфига {preset.Name} с {preset.ConfigUrl}");
                using (var response = await _httpClient.GetAsync(preset.ConfigUrl))
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        string err = $"HTTP ошибка {response.StatusCode} при загрузке {preset.ConfigUrl}";
                        _logger.Error(err);
                        return (false, err);
                    }
                    var content = await response.Content.ReadAsStringAsync();
                    _logger.Info($"Конфиг загружен, размер {content.Length} байт");

                    try
                    {
                        var deserializer = new DeserializerBuilder().Build();
                        var config = deserializer.Deserialize<GameConfig>(content);
                        if (config.TargetProcesses == null || config.TargetProcesses.Count == 0)
                            throw new Exception("Конфиг не содержит target_processes");
                        _logger.Info($"Валидация пройдена: target_processes = {config.TargetProcesses.Count}");
                    }
                    catch (Exception ex)
                    {
                        string err = $"Ошибка валидации конфига: {ex.Message}";
                        _logger.Error(err);
                        string snippet = content.Length > 500 ? content.Substring(0, 500) + "..." : content;
                        _logger.Error($"Содержимое конфига (первые 500 символов): {snippet}");
                        return (false, err);
                    }

                    await File.WriteAllTextAsync(localConfigPath, content);
                    _logger.Info($"Конфиг сохранён в {localConfigPath}");
                }

                var presetsFileAfter = LoadPresetsFile();
                var localPresetAfter = presetsFileAfter.Games.Find(g => g.Id == preset.Id);
                if (localPresetAfter != null)
                {
                    localPresetAfter.Installed = true;
                    SavePresetsFile(presetsFileAfter);
                }
                _logger.Info($"Установка {preset.Id} завершена успешно");
                return (true, null);
            }
            catch (Exception ex)
            {
                string err = $"Ошибка установки {preset.Name}: {ex.Message}";
                _logger.Error(err);
                _logger.Error($"Stack trace: {ex.StackTrace}");
                return (false, err);
            }
        }

        public void UninstallGame(string gameId)
        {
            var presetsFile = LoadPresetsFile();
            var preset = presetsFile.Games.Find(g => g.Id == gameId);
            if (preset != null)
            {
                preset.Installed = false;
                SavePresetsFile(presetsFile);
                _logger.Info($"Игра {gameId} отмечена как неустановленная");
            }
        }

        public GameConfig LoadGameConfig(string gameId)
        {
            string configPath = Path.Combine(_configsFolder, $"{gameId}.yaml");
            if (!File.Exists(configPath)) return null;
            try
            {
                var yaml = File.ReadAllText(configPath);
                var deserializer = new DeserializerBuilder().Build();
                var config = deserializer.Deserialize<GameConfig>(yaml);
                if (config.TargetProcesses == null || config.TargetProcesses.Count == 0)
                    throw new Exception("Конфиг не содержит target_processes");
                return config;
            }
            catch (Exception ex)
            {
                _logger.Error($"Ошибка загрузки конфига {gameId}: {ex.Message}");
                return null;
            }
        }
    }
}