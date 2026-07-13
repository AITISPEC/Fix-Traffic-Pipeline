using PlatformLauncher.Core.Interfaces;
using PlatformLauncher.Infrastructure.Python;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace PlatformLauncher.Infrastructure.ProcessManagement
{
    public class PythonProcessManager : IPythonProcessManager
    {
        private readonly ILogger _logger;
        private readonly IProcessKiller _processKiller;
        private readonly IPythonEnvironmentManager _pythonEnvManager; // <-- ДОБАВЛЕНО
        private Process? _process;

        public event Action<string>? OutputReceived;
        public event Action<int>? ProcessExited;

        private string _listsPath = string.Empty;
        private string _pythonPath = string.Empty;

        public bool IsRunning => _process != null && !_process.HasExited;

        public PythonProcessManager(ILogger logger, IProcessKiller processKiller, IPythonEnvironmentManager pythonEnvManager)
        {
            _logger = logger;
            _processKiller = processKiller;
            _pythonEnvManager = pythonEnvManager;
            _pythonPath = pythonEnvManager.GetVenvPythonPath();
        }

        private void CleanPythonCache()
        {
            try
            {
                string dataDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data");
                if (!Directory.Exists(dataDir))
                    return;

                foreach (var dir in Directory.GetDirectories(dataDir, "__pycache__", SearchOption.AllDirectories))
                {
                    try
                    {
                        Directory.Delete(dir, true);
                        _logger.Info($"Удалён кэш: {dir}");
                    }
                    catch (Exception ex)
                    {
                        _logger.Warning($"Не удалось удалить {dir}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Warning($"Ошибка очистки кэша Python: {ex.Message}");
            }
        }

        public async Task StartAsync(string gameId, string listsPath, bool monitorOnly = false, bool filterProcesses = true)
        {
            CleanPythonCache();
            _listsPath = listsPath;
            string pythonExe = _pythonEnvManager.GetVenvPythonPath();
            if (string.IsNullOrEmpty(pythonExe) || !File.Exists(pythonExe))
                throw new Exception("Виртуальное окружение Python не найдено.");

            string monitorScript = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "monitor.py");
            if (!File.Exists(monitorScript))
                throw new Exception($"Скрипт монитора не найден: {monitorScript}");

            var psi = new ProcessStartInfo(pythonExe)
            {
                WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            psi.Environment["PYTHONPATH"] = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data");

            // === ГЛАВНОЕ: передаём аргументы через ArgumentList ===
            psi.ArgumentList.Add(monitorScript);
            psi.ArgumentList.Add("--game");
            psi.ArgumentList.Add(gameId);  // гарантированно передаётся как отдельный аргумент

            if (!string.IsNullOrEmpty(listsPath))
            {
                psi.ArgumentList.Add("--lists-path");
                psi.ArgumentList.Add(listsPath);
            }

            if (monitorOnly)
                psi.ArgumentList.Add("--monitor-only");

            if (!filterProcesses)
                psi.ArgumentList.Add("--no-filter-processes");

            _process = new Process { StartInfo = psi };
            _process.OutputDataReceived += (s, e) => { if (e.Data != null) OutputReceived?.Invoke(e.Data); };
            _process.ErrorDataReceived += (s, e) => { if (e.Data != null) OutputReceived?.Invoke(e.Data); };
            _process.EnableRaisingEvents = true;
            _process.Exited += (s, e) => ProcessExited?.Invoke(_process.ExitCode);

            try
            {
                _processKiller.KillPythonVenvProcesses();
                _process.Start();
                _process.BeginOutputReadLine();
                _process.BeginErrorReadLine();
                await Task.Delay(500);
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 5)
            {
                throw new UnauthorizedAccessException("Недостаточно прав для запуска Python.", ex);
            }
        }

        public async Task StopAsync(int timeoutMs = 2000)
        {
            if (_process == null || _process.HasExited) return;

            _logger.Info("Остановка Python-процесса...");
            // создаём флаг
            if (!string.IsNullOrEmpty(_listsPath))
            {
                var flagPath = Path.Combine(_listsPath, ".stop_monitor");
                try { await File.WriteAllTextAsync(flagPath, "stop"); }
                catch (Exception ex) { _logger.Warning($"Не удалось создать флаг: {ex.Message}"); }
            }

            try
            {
                if (_process.StandardInput.BaseStream.CanWrite)
                {
                    await _process.StandardInput.WriteLineAsync("exit");
                    _logger.Info("Отправлена команда exit");
                }

                var exitTask = _process.WaitForExitAsync();
                if (await Task.WhenAny(exitTask, Task.Delay(timeoutMs)) != exitTask)
                {
                    _logger.Warning($"Таймаут {timeoutMs} мс, принудительное убийство");
                    _process.Kill(entireProcessTree: true);
                    await _process.WaitForExitAsync();
                    _logger.Info("Процесс убит");
                }
                else
                    _logger.Info("Процесс завершился корректно");
            }
            catch (InvalidOperationException)
            {
                _logger.Info("Процесс уже завершён");
            }
            catch (Exception ex)
            {
                _logger.Error("Ошибка остановки", ex);
                try { _process.Kill(entireProcessTree: true); }
                catch (Exception killEx) { _logger.Warning($"Не удалось убить: {killEx.Message}"); }
            }
            finally
            {
                // удаляем флаг
                if (!string.IsNullOrEmpty(_listsPath))
                {
                    var flagPath = Path.Combine(_listsPath, ".stop_monitor");
                    try { if (File.Exists(flagPath)) File.Delete(flagPath); }
                    catch (Exception ex) { _logger.Warning($"Не удалось удалить флаг: {ex.Message}"); }
                }
                _process?.Dispose();
                _process = null;
                _logger.Info("Python-процесс освобождён");
            }
        }
    }
}