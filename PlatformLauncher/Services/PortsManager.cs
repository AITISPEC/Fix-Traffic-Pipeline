using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Principal;
using System.Threading.Tasks;

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

        private static IEnumerable<object> ExpandPortRange(object portSpec)
        {
            string str = portSpec.ToString();
            if (str.Contains('-'))
            {
                return new[] { portSpec };
            }
            return new[] { int.Parse(str) };
        }

        public static async Task<bool> AddRulesAsync(IEnumerable<object> tcpPorts, IEnumerable<object> udpPorts, string gameId, Action<string> progressCallback = null)
        {
            if (!EnsureAdmin()) return false;
            string prefix = $"{RulePrefix}_{gameId}";

            int tcpCount = tcpPorts?.Count() ?? 0;
            int udpCount = udpPorts?.Count() ?? 0;
            int total = tcpCount + udpCount;
            int done = 0;

            progressCallback?.Invoke($"📌 Добавление правил портов: TCP={tcpCount}, UDP={udpCount}, всего={total * 2} (вх/исх)");

            foreach (var portSpec in tcpPorts)
            {
                foreach (object p in ExpandPortRange(portSpec))
                {
                    await AddRuleAsync(p, "TCP", "in", prefix);
                    done++;
                    progressCallback?.Invoke($"  [{done * 2}/{total * 2}] TCP {p} in");
                    await AddRuleAsync(p, "TCP", "out", prefix);
                    done++;
                    progressCallback?.Invoke($"  [{done * 2}/{total * 2}] TCP {p} out");
                }
            }

            foreach (var portSpec in udpPorts)
            {
                foreach (object p in ExpandPortRange(portSpec))
                {
                    await AddRuleAsync(p, "UDP", "in", prefix);
                    done++;
                    progressCallback?.Invoke($"  [{done * 2}/{total * 2}] UDP {p} in");
                    await AddRuleAsync(p, "UDP", "out", prefix);
                    done++;
                    progressCallback?.Invoke($"  [{done * 2}/{total * 2}] UDP {p} out");
                }
            }

            progressCallback?.Invoke($"✅ Все правила портов добавлены ({total * 2} правил)");
            return true;
        }

        public static async Task<bool> RemoveRulesAsync(IEnumerable<object> tcpPorts, IEnumerable<object> udpPorts, string gameId, Action<string> progressCallback = null)
        {
            if (!EnsureAdmin()) return false;
            string prefix = $"{RulePrefix}_{gameId}";

            int tcpCount = tcpPorts?.Count() ?? 0;
            int udpCount = udpPorts?.Count() ?? 0;
            int total = tcpCount + udpCount;
            int done = 0;

            progressCallback?.Invoke($"📌 Удаление правил портов: TCP={tcpCount}, UDP={udpCount}, всего={total * 2}");

            foreach (var portSpec in tcpPorts)
            {
                foreach (object p in ExpandPortRange(portSpec))
                {
                    await RemoveRuleAsync(p, "TCP", "in", prefix);
                    done++;
                    progressCallback?.Invoke($"  [{done * 2}/{total * 2}] TCP {p} in");
                    await RemoveRuleAsync(p, "TCP", "out", prefix);
                    done++;
                    progressCallback?.Invoke($"  [{done * 2}/{total * 2}] TCP {p} out");
                }
            }

            foreach (var portSpec in udpPorts)
            {
                foreach (object p in ExpandPortRange(portSpec))
                {
                    await RemoveRuleAsync(p, "UDP", "in", prefix);
                    done++;
                    progressCallback?.Invoke($"  [{done * 2}/{total * 2}] UDP {p} in");
                    await RemoveRuleAsync(p, "UDP", "out", prefix);
                    done++;
                    progressCallback?.Invoke($"  [{done * 2}/{total * 2}] UDP {p} out");
                }
            }

            progressCallback?.Invoke($"✅ Все правила портов удалены ({total * 2} правил)");
            return true;
        }

        private static async Task AddRuleAsync(object portSpec, string protocol, string direction, string prefix)
        {
            string ruleName = $"{prefix}_{protocol}_{direction}_{portSpec}";
            var psi = new ProcessStartInfo("netsh", $"advfirewall firewall add rule name=\"{ruleName}\" dir={direction} action=allow protocol={protocol} localport={portSpec}")
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

        private static async Task RemoveRuleAsync(object portSpec, string protocol, string direction, string prefix)
        {
            string ruleName = $"{prefix}_{protocol}_{direction}_{portSpec}";
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