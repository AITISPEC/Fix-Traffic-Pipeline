using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Text;

namespace PlatformLauncher.Services
{
    public class PortsManager
    {
        private readonly string _gameId;

        public PortsManager(string gameId)
        {
            _gameId = gameId;
        }

        // ----- Публичные методы с перегрузками -----

        public async Task<(bool Success, string Error)> AddRulesAsync(List<object> tcpPorts, List<object> udpPorts)
        {
            return await AddRulesAsync(tcpPorts, udpPorts, null);
        }

        public async Task<(bool Success, string Error)> AddRulesAsync(List<object> tcpPorts, List<object> udpPorts, Action<string> progressCallback)
        {
            try
            {
                int total = (tcpPorts?.Count ?? 0) + (udpPorts?.Count ?? 0);
                int done = 0;
                progressCallback?.Invoke($"📌 Установка правил портов (всего {total} записей)...");

                if (tcpPorts != null)
                {
                    foreach (var port in tcpPorts)
                    {
                        string portStr = port.ToString();
                        LauncherLogger.Info($"Добавление TCP {portStr}");
                        progressCallback?.Invoke($"   TCP {portStr}...");
                        await AddSingleRuleAsync(portStr, "TCP");
                        done++;
                        progressCallback?.Invoke($"   Прогресс: {done}/{total}");
                    }
                }

                if (udpPorts != null)
                {
                    foreach (var port in udpPorts)
                    {
                        string portStr = port.ToString();
                        LauncherLogger.Info($"Добавление UDP {portStr}");
                        progressCallback?.Invoke($"   UDP {portStr}...");
                        await AddSingleRuleAsync(portStr, "UDP");
                        done++;
                        progressCallback?.Invoke($"   Прогресс: {done}/{total}");
                    }
                }

                LauncherLogger.Info("Все правила портов добавлены");
                progressCallback?.Invoke("✅ Правила портов добавлены");
                return (true, null);
            }
            catch (Exception ex)
            {
                LauncherLogger.Error($"Ошибка добавления правил: {ex}");
                progressCallback?.Invoke($"❌ Ошибка: {ex.Message}");
                return (false, ex.Message);
            }
        }

        public async Task<(bool Success, string Error)> RemoveAllRulesAsync()
        {
            return await RemoveAllRulesAsync(null);
        }

        public async Task<(bool Success, string Error)> RemoveAllRulesAsync(Action<string> progressCallback)
        {
            try
            {
                progressCallback?.Invoke($"📌 Удаление всех правил для {_gameId} через PowerShell...");
                string psCmd = $"Get-NetFirewallRule -DisplayName \"GameFix_{_gameId}_*\" | Remove-NetFirewallRule";
                string args = $"-NoProfile -Command \"{psCmd}\"";
                LauncherLogger.Info($"PowerShell: {args}");
                await RunPowerShellAsync(args);
                progressCallback?.Invoke("✅ Все правила удалены");
                return (true, null);
            }
            catch (Exception ex)
            {
                LauncherLogger.Error($"Ошибка удаления через PowerShell: {ex}");
                progressCallback?.Invoke($"❌ Ошибка: {ex.Message}");
                return (false, ex.Message);
            }
        }

        // ----- Вспомогательные методы -----

        private async Task AddSingleRuleAsync(string portSpec, string protocol)
        {
            string name = $"GameFix_{_gameId}_{protocol}_{portSpec}";
            foreach (string dir in new[] { "in", "out" })
            {
                string args = $"advfirewall firewall add rule name=\"{name}\" dir={dir} action=allow protocol={protocol} localport={portSpec}";
                LauncherLogger.Info($"netsh {args}");
                await RunNetshAsync(args);
            }
        }

        private async Task RunNetshAsync(string arguments)
        {
            var psi = new ProcessStartInfo("netsh", arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            using var process = Process.Start(psi);
            if (process == null)
                throw new Exception("Не удалось запустить netsh");

            string output = await process.StandardOutput.ReadToEndAsync();
            string error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
            {
                LauncherLogger.Error($"netsh exit {process.ExitCode}, error: {error}");
                throw new Exception($"netsh failed: {error}");
            }
            LauncherLogger.Info($"netsh output: {output}");
        }

        private async Task RunPowerShellAsync(string arguments)
        {
            var psi = new ProcessStartInfo("powershell", arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            using var process = Process.Start(psi);
            if (process == null)
                throw new Exception("Не удалось запустить PowerShell");

            string output = await process.StandardOutput.ReadToEndAsync();
            string error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
            {
                LauncherLogger.Error($"PowerShell exit {process.ExitCode}, error: {error}");
                throw new Exception($"PowerShell failed: {error}");
            }
            LauncherLogger.Info($"PowerShell output: {output}");
        }
    }
}