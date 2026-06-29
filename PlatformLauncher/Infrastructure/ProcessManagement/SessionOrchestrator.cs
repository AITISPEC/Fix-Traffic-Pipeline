using PlatformLauncher.Core.Interfaces;
using System;
using System.IO;
using System.Threading.Tasks;

namespace PlatformLauncher.Infrastructure.ProcessManagement
{
    public class SessionOrchestrator : ISessionOrchestrator
    {
        private readonly IPythonProcessManager _pythonManager;
        private readonly IBackupManager _backupManager;
        private readonly IWarpManager _warpManager;
        private readonly IProcessKiller _processKiller;
        private readonly ILogger _logger;
        private readonly IListsSanitizer _listsSanitizer;
        private readonly IUpdateService _updateService;
        private string _currentBackupDir;
        private bool _backupRestored;
        private bool _warpStartedByUs;
        private string _currentListsPath;
        private bool _sessionEndedRaised;
        private bool _stopRequested = false;
        private Func<string, Task<bool>> _askUserCallback;

        public event Action<string> OutputReceived;
        public event Action<bool> SessionEnded;

        public bool IsRunning => _pythonManager.IsRunning;

        public SessionOrchestrator(
            IPythonProcessManager pythonManager,
            IBackupManager backupManager,
            IWarpManager warpManager,
            IProcessKiller processKiller,
            ILogger logger,
            IListsSanitizer listsSanitizer,
            IUpdateService updateService
            )
        {
            _pythonManager = pythonManager;
            _backupManager = backupManager;
            _warpManager = warpManager;
            _processKiller = processKiller;
            _logger = logger;
            _listsSanitizer = listsSanitizer;
            _updateService = updateService;

            _pythonManager.OutputReceived += msg => OutputReceived?.Invoke(msg);
            _pythonManager.ProcessExited += OnProcessExited;
        }

        public async Task StartAsync(string gameId, string listsPath, bool monitorOnly, bool warpEnabled, bool filterProcesses = true)
        {
            if (!monitorOnly)
            {
                _sessionEndedRaised = false;
                _stopRequested = false;
                _currentListsPath = listsPath;
                _backupRestored = false;
                _currentBackupDir = await _backupManager.CreateBackupAsync(listsPath, gameId);
                OutputReceived?.Invoke($"✅ Бэкап листов создан\n");

                // Санация
                var config = _updateService.LoadGameConfig(gameId);
                if (config != null)
                {
                    _listsSanitizer.Sanitize(listsPath, config);
                    OutputReceived?.Invoke("✅ Санация и начальное заполнение списков выполнены");
                }
                else
                {
                    OutputReceived?.Invoke($"⚠️ Конфиг для {gameId} не найден, санация пропущена.");
                }
            }

            _warpStartedByUs = false;
            if (warpEnabled)
            {
                OutputReceived?.Invoke("⏳ Запуск WARP...");
                try
                {
                    bool warpStarted = await _warpManager.EnsureStartedAsync();
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

            await _pythonManager.StartAsync(gameId, listsPath, monitorOnly, filterProcesses);
        }

        public void SetAskUserCallback(Func<string, Task<bool>> callback)
        {
            _askUserCallback = callback;
        }

        public async Task StopAsync()
        {
            _stopRequested = true;
            if (!_pythonManager.IsRunning)
            {
                KillAll();
                return;
            }
            await _pythonManager.StopAsync();

            if (_warpStartedByUs)
            {
                OutputReceived?.Invoke("⏳ Остановка WARP...");
                await _warpManager.DisconnectAsync();
                OutputReceived?.Invoke("✅ WARP остановлен");
                _warpStartedByUs = false;
            }
        }

        public void KillAll()
        {
            string ourWinwsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "runtimes", "zdy", "bin", "winws.exe");
            if (File.Exists(ourWinwsPath))
            {
                _processKiller.KillWinwsProcess(ourWinwsPath);
            }

            _processKiller.KillPythonVenvProcesses();
        }

        private async void OnProcessExited(int exitCode)
        {
            try
            {
                if (_sessionEndedRaised) return;
                _sessionEndedRaised = true;

                bool success = _stopRequested || exitCode == 0;

                if (_stopRequested && !string.IsNullOrEmpty(_currentBackupDir) && _backupManager != null)
                {
                    bool saveResult = false;
                    if (_askUserCallback != null)
                    {
                        saveResult = await _askUserCallback(_currentBackupDir);
                    }
                    if (saveResult)
                    {
                        _backupManager.MarkAsSaved(_currentBackupDir);
                        _backupRestored = true;
                        OutputReceived?.Invoke("✅ Результат сохранён, бэкап не восстановлен.");
                    }
                    else
                    {
                        bool restored = await _backupManager.RestoreBackupAsync(_currentListsPath, _currentBackupDir);
                        if (restored)
                        {
                            OutputReceived?.Invoke("✅ Бэкап восстановлен");
                            _backupRestored = true;
                        }
                        else
                            OutputReceived?.Invoke("⚠️ Ошибка восстановления бэкапа");
                    }
                }
                else
                {
                    if (_backupManager != null && !string.IsNullOrEmpty(_currentBackupDir) && !_backupRestored)
                    {
                        bool restored = await _backupManager.RestoreBackupAsync(_currentListsPath, _currentBackupDir);
                        if (restored)
                        {
                            OutputReceived?.Invoke("✅ Бэкап восстановлен");
                            _backupRestored = true;
                        }
                        else
                            OutputReceived?.Invoke("⚠️ Ошибка восстановления бэкапа");
                    }
                }

                if (_warpStartedByUs)
                {
                    try
                    {
                        OutputReceived?.Invoke("⏳ Остановка WARP...");
                        await _warpManager.DisconnectAsync();
                        OutputReceived?.Invoke("✅ WARP остановлен");
                        _warpStartedByUs = false;
                    }
                    catch (Exception ex)
                    {
                        OutputReceived?.Invoke($"❌ Ошибка остановки WARP: {ex.Message}");
                    }
                }

                SessionEnded?.Invoke(success);
            }
            catch (Exception ex)
            {
                _logger.Error("Critical error in OnProcessExited", ex);
                SessionEnded?.Invoke(false);
            }
        }
    }
}