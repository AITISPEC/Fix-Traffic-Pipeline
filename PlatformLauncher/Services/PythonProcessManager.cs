using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace PlatformLauncher.Services
{
    public class PythonProcessManager
    {
        private Process _process;
        public event Action<string> OutputReceived;
        public event Action<int> ProcessExited;

        public bool IsRunning => _process != null && !_process.HasExited;

        public async Task StartAsync(string gameId, string listsPath, bool warpEnabled, string backupRoot = "./backups", bool monitorOnly = false)
        {
            string pythonExe = PythonEnvironmentManager.GetVenvPythonPath();
            if (string.IsNullOrEmpty(pythonExe) || !File.Exists(pythonExe))
                throw new Exception("Виртуальное окружение Python не найдено.");

            string monitorScript = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "monitor.py");
            if (!File.Exists(monitorScript))
                throw new Exception($"Скрипт монитора не найден: {monitorScript}");

            string args = $"\"{monitorScript}\" --game {gameId} --lists-path \"{listsPath}\" --backup-root {backupRoot}";
            if (warpEnabled) args += " --warp";
            if (monitorOnly) args += " --monitor-only --no-ports"; // мониторинг без портов и бэкапов

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
                _process.Start();
                _process.BeginOutputReadLine();
                _process.BeginErrorReadLine();
                await Task.Delay(500);
            }
            catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 5)
            {
                throw new UnauthorizedAccessException("Недостаточно прав для запуска Python.", ex);
            }
        }

        public async Task StopAsync(int timeoutMs = 10000)
        {
            if (_process == null || _process.HasExited) return;

            try
            {
                _process.StandardInput.WriteLine("exit");
                _process.StandardInput.Flush();

                var exitTask = _process.WaitForExitAsync();
                if (await Task.WhenAny(exitTask, Task.Delay(timeoutMs)) != exitTask)
                {
                    _process.Kill();
                    await _process.WaitForExitAsync();
                }
            }
            catch (Exception ex)
            {
                LauncherLogger.Error($"Ошибка при остановке Python процесса: {ex}");
                try { _process.Kill(); } catch { }
            }
            finally
            {
                _process.Dispose();
                _process = null;
            }
        }
    }
}