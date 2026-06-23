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
            foreach (var proc in Process.GetProcessesByName("python"))
            {
                try
                {
                    if (proc.HasExited)
                    {
                        proc.Dispose();
                        continue;
                    }

                    string fileName = proc.MainModule?.FileName;
                    if (!string.IsNullOrEmpty(fileName) && fileName.Contains(".venv", StringComparison.OrdinalIgnoreCase))
                    {
                        proc.Kill(entireProcessTree: true);
                        proc.WaitForExit(2000);
                        _logger.Info($"Убит python (venv) PID {proc.Id}");
                    }
                }
                catch (InvalidOperationException)
                {
                    // Процесс уже завершился – игнорируем
                    _logger.Warning($"Процесс python PID {proc.Id} уже завершён");
                }
                catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 5)
                {
                    // Отказано в доступе – возможно, процесс системный
                    _logger.Warning($"Нет доступа к процессу python PID {proc.Id}: {ex.Message}");
                }
                catch (Exception ex)
                {
                    _logger.Warning($"Ошибка при завершении python PID {proc.Id}: {ex.Message}");
                }
                finally
                {
                    try { proc?.Dispose(); } catch { }
                }
            }
        }

        public void KillWinwsProcess(string expectedPath)
        {
            foreach (var proc in Process.GetProcessesByName("winws"))
            {
                try
                {
                    if (proc.HasExited)
                    {
                        proc.Dispose();
                        continue;
                    }

                    string exePath = proc.MainModule?.FileName;
                    if (string.Equals(exePath, expectedPath, StringComparison.OrdinalIgnoreCase))
                    {
                        proc.Kill();
                        proc.WaitForExit(2000);
                        _logger.Info($"Убит winws.exe PID {proc.Id}");
                    }
                }
                catch (InvalidOperationException)
                {
                    _logger.Warning($"Процесс winws PID {proc.Id} уже завершён");
                }
                catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 5)
                {
                    _logger.Warning($"Нет доступа к процессу winws PID {proc.Id}: {ex.Message}");
                }
                catch (Exception ex)
                {
                    _logger.Warning($"Ошибка при завершении winws PID {proc.Id}: {ex.Message}");
                }
                finally
                {
                    try { proc?.Dispose(); } catch { }
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