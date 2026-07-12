using Microsoft.Extensions.DependencyInjection;
using PlatformLauncher.Core.Interfaces;
using PlatformLauncher.Core.UseCases;
using PlatformLauncher.Domain.Models;
using PlatformLauncher.Presentation.Commands;
using PlatformLauncher.Presentation.Views;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace PlatformLauncher.Presentation.ViewModels
{
    public class SortOption
    {
        public string Id { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
    }

    public class MainViewModel : INotifyPropertyChanged
    {
        private readonly IGameListService _gameListService;
        private readonly IPythonValidatorService _pythonValidator;
        private readonly IZapretValidatorService _zapretValidator;
        private readonly ICommandRunnerService _commandRunner;
        private readonly ISettingsManager _settingsManager;
        private readonly InstallGameUseCase _installGameUseCase;
        private readonly UninstallGameUseCase _uninstallGameUseCase;
        private readonly SyncPresetsUseCase _syncPresetsUseCase;
        private readonly StartMonitoringUseCase _startMonitoringUseCase;
        private readonly StopMonitoringUseCase _stopMonitoringUseCase;
        private readonly IWinwsLocator _winwsLocator;
        private readonly ILogger _logger;
        private readonly ITerminalOutput _terminal;
        private readonly IServiceProvider _serviceProvider;
        private readonly ISessionOrchestrator _sessionOrchestrator;
        private readonly IUpdateService _updateService;
        private readonly IPortsManager _portsManager;

        private string _appVersion = "?.?.?";
        private GamePreset? _selectedGame;
        private bool _isAdministrator;
        private string _listsPath = string.Empty;
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
        private bool _isPortsProcessing = false;
        private string _searchText = string.Empty;
        private SortOption _selectedSortOption;
        private bool _listsPathWarningShown = false;

        private readonly Dictionary<string, (bool HasRules, DateTime CheckedAt)> _portsStatusCache = new();
        private const int CacheTtlSeconds = 3;

        public string? ConfigPath => SelectedGame == null || string.IsNullOrEmpty(SelectedGame.Id) ? null :
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "configs", $"{SelectedGame.Id}.yaml");

        public string AppVersion { get => _appVersion; set { _appVersion = value; OnPropertyChanged(); } }
        public ObservableCollection<GamePreset> Games => _gameListService.Games;

        private bool _isPythonValid;
        public bool IsPythonValid
        {
            get => _isPythonValid;
            set { _isPythonValid = value; OnPropertyChanged(); }
        }

        public GamePreset? SelectedGame
        {
            get => _selectedGame;
            set
            {
                if (_selectedGame == value) return;
                _selectedGame = value;
                OnPropertyChanged();
                UpdateButtonStates();
            }
        }

        public bool IsAdministrator
        {
            get => _isAdministrator;
            set { _isAdministrator = value; OnPropertyChanged(); UpdateButtonStates(); }
        }

        public string ListsPath
        {
            get => _listsPath;
            set { if (_listsPath != value) { _listsPath = value; OnPropertyChanged(); } }
        }

        private bool _isProcessing;
        public bool IsProcessing
        {
            get => _isProcessing;
            set
            {
                if (_isProcessing != value)
                {
                    _isProcessing = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsNotProcessing));
                    UpdateButtonStates();
                }
            }
        }

        public bool IsNotProcessing => !IsProcessing;

        public bool IsRunning
        {
            get => _isRunning;
            set { _isRunning = value; OnPropertyChanged(); UpdateButtonStates(); }
        }

        public string StatusText { get => _statusText; set { _statusText = value; OnPropertyChanged(); } }
        public string StatusBarText { get => _statusBarText; set { _statusBarText = value; OnPropertyChanged(); } }

        public bool GlobalInstalling
        {
            get => _isInstalling;
            private set { if (_isInstalling != value) { _isInstalling = value; OnPropertyChanged(); UpdateButtonStates(); } }
        }

        public bool GlobalUninstalling
        {
            get => _isUninstalling;
            private set { if (_isUninstalling != value) { _isUninstalling = value; OnPropertyChanged(); UpdateButtonStates(); } }
        }

        public bool IsPortsProcessing
        {
            get => _isPortsProcessing;
            set
            {
                if (_isPortsProcessing != value)
                {
                    _isPortsProcessing = value;
                    OnPropertyChanged();
                    UpdateButtonStates();
                }
            }
        }

        public string FilterHeader { get => _filterHeader; set { _filterHeader = value; OnPropertyChanged(); } }

        public bool FilterInstalled
        {
            get => _filterInstalled;
            set
            {
                if (_filterInstalled == value) return;
                _filterInstalled = value;
                if (value && _filterNotInstalled) { _filterNotInstalled = false; OnPropertyChanged(nameof(FilterNotInstalled)); }
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
                if (value && _filterInstalled) { _filterInstalled = false; OnPropertyChanged(nameof(FilterInstalled)); }
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
            set { if (_searchText != value) { _searchText = value; OnPropertyChanged(); ApplyFilters(); } }
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
            set { if (_selectedSortOption != value) { _selectedSortOption = value; OnPropertyChanged(); ApplyFilters(); } }
        }

        public string StartButtonText
        {
            get
            {
                if (SelectedGame == null || string.IsNullOrEmpty(SelectedGame.Id)) return "Выберите игру";
                if (SelectedGame.Id == "monitor") return "Только ===>";
                return File.Exists(ConfigPath) ? "Запустить" : "Скачать";
            }
        }

        public bool CanStart
        {
            get
            {
                if (IsRunning || SelectedGame == null || SelectedGame.Id == "monitor" || !IsAdministrator || _isInstalling || _isUninstalling || IsPortsProcessing) return false;
                if (!File.Exists(ConfigPath)) return true;

                if (!_pythonValidator.IsPythonValid())
                {
                    if (!_pythonValidationMessageShown) { _terminal.WriteLine("⚠️ Python не прошёл валидацию. Перейдите в Сервис -> Python"); _pythonValidationMessageShown = true; }
                    return false;
                }
                _pythonValidationMessageShown = false;

                if (string.IsNullOrEmpty(ListsPath) || !Directory.Exists(ListsPath))
                {
                    if (!_listsPathWarningShown)
                    {
                        _terminal.WriteLine("❌ Папка lists не найдена. Укажите путь в Сервис -> ZDY");
                        _listsPathWarningShown = true;
                    }
                    return false;
                }
                else
                {
                    _listsPathWarningShown = false; // сбрасываем, если путь появился
                }
                return true;
            }
        }

        public bool CanMonitor
        {
            get
            {
                if (IsRunning || SelectedGame == null || !IsAdministrator || _isInstalling || _isUninstalling || IsPortsProcessing) return false;
                return File.Exists(ConfigPath);
            }
        }

        public string MonitorButtonText => "Мониторинг";
        public bool CanStop => IsRunning;

        public bool CanBaseToggle => !IsRunning && !_isInstalling && !_isUninstalling && !IsPortsProcessing;
        public bool CanInstallPorts => SelectedGame != null && SelectedGame.Id != "monitor" && File.Exists(ConfigPath) && !SelectedGame.Installed && CanBaseToggle;
        public bool CanUninstallPorts => SelectedGame != null && SelectedGame.Id != "monitor" && File.Exists(ConfigPath) && SelectedGame.Installed && CanBaseToggle; 
        public bool CanShowProperties => SelectedGame != null && File.Exists(ConfigPath);

        public ICommand RefreshPresetsCommand { get; }
        public ICommand ResetFiltersCommand { get; }
        public ICommand StartCommand { get; }
        public ICommand MonitorCommand { get; }
        public ICommand StopCommand { get; }
        public ICommand PortsToggleCommand { get; }
        public ICommand PropertiesCommand { get; }
        public ICommand RunCommandCommand { get; }
        public ICommand ClearConsoleCommand { get; }
        public ICommand ShowWindowCommand { get; }

        public MainViewModel(
            IGameListService gameListService,
            IPythonValidatorService pythonValidator,
            IZapretValidatorService zapretValidator,
            ICommandRunnerService commandRunner,
            ISettingsManager settingsManager,
            InstallGameUseCase installGameUseCase,
            UninstallGameUseCase uninstallGameUseCase,
            SyncPresetsUseCase syncPresetsUseCase,
            StartMonitoringUseCase startMonitoringUseCase,
            StopMonitoringUseCase stopMonitoringUseCase,
            IWinwsLocator winwsLocator,
            ILogger logger,
            ITerminalOutput terminal,
            ISessionOrchestrator sessionOrchestrator,
            IServiceProvider serviceProvider,
            IUpdateService updateService,
            IPortsManager portsManager)
        {
            _gameListService = gameListService;
            _pythonValidator = pythonValidator;
            _zapretValidator = zapretValidator;
            _commandRunner = commandRunner;
            _settingsManager = settingsManager;
            _installGameUseCase = installGameUseCase;
            _uninstallGameUseCase = uninstallGameUseCase;
            _syncPresetsUseCase = syncPresetsUseCase;
            _startMonitoringUseCase = startMonitoringUseCase;
            _stopMonitoringUseCase = stopMonitoringUseCase;
            _winwsLocator = winwsLocator;
            _logger = logger;
            _terminal = terminal;
            _serviceProvider = serviceProvider;
            _sessionOrchestrator = sessionOrchestrator;
            _updateService = updateService;
            _portsManager = portsManager;

            _sessionOrchestrator.OutputReceived += msg => _terminal.WriteLine(msg);
            _sessionOrchestrator.SessionEnded += OnSessionEnded;
            _selectedSortOption = SortOptions[0];
            OnPropertyChanged(nameof(SelectedSortOption));
            OnPropertyChanged(nameof(StartButtonText));
            OnPropertyChanged(nameof(CanStart));

            RefreshPresetsCommand = new RelayCommand(async _ => await RefreshPresetsAsync(), _ => true);
            ResetFiltersCommand = new RelayCommand(_ => ResetFilters());
            StartCommand = new RelayCommand(async _ => await StartAsync(false), _ => CanStart);
            MonitorCommand = new RelayCommand(async _ => await StartAsync(true), _ => CanMonitor);
            StopCommand = new RelayCommand(async _ => await StopAsync(), _ => CanStop);
            PortsToggleCommand = new RelayCommand(async _ => await TogglePortsAsync(null), _ => CanBaseToggle);
            PropertiesCommand = new RelayCommand(_ => ShowProperties(), _ => CanShowProperties);
            RunCommandCommand = new RelayCommand(async param => await RunCommandAsync(param?.ToString()));
            ClearConsoleCommand = new RelayCommand(_ => ClearConsole());
            ShowWindowCommand = new RelayCommand(_ => ShowWindow());

            _ = Task.Run(async () =>
            {
                try { await InitializeAsync(); ClearConsole(); }
                catch (Exception ex) { DebugLogger.WriteException("InitializeAsync failed", ex); _terminal.WriteLine($"❌ Критическая ошибка инициализации: {ex.Message}"); }
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
            using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            var principal = new System.Security.Principal.WindowsPrincipal(identity);
            return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }

        private async Task InitializeAsync()
        {
            try
            {
                IsAdministrator = IsCurrentUserAdministrator();
                await FindListsPathAsync();
                var (installed, notInstalled, custom) = _settingsManager.GetFilterState();
                _filterInstalled = installed; _filterNotInstalled = notInstalled; _filterCustom = custom;
                OnPropertyChanged(nameof(FilterInstalled)); OnPropertyChanged(nameof(FilterNotInstalled)); OnPropertyChanged(nameof(FilterCustom));
                LoadGames();
                StatusBarText = $"Загружено пресетов: {Games.Count}";
                IsPythonValid = _pythonValidator.IsPythonValid();
                var appConfig = _serviceProvider.GetRequiredService<IAppConfigService>().Load();
                AppVersion = appConfig.App?.AppVersion ?? "?.?.?";
                await SyncAllPortsStatusAsync();
            }
            catch (Exception ex) { DebugLogger.WriteException("InitializeAsync error", ex); _terminal.WriteLine($"❌ Ошибка инициализации: {ex.Message}"); }
        }

        public void RefreshPythonStatus()
        {
            IsPythonValid = _pythonValidator.IsPythonValid();
            OnPropertyChanged(nameof(IsPythonValid));
            CommandManager.InvalidateRequerySuggested();
        }

        private async Task SyncAllPortsStatusAsync()
        {
            try
            {
                var presets = _updateService.LoadPresets();
                bool anyChanged = false;
                foreach (var preset in presets)
                {
                    // Проверяем только игры, у которых есть конфиг (ConfigDownloaded == true)
                    if (!preset.ConfigDownloaded || preset.Id == "monitor") continue;

                    var (hasRules, _) = await _portsManager.CheckRulesExistAsync(preset.Id);
                    if (preset.Installed != hasRules)
                    {
                        preset.Installed = hasRules;
                        anyChanged = true;
                    }
                }
                if (anyChanged)
                {
                    _updateService.SavePresetsFile(new PresetsFile { Games = presets });
                }
            }
            catch (Exception ex)
            {
                DebugLogger.WriteException("SyncAllPortsStatusAsync failed", ex);
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
                    if (!string.IsNullOrEmpty(savedPath) && Directory.Exists(savedPath)) { ListsPath = savedPath; _terminal.WriteLine($"ℹ️ Используется сохранённый путь: {savedPath}"); }
                    else _terminal.WriteLine("⚠️ Папка lists не найдена автоматически. Укажите вручную через меню Сервис -> ZDY.");
                }
            }
            catch (Exception ex) { DebugLogger.WriteException("FindListsPathAsync failed", ex); throw; }
        }

        private void LoadGames() { _gameListService.LoadGames(); ApplyFilters(); }

        private void ApplyFilters()
        {
            _gameListService.ApplyFilters(SearchText, FilterInstalled, FilterNotInstalled, FilterCustom, _selectedSortOption?.Id ?? string.Empty);
            var allPresets = _gameListService.Games.ToList();
            FilterHeader = _gameListService.GetFilterHeader(allPresets.Count, Games.Count);
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
            OnPropertyChanged(nameof(CanStart)); OnPropertyChanged(nameof(CanMonitor)); OnPropertyChanged(nameof(CanStop));
            OnPropertyChanged(nameof(CanInstallPorts)); OnPropertyChanged(nameof(CanUninstallPorts));
            OnPropertyChanged(nameof(CanShowProperties));
            OnPropertyChanged(nameof(StartButtonText)); OnPropertyChanged(nameof(MonitorButtonText));
            System.Windows.Input.CommandManager.InvalidateRequerySuggested();
        }

        public async Task RefreshPresetsAsync()
        {
            IsProcessing = true; UpdateButtonStates();
            try
            {
                _terminal.WriteLine("⏳ Синхронизация пресетов..."); StatusBarText = "Синхронизация...";
                bool ok = await _syncPresetsUseCase.ExecuteAsync();
                _terminal.WriteLine(ok ? "✅ Синхронизация завершена" : "❌ Ошибка синхронизации");
                LoadGames(); StatusBarText = $"Загружено {Games.Count} пресетов";
            }
            finally { IsProcessing = false; UpdateButtonStates(); }
        }

        private async Task StartAsync(bool monitorOnly)
        {
            if (SelectedGame == null) return;
            IsProcessing = true; UpdateButtonStates();
            try
            {
                if (!File.Exists(ConfigPath))
                {
                    IProgress<string> progress = new Progress<string>(msg => _terminal.WriteLine(msg));
                    var downloadResult = await _installGameUseCase.ExecuteDownloadAsync(SelectedGame, progress);
                    if (downloadResult.Success && downloadResult.UpdatedPreset != null)
                    {
                        var index = Games.IndexOf(SelectedGame!);
                        if (index >= 0) Games[index] = downloadResult.UpdatedPreset;
                        SelectedGame = downloadResult.UpdatedPreset;
                        _terminal.WriteLine($"✅ Конфиг {downloadResult.UpdatedPreset.Name} скачан");
                        UpdateButtonStates(); return;
                    }
                    _terminal.WriteLine($"❌ Ошибка: {downloadResult.ErrorMessage}"); return;
                }

                string pythonExe = _pythonValidator.GetPythonPath();
                if (string.IsNullOrEmpty(pythonExe) || !File.Exists(pythonExe))
                {
                    _terminal.WriteLine("⚠️ Python не найден или не установлен\n   Перейдите в Сервис -> Python, чтобы устранить неисправность"); return;
                }

                if (!monitorOnly && (string.IsNullOrEmpty(ListsPath) || !Directory.Exists(ListsPath)))
                { _terminal.WriteLine("❌ Папка lists не существует или не выбрана."); return; }

                if (monitorOnly && (string.IsNullOrEmpty(ListsPath) || !Directory.Exists(ListsPath)))
                    _terminal.WriteLine("⚠️ Мониторинг без папки lists (только просмотр соединений)");

                _terminal.Clear();
                bool warpEnabled = _settingsManager.GetWarpEnabled(SelectedGame.Id);
                bool filterProcesses = !monitorOnly;
                IsRunning = true; StatusText = "Запуск..."; UpdateButtonStates();
                await _sessionOrchestrator.StartAsync(SelectedGame.Id, ListsPath, monitorOnly, warpEnabled, filterProcesses);
                StatusText = monitorOnly ? "Мониторинг" : "Активен";
                StatusBarText = $"{SelectedGame.Name} {(monitorOnly ? "мониторинг" : "запущен")}";
            }
            catch (Exception ex)
            { _terminal.WriteLine($"❌ Ошибка: {ex.Message}"); StatusText = "Ошибка"; IsRunning = false; StatusBarText = "Ошибка запуска"; }
            finally { IsProcessing = false; UpdateButtonStates(); }
        }

        private async Task StopAsync()
        {
            IsProcessing = true; UpdateButtonStates();
            try { StatusText = "Остановка..."; await _sessionOrchestrator.StopAsync(); StatusText = "Завершён"; IsRunning = false; }
            catch (Exception ex) { _terminal.WriteLine($"❌ Ошибка остановки: {ex.Message}"); StatusText = "Ошибка"; IsRunning = false; StatusBarText = "Ошибка остановки"; }
            finally { IsProcessing = false; UpdateButtonStates(); }
        }

        private async Task TogglePortsAsync(bool? forceInstall)
        {
            if (SelectedGame == null || SelectedGame.Id == "monitor") return;

            IsPortsProcessing = true;
            IsProcessing = true;
            UpdateButtonStates();

            try
            {
                var (hasRules, checkError) = await _portsManager.CheckRulesExistAsync(SelectedGame.Id);
                _portsStatusCache[SelectedGame.Id] = (hasRules, DateTime.Now);

                bool wantToInstall = forceInstall.HasValue ? forceInstall.Value : !hasRules;

                if (wantToInstall && hasRules)
                {
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                        MessageBox.Show($"Порты уже установлены!\n\nПравила для игры {SelectedGame.Name} уже существуют в брандмауэре.", "Порты установлены", MessageBoxButton.OK, MessageBoxImage.Information));
                    return;
                }

                if (wantToInstall && !hasRules)
                {
                    var installConfirmResult = await Application.Current.Dispatcher.InvokeAsync(() =>
                        MessageBox.Show($"Установить порты для {SelectedGame.Name}?", "Установка портов", MessageBoxButton.YesNo, MessageBoxImage.Question));
                    if (installConfirmResult != MessageBoxResult.Yes) return;

                    var config = _updateService.LoadGameConfig(SelectedGame.Id);
                    if (config == null || config.Ports == null) { _terminal.WriteLine($"❌ Конфигурация портов для {SelectedGame.Name} не найдена"); return; }

                    IProgress<string> progress = new Progress<string>(msg => _terminal.WriteLine(msg));
                    var installResult = await _portsManager.AddRulesAsync(config.Ports.Tcp, config.Ports.Udp, SelectedGame.Id, msg => progress.Report(msg));

                    if (installResult.Success)
                    {
                        _terminal.WriteLine($"✅ Порты для {SelectedGame.Name} установлены");
                        await UpdateGameInstalledStatus(SelectedGame.Id, true, persist: true);
                    }
                    else _terminal.WriteLine($"❌ Ошибка установки портов: {installResult.Error}");
                }
                else
                {
                    if (!hasRules)
                    {
                        await Application.Current.Dispatcher.InvokeAsync(() =>
                            MessageBox.Show($"Порты уже были удалены!\n\nПравила для игры {SelectedGame.Name} не найдены в брандмауэре.", "Порты удалены", MessageBoxButton.OK, MessageBoxImage.Information));
                        return;
                    }

                    var uninstallConfirmResult = await Application.Current.Dispatcher.InvokeAsync(() =>
                        MessageBox.Show($"Найдены правила для игры {SelectedGame.Name}. Удалить?", "Удаление портов", MessageBoxButton.YesNo, MessageBoxImage.Question));
                    if (uninstallConfirmResult != MessageBoxResult.Yes) return;

                    IProgress<string> progress = new Progress<string>(msg => _terminal.WriteLine(msg));
                    var removeResult = await _portsManager.RemoveAllRulesAsync(SelectedGame.Id, msg => progress.Report(msg));

                    if (removeResult.Success)
                    {
                        _terminal.WriteLine($"✅ Порты для {SelectedGame.Name} удалены");
                        await UpdateGameInstalledStatus(SelectedGame.Id, false, persist: true);
                    }
                    else _terminal.WriteLine($"❌ Ошибка удаления портов: {removeResult.Error}");
                }
            }
            catch (Exception ex)
            { _terminal.WriteLine($"❌ Ошибка управления портами: {ex.Message}"); DebugLogger.WriteException("TogglePortsAsync error", ex); }
            finally { IsPortsProcessing = false; IsProcessing = false; UpdateButtonStates(); }
        }

        private async Task UpdateGameInstalledStatus(string gameId, bool installed, bool persist = false)
        {
            _portsStatusCache[gameId] = (installed, DateTime.Now);

            var gameInUi = Games.FirstOrDefault(g => g.Id == gameId);
            if (gameInUi != null && gameInUi.Installed != installed)
            {
                gameInUi.Installed = installed;
            }

            if (persist)
            {
                var presets = _updateService.LoadPresets();
                foreach (var p in presets)
                {
                    if (p.Id == gameId) { p.Installed = installed; break; }
                }
                _updateService.SavePresetsFile(new PresetsFile { Games = presets });
            }

            OnPropertyChanged(nameof(CanInstallPorts));
            OnPropertyChanged(nameof(CanUninstallPorts));
        }

        private async Task UpdatePortsStatusAsync(string gameId)
        {
            if (string.IsNullOrEmpty(gameId) || gameId == "monitor") return;
            await UpdateGameInstalledStatus(gameId, GetCachedPortsStatus());
        }

        private bool GetCachedPortsStatus()
        {
            if (SelectedGame == null) return false;
            if (_portsStatusCache.TryGetValue(SelectedGame.Id, out var cache) && (DateTime.Now - cache.CheckedAt).TotalSeconds < CacheTtlSeconds)
                return cache.HasRules;
            return false;
        }

        private void ShowProperties()
        {
            if (SelectedGame == null) return;
            var dialog = new GamePropertiesDialog(SelectedGame, _settingsManager);
            dialog.Owner = Application.Current.MainWindow;
            if (dialog.ShowDialog() == true) _terminal.WriteLine($"✅ Настройки для {SelectedGame.Name} сохранены");
        }

        private async Task RunCommandAsync(string? command)
        {
            if (string.IsNullOrEmpty(command)) return;
            string result = await _commandRunner.RunCommandAsync(command, msg => _terminal.WriteLine(msg));
            if (!string.IsNullOrEmpty(result))
                _terminal.WriteLine(result);
        }

        private void ResetFilters() { FilterInstalled = false; FilterNotInstalled = false; FilterCustom = false; }

        public void ClearConsole()
        {
            _terminal.Clear();
            _terminal.WriteLine("=== Fix Traffic Pipeline ===");
            _terminal.WriteLine("\nВыберите игру из списка слева.");
            _terminal.WriteLine("\n• Скачать — скачивает конфиг игры");
            _terminal.WriteLine("\n• Запустить — запускает фикс (требует скачанный конфиг)");
            _terminal.WriteLine("\n• Мониторинг — только просмотр соединений");
            _terminal.WriteLine("\n• ПКМ по игре → Установить/Удалить порты");
            _terminal.WriteLine("\nДля обновления списка игр нажмите «Обновить список»\n");
            if (!IsZapretValid())
            {
                _terminal.WriteLine("️ Zapret не найден или не установлен");
                _terminal.WriteLine("   Перейдите в Сервис -> ZDY и укажите папку lists");
                _terminal.WriteLine("   Там же можно установить Zapret\n");
            }
        }

        private bool IsZapretValid() => _zapretValidator.IsZapretValid(ListsPath);

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}