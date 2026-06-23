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
using YamlDotNet.Serialization;

namespace PlatformLauncher.Presentation.ViewModels
{
    public class ServiceTabViewModel : INotifyPropertyChanged
    {
        private readonly IWarpManager _warpManager;
        private readonly ISettingsManager _settingsManager;
        private readonly ILogger _logger;
        private readonly ITerminalOutput _terminal;

        private string _warpStatus = "неизвестен";
        private bool _isWarpConnected;
        private ThemeItem _selectedTheme;

        public event Action<string> ThemeChanged;

        public ObservableCollection<ThemeItem> Themes { get; } = new ObservableCollection<ThemeItem>();

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

        public ServiceTabViewModel(IWarpManager warpManager, ISettingsManager settingsManager, ILogger logger, ITerminalOutput terminal)
        {
            _warpManager = warpManager;
            _settingsManager = settingsManager;
            _logger = logger;
            _terminal = terminal;

            StartWarpCommand = new RelayCommand(_ => _ = StartWarpAsync());
            StopWarpCommand = new RelayCommand(_ => _ = StopWarpAsync());
            FixInternetCommand = new RelayCommand(_ => FixInternet());

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
                        theme.Id = theme.Id.Trim(); // обрезаем пробелы
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

        private void FixInternet()
        {
            _terminal.WriteLine("🔄 Сброс DNS и сетевых настроек...");
            try
            {
                // Глобальный NameServer
                using (var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters", true))
                {
                    if (key != null)
                    {
                        var ns = key.GetValue("NameServer") as string;
                        if (!string.IsNullOrEmpty(ns) && (ns.Contains("127.0.2") || ns.Contains("1.1.1.1") || ns.Contains("1.0.0.1")))
                        {
                            // key.SetValue("NameServer", "", RegistryValueKind.String);
                            // _logger.Info("Глобальный DNS очищен.");
                            // _terminal.WriteLine("   ✅ Глобальный DNS очищен");
                            _terminal.WriteLine($"   ✅ DNS {ns} в параметре {key}");
                        }
                    }
                }

                // Интерфейсы
                using (var interfacesKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces"))
                {
                    if (interfacesKey != null)
                    {
                        foreach (var subKeyName in interfacesKey.GetSubKeyNames())
                        {
                            using (var ifaceKey = interfacesKey.OpenSubKey(subKeyName, true))
                            {
                                if (ifaceKey != null)
                                {
                                    var ns = ifaceKey.GetValue("NameServer") as string;
                                    if (!string.IsNullOrEmpty(ns) && (ns.Contains("127.0.2") || ns.Contains("1.1.1.1") || ns.Contains("1.0.0.1")))
                                    {
                                        // ifaceKey.SetValue("NameServer", "", RegistryValueKind.String);
                                        //  _logger.Info($"DNS-настройки на интерфейсе {subKeyName} очищены.");
                                        //_terminal.WriteLine($"   ✅ DNS очищен на интерфейсе {subKeyName}");
                                        _terminal.WriteLine($"   ✅ DNS {ns} на интерфейсе {subKeyName}");
                                    }
                                }
                            }
                        }
                    }
                }

                // Сброс кэша и стека TCP/IP
                // _terminal.WriteLine("   ⏳ Сброс кэша DNS...");
                // RunCommand("ipconfig /flushdns");
                // _terminal.WriteLine("   ⏳ Сброс Winsock...");
                // RunCommand("netsh winsock reset");
                // _terminal.WriteLine("   ⏳ Сброс IP-стека...");
                // RunCommand("netsh int ip reset");

                // _logger.Info("Сброс сетевых настроек выполнен.");
                // _terminal.WriteLine("✅ Сброс DNS и сетевых настроек выполнен.");
                // _terminal.WriteLine("⚠️ Рекомендуется перезагрузить компьютер для полного применения.");

                // Переключение на вкладку "Фиксы"
                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (Application.Current.MainWindow is MainWindow mainWindow)
                    {
                        var tabControl = mainWindow.FindName("MainTabControl") as TabControl;
                        if (tabControl != null)
                            tabControl.SelectedIndex = 0; // переключение на первую вкладку (Фиксы)
                    }
                });
            }
            catch (Exception ex)
            {
                // _logger.Error($"Ошибка сброса DNS: {ex}");
                _terminal.WriteLine($"❌ Ошибка: {ex.Message}");
            }
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