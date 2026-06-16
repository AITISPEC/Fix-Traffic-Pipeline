using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;

namespace PlatformLauncher.Services
{
    public static class SettingsManager
    {
        private static readonly string SettingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "user_settings.json");
        private static Dictionary<string, bool> _settings = new();
        private static readonly ReaderWriterLockSlim _lock = new ReaderWriterLockSlim();

        static SettingsManager()
        {
            Load();
        }

        private static void Load()
        {
            _lock.EnterWriteLock();
            try
            {
                if (!File.Exists(SettingsPath))
                {
                    _settings = new Dictionary<string, bool>();
                    Save();
                    return;
                }
                var json = File.ReadAllText(SettingsPath);
                _settings = JsonSerializer.Deserialize<Dictionary<string, bool>>(json) ?? new();
            }
            catch (Exception ex)
            {
                LauncherLogger.Error($"Ошибка загрузки настроек: {ex.Message}");
                _settings = new();
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        private static void Save()
        {
            _lock.EnterWriteLock();
            try
            {
                var json = JsonSerializer.Serialize(_settings);
                File.WriteAllText(SettingsPath, json);
            }
            catch (Exception ex)
            {
                LauncherLogger.Error($"Ошибка сохранения настроек: {ex.Message}");
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        private static bool GetBool(string key)
        {
            _lock.EnterReadLock();
            try
            {
                return _settings.TryGetValue(key, out bool value) && value;
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }

        private static void SetBool(string key, bool value)
        {
            _lock.EnterWriteLock();
            try
            {
                _settings[key] = value;
                Save();
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        public static bool GetWarpEnabled(string gameId) => GetBool($"WarpEnabled_{gameId}");
        public static void SetWarpEnabled(string gameId, bool enabled) => SetBool($"WarpEnabled_{gameId}", enabled);
        public static bool IsGameInstalled(string gameId) => GetBool($"Installed_{gameId}");
        public static void SetGameInstalled(string gameId, bool installed) => SetBool($"Installed_{gameId}", installed);
    }
}