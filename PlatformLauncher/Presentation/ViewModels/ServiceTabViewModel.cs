using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using PlatformLauncher.Core.Interfaces;
using PlatformLauncher.Domain.Models;
using PlatformLauncher.Presentation.Commands;
using PlatformLauncher.Presentation.Views;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using YamlDotNet.Serialization;

namespace PlatformLauncher.Presentation.ViewModels
{
    public class ServiceTabViewModel : INotifyPropertyChanged
    {
        private readonly IWarpManager _warpManager;
        private readonly ISettingsManager _settingsManager;
        private readonly ILogger _logger;
        private readonly ITerminalOutput _terminal;
        private readonly IAppConfigService _appConfigService;
        private readonly IServiceProvider _serviceProvider; // <-- ДОБАВЛЕНО

        private string _warpStatus = "неизвестен";
        private bool _isWarpConnected;
        private ThemeItem _selectedTheme;
        private string _listsPath;

        public event Action<string> ThemeChanged;

        public ObservableCollection<ThemeItem> Themes { get; } = new ObservableCollection<ThemeItem>();

        public string ListsPath
        {
            get => _listsPath;
            set { _listsPath = value; OnPropertyChanged(); }
        }

        public string WarpStatus
        {
            get => _warpStatus;
            set { _warpStatus = value; OnPropertyChanged(); }
        }

