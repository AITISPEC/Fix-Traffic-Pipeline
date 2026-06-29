using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using PlatformLauncher.Core.Interfaces;

namespace PlatformLauncher.Infrastructure.ProcessManagement
{
    public class WarpManager : IWarpManager
    {
        private readonly ILogger _logger;
        private const string WARP_URL = "https://1111-releases.cloudflareclient.com/win/latest";
        private readonly string _extraFolder;

        public WarpManager(ILogger logger)
        {
            _logger = logger;
            _extraFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "extra");
            Directory.CreateDirectory(_extraFolder);
        }

        public async Task<bool> IsInstalledAsync()
        {
            try
            {
                var psi = new ProcessStartInfo("warp-cli", "--version")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true
                };
                using var p = Process.Start(psi);
                await p.WaitForExitAsync();
                return p.ExitCode == 0;
            }
            catch (Exception ex)
            {
                _logger.Warning($"WARP не установлен или недоступен: {ex.Message}");
                return false;
            }
        }

        private async Task<string> DownloadInstallerAsync()
        {
            using var client = new HttpClient();
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
            var response = await client.GetAsync(WARP_URL, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            string fileName = null;
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

        public async Task<bool> InstallAsync()
        {
            if (await IsInstalledAsync())
                return true;

            string installerPath = await DownloadInstallerAsync();
            if (string.IsNullOrEmpty(installerPath) || !File.Exists(installerPath))
                return false;

            try
            {
                var psi = new ProcessStartInfo("msiexec", $"/i \"{installerPath}\" /quiet /norestart")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var p = Process.Start(psi);
                await p.WaitForExitAsync();
                await Task.Delay(5000);
                return p.ExitCode == 0;
            }
            catch (Exception ex)
            {
                _logger.Warning($"Ошибка установки фикса: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> SetMasqueProtocolAsync()
        {
            try
            {
                var psi = new ProcessStartInfo("warp-cli", "tunnel protocol set MASQUE")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true
                };
                using var p = Process.Start(psi);
                await p.WaitForExitAsync();
                return p.ExitCode == 0;
            }
            catch (Exception ex)
            {
                _logger.Warning($"Ошибка установки WARP протокола: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> ConnectAsync()
        {
            try
            {
                var psi = new ProcessStartInfo("warp-cli", "connect")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true
                };
                using var p = Process.Start(psi);
                await p.WaitForExitAsync();
                return p.ExitCode == 0;
            }
            catch (Exception ex)
            {
                _logger.Warning($"Ошибка запуска WARP: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> DisconnectAsync()
        {
            try
            {
                var psi = new ProcessStartInfo("warp-cli", "disconnect")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true
                };
                using var p = Process.Start(psi);
                await p.WaitForExitAsync();
                return p.ExitCode == 0;
            }
            catch (Exception ex)
            {
                _logger.Warning($"Ошибка остановки WARP: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> EnsureStartedAsync()
        {
            if (!await IsInstalledAsync())
                if (!await InstallAsync())
                    return false;

            string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string warpExe = Path.Combine(programFiles, "Cloudflare", "Cloudflare WARP", "Cloudflare WARP.exe");
            if (File.Exists(warpExe))
            {
                bool isRunning = Process.GetProcessesByName("Cloudflare WARP").Length > 0;
                if (!isRunning)
                    Process.Start(warpExe);
            }

            await SetMasqueProtocolAsync();
            return await ConnectAsync();
        }

        public async Task<string> GetStatusAsync()
        {
            try
            {
                var psi = new ProcessStartInfo("warp-cli", "status")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    StandardOutputEncoding = System.Text.Encoding.UTF8
                };
                using var p = Process.Start(psi);
                string output = await p.StandardOutput.ReadToEndAsync();
                await p.WaitForExitAsync();
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