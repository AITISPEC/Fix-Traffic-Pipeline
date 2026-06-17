using PlatformLauncher.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using YamlDotNet.Serialization;

namespace PlatformLauncher.Services
{
    public static class UpdateService
    {
        private static readonly HttpClient _httpClient = new HttpClient();
        private static readonly string PresetsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "configs", "presets.yaml");
        private const string RemotePresetsUrl = "https://raw.githubusercontent.com/AITISPEC/Fix-Traffic-Pipeline/main/data/configs/presets.yaml";

        static UpdateService()
        {
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "PlatformLauncher/1.0");
        }

        public static PresetsFile LoadPresetsFile()
        {
            if (!File.Exists(PresetsPath))
            {
                return new PresetsFile { Games = new List<GamePreset>() };
            }
            try
            {
                var yaml = File.ReadAllText(PresetsPath);
                var deserializer = new DeserializerBuilder().Build();
                return deserializer.Deserialize<PresetsFile>(yaml) ?? new PresetsFile();
            }
            catch (Exception ex)
            {
                LauncherLogger.Error($"Ошибка загрузки presets.yaml: {ex.Message}");
                return new PresetsFile();
            }
        }

        public static void SavePresetsFile(PresetsFile presets)
        {
            try
            {
                var serializer = new SerializerBuilder().Build();
                var yaml = serializer.Serialize(presets);
                string dir = Path.GetDirectoryName(PresetsPath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(PresetsPath, yaml);
                LauncherLogger.Info($"presets.yaml сохранён ({presets.Games.Count} игр)");
            }
            catch (Exception ex)
            {
                LauncherLogger.Error($"Ошибка сохранения presets.yaml: {ex.Message}");
            }
        }

        public static List<GamePreset> LoadPresets()
        {
            var file = LoadPresetsFile();
            return file.Games;
        }

        /// <summary>
        /// Синхронизирует локальный presets.yaml с удалённым (скачивает по raw-ссылке).
        /// Сохраняет статус Installed для существующих игр.
        /// </summary>
        public static async Task<bool> SyncFromGitHubAsync()
        {
            try
            {
                LauncherLogger.Info("Начинаем синхронизацию пресетов (загрузка presets.yaml)...");
                using var response = await _httpClient.GetAsync(RemotePresetsUrl);
                if (!response.IsSuccessStatusCode)
                {
                    string error = $"Ошибка загрузки presets.yaml: {response.StatusCode}";
                    LauncherLogger.Error(error);
                    // При ошибке (404 и т.п.) локальные пресеты не изменяем
                    return false;
                }

                var remoteYaml = await response.Content.ReadAsStringAsync();
                LauncherLogger.Info($"Получен ответ, длина: {remoteYaml.Length} символов");

                var deserializer = new DeserializerBuilder().Build();
                PresetsFile remotePresets;
                try
                {
                    remotePresets = deserializer.Deserialize<PresetsFile>(remoteYaml);
                }
                catch (Exception ex)
                {
                    LauncherLogger.Error($"Ошибка десериализации удалённого presets.yaml: {ex.Message}");
                    return false;
                }

                if (remotePresets?.Games == null || remotePresets.Games.Count == 0)
                {
                    LauncherLogger.Warning("Удалённый presets.yaml не содержит списка игр или пуст. Локальные пресеты не изменены.");
                    return true;
                }

                var localPresets = LoadPresetsFile();
                var localDict = new Dictionary<string, GamePreset>();
                foreach (var g in localPresets.Games)
                    localDict[g.Id] = g;

                bool changed = false;
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
                        changed = true;
                    }
                    else
                    {
                        remoteGame.Installed = false;
                        localPresets.Games.Add(remoteGame);
                        changed = true;
                    }
                }

                var remoteIds = new HashSet<string>();
                foreach (var g in remotePresets.Games)
                    remoteIds.Add(g.Id);

                var toRemove = new List<GamePreset>();
                foreach (var g in localPresets.Games)
                {
                    if (!remoteIds.Contains(g.Id))
                        toRemove.Add(g);
                }
                foreach (var g in toRemove)
                {
                    localPresets.Games.Remove(g);
                    changed = true;
                }

                if (changed)
                {
                    SavePresetsFile(localPresets);
                    LauncherLogger.Info("Синхронизация завершена, presets.yaml обновлён");
                }
                else
                {
                    LauncherLogger.Info("Синхронизация не выявила изменений");
                }
                return true;
            }
            catch (Exception ex)
            {
                LauncherLogger.Error($"Ошибка синхронизации: {ex.Message}");
                return false;
            }
        }

        public static async Task<(bool Success, string ErrorMessage)> InstallGameAsync(GamePreset preset)
        {
            try
            {
                string configDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "configs");
                if (!Directory.Exists(configDir)) Directory.CreateDirectory(configDir);
                string localConfigPath = Path.Combine(configDir, $"{preset.Id}.yaml");

                LauncherLogger.Info($"Загрузка конфига {preset.Name} с {preset.ConfigUrl}");
                using (var response = await _httpClient.GetAsync(preset.ConfigUrl))
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        string err = $"HTTP ошибка {response.StatusCode} при загрузке {preset.ConfigUrl}";
                        LauncherLogger.Error(err);
                        return (false, err);
                    }
                    var content = await response.Content.ReadAsStringAsync();

                    try
                    {
                        var deserializer = new DeserializerBuilder().Build();
                        var config = deserializer.Deserialize<GameConfig>(content);
                        if (config.TargetProcesses == null || config.TargetProcesses.Count == 0)
                            throw new Exception("Конфиг не содержит target_processes");
                    }
                    catch (Exception ex)
                    {
                        string err = $"Ошибка валидации конфига: {ex.Message}";
                        LauncherLogger.Error(err);
                        return (false, err);
                    }

                    await File.WriteAllTextAsync(localConfigPath, content);
                    LauncherLogger.Info($"Конфиг {preset.Name} сохранён в {localConfigPath}");
                }

                var presets = LoadPresetsFile();
                var localPreset = presets.Games.Find(g => g.Id == preset.Id);
                if (localPreset != null)
                {
                    localPreset.Installed = true;
                    SavePresetsFile(presets);
                }
                return (true, null);
            }
            catch (Exception ex)
            {
                string err = $"Ошибка установки {preset.Name}: {ex.Message}";
                LauncherLogger.Error(err);
                return (false, err);
            }
        }

        public static void UninstallGame(string gameId)
        {
            string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "configs", $"{gameId}.yaml");
            if (File.Exists(configPath))
                File.Delete(configPath);

            var presets = LoadPresetsFile();
            var preset = presets.Games.Find(g => g.Id == gameId);
            if (preset != null)
            {
                preset.Installed = false;
                SavePresetsFile(presets);
            }
        }

        public static GameConfig LoadGameConfig(string gameId)
        {
            string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "configs", $"{gameId}.yaml");
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
                LauncherLogger.Error($"Ошибка загрузки конфига {gameId}: {ex.Message}");
                return null;
            }
        }
    }
}