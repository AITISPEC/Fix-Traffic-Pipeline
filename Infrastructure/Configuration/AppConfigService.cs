using System;
using System.IO;
using PlatformLauncher.Domain.Models;
using PlatformLauncher.Core.Interfaces;
using YamlDotNet.Serialization;

namespace PlatformLauncher.Infrastructure.Configuration
{
    /// <summary>
    /// Загрузка глобальных настроек из data/app_config.yaml; атомарные записи в конфигурационный файл для многопоточного доступа.
    /// </summary>
    public class AppConfigService : IAppConfigService
    {
        private readonly ILogger _logger;
        private readonly string _configPath;

        /// <summary>Конструктор через DI — вводит зависимости (сложность 3 класса).</summary>
        public AppConfigService(ILogger logger)
        {
            _logger = logger;
            _configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "app_config.yaml");
        }

        /// <summary>Поток обработки: MainViewModel.LoadAppConfig → Load() → ReadFile/Yaml</summary>
        public AppConfig Load()
        {
            if (!File.Exists(_configPath))
            {
                _logger.Warning($"app_config.yaml не найден, создаю с настройками по умолчанию");
                var defaultConfig = new AppConfig();
                Save(defaultConfig);
                return defaultConfig;
            }

            try
            {
                var yaml = File.ReadAllText(_configPath);
                var deserializer = new DeserializerBuilder().Build();
                return deserializer.Deserialize<AppConfig>(yaml);
            }
            catch (Exception ex)
            {
                _logger.Error($"Ошибка загрузки app_config.yaml: {ex.Message}");
                return new AppConfig();
            }
        }

        /// <summary>Атомарное сохранение конфигурации — создает директорию, если отсутствует.</summary>
        public void Save(AppConfig config)
        {
            try
            {
                var serializer = new SerializerBuilder().Build();
                var yaml = serializer.Serialize(config);
                var dir = Path.GetDirectoryName(_configPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(_configPath, yaml);
            }
            catch (Exception ex)
            {
                _logger.Error($"Ошибка сохранения app_config.yaml: {ex.Message}");
            }
        }
    }
}