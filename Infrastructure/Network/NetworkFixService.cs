using System;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Threading.Tasks;
using PlatformLauncher.Core.Interfaces;
using PlatformLauncher.Helpers;

namespace PlatformLauncher.Infrastructure.Network
{
    public class NetworkFixService : INetworkFixService
    {
        private readonly ILogger _logger;

        public NetworkFixService(ILogger logger)
        {
            _logger = logger;
        }

        public async Task FixInternetAsync(Action<string> progress)
        {
            progress?.Invoke("🔄 Сброс DNS и сетевых настроек...");

            try
            {
                // Базовый сброс
                progress?.Invoke("   Сброс DNS-кэша...");
                await RunCommandAsync("ipconfig /flushdns");

                progress?.Invoke("   Сброс Winsock...");
                await RunCommandAsync("netsh winsock reset");

                progress?.Invoke("   Сброс IP-стека...");
                await RunCommandAsync("netsh int ip reset");

                // Определяем основной адаптер
                string? mainAdapter = GetMainNetworkAdapter();
                if (!string.IsNullOrEmpty(mainAdapter))
                {
                    progress?.Invoke($"   Основной адаптер: {mainAdapter}");

                    // Настройка DNS для CloudflareWARP
                    await RunCommandAsync("netsh interface ipv4 set dns \"CloudflareWARP\" static 127.0.2.2");
                    await RunCommandAsync("netsh interface ipv4 add dns \"CloudflareWARP\" 127.0.2.3 index=2");

                    // Настройка DNS для основного адаптера
                    await RunCommandAsync($"netsh interface ipv4 set dns \"{mainAdapter}\" static 127.0.2.2");
                    await RunCommandAsync($"netsh interface ipv4 add dns \"{mainAdapter}\" 127.0.2.3 index=2");
                    await RunCommandAsync($"netsh interface ipv6 set dns \"{mainAdapter}\" static ::ffff:127.0.2.2");
                    await RunCommandAsync($"netsh interface ipv6 add dns \"{mainAdapter}\" ::ffff:127.0.2.3 index=2");

                    // Повторный сброс DNS
                    await RunCommandAsync("ipconfig /flushdns");

                    // Перезапуск службы DNS
                    progress?.Invoke("   Перезапуск службы DNS...");
                    await RunCommandAsync("net stop dnscache");
                    await RunCommandAsync("net start dnscache");

                    // Перезапуск адаптеров
                    progress?.Invoke("   Перезапуск адаптеров...");
                    await RunCommandAsync($"netsh interface set interface \"CloudflareWARP\" admin=disable");
                    await RunCommandAsync($"netsh interface set interface \"CloudflareWARP\" admin=enable");
                    await RunCommandAsync($"netsh interface set interface \"{mainAdapter}\" admin=disable");
                    await RunCommandAsync($"netsh interface set interface \"{mainAdapter}\" admin=enable");
                }
                else
                {
                    progress?.Invoke("⚠️ Не удалось определить основной адаптер, установка DNS для адаптеров пропущена.");
                }

                progress?.Invoke("✅ Сброс DNS и сетевых настроек выполнен.");
                progress?.Invoke("⚠️ Рекомендуется перезагрузить компьютер для полного применения.");
            }
            catch (Exception ex)
            {
                _logger.Error($"Ошибка сброса сети: {ex}");
                progress?.Invoke($"❌ Ошибка: {ex.Message}");
            }
        }

        private async Task RunCommandAsync(string? command)
        {
            try
            {
                var (exitCode, _, error) = await ProcessHelper.RunAsync(
                    "cmd.exe",
                    $"/c {command}",
                    _logger,
                    createNoWindow: true,
                    timeoutMs: 15000);

                if (exitCode != 0)
                {
                    _logger.Warning($"Команда '{command}' завершилась с кодом {exitCode}: {error}");
                }
                else
                {
                    _logger.Info($"Команда '{command}' выполнена успешно.");
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Ошибка выполнения команды '{command}': {ex.Message}");
            }
        }

        private string? GetMainNetworkAdapter()
        {
            // Попытка 1: через PowerShell
            try
            {
                var (exitCode, output, _) = ProcessHelper.RunAsync(
                    "powershell",
                    "-Command \"Get-NetRoute -DestinationPrefix 0.0.0.0/0 | Get-NetAdapter | Select-Object -ExpandProperty Name\"",
                    _logger,
                    timeoutMs: 5000).GetAwaiter().GetResult();

                if (exitCode == 0 && !string.IsNullOrWhiteSpace(output))
                    return output.Trim();
            }
            catch (Exception ex)
            {
                _logger.Warning($"Ошибка получения адаптера через PowerShell: {ex.Message}");
            }

            // Попытка 2: через NetworkInterface
            try
            {
                var adapter = NetworkInterface.GetAllNetworkInterfaces()
                    .Where(a => a.OperationalStatus == OperationalStatus.Up &&
                                a.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                                a.NetworkInterfaceType != NetworkInterfaceType.Tunnel)
                    .Select(a => a.Name)
                    .FirstOrDefault();

                if (!string.IsNullOrEmpty(adapter))
                    return adapter;
            }
            catch (Exception ex)
            {
                _logger.Warning($"Ошибка получения адаптера через NetworkInterface: {ex.Message}");
            }

            return null;
        }
    }
}