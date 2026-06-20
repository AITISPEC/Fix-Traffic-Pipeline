using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace PlatformLauncher.Services
{
    public class GameSessionManager
    {
        private readonly PythonProcessManager _pythonManager;
        private readonly string _listsPath;
        private BackupManager _backupManager;
        private string _currentBackupDir;
        private bool _backupRestored;
        private bool _warpStartedByUs;
        private int _restoreInProgress;
        private readonly string _gameId;

        public event Action<string> OutputReceived;
        public event Action<bool> SessionEnded; // true если успешно

        public bool IsRunning => _pythonManager.IsRunning;

        public GameSessionManager(string gameId, string listsPath)
        {
            _gameId = gameId;
            _listsPath = listsPath;
            _pythonManager = new PythonProcessManager();
            _pythonManager.OutputReceived += msg => OutputReceived?.Invoke(msg);
            _pythonManager.ProcessExited += OnProcessExited;
        }

        public async Task StartAsync(bool monitorOnly, bool warpEnabled)
        {
            if (!monitorOnly)
            {
                _backupRestored = false;
                _backupManager = new BackupManager("./backups", _gameId);
                try
                {
                    _currentBackupDir = await _backupManager.CreateBackupAsync(_listsPath);
                    OutputReceived?.Invoke($"✅ Бэкап листов создан\n");
                }
                catch (Exception ex)
                {
                    OutputReceived?.Invoke($"❌ Ошибка создания бэкапа: {ex.Message}");
                    throw;
                }
            }

            _warpStartedByUs = false;
            if (warpEnabled)
            {
                OutputReceived?.Invoke("⏳ Запуск WARP...");
                try
                {
                    bool warpStarted = await WarpManager.EnsureStartedAsync();
                    if (warpStarted)
                    {
                        OutputReceived?.Invoke("✅ WARP запущен");
                        _warpStartedByUs = true;
                    }
                    else
                        OutputReceived?.Invoke("⚠️ Не удалось запустить WARP");
                }
                catch (Exception ex)
                {
                    OutputReceived?.Invoke($"❌ Ошибка запуска WARP: {ex.Message}");
                }
            }

            await _pythonManager.StartAsync(_gameId, _listsPath, monitorOnly);
        }

        public async Task StopAsync()
        {
            if (_pythonManager.IsRunning)
                await _pythonManager.StopAsync();

            if (_warpStartedByUs)
            {
                OutputReceived?.Invoke("⏳ Остановка WARP...");
                await WarpManager.DisconnectAsync();
                OutputReceived?.Invoke("✅ WARP остановлен");
                _warpStartedByUs = false;
            }
        }

        private async void OnProcessExited(int exitCode)
        {
            // Восстановление бэкапа
            if (Interlocked.CompareExchange(ref _restoreInProgress, 1, 0) == 0)
            {
                try
                {
                    if (!_backupRestored && _backupManager != null && !string.IsNullOrEmpty(_currentBackupDir))
                    {
                        bool restored = await _backupManager.RestoreBackupAsync(_listsPath, _currentBackupDir);
                        if (restored)
                        {
                            OutputReceived?.Invoke("✅ Бэкап восстановлен");
                            _backupRestored = true;
                        }
                        else
                            OutputReceived?.Invoke("⚠️ Ошибка восстановления бэкапа");
                    }
                }
                finally
                {
                    Interlocked.Exchange(ref _restoreInProgress, 0);
                }
            }

            if (_warpStartedByUs)
            {
                try
                {
                    OutputReceived?.Invoke("⏳ Остановка WARP...");
                    await WarpManager.DisconnectAsync();
                    OutputReceived?.Invoke("✅ WARP остановлен");
                    _warpStartedByUs = false;
                }
                catch (Exception ex)
                {
                    OutputReceived?.Invoke($"❌ Ошибка остановки WARP: {ex.Message}");
                }
            }

            SessionEnded?.Invoke(exitCode == 0);
        }
    }
}