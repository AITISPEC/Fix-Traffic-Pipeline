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
using System.Net.Http;
using System.IO.Compression;
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
        private readonly IPythonEnvironmentManager _pythonEnvManager;

        private string _warpStatus = "неизвестен";
        private bool _isWarpConnected;
        private bool _isThemeChangeAllowed = true;
        private ThemeItem _selectedTheme;
        private string _listsPath;
        private DispatcherTimer _warpStatusTimer;
        private Visibility _lockVisibility = Visibility.Collapsed;

        private bool _isWarpInstalled;
        private bool _isSystemPythonAvailable;
        private bool _isVenvExists;
        private bool _isZapretValid;
        private bool _canRunZapret;
        private bool _isSessionActive;

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

        public bool IsSessionActive
        {
            get => _isSessionActive;
            set
            {
                if (_isSessionActive == value) return;
                _isSessionActive = value;
                OnPropertyChanged();
            }
        }

        public bool CanRunZapret
        {
            get => _canRunZapret;
            private set
            {
                if (_canRunZapret == value) return;
                _canRunZapret = value;
                OnPropertyChanged();
                CommandManager.InvalidateRequerySuggested();
            }
        }

        public bool IsZapretValid
        {
            get => _isZapretValid;
            private set
            {
                if (_isZapretValid == value) return;
                _isZapretValid = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsZapretMissing));
                CommandManager.InvalidateRequerySuggested();
            }
        }

        public bool IsWarpInstalled
        {
            get => _isWarpInstalled;
            private set
            {
                if (_isWarpInstalled == value) return;
                _isWarpInstalled = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanStartWarp));
                OnPropertyChanged(nameof(CanStopWarp));
                OnPropertyChanged(nameof(CanInstallWarp));
                CommandManager.InvalidateRequerySuggested();
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
                ValidateZapretInstallation();
                _settingsManager.SetListsPath(value);
                ListsPathChanged?.Invoke(value);
            }
        }

        public bool IsSystemPythonAvailable
        {
            get => _isSystemPythonAvailable;
            private set
            {
                if (_isSystemPythonAvailable == value)
                {
                    DebugLogger.Debug($"[PythonCheck] IsSystemPythonAvailable setter: уже равно {value}, пропуск");
                    return;
                }
                DebugLogger.Debug($"[PythonCheck] IsSystemPythonAvailable setter: изменение с {_isSystemPythonAvailable} на {value}");
                _isSystemPythonAvailable = value;
                OnPropertyChanged();
                UpdatePythonButtonsState();
            }
        }

        public bool IsVenvExists
        {
            get => _isVenvExists;
            private set
            {
                if (_isVenvExists == value)
                {
                    DebugLogger.Debug($"[PythonCheck] IsVenvExists setter: уже равно {value}, пропуск");
                    return;
                }
                DebugLogger.Debug($"[PythonCheck] IsVenvExists setter: изменение с {_isVenvExists} на {value}");
                _isVenvExists = value;
                OnPropertyChanged();
                UpdatePythonButtonsState();
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

        public bool CanStartWarp => IsWarpInstalled && !IsWarpConnected;
        public bool CanStopWarp => IsWarpInstalled && IsWarpConnected;
        public bool CanInstallWarp => !IsWarpInstalled;
        public bool IsZapretMissing => !_isZapretValid;
        public bool CanInstallPython => !IsVenvExists && !IsSystemPythonAvailable;
        public bool CanCreateVenv => !IsVenvExists && IsSystemPythonAvailable;

        public ICommand StartWarpCommand { get; }
        public ICommand StopWarpCommand { get; }
        public ICommand FixInternetCommand { get; }
        public ICommand WriteCloudflareCommand { get; }
        public ICommand SelectListsPathCommand { get; }
        public ICommand OpenListsFolderCommand { get; }
        public ICommand RunServiceBatCommand { get; }
        public ICommand InstallPythonCommand { get; }
        public ICommand FixPythonPathCommand { get; }
        public ICommand CreateVenvCommand { get; }
        public ICommand RunZapretCommand { get; }
        public ICommand InstallZapretCommand { get; }
        public ICommand InstallWarpCommand { get; }

        public ServiceTabViewModel(
            IWarpManager warpManager,
            ISettingsManager settingsManager,
            ILogger logger,
            ITerminalOutput terminal,
            IAppConfigService appConfigService,
            IPythonEnvironmentManager pythonEnvManager,
            IServiceProvider serviceProvider)
        {
            _warpManager = warpManager;
            _settingsManager = settingsManager;
            _logger = logger;
            _terminal = terminal;
            _appConfigService = appConfigService;
            _pythonEnvManager = pythonEnvManager;
            _serviceProvider = serviceProvider;

            StartWarpCommand = new RelayCommand(_ => _ = StartWarpAsync());
            StopWarpCommand = new RelayCommand(_ => _ = StopWarpAsync());
            FixInternetCommand = new RelayCommand(_ => FixInternet());
            WriteCloudflareCommand = new RelayCommand(_ => _ = WriteCloudflareAsync());
            SelectListsPathCommand = new RelayCommand(_ => SelectListsPath());
            OpenListsFolderCommand = new RelayCommand(_ => OpenListsFolder());
            RunServiceBatCommand = new RelayCommand(_ => RunServiceBat());
            InstallPythonCommand = new RelayCommand(async _ => await InstallPythonAsync());
            FixPythonPathCommand = new RelayCommand(_ => FixPythonPath());
            InstallWarpCommand = new RelayCommand(async _ => await InstallWarpAsync());
            RunZapretCommand = new RelayCommand(_ => RunZapret(), _ => CanRunZapret);
            InstallZapretCommand = new RelayCommand(_ => InstallZapret());
            CreateVenvCommand = new RelayCommand(async _ => await CreateVenvAsync());

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
            ValidateZapretInstallation();
            RefreshZapretState();
            CheckVenvExists();
            _ = CheckSystemPythonAsync();
        }

        private void RefreshZapretState()
        {
            if (string.IsNullOrEmpty(_listsPath) || !Directory.Exists(_listsPath))
            {
                IsZapretValid = false;
                CanRunZapret = false;
                return;
            }

            string parentDir = Path.GetDirectoryName(
                _listsPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

            if (string.IsNullOrEmpty(parentDir))
            {
                IsZapretValid = false;
                CanRunZapret = false;
                return;
            }

            bool hasServiceBat = File.Exists(Path.Combine(parentDir, "service.bat"));
            bool hasGeneralBat = File.Exists(Path.Combine(parentDir, "general.bat"));

            IsZapretValid = hasServiceBat;
            CanRunZapret = hasServiceBat && hasGeneralBat;
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

        private void ValidateZapretInstallation()
        {
            if (string.IsNullOrEmpty(_listsPath) || !Directory.Exists(_listsPath))
            {
                IsZapretValid = false;
                CanRunZapret = false;
                return;
            }

            string parentDir = Path.GetDirectoryName(
                _listsPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

            if (string.IsNullOrEmpty(parentDir))
            {
                IsZapretValid = false;
                CanRunZapret = false;
                return;
            }

            bool hasServiceBat = File.Exists(Path.Combine(parentDir, "service.bat"));
            bool hasGeneralBat = File.Exists(Path.Combine(parentDir, "general.bat"));

            IsZapretValid = hasServiceBat;
            CanRunZapret = hasServiceBat && hasGeneralBat;

            if (!hasServiceBat)
            {
                _terminal.WriteLine("⚠️ Zapret не найден или не установлен");
                _terminal.WriteLine("   Перейдите в Сервис -> ZDY и укажите папку lists");
                _terminal.WriteLine("   Там же можно установить Zapret\n");
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

        private async void InstallZapret()
        {
            _terminal.WriteLine("⏳ Проверка доступности Zapret...");

            string url = "https://github.com/Flowseal/zapret-discord-youtube/releases/download/1.9.9c/zapret-discord-youtube-1.9.9c.zip";
            string extraDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "extra");
            Directory.CreateDirectory(extraDir);

            string zipPath = null;
            bool downloaded = false;

            try
            {
                using var client = new HttpClient();
                client.Timeout = TimeSpan.FromSeconds(10);
                var response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Head, url));

                if (response.IsSuccessStatusCode)
                {
                    _terminal.WriteLine("✅ Ссылка доступна, скачивание...");
                    zipPath = Path.Combine(extraDir, "zapret-discord-youtube-1.9.9c.zip");
                    var fileBytes = await client.GetByteArrayAsync(url);
                    await File.WriteAllBytesAsync(zipPath, fileBytes);
                    downloaded = true;
                    _terminal.WriteLine($"✅ Скачано в {zipPath}");
                }
                else
                {
                    _terminal.WriteLine("⚠️ Ссылка недоступна, используем extra/zdy.zip");
                }
            }
            catch (Exception ex)
            {
                _terminal.WriteLine($"⚠️ Ошибка проверки/скачивания: {ex.Message}");
                _terminal.WriteLine("   Используем extra/zdy.zip");
            }

            if (!downloaded)
            {
                zipPath = Path.Combine(extraDir, "zdy.zip");
                if (!File.Exists(zipPath))
                {
                    _terminal.WriteLine("❌ extra/zdy.zip не найден!");
                    return;
                }
            }

            string zdyTarget = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "zdy");
            _terminal.WriteLine($"⏳ Распаковка в {zdyTarget}...");

            try
            {
                if (Directory.Exists(zdyTarget))
                {
                    Directory.Delete(zdyTarget, true);
                }

                ZipFile.ExtractToDirectory(zipPath, zdyTarget);
                NormalizeExtractedFolder(zdyTarget);
                _terminal.WriteLine("✅ Распаковка завершена");

                string listsPath = null;
                var listsDirs = Directory.GetDirectories(zdyTarget, "lists", SearchOption.AllDirectories);
                if (listsDirs.Length > 0)
                {
                    listsPath = listsDirs[0];
                }
                else
                {
                    listsPath = Path.Combine(zdyTarget, "lists");
                    Directory.CreateDirectory(listsPath);
                }

                ListsPath = listsPath;
                _terminal.WriteLine($"✅ Папка lists: {listsPath}");

                ValidateZapretInstallation();
            }
            catch (Exception ex)
            {
                _terminal.WriteLine($"❌ Ошибка распаковки: {ex.Message}");
            }
        }

        private void NormalizeExtractedFolder(string targetDir)
        {
            if (!Directory.Exists(targetDir)) return;

            var files = Directory.GetFiles(targetDir);
            var dirs = Directory.GetDirectories(targetDir);

            // Если в корне нет файлов и ровно одна папка — поднимаем её содержимое
            if (files.Length == 0 && dirs.Length == 1)
            {
                string subDir = dirs[0];
                _terminal.WriteLine($"   ↳ Обнаружена вложенная папка: {Path.GetFileName(subDir)}");
                _terminal.WriteLine("   ↳ Перемещаю содержимое на уровень выше...");

                // Перемещаем файлы
                foreach (var file in Directory.GetFiles(subDir, "*", SearchOption.AllDirectories))
                {
                    string relativePath = Path.GetRelativePath(subDir, file);
                    string destPath = Path.Combine(targetDir, relativePath);
                    string destDir = Path.GetDirectoryName(destPath);
                    if (!Directory.Exists(destDir))
                        Directory.CreateDirectory(destDir);
                    File.Move(file, destPath, overwrite: true);
                }

                // Удаляем пустую вложенную папку
                Directory.Delete(subDir, true);
            }
        }

        private void RunZapret()
        {
            string zdyDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "zdy");
            string generalBat = Path.Combine(zdyDir, "general.bat");

            if (!File.Exists(generalBat))
            {
                _terminal.WriteLine("❌ general.bat не найден в zdy/");
                return;
            }

            try
            {
                var psi = new ProcessStartInfo("cmd.exe", $"/c \"{generalBat}\"")
                {
                    UseShellExecute = true,
                    WorkingDirectory = zdyDir
                };
                Process.Start(psi);
                _terminal.WriteLine("✅ general.bat запущен");
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

        private void UpdatePythonButtonsState()
        {
            DebugLogger.Debug($"[PythonCheck] UpdatePythonButtonsState вызван: IsVenvExists={IsVenvExists}, IsSystemPythonAvailable={IsSystemPythonAvailable}");

            void DoUpdate()
            {
                DebugLogger.Debug($"[PythonCheck] DoUpdate: CanInstallPython={CanInstallPython}, CanCreateVenv={CanCreateVenv}");
                OnPropertyChanged(nameof(CanInstallPython));
                OnPropertyChanged(nameof(CanCreateVenv));
                CommandManager.InvalidateRequerySuggested();
            }

            if (Application.Current?.Dispatcher?.CheckAccess() == true)
            {
                DoUpdate();
            }
            else
            {
                Application.Current?.Dispatcher?.Invoke(DoUpdate);
            }
        }

        private void CheckVenvExists()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            bool exists = Directory.Exists(Path.Combine(baseDir, ".venv")) || Directory.Exists(Path.Combine(baseDir, "venv"));
            IsVenvExists = exists;
        }

        public async Task RefreshPythonStateAsync()
        {
            DebugLogger.Debug("[PythonCheck] RefreshPythonStateAsync вызван");
            CheckVenvExists();
            await CheckSystemPythonAsync();
        }

        private async Task CheckSystemPythonAsync()
        {
            DebugLogger.Debug("[PythonCheck] === Начало проверки системного Python ===");
            try
            {
                DebugLogger.Debug($"[PythonCheck] BaseDirectory: {AppDomain.CurrentDomain.BaseDirectory}");
                DebugLogger.Debug($"[PythonCheck] PATH: {Environment.GetEnvironmentVariable("PATH")}");

                DebugLogger.Debug("[PythonCheck] Шаг 1: проверка py launcher");
                if (await TryPythonCommand("py"))
                {
                    DebugLogger.Debug("[PythonCheck] ✅ py launcher сработал");
                    IsSystemPythonAvailable = true;
                    OnPropertyChanged(nameof(CanInstallPython));
                    OnPropertyChanged(nameof(CanCreateVenv));
                    CommandManager.InvalidateRequerySuggested();
                    return;
                }
                DebugLogger.Debug("[PythonCheck] ❌ py launcher не сработал");

                DebugLogger.Debug("[PythonCheck] Шаг 2: проверка через where python");
                string pythonPath = await FindPythonViaWhere();
                if (!string.IsNullOrEmpty(pythonPath))
                {
                    DebugLogger.Debug($"[PythonCheck] ✅ where python нашёл: {pythonPath}");
                    IsSystemPythonAvailable = true;
                    OnPropertyChanged(nameof(CanInstallPython));
                    OnPropertyChanged(nameof(CanCreateVenv));
                    CommandManager.InvalidateRequerySuggested();
                    return;
                }
                DebugLogger.Debug("[PythonCheck] ❌ where python ничего не нашёл");

                DebugLogger.Debug("[PythonCheck] Шаг 3: проверка python --version (UseShellExecute=true)");
                if (await TryPythonCommand("python", useShellExecute: true))
                {
                    DebugLogger.Debug("[PythonCheck] ✅ python (shell) сработал");
                    IsSystemPythonAvailable = true;
                    OnPropertyChanged(nameof(CanInstallPython));
                    OnPropertyChanged(nameof(CanCreateVenv));
                    CommandManager.InvalidateRequerySuggested();
                    return;
                }
                DebugLogger.Debug("[PythonCheck] ❌ python (shell) не сработал");

                DebugLogger.Debug("[PythonCheck] === Системный Python НЕ найден ===");
                IsSystemPythonAvailable = false;
                OnPropertyChanged(nameof(CanInstallPython));
                OnPropertyChanged(nameof(CanCreateVenv));
                CommandManager.InvalidateRequerySuggested();
            }
            catch (Exception ex)
            {
                DebugLogger.Error($"[PythonCheck] Исключение в CheckSystemPythonAsync: {ex.GetType().Name}: {ex.Message}");
                DebugLogger.Debug($"[PythonCheck] StackTrace: {ex.StackTrace}");
                IsSystemPythonAvailable = false;
                OnPropertyChanged(nameof(CanInstallPython));
                OnPropertyChanged(nameof(CanCreateVenv));
                CommandManager.InvalidateRequerySuggested();
            }
        }

        private async Task<bool> TryPythonCommand(string command, bool useShellExecute = false)
        {
            DebugLogger.Debug($"[PythonCheck] TryPythonCommand: cmd={command}, useShellExecute={useShellExecute}");
            try
            {
                var psi = new ProcessStartInfo(command, "--version")
                {
                    RedirectStandardOutput = !useShellExecute,
                    RedirectStandardError = !useShellExecute,
                    UseShellExecute = useShellExecute,
                    CreateNoWindow = !useShellExecute
                };
                using var p = Process.Start(psi);
                if (p == null)
                {
                    DebugLogger.Debug("[PythonCheck]   Process.Start вернул null");
                    return false;
                }

                string output = "", error = "";
                if (!useShellExecute)
                {
                    output = await p.StandardOutput.ReadToEndAsync();
                    error = await p.StandardError.ReadToEndAsync();
                }
                await p.WaitForExitAsync();

                string combined = (output + error).Trim();
                bool hasPython = combined.Contains("Python", StringComparison.OrdinalIgnoreCase);
                DebugLogger.Debug($"[PythonCheck]   ExitCode={p.ExitCode}, output='{output.Trim()}', error='{error.Trim()}', hasPython={hasPython}");

                return p.ExitCode == 0 && hasPython;
            }
            catch (Exception ex)
            {
                DebugLogger.Error($"[PythonCheck]   Исключение: {ex.GetType().Name}: {ex.Message}");
                return false;
            }
        }

        private async Task<string> FindPythonViaWhere()
        {
            try
            {
                var psi = new ProcessStartInfo("where", "python")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var p = Process.Start(psi);
                string output = await p.StandardOutput.ReadToEndAsync();
                string error = await p.StandardError.ReadToEndAsync();
                await p.WaitForExitAsync();

                DebugLogger.Debug($"[PythonCheck] where python: ExitCode={p.ExitCode}, output='{output.Trim()}', error='{error.Trim()}'");

                if (p.ExitCode != 0 || string.IsNullOrEmpty(output))
                    return null;

                string[] paths = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var path in paths)
                {
                    string trimmed = path.Trim();
                    if (string.IsNullOrEmpty(trimmed)) continue;

                    bool isWindowsApps = trimmed.Contains("WindowsApps", StringComparison.OrdinalIgnoreCase);
                    DebugLogger.Debug($"[PythonCheck]   Путь: {trimmed}, isWindowsApps={isWindowsApps}, exists={File.Exists(trimmed)}");

                    if (isWindowsApps)
                    {
                        DebugLogger.Debug("[PythonCheck]     Пропущен (WindowsApps alias)");
                        continue;
                    }

                    if (!File.Exists(trimmed))
                    {
                        DebugLogger.Debug("[PythonCheck]     Пропущен (файл не существует)");
                        continue;
                    }

                    var checkPsi = new ProcessStartInfo(trimmed, "--version")
                    {
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    using var checkProc = Process.Start(checkPsi);
                    if (checkProc == null) continue;

                    string checkOutput = await checkProc.StandardOutput.ReadToEndAsync();
                    string checkError = await checkProc.StandardError.ReadToEndAsync();
                    await checkProc.WaitForExitAsync();

                    bool ok = checkProc.ExitCode == 0 && (checkOutput + checkError).Contains("Python", StringComparison.OrdinalIgnoreCase);
                    DebugLogger.Debug($"[PythonCheck]     Проверка '{trimmed}': ExitCode={checkProc.ExitCode}, output='{checkOutput.Trim()}', ok={ok}");

                    if (ok)
                        return trimmed;
                }
                return null;
            }
            catch (Exception ex)
            {
                DebugLogger.Error($"[PythonCheck] Исключение в FindPythonViaWhere: {ex.GetType().Name}: {ex.Message}");
                return null;
            }
        }

        private async Task CreateVenvAsync()
        {
            _terminal.WriteLine("⏳ Создание виртуального окружения...");
            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string venvDir = Path.Combine(baseDir, ".venv");

                var psi = new ProcessStartInfo("python", $"-m venv \"{venvDir}\"")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var p = Process.Start(psi);
                await p.WaitForExitAsync();
                if (p.ExitCode != 0)
                {
                    string err = await p.StandardError.ReadToEndAsync();
                    _terminal.WriteLine($"❌ Ошибка создания venv: {err}");
                    return;
                }
                _terminal.WriteLine("✅ Venv создан.");

                string pipExe = Path.Combine(venvDir, "Scripts", "pip.exe");
                string reqPath = Path.Combine(baseDir, "data", "requirements.txt");

                bool installSuccess = false;
                if (File.Exists(reqPath))
                {
                    _terminal.WriteLine("⏳ Установка пакетов из requirements.txt...");
                    var pipPsi = new ProcessStartInfo(pipExe, $"install -r \"{reqPath}\"")
                    {
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    using var pipProc = Process.Start(pipPsi);
                    await pipProc.WaitForExitAsync();
                    installSuccess = pipProc.ExitCode == 0;
                    if (!installSuccess)
                    {
                        string err = await pipProc.StandardError.ReadToEndAsync();
                        _terminal.WriteLine($"⚠️ Ошибка установки из requirements.txt: {err}");
                    }
                    else
                    {
                        _terminal.WriteLine("✅ Пакеты установлены из requirements.txt.");
                    }
                }

                if (!installSuccess)
                {
                    _terminal.WriteLine("⏳ Попытка установки из локальных whl...");
                    string extraPythonDir = Path.Combine(baseDir, "extra", "python");
                    if (Directory.Exists(extraPythonDir))
                    {
                        string[] whlFiles = Directory.GetFiles(extraPythonDir, "*.whl");
                        foreach (var whl in whlFiles)
                        {
                            var whlPsi = new ProcessStartInfo(pipExe, $"install \"{whl}\"")
                            {
                                RedirectStandardOutput = true,
                                RedirectStandardError = true,
                                UseShellExecute = false,
                                CreateNoWindow = true
                            };
                            using var whlProc = Process.Start(whlPsi);
                            await whlProc.WaitForExitAsync();
                            if (whlProc.ExitCode != 0)
                            {
                                string err = await whlProc.StandardError.ReadToEndAsync();
                                _terminal.WriteLine($"⚠️ Ошибка установки {Path.GetFileName(whl)}: {err}");
                            }
                        }
                        _terminal.WriteLine("✅ Локальные пакеты установлены.");
                    }
                    else
                    {
                        _terminal.WriteLine("❌ Папка extra/python не найдена.");
                    }
                }

                CheckVenvExists();
                _terminal.WriteLine("✅ Окружение готово.");
            }
            catch (Exception ex)
            {
                _terminal.WriteLine($"❌ Ошибка: {ex.Message}");
            }
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
            RefreshZapretState();
            try
            {
                IsWarpInstalled = await _warpManager.IsInstalledAsync();
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

        private async Task InstallWarpAsync()
        {
            try
            {
                _terminal.WriteLine("⏳ Проверка доступности сервера Cloudflare...");
                string url = "https://downloads.cloudflareclient.com/v1/download/windows/version/2026.6.822.0";
                string extraDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "extra");
                Directory.CreateDirectory(extraDir);
                string msiPath = Path.Combine(extraDir, "WARP.msi");

                bool downloaded = false;
                try
                {
                    using var client = new HttpClient();
                    client.Timeout = TimeSpan.FromSeconds(10);
                    var response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Head, "https://downloads.cloudflareclient.com"));
                    if (response.IsSuccessStatusCode)
                    {
                        _terminal.WriteLine("✅ Сервер доступен, скачивание...");
                        var fileBytes = await client.GetByteArrayAsync(url);
                        await File.WriteAllBytesAsync(msiPath, fileBytes);
                        downloaded = true;
                        _terminal.WriteLine($"✅ Скачано в {msiPath}");
                    }
                    else
                    {
                        _terminal.WriteLine("⚠️ Сервер недоступен, проверяю локальный файл...");
                    }
                }
                catch (Exception ex)
                {
                    _terminal.WriteLine($"⚠️ Ошибка проверки/скачивания: {ex.Message}");
                    _terminal.WriteLine("   Проверяю локальный файл...");
                }

                if (!downloaded)
                {
                    if (!File.Exists(msiPath))
                    {
                        _terminal.WriteLine("❌ Файл WARP.msi не найден в extra/");
                        return;
                    }
                    _terminal.WriteLine($"✅ Используем локальный файл: {msiPath}");
                }

                _terminal.WriteLine("⏳ Установка WARP...");
                var psi = new ProcessStartInfo("msiexec", $"/i \"{msiPath}\" /quiet /norestart")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var p = Process.Start(psi);
                await p.WaitForExitAsync();

                if (p.ExitCode == 0)
                {
                    _terminal.WriteLine("✅ WARP установлен");
                    await UpdateStatusAsync();
                }
                else
                {
                    _terminal.WriteLine($"❌ Ошибка установки, код: {p.ExitCode}");
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

        private async Task InstallPythonAsync()
        {
            _terminal.WriteLine("⏳ Проверка и установка окружения Python...");
            var progress = new Progress<string>(msg => _terminal.WriteLine(msg));
            bool ok = await _pythonEnvManager.EnsureEnvironmentAsync(AppDomain.CurrentDomain.BaseDirectory, progress);
            if (ok)
                _terminal.WriteLine("✅ Окружение Python готово.");
            else
                _terminal.WriteLine("❌ Ошибка установки Python.");
        }

        private void FixPythonPath()
        {
            try
            {
                Process.Start(new ProcessStartInfo("https://github.com/AITISPEC/Helpful/releases/tag/fix-python-path")
                {
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                _terminal.WriteLine($"❌ Не удалось открыть ссылку: {ex.Message}");
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}