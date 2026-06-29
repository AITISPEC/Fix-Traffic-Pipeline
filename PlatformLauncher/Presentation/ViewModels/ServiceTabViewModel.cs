using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using PlatformLauncher.Core.Interfaces;
using PlatformLauncher.Domain.Models;
using PlatformLauncher.Presentation.Commands;
using PlatformLauncher.Presentation.Views;
using System;
using System.Collections.Generic;
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
using System.Windows.Threading;
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
        private readonly IServiceProvider _serviceProvider;

        private string _warpStatus = "неизвестен";
        private bool _isWarpConnected;
        private bool _isThemeChangeAllowed = true;
        private ThemeItem _selectedTheme;
        private string _listsPath;
        private DispatcherTimer _warpStatusTimer;
        private Visibility _lockVisibility = Visibility.Collapsed;

        public event Action<bool, bool> DebugEnabledChanged;
        public event Action<string> ThemeChanged;
        public event Action<string> ListsPathChanged;

        public ObservableCollection<ThemeItem> LightThemes { get; } = new ObservableCollection<ThemeItem>();
        public ObservableCollection<ThemeItem> DarkThemes { get; } = new ObservableCollection<ThemeItem>();
        public IEnumerable<ThemeItem> AllThemes => LightThemes.Concat(DarkThemes);
        public bool InitialDebugEnabled { get; private set; }

        public bool DebugEnabled
        {
            get => _appConfigService.Load().Logging?.DebugEnabled ?? false;
            set
            {
                var config = _appConfigService.Load();
                if (config.Logging == null) config.Logging = new PlatformLauncher.Domain.Models.LoggingSettings();
                config.Logging.DebugEnabled = value;
                _appConfigService.Save(config);
                OnPropertyChanged();
                DebugEnabledChanged?.Invoke(value, InitialDebugEnabled);
            }
        }

        public string ListsPath
        {
            get => _listsPath;
            set
            {
                if (_listsPath == value) return;
                _listsPath = value;
                OnPropertyChanged();
                ListsPathChanged?.Invoke(value);
            }
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

        public bool IsThemeChangeAllowed
        {
            get => _isThemeChangeAllowed;
            set
            {
                _isThemeChangeAllowed = value;
                OnPropertyChanged();
                LockVisibility = value ? Visibility.Collapsed : Visibility.Visible;
            }
        }

        public Visibility LockVisibility
        {
            get => _lockVisibility;
            set { _lockVisibility = value; OnPropertyChanged(); }
        }

        public ThemeItem SelectedTheme
        {
            get => _selectedTheme;
            set
            {
                if (Equals(_selectedTheme, value)) return;
                if (!_isThemeChangeAllowed)
                {
                    _terminal.WriteLine("⚠️ Смена темы запрещена во время работы фикса/мониторинга");
                    OnPropertyChanged(nameof(SelectedTheme));
                    return;
                }
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
        public ICommand SelectListsPathCommand { get; }
        public ICommand OpenListsFolderCommand { get; }
        public ICommand RunServiceBatCommand { get; }

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
            SelectListsPathCommand = new RelayCommand(_ => SelectListsPath());
            OpenListsFolderCommand = new RelayCommand(_ => OpenListsFolder());
            RunServiceBatCommand = new RelayCommand(_ => RunServiceBat());

            LoadThemesFromFolder();
            InitialDebugEnabled = _appConfigService.Load().Logging?.DebugEnabled ?? false;
            string savedThemeId = _settingsManager.GetTheme()?.Trim();

            var savedTheme = AllThemes.FirstOrDefault(t => t.Id.Equals(savedThemeId, StringComparison.OrdinalIgnoreCase));
            if (savedTheme != null)
            {
                SelectedTheme = savedTheme;
            }
            else
            {
                var defaultTheme = LightThemes.FirstOrDefault(t => t.Id == "fluent-light")
                                ?? DarkThemes.FirstOrDefault(t => t.Id == "fluent-dark")
                                ?? AllThemes.FirstOrDefault();
                if (defaultTheme != null)
                {
                    _selectedTheme = defaultTheme;
                    OnPropertyChanged(nameof(SelectedTheme));
                    ThemeChanged?.Invoke(defaultTheme.Id);
                }
            }
            _ = UpdateStatusAsync();
        }

        private void SelectListsPath()
        {
            try
            {
                var dialog = new OpenFolderDialog
                {
                    Title = "Выберите папку lists",
                    InitialDirectory = string.IsNullOrEmpty(ListsPath) ? @"C:\" : ListsPath
                };

                var owner = Application.Current.MainWindow;

                if (dialog.ShowDialog(owner) == true)
                {
                    ListsPath = dialog.FolderName;

                    _terminal.WriteLine($"✅ Папка lists выбрана: {ListsPath}");
                }
            }
            catch (Exception ex)
            {
                _terminal.WriteLine($"❌ Ошибка выбора папки: {ex.Message}");
                DebugLogger.Write($"❌ Ошибка выбора папки: {ex.Message}");
            }
        }

        private void OpenListsFolder()
        {
            try
            {
                if (!string.IsNullOrEmpty(ListsPath) && Directory.Exists(ListsPath))
                    Process.Start("explorer.exe", ListsPath);
                else
                    _terminal.WriteLine("❌ Папка lists не существует или не выбрана");
            }
            catch (Exception ex)
            {
                _terminal.WriteLine($"❌ Ошибка открытия папки: {ex.Message}");
            }
        }

        private void RunServiceBat()
        {
            if (string.IsNullOrEmpty(ListsPath))
            {
                _terminal.WriteLine("❌ Папка lists не выбрана");
                return;
            }

            string parentDir = Path.GetDirectoryName(ListsPath.TrimEnd(
                Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            string batPath = Path.Combine(parentDir, "service.bat");

            if (!File.Exists(batPath))
            {
                _terminal.WriteLine($" Файл service.bat не найден: {batPath}");
                return;
            }

            try
            {
                var psi = new ProcessStartInfo("cmd.exe", $"/c \"{batPath}\"")
                {
                    UseShellExecute = true,
                    WorkingDirectory = parentDir
                };
                Process.Start(psi);
                _terminal.WriteLine($"✅ service.bat запущен");
            }
            catch (Exception ex)
            {
                _terminal.WriteLine($"❌ Ошибка запуска: {ex.Message}");
            }
        }

        public void StartWarpStatusMonitoring()
        {
            if (_warpStatusTimer != null) return;
            _warpStatusTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(2)
            };
            _warpStatusTimer.Tick += async (s, e) => await UpdateStatusAsync();
            _warpStatusTimer.Start();
        }

        public void StopWarpStatusMonitoring()
        {
            _warpStatusTimer?.Stop();
            _warpStatusTimer = null;
        }

        private void LoadThemesFromFolder()
        {
            string themesFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "themes");

            LightThemes.Clear();
            DarkThemes.Clear();

            if (!Directory.Exists(themesFolder))
            {
                DebugLogger.Warn("data/themes folder not found, loading built-in themes");
                LoadBuiltinThemes();
                return;
            }

            var deserializer = new DeserializerBuilder().Build();
            bool anyLoaded = false;

            foreach (var subFolder in new[] { "Light", "Dark" })
            {
                string folderPath = Path.Combine(themesFolder, subFolder);
                if (!Directory.Exists(folderPath))
                    continue;

                var files = Directory.GetFiles(folderPath, "*.yaml");

                foreach (var file in files)
                {
                    try
                    {
                        var yaml = File.ReadAllText(file);
                        var theme = deserializer.Deserialize<ThemeItem>(yaml);
                        if (theme != null && !string.IsNullOrEmpty(theme.Id))
                        {
                            theme.Id = theme.Id.Trim();
                            theme.TerminalTheme = subFolder;
                            if (subFolder == "Light")
                                LightThemes.Add(theme);
                            else
                                DarkThemes.Add(theme);
                            anyLoaded = true;
                        }
                    }
                    catch (Exception ex)
                    {
                        DebugLogger.WriteException($"Failed to load theme from {file}", ex);
                    }
                }
            }

            if (!anyLoaded)
            {
                DebugLogger.Warn("No themes loaded from files, using built-in");
                LoadBuiltinThemes();
            }
            else
            {
                DebugLogger.Info($"Themes loaded: Light={LightThemes.Count}, Dark={DarkThemes.Count}");
            }
        }

        private void LoadBuiltinThemes()
        {
            LightThemes.Clear();
            DarkThemes.Clear();

            LightThemes.Add(new ThemeItem
            {
                Id = "fluent-light",
                DisplayName = "Fluent Light",
                TerminalTheme = "Light",
                Background = "#FAFAFA",
                Foreground = "#1E293B",
                Accent = "#4F46E5",
                ControlBackground = "#F1F5F9",
                ControlForeground = "#1E293B",
                BorderBrush = "#E2E8F0",
                ScrollBarBackground = "#F1F5F9",
                ScrollBarForeground = "#CBD5E1",
                HoverBrush = "#EEF2F6",
                SelectedBrush = "#E0E7FF",
                DisabledBrush = "#F8FAFC",
                DisabledForeground = "#94A3B8",
                InputBackground = "#FFFFFF",
                InputForeground = "#1E293B",
                InputBorderBrush = "#CBD5E1",
                ErrorBrush = "#EF4444",
                WarningBrush = "#F59E0B",
                SuccessBrush = "#10B981",
                SeparatorBrush = "#E2E8F0",
                OverlayColor = "#400F172A"
            });

            DarkThemes.Add(new ThemeItem
            {
                Id = "fluent-dark",
                DisplayName = "Fluent Dark",
                TerminalTheme = "Dark",
                Background = "#1E1E2E",
                Foreground = "#CDD6F4",
                Accent = "#89B4FA",
                ControlBackground = "#2A2A3D",
                ControlForeground = "#CDD6F4",
                BorderBrush = "#3F3F56",
                ScrollBarBackground = "#252538",
                ScrollBarForeground = "#45475A",
                HoverBrush = "#313244",
                SelectedBrush = "#364A75",
                DisabledBrush = "#252538",
                DisabledForeground = "#6C7086",
                InputBackground = "#2A2A3D",
                InputForeground = "#CDD6F4",
                InputBorderBrush = "#45475A",
                ErrorBrush = "#F38BA8",
                WarningBrush = "#F9E2AF",
                SuccessBrush = "#A6E3A1",
                SeparatorBrush = "#313244",
                OverlayColor = "#8011111B"
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
            catch (Exception ex)
            {
                _logger.Warning($"Ошибка получения адаптера через PowerShell: {ex.Message}");
            }

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
            catch (Exception ex)
            {
                _logger.Warning($"Ошибка получения адаптера через NetworkInterface: {ex.Message}");
            }

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