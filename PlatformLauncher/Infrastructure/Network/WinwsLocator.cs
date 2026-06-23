using PlatformLauncher.Core.Interfaces;
using PlatformLauncher.Infrastructure.Services;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace PlatformLauncher.Infrastructure.Network
{
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
                        _logger.Warning("Нет доступа к процессу winws. Запустите лаунчер от имени администратора для автоматического поиска папки lists.");
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

                // 2. Если не найден – распаковываем zapret из extra
                _logger.Info("winws не найден, распаковываем zapret из extra/zdy.zip...");
                return await _zapretManager.EnsureZapretInstalledAsync();
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