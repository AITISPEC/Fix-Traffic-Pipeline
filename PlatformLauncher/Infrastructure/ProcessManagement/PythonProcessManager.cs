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
        private Process _process;

        public event Action<string> OutputReceived;
        public event Action<int> ProcessExited;

        public bool IsRunning => _process != null && !_process.HasExited;

        public PythonProcessManager(ILogger logger, IProcessKiller processKiller, IPythonEnvironmentManager pythonEnvManager)
        {
            _logger = logger;
            _processKiller = processKiller;
            _pythonEnvManager = pythonEnvManager; // <-- СОХРАНЯЕМ
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

            string pythonExe = _pythonEnvManager.GetVenvPythonPath(); // <-- ИСПРАВЛЕНО: через экземпляр
            if (string.IsNullOrEmpty(pythonExe) || !File.Exists(pythonExe))
                throw new Exception("Виртуальное окружение Python не найдено.");

            string monitorScript = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "monitor.py");
            if (!File.Exists(monitorScript))
                throw new Exception($"Скрипт монитора не найден: {monitorScript}");

            string args = $"\"{monitorScript}\" --game {gameId} --lists-path \"{listsPath}\"";
            if (monitorOnly)
                args += " --monitor-only";
            if (!filterProcesses)
                args += " --no-filter-processes";

            var psi = new ProcessStartInfo(pythonExe, args)
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

            _process = new Process { StartInfo = psi };
            _process.OutputDataReceived += (s, e) => OutputReceived?.Invoke(e.Data);
            _process.ErrorDataReceived += (s, e) => OutputReceived?.Invoke(e.Data);
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

            try
            {
                _process.StandardInput.WriteLine("exit");
                var exitTask = _process.WaitForExitAsync();
                if (await Task.WhenAny(exitTask, Task.Delay(timeoutMs)) != exitTask)
                {
                    _process.Kill(entireProcessTree: true);
                    await _process.WaitForExitAsync();
                }
            }
            catch (InvalidOperationException)
            {
                // Процесс уже завершился – игнорируем
            }
            catch (Exception ex)
            {
                _logger.Error($"Ошибка при остановке Python: {ex}");
                try { _process.Kill(); } catch { }
            }
            finally
            {
                _process?.Dispose();
                _process = null;
            }
        }
    }
}