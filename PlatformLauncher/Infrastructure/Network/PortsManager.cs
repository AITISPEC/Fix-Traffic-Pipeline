using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;
using PlatformLauncher.Core.Interfaces;

namespace PlatformLauncher.Infrastructure.Network
{
    public class PortsManager : IPortsManager
    {
        private readonly ILogger _logger;

        public PortsManager(ILogger logger)
        {
            _logger = logger;
        }

        public async Task<(bool Success, string Error)> AddRulesAsync(
            List<object> tcpPorts,
            List<object> udpPorts,
            string gameId,
            Action<string> progressCallback = null)
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
                        _logger.Info($"Добавление TCP {portStr} для {gameId}");
                        progressCallback?.Invoke($"   TCP {portStr}...");
                        await AddSingleRuleAsync(portStr, "TCP", gameId);
                        done++;
                        progressCallback?.Invoke($"   Прогресс: {done}/{total}");
                    }
                }

                if (udpPorts != null)
                {
                    foreach (var port in udpPorts)
                    {
                        string portStr = port.ToString();
                        _logger.Info($"Добавление UDP {portStr} для {gameId}");
                        progressCallback?.Invoke($"   UDP {portStr}...");
                        await AddSingleRuleAsync(portStr, "UDP", gameId);
                        done++;
                        progressCallback?.Invoke($"   Прогресс: {done}/{total}");
                    }
                }

                _logger.Info($"Все правила портов для {gameId} добавлены");
                progressCallback?.Invoke("✅ Правила портов добавлены");
                return (true, null);
            }
            catch (Exception ex)
            {
                _logger.Error($"Ошибка добавления правил для {gameId}: {ex}");
                progressCallback?.Invoke($"❌ Ошибка: {ex.Message}");
                return (false, ex.Message);
            }
        }

        public async Task<(bool Success, string Error)> RemoveAllRulesAsync(
            string gameId,
            Action<string> progressCallback = null)
        {
            try
            {
                progressCallback?.Invoke($"📌 Удаление всех правил для {gameId} через PowerShell...");
                string psCmd = $"Get-NetFirewallRule -DisplayName \"GameFix_{gameId}_*\" | Remove-NetFirewallRule";
                string args = $"-NoProfile -Command \"{psCmd}\"";
                _logger.Info($"PowerShell: {args}");
                await RunPowerShellAsync(args);
                progressCallback?.Invoke("✅ Все правила удалены");
                return (true, null);
            }
            catch (Exception ex)
            {
                _logger.Error($"Ошибка удаления через PowerShell для {gameId}: {ex}");
                progressCallback?.Invoke($"❌ Ошибка: {ex.Message}");
                return (false, ex.Message);
            }
        }

        private async Task AddSingleRuleAsync(string portSpec, string protocol, string gameId)
        {
            string name = $"GameFix_{gameId}_{protocol}_{portSpec}";
            foreach (string dir in new[] { "in", "out" })
            {
                string args = $"advfirewall firewall add rule name=\"{name}\" dir={dir} action=allow protocol={protocol} localport={portSpec}";
                _logger.Info($"netsh {args}");
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
                _logger.Error($"netsh exit {process.ExitCode}, error: {error}");
                throw new Exception($"netsh failed: {error}");
            }
            _logger.Info($"netsh output: {output}");
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
                _logger.Error($"PowerShell exit {process.ExitCode}, error: {error}");
                throw new Exception($"PowerShell failed: {error}");
            }
            _logger.Info($"PowerShell output: {output}");
        }
    }
}