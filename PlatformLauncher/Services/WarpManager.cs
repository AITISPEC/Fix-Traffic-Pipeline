using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Net.Http.Headers;

namespace PlatformLauncher.Services
{
    public static class WarpManager
    {
        // ИЗМЕНЕНИЕ: новый URL для загрузки
        private const string WARP_URL = "https://1111-releases.cloudflareclient.com/win/latest";

        // ИЗМЕНЕНИЕ: папка для сохранения установщика
        private static readonly string ExtraFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "extra");

        static WarpManager()
        {
            Directory.CreateDirectory(ExtraFolder);
        }

        public static async Task<bool> IsInstalledAsync()
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
            catch
            {
                return false;
            }
        }

        // ИЗМЕНЕНИЕ: метод загрузки с сохранением оригинального имени файла
        private static async Task<string> DownloadInstallerAsync()
        {
            using var client = new HttpClient();
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
            var response = await client.GetAsync(WARP_URL, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            // Получаем имя файла из Content-Disposition или из последнего сегмента URL
            string fileName = null;
            if (response.Content.Headers.ContentDisposition != null)
            {
                fileName = response.Content.Headers.ContentDisposition.FileNameStar ??
                           response.Content.Headers.ContentDisposition.FileName;
            }
            if (string.IsNullOrEmpty(fileName))
            {
                // Пытаемся извлечь из URL (после редиректа)
                var uri = response.RequestMessage?.RequestUri;
                if (uri != null && !string.IsNullOrEmpty(uri.Segments[^1]))
                    fileName = uri.Segments[^1];
                else
                    fileName = "CloudflareWARP.msi"; // запасное имя
            }
            // Очищаем от кавычек
            fileName = fileName.Trim('"');

            string localPath = Path.Combine(ExtraFolder, fileName);

            // Скачиваем
            using var fs = new FileStream(localPath, FileMode.Create, FileAccess.Write, FileShare.None);
            await response.Content.CopyToAsync(fs);
            return localPath;
        }

        public static async Task<bool> InstallAsync()
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
                // Не удаляем установщик, чтобы можно было переустановить
                return p.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }

        public static async Task<bool> SetMasqueProtocolAsync()
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
            catch
            {
                return false;
            }
        }

        public static async Task<bool> ConnectAsync()
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
            catch
            {
                return false;
            }
        }

        public static async Task<bool> DisconnectAsync()
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
            catch
            {
                return false;
            }
        }

        public static async Task<bool> EnsureStartedAsync()
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

        public static async Task<string> GetStatusAsync()
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
            catch
            {
                return "disconnected";
            }
        }
    }
}