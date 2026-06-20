using System;
using System.IO;
using PlatformLauncher.Models;
using YamlDotNet.Serialization;

namespace PlatformLauncher.Services
{
    public static class ConfigService
    {
        private static AppConfig _config;
        private static readonly string ConfigPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "app_config.yaml");

        public static AppConfig Load()
        {
            if (_config != null)
                return _config;

            if (!File.Exists(ConfigPath))
            {
                LauncherLogger.Warning($"app_config.yaml не найден, создаю с настройками по умолчанию");
                _config = new AppConfig();
                Save(_config);
                return _config;
            }

            try
            {
                var yaml = File.ReadAllText(ConfigPath);
                var deserializer = new DeserializerBuilder().Build();
                _config = deserializer.Deserialize<AppConfig>(yaml);
                return _config;
            }
            catch (Exception ex)
            {
                LauncherLogger.Error($"Ошибка загрузки app_config.yaml: {ex.Message}");
                _config = new AppConfig();
                return _config;
            }
        }

        public static void Save(AppConfig config)
        {
            try
            {
                var serializer = new SerializerBuilder().Build();
                var yaml = serializer.Serialize(config);
                var dir = Path.GetDirectoryName(ConfigPath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(ConfigPath, yaml);
            }
            catch (Exception ex)
            {
                LauncherLogger.Error($"Ошибка сохранения app_config.yaml: {ex.Message}");
            }
        }
    }
}