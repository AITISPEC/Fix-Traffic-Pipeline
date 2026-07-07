using PlatformLauncher.Core.Interfaces;
using System;
using System.IO;
using System.Threading.Tasks;

namespace PlatformLauncher.Infrastructure.ProcessManagement
{
    /// <summary>
    /// Оркестрация сессии мониторинга: координирует Python-процесс, бэкапы/восстановление lists, WARP и UserCallback. Узкое место: OnProcessExited использует await внутри async void — стектрейс теряется при критической ошибке (хватаем в catch).
    /// </summary>
    public class SessionOrchestrator : ISessionOrchestrator
    {
        private readonly IPythonProcessManager _pythonManager;
        private readonly IPythonEnvironmentManager _pythonEnvManager;
        private readonly IBackupManager _backupManager;
        private readonly IWarpManager _warpManager;
        private readonly IProcessKiller _processKiller;
        private readonly ILogger _logger;
        private readonly IListsSanitizer _listsSanitizer;
        private readonly IUpdateService _updateService;
        private string _currentBackupDir = string.Empty;
        private string _currentListsPath = string.Empty;
        private bool _backupRestored;
        private bool _warpStartedByUs;
        private bool _sessionEndedRaised;
        private bool _stopRequested = false;
        private Func<string, Task<bool>>? _askUserCallback;

        public event Action<string>? OutputReceived;
        public event Action<bool>? SessionEnded;

        // Лейбл IsRunning считывает _pythonManager.IsRunning — синхронно без блокировок.
        public bool IsRunning => _pythonManager.IsRunning;

        /// <summary>
        /// Конструктор через DI — вводит зависимости (сложность 8 класса).
        /// </summary>
        public SessionOrchestrator(
            IPythonProcessManager pythonManager,
            IBackupManager backupManager,
            IWarpManager warpManager,
            IProcessKiller processKiller,
            ILogger logger,
            IListsSanitizer listsSanitizer,
            IUpdateService updateService,
            IPythonEnvironmentManager pythonEnvManager
        )
        {
            _pythonManager = pythonManager;
            _backupManager = backupManager;
            _warpManager = warpManager;
            _processKiller = processKiller;
            _logger = logger;
            _listsSanitizer = listsSanitizer;
            _updateService = updateService;
            _pythonEnvManager = pythonEnvManager;

            // Привязываем делегаты событий Python-менеджера:
            // — OutputReceived → перенаправляем дальше в UI;
            // — ProcessExited → OnProcessExited, которая завершает сессию.
            _pythonManager.OutputReceived += msg => OutputReceived?.Invoke(msg);
            _pythonManager.ProcessExited += OnProcessExited;
        }

        /// <summary>Поток обработки: StartAsync → CreateBackupAsync → Sanitize → EnsureStartedAsync → python.StartAsync.</summary>
        public async Task StartAsync(string gameId, string listsPath, bool monitorOnly, bool warpEnabled, bool filterProcesses = true)
        {
            if (!monitorOnly)
            {
                // 1. Валидация Python
                string pythonExe = _pythonEnvManager.GetVenvPythonPath();
                if (string.IsNullOrEmpty(pythonExe) || !File.Exists(pythonExe))
                {
                    OutputReceived?.Invoke("❌ Python не найден. Перейдите в Сервис -> Python");
                    throw new Exception("Python validation failed");
                }

                // 2. Валидация скрипта
                string monitorScript = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "monitor.py");
                if (!File.Exists(monitorScript))
                {
                    OutputReceived?.Invoke("❌ Скрипт monitor.py не найден");
                    throw new Exception("Monitor script not found");
                }

                // 3. Валидация lists
                if (string.IsNullOrEmpty(listsPath) || !Directory.Exists(listsPath))
                {
                    OutputReceived?.Invoke("❌ Папка lists не найдена");
                    throw new Exception("Lists path validation failed");
                }

                // 4. Валидация config
                var config = _updateService.LoadGameConfig(gameId);
                if (config == null)
                {
                    OutputReceived?.Invoke($"❌ Конфиг {gameId} не найден или невалиден");
                    OutputReceived?.Invoke($"⚠️ Cанация пропущена.");
                    throw new Exception("Config validation failed");
                }
                // Санация — фильтрация записей по include/exclude спискам из config.yaml:
                else
                {
                    _listsSanitizer.Sanitize(listsPath, config);
                    OutputReceived?.Invoke("✅ Санация и начальное заполнение списков выполнены");
                }

                _sessionEndedRaised = false;
                _stopRequested = false;
                _backupRestored = false;
                _currentListsPath = listsPath;
                _currentBackupDir = await _backupManager.CreateBackupAsync(listsPath, gameId);
                OutputReceived?.Invoke($"✅ Бэкап листов создан\n");
            }

            _warpStartedByUs = false;
            if (warpEnabled)
            {
                // Запускаем Cloudflare WARP через warp-cli.
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
                    // Catch-all для любых ошибок запуска WARP.
                    OutputReceived?.Invoke($"❌ Ошибка запуска WARP: {ex.Message}");
                }
            }

            await _pythonManager.StartAsync(gameId, listsPath, monitorOnly, filterProcesses);
        }

        public void SetAskUserCallback(Func<string, Task<bool>> callback)
        {
            _askUserCallback = callback;
        }

        /// <summary>Поток обработки: _stopRequested=true → python.StopAsync() → warp.DisconnectAsync().</summary>
        public async Task StopAsync()
        {
            _stopRequested = true;
            // Если Python-процесс уже завершён → просто убиваем все процессы через KillAll.
            if (!_pythonManager.IsRunning)
            {
                KillAll();
                return;
            }
            await _pythonManager.StopAsync();

            if (_warpStartedByUs)
            {
                OutputReceived?.Invoke("⏳ Остановка WARP...");
                // Отключаем от Cloudflare — не запускает warp-cli, а закрывает туннель.
                await _warpManager.DisconnectAsync();
                OutputReceived?.Invoke("✅ WARP остановлен");
                _warpStartedByUs = false;
            }
        }

        /// <summary>Убийство процессов: winws.exe из runtimes/zdy/, все venv-процессы Python.</summary>
        public void KillAll()
        {
            string ourWinwsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "runtimes", "zdy", "bin", "winws.exe");
            if (File.Exists(ourWinwsPath))
            {
                _processKiller.KillWinwsProcess(ourWinwsPath);
            }

            // Убиваем зависшие процессы Python через processKiller.KillPythonVenvProcesses.
            _processKiller.KillPythonVenvProcesses();
        }

        /// <summary>Поток обработки: ProcessExited → if (sessionEndedRaised) return; _sessionEndedRaised=true → обработка бэкапа.</summary>
        private async void OnProcessExited(int exitCode)
        {
            try
            {
                // Двойная проверка: если сессия уже завершена, не запускаем логику снова.
                if (_sessionEndedRaised) return;
                _sessionEndedRaised = true;

                bool success = _stopRequested || exitCode == 0;

                // Если пользователь попросил остановить → запрашиваем результат бэкапа через callback:
                if (_stopRequested && !string.IsNullOrEmpty(_currentBackupDir) && _backupManager != null)
                {
                    bool saveResult = false;
                    // Запрашиваем у пользователя, сохранить или восстановить бэкап.
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
                        // Восстанавливаем папку lists из бэкапа (атомарно заменяем целевой путь).
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
                // Если пользователя не спросили → автоматически восстанавливаем.
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

                // Если мы запускали WARP — закрываем туннель.
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

                // Финальное событие сессии — завершение работы.
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