using PlatformLauncher.Core.Interfaces;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace PlatformLauncher.Infrastructure.ProcessManagement
{
    /// <summary>
    /// Убийство зависших процессов (python, winws.exe) — узкое место: Process.WaitForExit(2000) блокирует на 2 секунды каждый процесс. Дублирующиеся catch(Exception ex): последняя ветка "съедает" любые ошибки, включая InvalidOperationException.
    /// </summary>
    public class ProcessKiller : IProcessKiller
    {
        private readonly ILogger _logger;

        /// <summary>Конструктор через DI — вводит зависимости (сложность 1 класса).</summary>
        public ProcessKiller(ILogger logger)
        {
            _logger = logger;
        }

        /// <summary>Поток обработки: GetProcessesByName("python") → foreach → Kill(entireProcessTree=true). </summary>
        public void KillPythonVenvProcesses()
        {
            var processes = Process.GetProcessesByName("python")
                .Where(p => !p.HasExited && p.MainModule?.FileName?.Contains(".venv", StringComparison.OrdinalIgnoreCase) == true)
                .ToList();
            if (!processes.Any()) return;
            foreach (var p in processes)
            {
                try
                {
                    p.Kill(entireProcessTree: true);
                    _logger.Info($"Процесс Python {p.Id} убит");
                }
                catch (Win32Exception ex) when (ex.NativeErrorCode == 5)
                {
                    _logger.Warning($"Нет прав на убийство Python {p.Id}");
                }
                catch (InvalidOperationException) { }
                catch (Exception ex)
                {
                    _logger.Error($"Ошибка убийства Python {p.Id}", ex);
                }
            }
            Task.WaitAll(processes.Select(p => p.WaitForExitAsync()).ToArray(), 2000);
        }

        /// <summary>Убийство процесса winws.exe по конкретному пути — узкое место: WaitforExit(2000).</summary>
        public void KillWinwsProcess(string expectedPath)
        {
            var processes = Process.GetProcessesByName("winws")
                .Where(p => !p.HasExited && string.Equals(p.MainModule?.FileName, expectedPath, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (!processes.Any()) return;
            foreach (var p in processes)
            {
                try
                {
                    p.Kill();
                    _logger.Info($"Процесс winws {p.Id} убит");
                }
                catch (Win32Exception ex) when (ex.NativeErrorCode == 5)
                {
                    _logger.Warning($"Нет прав на убийство winws {p.Id}");
                }
                catch (InvalidOperationException) { }
                catch (Exception ex)
                {
                    _logger.Error($"Ошибка убийства winws {p.Id}", ex);
                }
            }
            Task.WaitAll(processes.Select(p => p.WaitForExitAsync()).ToArray(), 2000);
        }

        /// <summary>Убийство всех управляемых процессов — узкое место: параллельные вызовы KillPythonVenvProcesses() и killWinwsProcess().</summary>
        public void KillAllManagedProcesses()
        {
            KillPythonVenvProcesses();  // Убивает все python-процессы venv.
            // Дополнительно можно убить winws по общему пути, но конкретный путь должен быть передан отдельно.
        }
    }
}