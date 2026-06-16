using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace PlatformLauncher.Services
{
    public static class WinwsLocator
    {
        public static async Task<string> FindListsPathAsync(int timeoutMs = 3000)
        {
            try
            {
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
                        LauncherLogger.Warning("Нет доступа к процессу winws. Запустите лаунчер от имени администратора для автоматического поиска папки lists.");
                        return null;
                    }
                    catch (Exception ex)
                    {
                        LauncherLogger.Error($"Ошибка при поиске winws: {ex.Message}");
                        return null;
                    }
                });
                if (await Task.WhenAny(task, Task.Delay(timeoutMs)) == task)
                    return await task;
                else
                    return null;
            }
            catch
            {
                return null;
            }
        }

        public static string FindListsPath(int timeoutMs = 3000)
        {
            return FindListsPathAsync(timeoutMs).GetAwaiter().GetResult();
        }
    }
}