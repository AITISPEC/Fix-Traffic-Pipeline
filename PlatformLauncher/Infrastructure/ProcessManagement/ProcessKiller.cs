using System;
using System.Diagnostics;
using System.Linq;
using PlatformLauncher.Core.Interfaces;

namespace PlatformLauncher.Infrastructure.ProcessManagement
{
    public class ProcessKiller : IProcessKiller
    {
        private readonly ILogger _logger;

        public ProcessKiller(ILogger logger)
        {
            _logger = logger;
        }

        public void KillPythonVenvProcesses()
        {
            var processes = Process.GetProcessesByName("python");
            foreach (var proc in processes)
            {
                using (proc)
                {
                    try
                    {
                        if (proc.HasExited) continue;
                        string fileName = proc.MainModule?.FileName;
                        if (!string.IsNullOrEmpty(fileName) && fileName.Contains(".venv", StringComparison.OrdinalIgnoreCase))
                        {
                            proc.Kill(entireProcessTree: true);
                            proc.WaitForExit(2000);
                            _logger.Info($"Убит python (venv) PID {proc.Id}");
                        }
                    }
                    catch (Exception ex) when (ex is InvalidOperationException || ex is System.ComponentModel.Win32Exception)
                    {
                        _logger.Warning($"Ошибка при завершении python PID {proc.Id}: {ex.Message}");
                    }
                    catch (Exception ex)
                    {
                        _logger.Warning($"Ошибка при завершении python PID {proc.Id}: {ex.Message}");
                    }
                }
            }
        }

        public void KillWinwsProcess(string expectedPath)
        {
            var processes = Process.GetProcessesByName("winws");
            foreach (var proc in processes)
            {
                using (proc)
                {
                    try
                    {
                        if (proc.HasExited) continue;
                        string exePath = proc.MainModule?.FileName;
                        if (string.Equals(exePath, expectedPath, StringComparison.OrdinalIgnoreCase))
                        {
                            proc.Kill();
                            proc.WaitForExit(2000);
                            _logger.Info($"Убит winws.exe PID {proc.Id}");
                        }
                    }
                    catch (Exception ex) when (ex is InvalidOperationException || ex is System.ComponentModel.Win32Exception)
                    {
                        _logger.Warning($"Ошибка при завершении winws PID {proc.Id}: {ex.Message}");
                    }
                    catch (Exception ex)
                    {
                        _logger.Warning($"Ошибка при завершении winws PID {proc.Id}: {ex.Message}");
                    }
                }
            }
        }

        public void KillAllManagedProcesses()
        {
            KillPythonVenvProcesses();
            // дополнительно можно убить winws по общему пути
            // но конкретный путь должен быть передан отдельно
        }
    }
}