using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using PlatformLauncher.Core.Interfaces;

namespace PlatformLauncher.Infrastructure.Network
{
    public interface IZapretManager
    {
        Task<string> EnsureZapretInstalledAsync();
    }

    public class ZapretManager : IZapretManager
    {
        private readonly ILogger _logger;
        private readonly string _extraDir;
        private readonly string _zapretZip;
        private readonly string _runtimeDir;
        private readonly string _zapretTargetDir;

        public ZapretManager(ILogger logger)
        {
            _logger = logger;
            _extraDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "extra");
            _zapretZip = Path.Combine(_extraDir, "zdy.zip");
            _runtimeDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "runtimes");
            _zapretTargetDir = Path.Combine(_runtimeDir, "zdy");
        }

        public async Task<string> EnsureZapretInstalledAsync()
        {
            var existingWinws = Directory.GetFiles(_zapretTargetDir, "winws.exe", SearchOption.AllDirectories).FirstOrDefault();
            if (!string.IsNullOrEmpty(existingWinws))
            {
                _logger.Info($"winws.exe уже найден в {existingWinws}");
                return GetListsPath(existingWinws);
            }

            if (!File.Exists(_zapretZip))
                throw new FileNotFoundException("Архив zapret не найден в папке extra", _zapretZip);

            _logger.Info("Начинаем распаковку zapret из " + _zapretZip);

            if (Directory.Exists(_zapretTargetDir))
                Directory.Delete(_zapretTargetDir, true);
            Directory.CreateDirectory(_zapretTargetDir);

            await Task.Run(() => ZipFile.ExtractToDirectory(_zapretZip, _zapretTargetDir));

            var found = Directory.GetFiles(_zapretTargetDir, "winws.exe", SearchOption.AllDirectories).FirstOrDefault();
            if (string.IsNullOrEmpty(found))
                throw new Exception("После распаковки не найден winws.exe ни в одной папке");

            _logger.Info($"Распаковка завершена, winws.exe найден в {found}");

            return GetListsPath(found);
        }

        private string GetListsPath(string winwsPath)
        {
            var baseDir = Path.GetDirectoryName(winwsPath);
            if (string.IsNullOrEmpty(baseDir))
                throw new Exception("Не удалось определить папку для winws.exe");

            string[] candidates = {
                Path.Combine(baseDir, "lists"),
                Path.Combine(baseDir, "..", "lists"),
                Path.Combine(baseDir, "config", "lists"),
                Path.Combine(baseDir, "..", "config", "lists")
            };

            foreach (var candidate in candidates)
            {
                string fullPath = Path.GetFullPath(candidate);
                if (Directory.Exists(fullPath))
                {
                    _logger.Info($"Папка lists найдена: {fullPath}");
                    return fullPath;
                }
            }

            var defaultLists = Path.GetFullPath(Path.Combine(baseDir, "..", "lists"));
            Directory.CreateDirectory(defaultLists);
            _logger.Info($"Папка lists создана: {defaultLists}");
            return defaultLists;
        }
    }
}