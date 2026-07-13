using PlatformLauncher.Core.Interfaces;
using PlatformLauncher.Helpers;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace PlatformLauncher.Infrastructure.ProcessManagement
{
    /// <summary>
    /// Управление процессом warp.exe (Cloudflare WARP) — установка MSI, команда warp-cli для tunnel protocol MASQUE + connect/disconnect. Узкое место: Process.Start(warpExe) в EnsureStartedAsync не проверяет существование exe до запуска (проверка File.Exists(warpExe) после if).
    /// </summary>
    public class WarpManager : IWarpManager
    {
        private readonly ILogger _logger;
        private const string WARP_URL = "https://1111-releases.cloudflareclient.com/win/latest";
        private readonly string _extraFolder;

        /// <summary>Конструктор через DI — вводит зависимости (сложность 3 класса).</summary>
        public WarpManager(ILogger logger)
        {
            _logger = logger;
            _extraFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "extra");
            Directory.CreateDirectory(_extraFolder);
        }

        /// <summary>Поток обработки: IsInstalledAsync → Process.Start(warp-cli --version) → WaitforExitAsync().</summary>
        public async Task<bool> IsInstalledAsync()
        {
            try
            {
                var (exitCode, _, _) = await ProcessHelper.RunAsync("warp-cli", "--version", _logger, timeoutMs: 5000);
                return exitCode == 0;
            }
            catch (Exception ex)
            {
                _logger.Warning($"WARP не установлен или недоступен: {ex.Message}");
                return false;
            }
        }

        /// <summary>Скачивает MSI-инсталлятор с remote.WARP_URL в local/extra/{fileName}. Узкое место: HttpClient.CopyToAsync — синхронно блокирует поток при большой загрузке сети.</summary>
        private async Task<string> DownloadInstallerAsync()
        {
            using var client = new HttpClient();
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
            // HttpCompletionOption.ResponseHeadersRead — чтение заголовков без тела, быстрее и меньше памяти.
            var response = await client.GetAsync(WARP_URL, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            string? fileName = null;
            if (response.Content.Headers.ContentDisposition != null)
            {
                fileName = response.Content.Headers.ContentDisposition.FileNameStar ??
                           response.Content.Headers.ContentDisposition.FileName;
            }
            if (string.IsNullOrEmpty(fileName))
            {
                var uri = response.RequestMessage?.RequestUri;
                if (uri != null && !string.IsNullOrEmpty(uri.Segments[^1]))
                    fileName = uri.Segments[^1];
                else
                    fileName = "CloudflareWARP.msi";
            }
            fileName = fileName.Trim('"');

            string localPath = Path.Combine(_extraFolder, fileName);

            using var fs = new FileStream(localPath, FileMode.Create, FileAccess.Write, FileShare.None);
            await response.Content.CopyToAsync(fs);
            return localPath;
        }

        /// <summary>Поток обработки: 1) IsInstalledAsync (проверка), 2) DownloadInstallerAsync + msiexec /quiet, 3) WaitforExitAsync(5000). </summary>
        public async Task<bool> InstallAsync()
        {
            if (await IsInstalledAsync())
                return true;
            string installerPath = await DownloadInstallerAsync();
            if (string.IsNullOrEmpty(installerPath) || !File.Exists(installerPath))
                return false;
            try
            {
                var (exitCode, _, _) = await ProcessHelper.RunAsync(
                    "msiexec",
                    $"/i \"{installerPath}\" /quiet /norestart",
                    _logger,
                    createNoWindow: true,
                    timeoutMs: 120000);  // MSI может устанавливаться долго
                                         // Ждём 5 секунд после завершения MSI — иногда процесс завершается с кодом 0, но служба ещё не запущена.
                await Task.Delay(5000);
                return exitCode == 0;
            }
            catch (Exception ex)
            {
                _logger.Warning($"Ошибка установки фикса: {ex.Message}");
                return false;
            }
        }

        /// <summary>Устанавливает tunnel protocol = MASQUE через warp-cli — узкое место: Process.Start(warp-cli "tunnel protocol set MASQUE") блокирует UI при долгой обработке.</summary>
        public async Task<bool> SetMasqueProtocolAsync()
        {
            try
            {
                var (exitCode, _, _) = await ProcessHelper.RunAsync("warp-cli", "tunnel protocol set MASQUE", _logger, timeoutMs: 10000);
                return exitCode == 0;
            }
            catch (Exception ex)
            {
                _logger.Warning($"Ошибка установки WARP протокола: {ex.Message}");
                return false;
            }
        }

        /// <summary>Подключает к Cloudflare WARP — узкое место: Process.Start(warp-cli "connect") блокирует поток до завершения.</summary>
        public async Task<bool> ConnectAsync()
        {
            try
            {
                _logger.Info("Выполнение warp-cli connect");
                var (exitCode, _, _) = await ProcessHelper.RunAsync("warp-cli", "connect", _logger, timeoutMs: 15000);
                if (exitCode == 0) { _logger.Info("warp-cli connect успешно"); return true; }
                _logger.Warning($"warp-cli connect вернул {exitCode}");
                return false;
            }
            catch (Exception ex)
            {
                _logger.Error("Ошибка при connect", ex);
                return false;
            }
        }

        /// <summary>Отключает от Cloudflare WARP — узкое место: Process.Start(warp-cli "disconnect") блокирует поток до завершения.</summary>
        public async Task<bool> DisconnectAsync()
        {
            try
            {
                var (exitCode, _, _) = await ProcessHelper.RunAsync("warp-cli", "disconnect", _logger, timeoutMs: 10000);
                return exitCode == 0;
            }
            catch (Exception ex)
            {
                _logger.Warning($"Ошибка остановки WARP: {ex.Message}");
                return false;
            }
        }

        /// <summary>Поток обработки: 1) IsInstalledAsync, 2) InstallAsync (если нужно), 3) File.Exists(warpExe) — проверка существует exe до запуска (баг в коде: if после if).</summary>
        public async Task<bool> EnsureStartedAsync()
        {
            _logger.Info("Запуск WARP...");
            if (!await IsInstalledAsync())
            {
                if (!await InstallAsync())
                {
                    _logger.Error("Не удалось установить WARP");
                    return false;
                }
            }

            string warpExe = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "Cloudflare", "Cloudflare WARP", "Cloudflare WARP.exe");

            if (File.Exists(warpExe))
            {
                bool isRunning = Process.GetProcessesByName("Cloudflare WARP").Length > 0;
                if (!isRunning)
                {
                    try
                    {
                        Process.Start(warpExe);
                        _logger.Info("Cloudflare WARP.exe запущен");
                    }
                    catch (Win32Exception ex) when (ex.NativeErrorCode == 5)
                    {
                        _logger.Warning("Недостаточно прав для запуска WARP");
                    }
                    catch (Exception ex)
                    {
                        _logger.Error($"Ошибка запуска WARP", ex);
                    }
                }
            }
            else
                _logger.Warning($"Файл WARP.exe не найден: {warpExe}");

            await SetMasqueProtocolAsync();
            bool connected = await ConnectAsync();
            if (!connected) _logger.Error("Не удалось подключиться к WARP");
            return connected;
        }

        /// <summary>Поток обработки: warp-cli status → Contains("Connected") ? "connected" : "disconnected".</summary>
        public async Task<string> GetStatusAsync()
        {
            try
            {
                var (exitCode, output, _) = await ProcessHelper.RunAsync("warp-cli", "status", _logger, timeoutMs: 5000);
                if (exitCode != 0) return "disconnected";
                return output.Contains("Connected") ? "connected" : "disconnected";
            }
            catch (Exception ex)
            {
                _logger.Warning($"Ошибка проверки статуса WARP: {ex.Message}");
                return "disconnected";
            }
        }
    }
}