        public bool IsWarpConnected
        {
            get => _isWarpConnected;
            set { _isWarpConnected = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanStartWarp)); OnPropertyChanged(nameof(CanStopWarp)); }
        }

        public ThemeItem SelectedTheme
        {
            get => _selectedTheme;
            set
            {
                if (Equals(_selectedTheme, value)) return;
                _selectedTheme = value;
                OnPropertyChanged();
                if (value != null)
                {
                    ThemeChanged?.Invoke(value.Id);
                    _settingsManager.SetTheme(value.Id);
                }
            }
        }

        public bool CanStartWarp => !IsWarpConnected;
        public bool CanStopWarp => IsWarpConnected;

        public ICommand StartWarpCommand { get; }
        public ICommand StopWarpCommand { get; }
        public ICommand FixInternetCommand { get; }
        public ICommand WriteCloudflareCommand { get; }

        public ServiceTabViewModel(
            IWarpManager warpManager,
            ISettingsManager settingsManager,
            ILogger logger,
            ITerminalOutput terminal,
            IAppConfigService appConfigService,
            IServiceProvider serviceProvider) // <-- ДОБАВЛЕН ПАРАМЕТР
        {
            _warpManager = warpManager;
            _settingsManager = settingsManager;
            _logger = logger;
            _terminal = terminal;
            _appConfigService = appConfigService;
            _serviceProvider = serviceProvider;

            StartWarpCommand = new RelayCommand(_ => _ = StartWarpAsync());
            StopWarpCommand = new RelayCommand(_ => _ = StopWarpAsync());
            FixInternetCommand = new RelayCommand(_ => FixInternet());
            WriteCloudflareCommand = new RelayCommand(_ => _ = WriteCloudflareAsync());

            LoadThemesFromFolder();
            string savedThemeId = _settingsManager.GetTheme()?.Trim();

            var savedTheme = Themes.FirstOrDefault(t => t.Id.Equals(savedThemeId, StringComparison.OrdinalIgnoreCase));
            if (savedTheme != null)
            {
                SelectedTheme = savedTheme;
            }
            else
            {
                var defaultTheme = Themes.FirstOrDefault(t => t.Id == "fluent-light") ?? Themes.FirstOrDefault();
                if (defaultTheme != null)
                {
                    _selectedTheme = defaultTheme;
                    OnPropertyChanged(nameof(SelectedTheme));
                    ThemeChanged?.Invoke(defaultTheme.Id);
                }
            }
            _ = UpdateStatusAsync();
        }

        private void LoadThemesFromFolder()
        {
            string themesFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "themes");
            if (!Directory.Exists(themesFolder))
            {
                _logger.Warning("Папка data/themes не найдена, загружаем встроенные темы");
                LoadBuiltinThemes();
                return;
            }

            var files = Directory.GetFiles(themesFolder, "*.yaml");
            if (files.Length == 0)
            {
                _logger.Warning("В папке data/themes нет YAML-файлов, загружаем встроенные темы");
                LoadBuiltinThemes();
                return;
            }

            Themes.Clear();
            var deserializer = new DeserializerBuilder().Build();
            bool anyLoaded = false;

            foreach (var file in files)
            {
                try
                {
                    var yaml = File.ReadAllText(file);
                    var theme = deserializer.Deserialize<ThemeItem>(yaml);
                    if (theme != null && !string.IsNullOrEmpty(theme.Id))
                    {
                        theme.Id = theme.Id.Trim();
                        Themes.Add(theme);
                        anyLoaded = true;
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error($"Ошибка загрузки темы из {file}: {ex.Message}");
                }
            }

            if (!anyLoaded)
            {
                _logger.Warning("Не удалось загрузить ни одной темы из data/themes, загружаем встроенные");
                LoadBuiltinThemes();
            }
            else
            {
                _logger.Info($"Загружено {Themes.Count} тем из data/themes");
            }
        }

        private void LoadBuiltinThemes()
        {
            Themes.Clear();
            Themes.Add(new ThemeItem
            {
                Id = "fluent-light",
                DisplayName = "Fluent Light",
                TerminalTheme = "Light",
                Background = "#FFFFFF",
                Foreground = "#1A1A2E",
                Accent = "#6C8BFF",
                ControlBackground = "#F5F6FA",
                ControlForeground = "#1A1A2E",
                BorderBrush = "#D1D5E0"
            });
            Themes.Add(new ThemeItem
            {
                Id = "fluent-dark",
                DisplayName = "Fluent Dark",
                TerminalTheme = "Dark",
                Background = "#1A1A2E",
                Foreground = "#CDD6F4",
                Accent = "#89B4FA",
                ControlBackground = "#2A2A3E",
                ControlForeground = "#CDD6F4",
                BorderBrush = "#454560"
            });
        }

        private async Task UpdateStatusAsync()
        {
            try
            {
                string status = await _warpManager.GetStatusAsync();
                IsWarpConnected = status == "connected";
                WarpStatus = IsWarpConnected ? "подключён" : "отключён";
            }
            catch (Exception ex)
            {
                _logger.Error($"Ошибка проверки статуса WARP: {ex}");
                WarpStatus = "ошибка";
            }
        }

        private async Task StartWarpAsync()
        {
            try
            {
                bool ok = await _warpManager.EnsureStartedAsync();
                if (ok)
                {
                    _terminal.WriteLine("✅ WARP запущен");
                    await UpdateStatusAsync();
                }
                else
                {
                    _terminal.WriteLine("❌ Ошибка запуска WARP");
                }
            }
            catch (Exception ex)
            {
                _terminal.WriteLine($"❌ Ошибка: {ex.Message}");
            }
        }

        private async Task StopWarpAsync()
        {
            try
            {
                bool ok = await _warpManager.DisconnectAsync();
                if (ok)
                {
                    _terminal.WriteLine("✅ WARP отключён");
                    await UpdateStatusAsync();
                }
                else
                {
                    _terminal.WriteLine("❌ Ошибка отключения WARP");
                }
            }
            catch (Exception ex)
            {
                _terminal.WriteLine($"❌ Ошибка: {ex.Message}");
            }
        }

        private async Task WriteCloudflareAsync()
        {
            try
            {
                var appConfig = _appConfigService.Load();
                if (appConfig?.CloudflareDomains == null || appConfig.CloudflareDomains.Count == 0)
                {
                    _terminal.WriteLine("⚠️ Список cloudflare_domains в app_config.yaml пуст.");
                    return;
                }
                if (string.IsNullOrEmpty(ListsPath))
                {
                    _terminal.WriteLine("❌ Папка lists не выбрана.");
                    return;
                }
                var sanitizer = _serviceProvider.GetRequiredService<IListsSanitizer>();
                sanitizer.WriteCloudflareDomains(ListsPath, appConfig.CloudflareDomains);
                _terminal.WriteLine($"✅ Cloudflare домены прописаны в {ListsPath}");
            }
            catch (Exception ex)
            {
                _terminal.WriteLine($"❌ Ошибка: {ex.Message}");
            }
        }

        private void FixInternet()
        {
            _terminal.WriteLine("🔄 Сброс DNS и сетевых настроек...");

            // Переключение на вкладку "Фиксы"
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (Application.Current.MainWindow is MainWindow mainWindow)
                {
                    var tabControl = mainWindow.FindName("MainTabControl") as TabControl;
                    if (tabControl != null)
                        tabControl.SelectedIndex = 0;
                }
            });

            try
            {
                // Базовый сброс
                RunCommand("ipconfig /flushdns");
                RunCommand("netsh winsock reset");
                RunCommand("netsh int ip reset");

                // Определяем основной адаптер
                string mainAdapter = GetMainNetworkAdapter();
                if (!string.IsNullOrEmpty(mainAdapter))
                {
                    _terminal.WriteLine($"   Основной адаптер: {mainAdapter}");
                    RunCommand($"netsh interface ipv4 set dns \"CloudflareWARP\" static 127.0.2.2");
                    RunCommand($"netsh interface ipv4 add dns \"CloudflareWARP\" 127.0.2.3 index=2");
                    RunCommand($"netsh interface ipv4 set dns \"{mainAdapter}\" static 127.0.2.2");
                    RunCommand($"netsh interface ipv4 add dns \"{mainAdapter}\" 127.0.2.3 index=2");
                    RunCommand($"netsh interface ipv6 set dns \"{mainAdapter}\" static ::ffff:127.0.2.2");
                    RunCommand($"netsh interface ipv6 add dns \"{mainAdapter}\" ::ffff:127.0.2.3 index=2");
                    RunCommand("ipconfig /flushdns");
                    RunCommand("net stop dnscache");
                    RunCommand("net start dnscache");
                    RunCommand($"netsh interface set interface \"CloudflareWARP\" admin=disable");
                    RunCommand($"netsh interface set interface \"CloudflareWARP\" admin=enable");
                    RunCommand($"netsh interface set interface \"{mainAdapter}\" admin=disable");
                    RunCommand($"netsh interface set interface \"{mainAdapter}\" admin=enable");
                }
                else
                {
                    _terminal.WriteLine("⚠️ Не удалось определить основной адаптер, установка DNS для адаптеров пропущена.");
                }

                _terminal.WriteLine("✅ Сброс DNS и сетевых настроек выполнен.");
                _terminal.WriteLine("⚠️ Рекомендуется перезагрузить компьютер для полного применения.");
            }
            catch (Exception ex)
            {
                _terminal.WriteLine($"❌ Ошибка: {ex.Message}");
            }
        }

        private string GetMainNetworkAdapter()
        {
            try
            {
                var psi = new ProcessStartInfo("powershell", "-Command \"Get-NetRoute -DestinationPrefix 0.0.0.0/0 | Get-NetAdapter | Select-Object -ExpandProperty Name\"")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    StandardOutputEncoding = System.Text.Encoding.UTF8
                };
                using var p = Process.Start(psi);
                string output = p.StandardOutput.ReadToEnd().Trim();
                p.WaitForExit(2000);
                if (!string.IsNullOrEmpty(output))
                    return output;
            }
            catch { }

            try
            {
                var adapters = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
                    .Where(a => a.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up &&
                                a.NetworkInterfaceType != System.Net.NetworkInformation.NetworkInterfaceType.Loopback &&
                                a.NetworkInterfaceType != System.Net.NetworkInformation.NetworkInterfaceType.Tunnel)
                    .Select(a => a.Name)
                    .FirstOrDefault();
                if (!string.IsNullOrEmpty(adapters))
                    return adapters;
            }
            catch { }

            return null;
        }

        private void RunCommand(string command)
        {
            try
            {
                var psi = new ProcessStartInfo("cmd.exe", "/c " + command)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using var p = Process.Start(psi);
                p.WaitForExit(5000);
                if (p.ExitCode != 0)
                {
                    string error = p.StandardError.ReadToEnd();
                    _logger.Warning($"Команда {command} завершилась с кодом {p.ExitCode}: {error}");
                }
                else
                {
                    _logger.Info($"Команда {command} выполнена успешно.");
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Ошибка выполнения команды {command}: {ex}");
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}