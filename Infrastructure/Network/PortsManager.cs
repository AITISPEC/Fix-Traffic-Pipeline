using PlatformLauncher.Core.Interfaces;
using PlatformLauncher.Helpers;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

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

        public async Task<(bool HasRules, string Error)> CheckRulesExistAsync(string gameId)
        {
            try
            {
                string psCmd = $"(Get-NetFirewallRule -DisplayName \"GameFix_{gameId}_*\" | Measure-Object).Count";
                string args = $"-NoProfile -Command \"{psCmd}\"";

                var (exitCode, output, error) = await ProcessHelper.RunAsync("powershell", args, _logger, timeoutMs: 5000);

                if (exitCode != 0)
                {
                    _logger.Warning($"Ошибка проверки правил: {error}");
                    return (false, error);
                }

                if (int.TryParse(output.Trim(), out int count))
                {
                    bool hasRules = count > 0;
                    _logger.Info($"Найдено {count} правил для {gameId}");
                    return (hasRules, string.Empty);
                }

                return (false, "Не удалось распарсить вывод PowerShell");
            }
            catch (Exception ex)
            {
                _logger.Error($"Ошибка проверки правил для {gameId}: {ex}");
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
            var (exitCode, _, error) = await ProcessHelper.RunAsync("netsh", arguments, _logger);
            if (exitCode != 0)
                throw new Exception($"netsh failed: {error}");
        }

        private async Task RunPowerShellAsync(string arguments)
        {
            var (exitCode, _, error) = await ProcessHelper.RunAsync("powershell", arguments, _logger);
            if (exitCode != 0)
                throw new Exception($"PowerShell failed: {error}");
        }
    }
}