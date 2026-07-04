using PlatformLauncher.Core.Interfaces;
using PlatformLauncher.Infrastructure.Services;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace PlatformLauncher.Infrastructure.Network
{
    /// <summary>
    /// Локализатор Zapret — находит папку lists по трём уровням поиска:
    /// 1) через процесс winws.exe (используется если запущен),
    /// 2) ./zdy/lists в корне приложения,
    /// 3) возвращает null.
    /// </summary>
    public class WinwsLocator : IWinwsLocator
    {
        private readonly ILogger _logger;
        private readonly IZapretManager _zapretManager;

        public WinwsLocator(ILogger logger, IZapretManager zapretManager)
        {
            _logger = logger;
            _zapretManager = zapretManager;
        }

        public async Task<string> FindListsPathAsync(int timeoutMs = 3000)
        {
            try
            {
                // 1. Поиск через процесс winws
                var task = Task.Run(() =>
                {
                    try
                    {
                        var processes = Process.GetProcessesByName("winws");
                        if (processes.Length == 0) return null;
                        string exePath = processes[0].MainModule?.FileName;
                        if (string.IsNullOrEmpty(exePath)) return null;
                        var dir = Directory.GetParent(exePath);
                        if (dir == null) return null;
                        dir = Directory.GetParent(dir.FullName);
                        if (dir == null) return null;
                        string listsPath = Path.Combine(dir.FullName, "lists");
                        if (!Directory.Exists(listsPath)) return null;
                        if (!Directory.GetFiles(listsPath, "*.txt").Any()) return null;
                        return listsPath;
                    }
                    catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 5)
                    {
                        _logger.Warning("Нет доступа к процессу winws.");
                        return null;
                    }
                    catch (Exception ex)
                    {
                        _logger.Error($"Ошибка при поиске winws: {ex.Message}");
                        return null;
                    }
                });

                if (await Task.WhenAny(task, Task.Delay(timeoutMs)) == task)
                {
                    var found = await task;
                    if (found != null) return found;
                }

                // 2. Проверка папки ./zdy/lists в корне приложения
                string zdyLists = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "zdy", "lists");
                if (Directory.Exists(zdyLists))
                {
                    _logger.Info($"Найдена папка lists в ./zdy: {zdyLists}");
                    return zdyLists;
                }

                // 3. Не найдено — возвращаем null (распаковка только по кнопке "Установить Zapret")
                _logger.Info("winws не найден, папка ./zdy/lists отсутствует. Требуется установка Zapret.");
                return null;
            }
            catch (Exception ex)
            {
                _logger.Error($"Ошибка при поиске lists: {ex.Message}");
                return null;
            }
        }

        public string FindListsPath(int timeoutMs = 3000)
        {
            return FindListsPathAsync(timeoutMs).GetAwaiter().GetResult();
        }
    }
}