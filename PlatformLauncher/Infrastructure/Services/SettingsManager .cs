using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using PlatformLauncher.Core.Interfaces;

namespace PlatformLauncher.Infrastructure.Services
{
    public class SettingsManager : ISettingsManager
    {
        private readonly ILogger _logger;
        private readonly IAppConfigService _appConfigService;
        private readonly string _settingsPath;
        private Dictionary<string, object> _settings = new();
        private bool _isWritable = true; // флаг, что запись возможна

        public SettingsManager(ILogger logger, IAppConfigService appConfigService)
        {
            _logger = logger;
            _appConfigService = appConfigService;

            try
            {
                var dataDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data");
                Directory.CreateDirectory(dataDir); // может выбросить IOException, UnauthorizedAccessException
                _settingsPath = Path.Combine(dataDir, "user_settings.json");
                Load();
            }
            catch (Exception ex)
            {
                _logger.Error($"Ошибка инициализации файла настроек: {ex.Message}");
                _settingsPath = null;
                _isWritable = false;
                _settings = new Dictionary<string, object>(); // работаем с пустыми настройками в памяти
            }
        }

        private void Load()
        {
            if (string.IsNullOrEmpty(_settingsPath) || !File.Exists(_settingsPath))
            {
                _settings = new Dictionary<string, object>();
                return;
            }

            try
            {
                var json = File.ReadAllText(_settingsPath);
                _settings = JsonSerializer.Deserialize<Dictionary<string, object>>(json) ?? new();
                _isWritable = true;
            }
            catch (Exception ex)
            {
                _logger.Error($"Ошибка загрузки настроек из {_settingsPath}: {ex.Message}");
                _settings = new Dictionary<string, object>();
                _isWritable = true;
            }
        }

        private void Save()
        {
            if (!_isWritable || string.IsNullOrEmpty(_settingsPath))
            {
                _logger.Warning("Сохранение настроек отключено (файл недоступен)");
                return;
            }

            try
            {
                var json = JsonSerializer.Serialize(_settings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_settingsPath, json);
                _isWritable = true;
            }
            catch (Exception ex)
            {
                _logger.Error($"Ошибка сохранения настроек в {_settingsPath}: {ex.Message}");
                // при ошибке записи помечаем как недоступный для записи, чтобы не спамить ошибками
                _isWritable = false;
            }
        }

        public bool GetWarpEnabled(string gameId)
        {
            var key = $"WarpEnabled_{gameId}";
            if (!_settings.TryGetValue(key, out object value))
                return false;

            // System.Text.Json десериализует bool как JsonElement
            if (value is System.Text.Json.JsonElement element)
            {
                if (element.ValueKind == System.Text.Json.JsonValueKind.True)
                    return true;
                if (element.ValueKind == System.Text.Json.JsonValueKind.False)
                    return false;
            }

            return value is bool b && b;
        }

        public void SetWarpEnabled(string gameId, bool enabled)
        {
            var key = $"WarpEnabled_{gameId}";
            _settings[key] = enabled;
            Save();
        }

        public string GetTheme()
        {
            var config = _appConfigService.Load();
            return config.SelectedTheme ?? "fluent-light";
        }

        public void SetTheme(string themeId)
        {
            var config = _appConfigService.Load();
            config.SelectedTheme = themeId;
            _appConfigService.Save(config);
        }
    }
}