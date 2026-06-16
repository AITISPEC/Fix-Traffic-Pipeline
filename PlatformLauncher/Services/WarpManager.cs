using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;

namespace PlatformLauncher.Services
{
    public static class WarpManager
    {
        private const string WarpMsiUrl = "https://github.com/AITISPEC/Helpful/releases/download/apex-fix/Cloudflare.msi";

        public static bool IsInstalled()
        {
            try
            {
                var p = Process.Start(new ProcessStartInfo("warp-cli", "--version") { CreateNoWindow = true, RedirectStandardOutput = true });
                p?.WaitForExit(2000);
                return p != null && p.ExitCode == 0;
            }
            catch { return false; }
        }

        public static void EnsureStarted()
        {
            if (!IsInstalled())
            {
                Install();
            }
            StartGui();
            SetMasqueProtocol();
            Connect();
        }

        private static bool IsAdministrator()
        {
            using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            var principal = new System.Security.Principal.WindowsPrincipal(identity);
            return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }

        private static void Install()
        {
            if (!IsAdministrator())
            {
                var psi = new ProcessStartInfo(System.Reflection.Assembly.GetExecutingAssembly().Location)
                {
                    Verb = "runas",
                    UseShellExecute = true
                };
                Process.Start(psi);
                Environment.Exit(0);
                return;
            }

            string tempMsi = Path.GetTempFileName() + ".msi";
            using (var client = new HttpClient())
            {
                var response = client.GetAsync(WarpMsiUrl).Result;
                response.EnsureSuccessStatusCode();
                using (var fs = new FileStream(tempMsi, FileMode.Create))
                {
                    response.Content.CopyToAsync(fs).Wait();
                }
            }

            var installPsi = new ProcessStartInfo("msiexec", $"/i \"{tempMsi}\" /quiet /norestart")
            {
                Verb = "runas",
                UseShellExecute = true
            };
            var p = Process.Start(installPsi);
            p?.WaitForExit();
            if (p?.ExitCode != 0)
            {
                throw new Exception($"Установка WARP завершилась с ошибкой (код {p?.ExitCode}).");
            }
            File.Delete(tempMsi);
        }

        private static void StartGui()
        {
            string progFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string exePath = Path.Combine(progFiles, "Cloudflare", "Cloudflare WARP", "Cloudflare WARP.exe");
            if (File.Exists(exePath))
                Process.Start(exePath);
        }

        private static void SetMasqueProtocol()
        {
            var psi = new ProcessStartInfo("warp-cli", "tunnel protocol set MASQUE") { CreateNoWindow = true };
            Process.Start(psi)?.WaitForExit(2000);
        }

        private static void Connect()
        {
            var psi = new ProcessStartInfo("warp-cli", "connect") { CreateNoWindow = true };
            Process.Start(psi)?.WaitForExit(2000);
        }

        public static void Disconnect()
        {
            var psi = new ProcessStartInfo("warp-cli", "disconnect") { CreateNoWindow = true };
            Process.Start(psi)?.WaitForExit(2000);
        }
    }
}