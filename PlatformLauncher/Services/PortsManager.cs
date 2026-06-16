using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Windows;

namespace PlatformLauncher.Services
{
    public static class PortsManager
    {
        private static string RulePrefix = "GameFix";

        public static bool IsAdministrator()
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }

        private static bool EnsureAdmin()
        {
            if (!IsAdministrator())
            {
                LauncherLogger.Warning("Попытка изменить правила брандмауэра без прав администратора.");
                return false;
            }
            return true;
        }

        private static IEnumerable<int> ExpandPortRange(object portSpec)
        {
            string str = portSpec.ToString();
            if (str.Contains('-'))
            {
                var parts = str.Split('-');
                int start = int.Parse(parts[0]);
                int end = int.Parse(parts[1]);
                return Enumerable.Range(start, end - start + 1);
            }
            return new[] { int.Parse(str) };
        }

        public static async Task<bool> AddRulesAsync(IEnumerable<object> tcpPorts, IEnumerable<object> udpPorts, string gameId)
        {
            if (!EnsureAdmin()) return false;
            string prefix = $"{RulePrefix}_{gameId}";

            foreach (var portSpec in tcpPorts)
                foreach (int port in ExpandPortRange(portSpec))
                {
                    await AddRuleAsync(port, "TCP", "in", prefix);
                    await AddRuleAsync(port, "TCP", "out", prefix);
                }

            foreach (var portSpec in udpPorts)
                foreach (int port in ExpandPortRange(portSpec))
                {
                    await AddRuleAsync(port, "UDP", "in", prefix);
                    await AddRuleAsync(port, "UDP", "out", prefix);
                }
            return true;
        }

        public static async Task<bool> RemoveRulesAsync(IEnumerable<object> tcpPorts, IEnumerable<object> udpPorts, string gameId)
        {
            if (!EnsureAdmin()) return false;
            string prefix = $"{RulePrefix}_{gameId}";

            foreach (var portSpec in tcpPorts)
                foreach (int port in ExpandPortRange(portSpec))
                {
                    await RemoveRuleAsync(port, "TCP", "in", prefix);
                    await RemoveRuleAsync(port, "TCP", "out", prefix);
                }

            foreach (var portSpec in udpPorts)
                foreach (int port in ExpandPortRange(portSpec))
                {
                    await RemoveRuleAsync(port, "UDP", "in", prefix);
                    await RemoveRuleAsync(port, "UDP", "out", prefix);
                }
            return true;
        }

        private static async Task AddRuleAsync(int port, string protocol, string direction, string prefix)
        {
            string ruleName = $"{prefix}_{protocol}_{direction}_{port}";
            var psi = new ProcessStartInfo("netsh", $"advfirewall firewall add rule name=\"{ruleName}\" dir={direction} action=allow protocol={protocol} localport={port}")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true
            };

            try
            {
                using var process = new Process { StartInfo = psi };
                process.Start();
                await process.WaitForExitAsync();
                if (process.ExitCode != 0)
                    LauncherLogger.Warning($"Добавление правила {ruleName} вернуло код {process.ExitCode}");
                else
                    LauncherLogger.Info($"Добавлено правило: {ruleName}");
            }
            catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 5)
            {
                LauncherLogger.Error($"Недостаточно прав для добавления правила {ruleName}: {ex.Message}");
                throw new UnauthorizedAccessException("Требуются права администратора для изменения брандмауэра.", ex);
            }
            catch (Exception ex)
            {
                LauncherLogger.Error($"Ошибка добавления правила {ruleName}: {ex.Message}");
                throw;
            }
        }

        private static async Task RemoveRuleAsync(int port, string protocol, string direction, string prefix)
        {
            string ruleName = $"{prefix}_{protocol}_{direction}_{port}";
            var psi = new ProcessStartInfo("netsh", $"advfirewall firewall delete rule name=\"{ruleName}\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true
            };

            try
            {
                using var process = new Process { StartInfo = psi };
                process.Start();
                await process.WaitForExitAsync();
                LauncherLogger.Info($"Удалено правило: {ruleName}");
            }
            catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 5)
            {
                LauncherLogger.Warning($"Недостаточно прав для удаления правила {ruleName}. Возможно, оно уже удалено.");
            }
            catch (Exception ex)
            {
                LauncherLogger.Error($"Ошибка удаления правила {ruleName}: {ex.Message}");
            }
        }
    }
}