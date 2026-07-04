using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using PlatformLauncher.Core.Interfaces;
using PlatformLauncher.Core.UseCases;
using PlatformLauncher.Domain.Models;
using PlatformLauncher.Infrastructure.Configuration;
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
using System.Security.Principal;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace PlatformLauncher.Presentation.ViewModels
{
    public class SortOption
    {
        public string Id { get; set; }
        public string DisplayName { get; set; }
    }

    public class MainViewModel : INotifyPropertyChanged
    {
        private readonly IUpdateService _updateService;
        private readonly ISettingsManager _settingsManager;
        private readonly InstallGameUseCase _installGameUseCase;
        private readonly UninstallGameUseCase _uninstallGameUseCase;
        private readonly SyncPresetsUseCase _syncPresetsUseCase;
        private readonly StartMonitoringUseCase _startMonitoringUseCase;
        private readonly StopMonitoringUseCase _stopMonitoringUseCase;
        private readonly IWinwsLocator _winwsLocator;
        private readonly IPythonEnvironmentManager _pythonEnvManager;
        private readonly ILogger _logger;
        private readonly ITerminalOutput _terminal;
        private readonly IServiceProvider _serviceProvider;
        private readonly ISessionOrchestrator _sessionOrchestrator;
        private readonly IAppConfigService _appConfigService;

        private string _appVersion = "?.?.?";
        private ObservableCollection<GamePreset> _games = new();
        private GamePreset _selectedGame;

        // Путь к конфигу для проверки наличия файла
        public string ConfigPath => SelectedGame == null ? null :
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "configs", $"{SelectedGame.Id}.yaml");
        private bool _isAdministrator;
        private string _listsPath;
        private bool _isRunning;
        private string _statusText = "Готов";
        private string _statusBarText = "Загрузка пресетов...";
        private string _filterHeader = "Фильтры";
        private bool _filterInstalled;
        private bool _filterNotInstalled;
        private bool _filterCustom;
        private bool _pythonValidationMessageShown = false;
        private bool _isInstalling = false;
        private bool _isUninstalling = false;
        private string _searchText = string.Empty;
        private SortOption _selectedSortOption;

        public string AppVersion
        {
            get => _appVersion;
            set { _appVersion = value; OnPropertyChanged(); }
        }

        public ObservableCollection<GamePreset> Games
        {
            get => _games;
            set { _games = value; OnPropertyChanged(); }
        }

        public GamePreset SelectedGame
        {
            get => _selectedGame;
            set { _selectedGame = value; OnPropertyChanged(); UpdateButtonStates(); }
        }

        public bool IsAdministrator
        {
            get => _isAdministrator;
            set { _isAdministrator = value; OnPropertyChanged(); UpdateButtonStates(); }
        }

        public string ListsPath
        {
            get => _listsPath;
            set
            {
                if (_listsPath == value) return;
                _listsPath = value;
                OnPropertyChanged();
            }
        }

        public bool IsRunning
        {
            get => _isRunning;
            set { _isRunning = value; OnPropertyChanged(); UpdateButtonStates(); }
        }

        public string StatusText
        {
            get => _statusText;
            set { _statusText = value; OnPropertyChanged(); }
        }

        public string StatusBarText
        {
            get => _statusBarText;
            set { _statusBarText = value; OnPropertyChanged(); }
        }

        public bool GlobalInstalling
        {
            get => _isInstalling;
            private set
            {
                if (_isInstalling == value) return;
                _isInstalling = value;
                OnPropertyChanged();
                UpdateButtonStates();
            }
        }

        public bool GlobalUninstalling
        {
            get => _isUninstalling;
            private set
            {
                if (_isUninstalling == value) return;
                _isUninstalling = value;
                OnPropertyChanged();
                UpdateButtonStates();
            }
        }
        public string FilterHeader
        {
            get => _filterHeader;
            set { _filterHeader = value; OnPropertyChanged(); }
        }

        public bool FilterInstalled
        {
            get => _filterInstalled;
            set
            {
                if (_filterInstalled == value) return;
                _filterInstalled = value;
                if (value && _filterNotInstalled)
                {
                    _filterNotInstalled = false;
                    OnPropertyChanged(nameof(FilterNotInstalled));
                }
                OnPropertyChanged();
                _settingsManager.SetFilterState(_filterInstalled, _filterNotInstalled, _filterCustom);
                ApplyFilters();
            }
        }

        public bool FilterNotInstalled
        {
            get => _filterNotInstalled;
            set
            {
                if (_filterNotInstalled == value) return;
                _filterNotInstalled = value;
                if (value && _filterInstalled)
                {
                    _filterInstalled = false;
                    OnPropertyChanged(nameof(FilterInstalled));
                }
                OnPropertyChanged();
                _settingsManager.SetFilterState(_filterInstalled, _filterNotInstalled, _filterCustom);
                ApplyFilters();
            }
        }

        public bool FilterCustom
        {
            get => _filterCustom;
            set
            {
                if (_filterCustom == value) return;
                _filterCustom = value;
                OnPropertyChanged();
                _settingsManager.SetFilterState(_filterInstalled, _filterNotInstalled, _filterCustom);
                ApplyFilters();
            }
        }

        public string SearchText
        {
            get => _searchText;
            set
            {
                if (_searchText == value) return;
                _searchText = value;
                OnPropertyChanged();
                ApplyFilters();
            }
        }

        public List<SortOption> SortOptions { get; } = new List<SortOption>
            {
                new SortOption { Id = "installed_first", DisplayName = "Установленные" },
                new SortOption { Id = "alphabetical", DisplayName = "По алфавиту" },
                new SortOption { Id = "not_installed", DisplayName = "Не установленные" }
            };

        public SortOption SelectedSortOption
        {
            get => _selectedSortOption;
            set
            {
                if (_selectedSortOption == value) return;
                _selectedSortOption = value;
                OnPropertyChanged();
                ApplyFilters();
            }
        }

        public string StartButtonText
        {
            get
            {
                if (SelectedGame == null) return "Выберите игру";
                if (SelectedGame.Id == "monitor") return "Недоступно";

                // Пункт 2.1-2.3: активность текста кнопки в зависимости от статуса
                string status;

                // Текст "Скачать": файл отсутствует
                if (!File.Exists(ConfigPath))
                    status = "Скачать";
                // Текст "Установить": файл существует, но installed = false или отсутствует
                else if (_updateService.LoadPresets().Any(p => p.Id == SelectedGame.Id && !p.Installed))
                    status = "Установить";
                // Текст "Запустить": installed = true
                else
                    status = "Запустить";

                return status;
            }
        }

        public bool CanStart
        {
            get
            {
                if (IsRunning) return false;
                if (SelectedGame == null) return false;
                if (SelectedGame.Id == "monitor") return false;
                if (!IsAdministrator) return false;
                if (_isInstalling || _isUninstalling) return false;

                // Python и lists требуются только для запуска (когда игра уже установлена)
                if (SelectedGame.Installed)
                {
                    if (!IsPythonValid())
                    {
                        if (!_pythonValidationMessageShown)
                        {
                            _terminal.WriteLine("⚠️ Python не прошёл валидацию. Перейдите в Сервис -> Python");
                            _pythonValidationMessageShown = true;
                        }
                        return false;
                    }
                    _pythonValidationMessageShown = false;

                    if (string.IsNullOrEmpty(ListsPath) || !Directory.Exists(ListsPath))
                    {
                        _terminal.WriteLine("❌ Папка lists не найдена. Укажите путь в Сервис -> ZDY");
                        return false;
                    }
                }

                return true;
            }
        }

        public bool CanMonitor
        {
            get
            {
                if (IsRunning) return false;
                if (SelectedGame == null) return false;
                if (!IsAdministrator) return false;
                if (_isInstalling || _isUninstalling) return false;

                if (!IsPythonValid())
                {
                    if (!_pythonValidationMessageShown)
                    {
                        _terminal.WriteLine("⚠️ Python не прошёл валидацию. Перейдите в Сервис -> Python");
                        _pythonValidationMessageShown = true;
                    }
                    return false;
                }

                // Сброс флага, если Python стал валидным
                _pythonValidationMessageShown = false;

                return SelectedGame.ConfigDownloaded;
            }
        }

        public bool CanInstall =>
            SelectedGame != null &&
            SelectedGame.Id != "monitor" &&
            !SelectedGame.Installed &&
            !_isInstalling && !_isUninstalling;

        public bool CanUninstall =>
             SelectedGame != null &&
             SelectedGame.Installed &&
             SelectedGame.Id != "monitor" &&
             !_isInstalling && !_isUninstalling; 

        public bool CanStop => IsRunning;

        public string ContextMenuActionText => StartButtonText;

        public ICommand RefreshPresetsCommand { get; }
        public ICommand ResetFiltersCommand { get; }
        public ICommand StartCommand { get; }
        public ICommand MonitorCommand { get; }
        public ICommand StopCommand { get; }
        public ICommand InstallCommand { get; }
        public ICommand UninstallCommand { get; }
        public ICommand PropertiesCommand { get; }
        public ICommand RunCommandCommand { get; }
        public ICommand ClearConsoleCommand { get; }
        public ICommand ShowWindowCommand { get; }

        // Пункт 3.1: контекстное меню соответствует поведению кнопки
        // + Пункт 3.2: для monitor.yaml два пункта - "Мониторинг" и "Свойства"
        public ICommand ContextMenuActionCommand
        {
            get
            {
                if (SelectedGame == null) return null;

                if (SelectedGame.Id == "monitor")
                    return MonitorCommand; // Пункт 3.2 для monitor.yaml

                // Пункт 3.1: статус определяется по installed из presets.yaml или отсутствию файла
                string status;
                if (!File.Exists(ConfigPath))
                    status = "Скачать";
                else if (_updateService.LoadPresets().Any(p => p.Id == SelectedGame.Id && !p.Installed))
                    status = "Установить";
                else
                    status = "Запустить";

                return status switch
                {
                    "Скачать" => InstallCommand,
                    "Установить" => InstallCommand, // По логике кнопки - это установка
                    _ => StartCommand // Запуск
                };
            }
        }

        public MainViewModel(
            IUpdateService updateService,
            ISettingsManager settingsManager,
            InstallGameUseCase installGameUseCase,
            UninstallGameUseCase uninstallGameUseCase,
            SyncPresetsUseCase syncPresetsUseCase,
            StartMonitoringUseCase startMonitoringUseCase,
            StopMonitoringUseCase stopMonitoringUseCase,
            IWinwsLocator winwsLocator,
            IPythonEnvironmentManager pythonEnvManager,
            ILogger logger,
            ITerminalOutput terminal,
            ISessionOrchestrator sessionOrchestrator,
            IServiceProvider serviceProvider)
        {
            _updateService = updateService;
            _settingsManager = settingsManager;
            _installGameUseCase = installGameUseCase;
            _uninstallGameUseCase = uninstallGameUseCase;
            _syncPresetsUseCase = syncPresetsUseCase;
            _startMonitoringUseCase = startMonitoringUseCase;
            _stopMonitoringUseCase = stopMonitoringUseCase;
            _winwsLocator = winwsLocator;
            _pythonEnvManager = pythonEnvManager;
            _logger = logger;
            _terminal = terminal;
            _serviceProvider = serviceProvider;
            _appConfigService = serviceProvider.GetRequiredService<IAppConfigService>();
            _sessionOrchestrator = sessionOrchestrator;
            _sessionOrchestrator.OutputReceived += msg => _terminal.WriteLine(msg);
            _sessionOrchestrator.SessionEnded += OnSessionEnded;
            _selectedSortOption = SortOptions[0];
            OnPropertyChanged(nameof(SelectedSortOption));

            RefreshPresetsCommand = new RelayCommand(async _ => await RefreshPresetsAsync(), _ => true);
            ResetFiltersCommand = new RelayCommand(_ => ResetFilters());
            StartCommand = new RelayCommand(async _ => await StartAsync(false), _ => CanStart);
            MonitorCommand = new RelayCommand(async _ => await StartAsync(true), _ => CanMonitor);
            StopCommand = new RelayCommand(async _ => await StopAsync(), _ => CanStop);
            InstallCommand = new RelayCommand(async _ => await InstallAsync(), _ => CanInstall);
            UninstallCommand = new RelayCommand(async _ => await UninstallAsync(), _ => CanUninstall);
            PropertiesCommand = new RelayCommand(_ => ShowProperties(), _ => SelectedGame != null);
            RunCommandCommand = new RelayCommand(async param => await RunCommandAsync(param?.ToString()));
            ClearConsoleCommand = new RelayCommand(_ => ClearConsole());
            ShowWindowCommand = new RelayCommand(_ => ShowWindow());

            _ = Task.Run(async () =>
            {
                try
                {
                    await InitializeAsync();
                    ClearConsole();
                }
                catch (Exception ex)
                {
                    DebugLogger.WriteException("InitializeAsync failed", ex);
                    _terminal.WriteLine($"❌ Критическая ошибка инициализации: {ex.Message}");
                }
            });
        }

        private void ShowWindow()
        {
            Application.Current.MainWindow.Show();
            Application.Current.MainWindow.WindowState = WindowState.Normal;
            Application.Current.MainWindow.Activate();
        }

        private static bool IsCurrentUserAdministrator()
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }

        private async Task InitializeAsync()
        {
            try
            {
                IsAdministrator = IsCurrentUserAdministrator();
                await FindListsPathAsync();
                var (installed, notInstalled, custom) = _settingsManager.GetFilterState();
                _filterInstalled = installed;
                _filterNotInstalled = notInstalled;
                _filterCustom = custom;
                OnPropertyChanged(nameof(FilterInstalled));
                OnPropertyChanged(nameof(FilterNotInstalled));
                OnPropertyChanged(nameof(FilterCustom));
                LoadGames();
                StatusBarText = $"Загружено пресетов: {Games.Count}";
                var appConfig = _serviceProvider.GetRequiredService<IAppConfigService>().Load();
                AppVersion = appConfig.App?.AppVersion ?? "?.?.?";
                _ = Task.Run(async () => await CheckUnrestoredBackupsAsync());
            }
            catch (Exception ex)
            {
                DebugLogger.WriteException("InitializeAsync error", ex);
                _terminal.WriteLine($"❌ Ошибка инициализации: {ex.Message}");
            }
        }

        private async Task CheckUnrestoredBackupsAsync()
        {
            try
            {
                var backupManager = _serviceProvider.GetRequiredService<IBackupManager>();
                var unrestored = backupManager.GetUnrestoredBackups();

                if (unrestored.Count > 0)
                {
                    string latest = backupManager.GetLatestUnrestoredBackup();
                    if (latest != null && !string.IsNullOrEmpty(ListsPath) && Directory.Exists(ListsPath))
                    {
                        string gameId = new DirectoryInfo(Path.GetDirectoryName(latest)).Name;

                        var result = await Application.Current.Dispatcher.InvokeAsync(() =>
                            MessageBox.Show(
                                $"Обнаружен невосстановленный бэкап для '{gameId}'.\nВосстановить lists? ",
                                "Восстановление бэкапа ",
                                MessageBoxButton.YesNo,
                                MessageBoxImage.Warning));

                        if (result == MessageBoxResult.Yes)
                        {
                            bool restored = await backupManager.RestoreBackupAsync(ListsPath, latest);
                            if (restored)
                            {
                                backupManager.MarkAsSaved(latest);
                                _terminal.WriteLine($"✅ Бэкап для {gameId} восстановлен. ");
                            }
                            else
                            {
                                _terminal.WriteLine($"❌ Ошибка восстановления бэкапа для {gameId}. ");
                            }
                        }
                        else
                        {
                            backupManager.MarkAsNoRestore(latest);
                            _terminal.WriteLine($"ℹ️ Бэкап для {gameId} пропущен пользователем. ");
                        }
                    }
                    else
                    {
                        _terminal.WriteLine($"⚠️ Обнаружено невосстановленных бэкапов: {unrestored.Count}. Укажите папку lists для восстановления. ");
                    }
                }
            }
            catch (Exception ex)
            {
                DebugLogger.WriteException("CheckUnrestoredBackupsAsync failed ", ex);
            }
        }

        private async Task FindListsPathAsync()
        {
            try
            {
                ListsPath = await _winwsLocator.FindListsPathAsync();
                if (string.IsNullOrEmpty(ListsPath))
                {
                    string savedPath = _settingsManager.GetListsPath();
                    if (!string.IsNullOrEmpty(savedPath) && Directory.Exists(savedPath))
                    {
                        ListsPath = savedPath;
                        _terminal.WriteLine($"ℹ️ Используется сохранённый путь: {savedPath}");
                    }
                    else
                    {
                        _terminal.WriteLine("⚠️ Папка lists не найдена автоматически. Укажите вручную через меню Сервис -> ZDY.");
                    }
                }
            }
            catch (Exception ex)
            {
                DebugLogger.WriteException("FindListsPathAsync failed", ex);
                throw;
            }
        }

        private void LoadGames()
        {
            var presets = _updateService.LoadPresets();
            Games.Clear();
            foreach (var p in presets)
            {
                string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "configs", $"{p.Id}.yaml");

                // Пункт 0.1: пресет считается скачанным, ТОЛЬКО если конфиг валиден
                bool isValid = false;
                if (File.Exists(configPath))
                {
                    try
                    {
                        var config = _updateService.LoadGameConfig(p.Id);
                        isValid = config != null;
                    }
                    catch
                    {
                        isValid = false;
                    }
                }
                p.ConfigDownloaded = isValid;
                Games.Add(p);
            }
            ApplyFilters();
        }

        private void ApplyFilters()
        {
            var allPresets = _updateService.LoadPresets();
            var filtered = allPresets.AsEnumerable();

            // Фильтр по поиску
            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                string search = SearchText.ToLower();
                filtered = filtered.Where(p => p.Name.ToLower().Contains(search) ||
                                               p.Id.ToLower().Contains(search));
            }

            // Фильтр по статусу установки (взаимоисключающие)
            if (FilterInstalled)
                filtered = filtered.Where(p => p.Installed);
            else if (FilterNotInstalled)
                filtered = filtered.Where(p => !p.Installed);

            // Фильтр "Пользовательские" (локальные)
            if (FilterCustom)
                filtered = filtered.Where(p => IsLocal(p.Id));

            // Сортировка
            var list = _selectedSortOption?.Id switch
            {
                "alphabetical" => filtered.OrderBy(p => p.Name).ToList(),
                "not_installed" => filtered
                    .OrderBy(p => p.Installed ? 1 : 0)
                    .ThenBy(p => p.Name)
                    .ToList(),
                _ => filtered // installed_first (по умолчанию)
                    .OrderBy(p => p.Installed ? 0 : 1)
                    .ThenBy(p => p.Name)
                    .ToList()
            };

            Games.Clear();
            foreach (var p in list)
                Games.Add(p);

            // Обновляем заголовок Expander
            int total = allPresets.Count;
            int shown = Games.Count;
            FilterHeader = shown == total
                ? $"Фильтры ({shown})"
                : $"Фильтры (показано {shown} из {total})";
        }

        private bool IsLocal(string gameId)
        {
            string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "configs", $"{gameId}.yaml");
            return File.Exists(configPath);
        }

        private void OnSessionEnded(bool success)
        {
            IsRunning = false;
            StatusText = success ? "Завершён" : "Ошибка";
            StatusBarText = success ? "Завершено успешно" : "Завершено с ошибкой";
            _terminal.WriteLine(success ? "✅ Мониторинг остановлен" : "❌ Мониторинг завершился с ошибкой");
            UpdateButtonStates();
        }

        private void UpdateButtonStates()
        {
            OnPropertyChanged(nameof(CanStart));
            OnPropertyChanged(nameof(CanMonitor));
            OnPropertyChanged(nameof(CanStop));
            OnPropertyChanged(nameof(CanInstall));
            OnPropertyChanged(nameof(CanUninstall));
            OnPropertyChanged(nameof(StartButtonText));
            OnPropertyChanged(nameof(ContextMenuActionText));
            OnPropertyChanged(nameof(ContextMenuActionCommand));
            CommandManager.InvalidateRequerySuggested();
        }

        public async Task RefreshPresetsAsync()
        {
            _terminal.WriteLine("⏳ Синхронизация пресетов...");
            StatusBarText = "Синхронизация...";
            bool ok = await _syncPresetsUseCase.ExecuteAsync();
            _terminal.WriteLine(ok ? "✅ Синхронизация завершена" : "❌ Ошибка синхронизации");
            LoadGames();
            StatusBarText = $"Загружено {Games.Count} пресетов";
        }

        private async Task StartAsync(bool monitorOnly)
        {
            if (SelectedGame == null) return;

            // Если конфиг не скачан — скачиваем
            if (!SelectedGame.ConfigDownloaded)
            {
                var progress = new Progress<string>(msg => _terminal.WriteLine(msg));
                var (success, error, updated) = await _installGameUseCase.ExecuteDownloadAsync(SelectedGame, progress);
                if (success && updated != null)
                {
                    var index = Games.IndexOf(SelectedGame);
                    if (index >= 0)
                        Games[index] = updated;
                    SelectedGame = updated;
                    _terminal.WriteLine($"✅ Конфиг {updated.Name} скачан");
                    UpdateButtonStates();
                }
                else
                {
                    _terminal.WriteLine($"❌ Ошибка: {error}");
                }
                return;
            }

            // Если конфиг скачан, но не установлен — устанавливаем
            if (!monitorOnly && !SelectedGame.Installed)
            {
                var progress = new Progress<string>(msg => _terminal.WriteLine(msg));
                var (success, error, updated) = await _installGameUseCase.ExecuteInstallAsync(SelectedGame, progress);
                if (success && updated != null)
                {
                    var index = Games.IndexOf(SelectedGame);
                    if (index >= 0)
                        Games[index] = updated;
                    SelectedGame = updated;
                    _terminal.WriteLine($"✅ Фикс {updated.Name} установлен");
                    UpdateButtonStates();
                }
                else
                {
                    _terminal.WriteLine($"❌ Ошибка: {error}");
                }
                return;
            }

            // Для мониторинга установка не требуется — сразу запускаем
            if (!monitorOnly && !SelectedGame.Installed)
            {
                var progress = new Progress<string>(msg => _terminal.WriteLine(msg));
                var (success, error, updated) = await _installGameUseCase.ExecuteInstallAsync(SelectedGame, progress);
                if (success && updated != null)
                {
                    var index = Games.IndexOf(SelectedGame);
                    if (index >= 0)
                        Games[index] = updated;
                    SelectedGame = updated;
                    _terminal.WriteLine($"✅ Фикс {updated.Name} установлен");
                    UpdateButtonStates();
                }
                else
                {
                    _terminal.WriteLine($"❌ Ошибка: {error}");
                }
                return;
            }

            // Жёсткая проверка Python ДО бэкапа и санации
            string pythonExe = _pythonEnvManager.GetVenvPythonPath();
            if (string.IsNullOrEmpty(pythonExe) || !File.Exists(pythonExe))
            {
                _terminal.WriteLine("⚠️ Python не найден или не установлен");
                _terminal.WriteLine("   Перейдите в Сервис -> Python, чтобы устранить неисправность");
                return;
            }

            // Проверка lists нужна ТОЛЬКО для фикса, не для мониторинга
            if (!monitorOnly && (string.IsNullOrEmpty(ListsPath) || !Directory.Exists(ListsPath)))
            {
                _terminal.WriteLine("❌ Папка lists не существует или не выбрана.");
                return;
            }

            // Для мониторинга без lists - предупреждение
            if (monitorOnly && (string.IsNullOrEmpty(ListsPath) || !Directory.Exists(ListsPath)))
            {
                _terminal.WriteLine("⚠️ Мониторинг без папки lists (только просмотр соединений)");
            }

            _terminal.Clear();

            bool warpEnabled = _settingsManager.GetWarpEnabled(SelectedGame.Id);
            bool filterProcesses = !monitorOnly;

            try
            {
                IsRunning = true;
                StatusText = "Запуск...";
                await _sessionOrchestrator.StartAsync(SelectedGame.Id, ListsPath, monitorOnly, warpEnabled, filterProcesses);
                StatusText = monitorOnly ? "Мониторинг" : "Активен";
                StatusBarText = $"{SelectedGame.Name} {(monitorOnly ? "мониторинг" : "запущен")}";
            }
            catch (Exception ex)
            {
                _terminal.WriteLine($"❌ Ошибка: {ex.Message}");
                StatusText = "Ошибка";
                IsRunning = false;
                StatusBarText = "Ошибка запуска";
            }
        }

        private async Task StopAsync()
        {
            try
            {
                StatusText = "Остановка...";
                await _sessionOrchestrator.StopAsync();
                StatusText = "Завершён";
                IsRunning = false;
            }
            catch (Exception ex)
            {
                _terminal.WriteLine($"❌ Ошибка остановки: {ex.Message}");
                StatusText = "Ошибка";
                IsRunning = false;
                StatusBarText = "Ошибка остановки";
            }
        }

        private async Task InstallAsync()
        {
            if (SelectedGame == null) return;

            GlobalInstalling = true;
            UpdateButtonStates();

            var progress = new Progress<string>(msg => _terminal.WriteLine(msg));

            if (!SelectedGame.ConfigDownloaded)
            {
                var (success, error, updated) = await _installGameUseCase.ExecuteDownloadAsync(SelectedGame, progress);
                if (success && updated != null)
                {
                    var index = Games.IndexOf(SelectedGame);
                    if (index >= 0)
                        Games[index] = updated;
                    SelectedGame = updated;
                    _terminal.WriteLine($"✅ Конфиг {updated.Name} скачан");
                }
                else
                {
                    _terminal.WriteLine($"❌ Ошибка: {error}");
                }
            }
            else
            {
                var (success, error, updated) = await _installGameUseCase.ExecuteInstallAsync(SelectedGame, progress);
                if (success && updated != null)
                {
                    var index = Games.IndexOf(SelectedGame);
                    if (index >= 0)
                        Games[index] = updated;
                    SelectedGame = updated;
                    _terminal.WriteLine($"✅ Фикс {updated.Name} установлен");
                }
                else
                {
                    _terminal.WriteLine($"❌ Ошибка: {error}");
                }
            }

            GlobalInstalling = false;
            UpdateButtonStates();
        }

        private async Task UninstallAsync()
        {
            if (SelectedGame == null) return;

            GlobalInstalling = false;
            UpdateButtonStates();

            var progress = new Progress<string>(msg => _terminal.WriteLine(msg));
            var (success, error) = await _uninstallGameUseCase.ExecuteAsync(SelectedGame, progress);
            if (success)
            {
                SelectedGame.Installed = false;
                _terminal.WriteLine($"✅ Фикс {SelectedGame.Name} удалён");
                StatusBarText = $"Фикс {SelectedGame.Name} удалён";
                LoadGames();
            }
            else
            {
                _terminal.WriteLine($"❌ Ошибка: {error}");
                StatusBarText = "Ошибка удаления";
            }

            GlobalInstalling = false;
            UpdateButtonStates();
        }

        private void ShowProperties()
        {
            if (SelectedGame == null) return;
            var dialog = new GamePropertiesDialog(SelectedGame, _settingsManager);
            dialog.Owner = Application.Current.MainWindow;
            if (dialog.ShowDialog() == true)
            {
                _terminal.WriteLine($"✅ Настройки для {SelectedGame.Name} сохранены");
            }
        }

        private async Task RunCommandAsync(string command)
        {
            if (string.IsNullOrEmpty(command))
            {
                _terminal.WriteLine("❌ Команда не указана");
                return;
            }

            _terminal.WriteLine($"> {command}");
            try
            {
                string pythonExe = _pythonEnvManager.GetVenvPythonPath();
                if (string.IsNullOrEmpty(pythonExe) || !File.Exists(pythonExe))
                {
                    _terminal.WriteLine("❌ Виртуальное окружение Python не найдено");
                    return;
                }
                pythonExe = $"\"{pythonExe}\"";
                string cmd = command;
                if (cmd.StartsWith("python "))
                    cmd = cmd.Replace("python ", pythonExe + " ");
                else if (cmd.StartsWith("pip "))
                    cmd = cmd.Replace("pip ", pythonExe + " -m pip ");
                else
                    cmd = pythonExe + " -m " + cmd;

                var psi = new ProcessStartInfo("cmd.exe", "/c " + cmd)
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = System.Text.Encoding.UTF8,
                    StandardErrorEncoding = System.Text.Encoding.UTF8
                };

                using var proc = Process.Start(psi);
                var outputTask = proc.StandardOutput.ReadToEndAsync();
                var errorTask = proc.StandardError.ReadToEndAsync();
                var exitTask = proc.WaitForExitAsync();

                if (await Task.WhenAny(exitTask, Task.Delay(30000)) != exitTask)
                {
                    try { proc.Kill(); }
                    catch (Exception ex) { _logger.Warning($"Не удалось принудительно завершить команду: {ex.Message}"); }
                    _terminal.WriteLine("⚠️ Команда превысила время выполнения (30 сек), принудительно завершена");
                    return;
                }

                string output = await outputTask;
                string error = await errorTask;
                if (!string.IsNullOrEmpty(output))
                    _terminal.WriteLine(output);
                if (!string.IsNullOrEmpty(error))
                    _terminal.WriteLine($"⚠️ {error}");
            }
            catch (Exception ex)
            {
                _terminal.WriteLine($"❌ Ошибка: {ex.Message}");
            }
        }

        private void ResetFilters()
        {
            FilterInstalled = false;
            FilterNotInstalled = false;
            FilterCustom = false;
        }
        public void ClearConsole()
        {
            _terminal.Clear();
            _terminal.WriteLine("=== Fix Traffic Pipeline ===");
            _terminal.WriteLine("\nВыберите игру из списка слева.");
            _terminal.WriteLine("\n• Запустить фикс — применяет настройки, создаёт бэкап lists");
            _terminal.WriteLine("\n  Запуск с WARP — в свойствах фикса (ПКМ > Свойства).");
            _terminal.WriteLine("\n• Мониторинг — только просмотр соединений, без изменения lists");
            _terminal.WriteLine("\nДля обновления списка игр нажмите «Обновить список»\n");

            if (!IsZapretValid())
            {
                _terminal.WriteLine("⚠️ Zapret не найден или не установлен");
                _terminal.WriteLine("   Перейдите в Сервис -> ZDY и укажите папку lists");
                _terminal.WriteLine("   Там же можно установить Zapret\n");
            }
        }

        private bool IsZapretValid()
        {
            if (string.IsNullOrEmpty(ListsPath) || !Directory.Exists(ListsPath))
                return false;

            string parentDir = Path.GetDirectoryName(
                ListsPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

            if (string.IsNullOrEmpty(parentDir))
                return false;

            return File.Exists(Path.Combine(parentDir, "service.bat"));
        }

        private bool IsPythonValid()
        {
            string pythonExe = _pythonEnvManager.GetVenvPythonPath();
            return !string.IsNullOrEmpty(pythonExe) && File.Exists(pythonExe);
        }

        private bool IsConfigValid(string configPath)
        {
            try
            {
                var config = _updateService.LoadGameConfig(Path.GetFileNameWithoutExtension(configPath));
                return config != null;  // LoadGameConfig сам бросает исключение при невалидном
            }
            catch
            {
                return false;
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}