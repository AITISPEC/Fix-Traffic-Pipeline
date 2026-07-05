using PlatformLauncher.Core.Interfaces;
using PlatformLauncher.Domain.Models;
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
        private UserSettings _settings = new();
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
                _settings = new UserSettings(); // работаем с пустыми настройками в памяти
            }
        }

        private void Load()
        {
            if (string.IsNullOrEmpty(_settingsPath) || !File.Exists(_settingsPath))
            {
                _settings = new UserSettings();
                return;
            }
            try
            {
                var json = File.ReadAllText(_settingsPath);
                _settings = JsonSerializer.Deserialize<UserSettings>(json) ?? new UserSettings();
                _isWritable = true;
            }
            catch (Exception ex)
            {
                _logger.Error($"Ошибка загрузки настроек из {_settingsPath}: {ex.Message}");
                _settings = new UserSettings();
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
            return _settings.WarpEnabled.TryGetValue(gameId, out bool enabled) && enabled;
        }

        public void SetWarpEnabled(string gameId, bool enabled)
        {
            _settings.WarpEnabled[gameId] = enabled;
            Save();
        }

        public (bool Installed, bool NotInstalled, bool Custom) GetFilterState()
        {
            return (_settings.FilterInstalled, _settings.FilterNotInstalled, _settings.FilterCustom);
        }

        public void SetFilterState(bool installed, bool notInstalled, bool custom)
        {
            _settings.FilterInstalled = installed;
            _settings.FilterNotInstalled = notInstalled;
            _settings.FilterCustom = custom;
            Save();
        }

        public string GetListsPath()
        {
            return _settings.ListsPath ?? string.Empty;
        }

        public void SetListsPath(string path)
        {
            _settings.ListsPath = path ?? string.Empty;
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