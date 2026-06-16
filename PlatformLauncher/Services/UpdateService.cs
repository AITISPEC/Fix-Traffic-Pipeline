using PlatformLauncher.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace PlatformLauncher.Services
{
    public static class UpdateService
    {
        private const string PresetsUrl = "https://raw.githubusercontent.com/AITISPEC/Helpful/main/configs/presets.yaml";
        private static readonly HttpClient _httpClient = new HttpClient();

        public static async Task<bool> UpdatePresetsAsync(string localPresetsPath)
        {
            try
            {
                string dir = Path.GetDirectoryName(localPresetsPath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                using (var response = await _httpClient.GetAsync(PresetsUrl))
                {
                    response.EnsureSuccessStatusCode();
                    var content = await response.Content.ReadAsStringAsync();
                    var deserializer = new DeserializerBuilder().Build();
                    var temp = deserializer.Deserialize<PresetsFile>(content);
                    if (temp?.Games == null) throw new Exception("Invalid presets structure");
                    await File.WriteAllTextAsync(localPresetsPath, content);
                }
                return true;
            }
            catch (Exception ex)
            {
                LauncherLogger.Error($"Ошибка обновления пресетов: {ex.Message}");
                return false;
            }
        }

        // Метод теперь ничего не создаёт
        public static void CreateDefaultPresets(string localPresetsPath)
        {
            // Пользователь отказался от автосоздания файлов
            LauncherLogger.Info("CreateDefaultPresets вызван, но ничего не делает.");
        }

        public static List<GamePreset> LoadPresets(string presetsPath)
        {
            if (!File.Exists(presetsPath)) return new List<GamePreset>();
            try
            {
                var yaml = File.ReadAllText(presetsPath);
                var deserializer = new DeserializerBuilder().Build();
                var presetsFile = deserializer.Deserialize<PresetsFile>(yaml);
                LauncherLogger.Info($"Десериализовано {presetsFile?.Games?.Count ?? 0} игр");
                return presetsFile?.Games ?? new List<GamePreset>();
            }
            catch (Exception ex)
            {
                LauncherLogger.Error($"Ошибка загрузки пресетов: {ex.Message}");
                LauncherLogger.Error($"Stack trace: {ex.StackTrace}");
                return new List<GamePreset>();
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

                    // Проверяем, что конфиг валидный
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
                return (true, null);
            }
            catch (Exception ex)
            {
                string err = $"Ошибка установки {preset.Name}: {ex.Message}";
                LauncherLogger.Error(err);
                return (false, err);
            }
        }
    }
}