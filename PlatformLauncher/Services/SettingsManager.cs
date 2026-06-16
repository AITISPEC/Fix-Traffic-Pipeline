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
        private static Dictionary<string, bool> _warpSettings = new();
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
                    _warpSettings = new Dictionary<string, bool>();
                    return; // НЕ вызываем Save() здесь
                }
                var json = File.ReadAllText(SettingsPath);
                _warpSettings = JsonSerializer.Deserialize<Dictionary<string, bool>>(json) ?? new();
            }
            catch (Exception ex)
            {
                LauncherLogger.Error($"Ошибка загрузки настроек: {ex.Message}");
                _warpSettings = new();
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
                var json = JsonSerializer.Serialize(_warpSettings);
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

        public static bool GetWarpEnabled(string gameId)
        {
            var key = $"WarpEnabled_{gameId}";
            _lock.EnterReadLock();
            try
            {
                return _warpSettings.TryGetValue(key, out bool value) && value;
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }

        public static void SetWarpEnabled(string gameId, bool enabled)
        {
            var key = $"WarpEnabled_{gameId}";
            _lock.EnterWriteLock();
            try
            {
                _warpSettings[key] = enabled;
                Save();
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }
    }
}