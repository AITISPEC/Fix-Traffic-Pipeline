using System;
using System.Diagnostics;
using System.Linq;
using PlatformLauncher.Core.Interfaces;

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
            // Process.GetProcessesByName — асинхронно не работает, блокирует до получения списка.
            var processes = Process.GetProcessesByName("python");
            foreach (var proc in processes)
            {
                using (proc)
                {
                    try
                    {
                        // Double-check pattern: если процесс уже завершён между проверкой и Kill(), не пытаемся убить снова.
                        if (proc.HasExited) continue;

                        string? fileName = proc.MainModule?.FileName;
                        // Идентификация процесса через .venv в имени модуля — гарантирует, что это наше окружение.
                        if (!string.IsNullOrEmpty(fileName) && fileName.Contains(".venv", StringComparison.OrdinalIgnoreCase))
                        {
                            proc.Kill(entireProcessTree: true);  // Убиваем дерево процессов (потомки тоже).
                            proc.WaitForExit(2000);  // Ждём завершения — узкое место.
                            _logger.Info($"Убит python (venv) PID {proc.Id}");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Warning($"Ошибка при завершении python PID {proc.Id}: {ex.Message}");
                    }
                }
            }
        }

        /// <summary>Убийство процесса winws.exe по конкретному пути — узкое место: WaitforExit(2000).</summary>
        public void KillWinwsProcess(string expectedPath)
        {
            // Process.GetProcessesByName("winws") — поиск всех процессов с именем winws (включая запущенные напрямую).
            var processes = Process.GetProcessesByName("winws");
            foreach (var proc in processes)
            {
                using (proc)
                {
                    try
                    {
                        if (proc.HasExited) continue;

                        string? exePath = proc.MainModule?.FileName;
                        // Точное совпадение по пути — гарантируем, что убиваем наш winws.exe.
                        if (string.Equals(exePath, expectedPath, StringComparison.OrdinalIgnoreCase))
                        {
                            proc.Kill();  // Жёсткое завершение без таймаута — может повлиять на зависшие процессы.
                            proc.WaitForExit(2000);
                            _logger.Info($"Убит winws.exe PID {proc.Id}");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Warning($"Ошибка при завершении winws PID {proc.Id}: {ex.Message}");
                    }
                }
            }
        }

        /// <summary>Убийство всех управляемых процессов — узкое место: параллельные вызовы KillPythonVenvProcesses() и killWinwsProcess().</summary>
        public void KillAllManagedProcesses()
        {
            KillPythonVenvProcesses();  // Убивает все python-процессы venv.
            // Дополнительно можно убить winws по общему пути, но конкретный путь должен быть передан отдельно.
        }
    }
}