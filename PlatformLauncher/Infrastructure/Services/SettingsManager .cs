using PlatformLauncher.Core.Interfaces;
using PlatformLauncher.Infrastructure.Configuration;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace PlatformLauncher.Infrastructure.Services
{
    public class SettingsManager : ISettingsManager
    {
        private readonly ILogger _logger;
        private readonly IAppConfigService _appConfigService;
        private readonly string _settingsPath;
        private Dictionary<string, object> _settings = new();

        public SettingsManager(ILogger logger, IAppConfigService appConfigService)
        {
            _logger = logger;
            _appConfigService = appConfigService;
            _settingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "user_settings.json");
            Load();
        }

        private void Load()
        {
            try
            {
                if (!File.Exists(_settingsPath))
                {
                    _settings = new Dictionary<string, object>();
                    return;
                }
                var json = File.ReadAllText(_settingsPath);
                _settings = JsonSerializer.Deserialize<Dictionary<string, object>>(json) ?? new();
            }
            catch (Exception ex)
            {
                _logger.Error($"Ошибка загрузки настроек: {ex.Message}");
                _settings = new();
            }
        }

        private void Save()
        {
            try
            {
                var json = JsonSerializer.Serialize(_settings);
                File.WriteAllText(_settingsPath, json);
            }
            catch (Exception ex)
            {
                _logger.Error($"Ошибка сохранения настроек: {ex.Message}");
            }
        }

        public bool GetWarpEnabled(string gameId)
        {
            var key = $"WarpEnabled_{gameId}";
            return _settings.TryGetValue(key, out object value) && value is bool b && b;
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