using PlatformLauncher.Core.Interfaces;
using PlatformLauncher.Helpers;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;

namespace PlatformLauncher.Infrastructure.Network
{
    /// <summary>
    /// Управление правилами портов брандмауэра Windows через netsh и PowerShell.
    /// Узкое место: создание процесса Process.Start() + ожидание выхода — блокирующее валидирование (process.WaitForExitAsync) на больших списках портов.
    /// </summary>
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
            Action<string>? progressCallback = null)
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
                        string portStr = port.ToString() ?? throw new InvalidOperationException("Port cannot be null");
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
                        string portStr = port.ToString() ?? throw new InvalidOperationException("Port cannot be null");
                        _logger.Info($"Добавление UDP {portStr} для {gameId}");
                        progressCallback?.Invoke($"   UDP {portStr}...");
                        await AddSingleRuleAsync(portStr, "UDP", gameId);
                        done++;
                        progressCallback?.Invoke($"   Прогресс: {done}/{total}");
                    }
                }

                _logger.Info($"Все правила портов для {gameId} добавлены");
                progressCallback?.Invoke("✅ Правила портов добавлены");
                return (true, string.Empty);
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
            Action<string>? progressCallback = null)
        {
            try
            {
                progressCallback?.Invoke($"📌 Удаление всех правил для {gameId} через PowerShell...");
                string psCmd = $"Get-NetFirewallRule -DisplayName \"GameFix_{gameId}_*\" | Remove-NetFirewallRule";
                string args = $"-NoProfile -Command \"{psCmd}\"";
                _logger.Info($"PowerShell: {args}");
                await RunPowerShellAsync(args);
                progressCallback?.Invoke("✅ Все правила удалены");
                return (true, string.Empty);
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
            var (exitCode, output, error) = await ProcessHelper.RunAsync("netsh", arguments, _logger);
            if (exitCode != 0)
            {
                _logger.Error($"netsh exit {exitCode}, error: {error}");
                throw new Exception($"netsh failed: {error}");
            }
            _logger.Info($"netsh output: {output}");
        }

        private async Task RunPowerShellAsync(string arguments)
        {
            var (exitCode, output, error) = await ProcessHelper.RunAsync("powershell", arguments, _logger);
            if (exitCode != 0)
            {
                _logger.Error($"PowerShell exit {exitCode}, error: {error}");
                throw new Exception($"PowerShell failed: {error}");
            }
            _logger.Info($"PowerShell output: {output}");
        }
    }
}