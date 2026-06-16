using PlatformLauncher.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace PlatformLauncher.Services
{
    public static class UpdateService
    {
        private static readonly HttpClient _httpClient = new HttpClient();
        private static readonly string PresetsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "configs", "presets.yaml");
        private const string GitHubApiUrl = "https://api.github.com/repos/AITISPEC/Fix-Traffic-Pipeline/contents/data/configs";

        static UpdateService()
        {
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "PlatformLauncher");
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

        public static async Task<bool> SyncFromGitHubAsync()
        {
            try
            {
                LauncherLogger.Info("Начинаем синхронизацию пресетов с GitHub...");
                var localPresets = LoadPresetsFile();
                var localDict = new Dictionary<string, GamePreset>();
                foreach (var g in localPresets.Games)
                    localDict[g.Id] = g;

                // Добавляем правильные заголовки
                using var request = new HttpRequestMessage(HttpMethod.Get, GitHubApiUrl);
                request.Headers.Add("Accept", "application/vnd.github.v3+json");
                request.Headers.Add("User-Agent", "PlatformLauncher/1.0");

                var response = await _httpClient.SendAsync(request);
                if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
                {
                    string error = "Доступ к GitHub API запрещён (403). Возможно, превышен лимит запросов (60/час). Попробуйте позже.";
                    LauncherLogger.Error(error);
                    return false;
                }
                if (!response.IsSuccessStatusCode)
                {
                    string error = $"GitHub API вернул ошибку: {response.StatusCode}";
                    LauncherLogger.Error(error);
                    return false;
                }

                var json = await response.Content.ReadAsStringAsync();
                var files = JsonSerializer.Deserialize<List<GitHubFileInfo>>(json);
                if (files == null)
                {
                    LauncherLogger.Error("Не удалось десериализовать ответ API");
                    return false;
                }

                if (files.Count == 0)
                {
                    LauncherLogger.Warning("GitHub папка data/configs пуста, локальные пресеты не изменяются");
                    return true;
                }

                bool changed = false;
                foreach (var file in files)
                {
                    if (file.Type != "file" || !file.Name.EndsWith(".yaml") || file.Name == "presets.yaml")
                        continue;

                    var contentResponse = await _httpClient.GetAsync(file.DownloadUrl);
                    if (!contentResponse.IsSuccessStatusCode) continue;
                    var content = await contentResponse.Content.ReadAsStringAsync();
                    var deserializer = new DeserializerBuilder().Build();
                    GameConfig config;
                    try
                    {
                        config = deserializer.Deserialize<GameConfig>(content);
                    }
                    catch
                    {
                        LauncherLogger.Warning($"Не удалось десериализовать {file.Name}, пропускаем");
                        continue;
                    }

                    string id = Path.GetFileNameWithoutExtension(file.Name);
                    string name = config.TargetProcesses?.Count > 0 ? config.TargetProcesses[0].Name : id;
                    bool warpSupported = config.WarpSupported ?? false;

                    if (localDict.TryGetValue(id, out var existing))
                    {
                        if (existing.Name != name || existing.WarpSupported != warpSupported || existing.Version != (config.Version ?? 1))
                        {
                            existing.Name = name;
                            existing.WarpSupported = warpSupported;
                            existing.Version = config.Version ?? 1;
                            changed = true;
                        }
                    }
                    else
                    {
                        var newPreset = new GamePreset
                        {
                            Id = id,
                            Name = name,
                            ConfigUrl = file.DownloadUrl,
                            WarpSupported = warpSupported,
                            Version = config.Version ?? 1,
                            Installed = false
                        };
                        localPresets.Games.Add(newPreset);
                        changed = true;
                    }
                }

                if (files.Count > 0)
                {
                    var remoteIds = new HashSet<string>();
                    foreach (var file in files)
                    {
                        if (file.Type == "file" && file.Name.EndsWith(".yaml") && file.Name != "presets.yaml")
                            remoteIds.Add(Path.GetFileNameWithoutExtension(file.Name));
                    }
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

        private class GitHubFileInfo
        {
            public string Name { get; set; }
            public string DownloadUrl { get; set; }
            public string Type { get; set; }
        }
    }
}