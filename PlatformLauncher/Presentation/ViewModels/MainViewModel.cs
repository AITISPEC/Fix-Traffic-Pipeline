using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using PlatformLauncher.Core.Interfaces;
using PlatformLauncher.Core.UseCases;
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
using System.Security.Principal;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace PlatformLauncher.Presentation.ViewModels
{
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

        private ObservableCollection<GamePreset> _games = new();
        private GamePreset _selectedGame;
        private bool _isAdministrator;
        private string _listsPath;
        private bool _isRunning;
        private string _statusText = "Готов";
        private string _statusBarText = "Загрузка пресетов...";
        private bool _showOnlyInstalled;
        private bool _showOnlyLocal;

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
            set { _listsPath = value; OnPropertyChanged(); }
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

        public bool ShowOnlyInstalled
        {
            get => _showOnlyInstalled;
            set { _showOnlyInstalled = value; OnPropertyChanged(); ApplyFilters(); }
        }

        public bool ShowOnlyLocal
        {
            get => _showOnlyLocal;
            set { _showOnlyLocal = value; OnPropertyChanged(); ApplyFilters(); }
        }

        public string StartButtonText => SelectedGame != null && !SelectedGame.Installed ? "Установить" : "Запустить фикс";

        public bool CanStart => !IsRunning && SelectedGame != null && IsAdministrator && SelectedGame.Installed && SelectedGame.Id != "monitor";
        public bool CanMonitor => !IsRunning && SelectedGame != null && IsAdministrator;
        public bool CanStop => IsRunning;
        public bool CanInstall => SelectedGame != null && !SelectedGame.Installed && SelectedGame.Id != "monitor";
        public bool CanUninstall => SelectedGame != null && SelectedGame.Installed && SelectedGame.Id != "monitor";

        public ICommand RefreshPresetsCommand { get; }
        public ICommand StartCommand { get; }
        public ICommand MonitorCommand { get; }
        public ICommand StopCommand { get; }
        public ICommand InstallCommand { get; }
        public ICommand UninstallCommand { get; }
        public ICommand PropertiesCommand { get; }
        public ICommand FilterCommand { get; }
        public ICommand RunCommandCommand { get; }
        public ICommand ClearConsoleCommand { get; }
        public ICommand ShowWindowCommand { get; }

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
            DebugLogger.Write("=== MainViewModel.CTOR START ===");

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
            _sessionOrchestrator = sessionOrchestrator;
            _sessionOrchestrator.OutputReceived += msg => _terminal.WriteLine(msg);
            _sessionOrchestrator.SessionEnded += OnSessionEnded;

            RefreshPresetsCommand = new RelayCommand(async _ => await RefreshPresetsAsync(), _ => true);
            StartCommand = new RelayCommand(async _ => await StartAsync(false), _ => CanStart);
            MonitorCommand = new RelayCommand(async _ => await StartAsync(true), _ => CanMonitor);
            StopCommand = new RelayCommand(async _ => await StopAsync(), _ => CanStop);
            InstallCommand = new RelayCommand(async _ => await InstallAsync(), _ => CanInstall);
            UninstallCommand = new RelayCommand(async _ => await UninstallAsync(), _ => CanUninstall);
            PropertiesCommand = new RelayCommand(_ => ShowProperties(), _ => SelectedGame != null);
            FilterCommand = new RelayCommand(_ => ApplyFilters());
            RunCommandCommand = new RelayCommand(async param => await RunCommandAsync(param?.ToString()));
            ClearConsoleCommand = new RelayCommand(_ => ClearConsole());
            ShowWindowCommand = new RelayCommand(_ => ShowWindow());

            DebugLogger.Write("Commands initialized, calling InitializeAsync");

            _ = Task.Run(async () =>
            {
                try
                {
                    await InitializeAsync();
                    ClearConsole();
                }
                catch (Exception ex)
                {
                    DebugLogger.WriteException("InitializeAsync FAILED", ex);
                    _terminal.WriteLine($"❌ Критическая ошибка инициализации: {ex.Message}");
                    // Не выходим, но даём знать пользователю
                }
            });
            DebugLogger.Write("=== MainViewModel.CTOR END ===");
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
            DebugLogger.Write("InitializeAsync START");
            try
            {
                IsAdministrator = IsCurrentUserAdministrator();
                DebugLogger.Write("IsAdministrator set");
                await FindListsPathAsync();
                DebugLogger.Write("FindListsPathAsync done");
                LoadGames();
                DebugLogger.Write("LoadGames done");
                StatusBarText = $"Загружено {Games.Count} пресетов";
                DebugLogger.Write("StatusBarText updated");
            }
            catch (Exception ex)
            {
                DebugLogger.WriteException("InitializeAsync ERROR", ex);
                _terminal.WriteLine($"❌ Ошибка инициализации: {ex.Message}");
            }
            DebugLogger.Write("InitializeAsync END");
        }

        private async Task FindListsPathAsync()
        {
            DebugLogger.Write("FindListsPathAsync START");
            try
            {
                ListsPath = await _winwsLocator.FindListsPathAsync();
                DebugLogger.Write($"ListsPath = {ListsPath ?? "null"}");
                if (string.IsNullOrEmpty(ListsPath))
                {
                    _terminal.WriteLine("⚠️ Папка lists не найдена автоматически. Укажите вручную через меню LISTS.");
                }
            }
            catch (Exception ex)
            {
                DebugLogger.WriteException("FindListsPathAsync ERROR", ex);
                throw;
            }
            DebugLogger.Write("FindListsPathAsync END");
        }

        private void LoadGames()
        {
            var presets = _updateService.LoadPresets();
            Games.Clear();
            foreach (var p in presets)
                Games.Add(p);
            ApplyFilters();
        }

        private void ApplyFilters()
        {
            var filtered = _updateService.LoadPresets()
                .Where(p => (!ShowOnlyInstalled || p.Installed) &&
                            (!ShowOnlyLocal || IsLocal(p.Id)))
                .OrderBy(p => p.Installed ? 0 : 1)
                .ThenBy(p => p.Name)
                .ToList();
            Games.Clear();
            foreach (var p in filtered)
                Games.Add(p);
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
            var progress = new Progress<string>(msg => _terminal.WriteLine(msg));
            var (success, error, updated) = await _installGameUseCase.ExecuteAsync(SelectedGame, progress);
            if (success && updated != null)
            {
                var index = Games.IndexOf(SelectedGame);
                if (index >= 0)
                    Games[index] = updated;
                SelectedGame = updated;
                _terminal.WriteLine($"✅ Фикс {updated.Name} установлен");
                StatusBarText = $"Фикс {updated.Name} установлен";
            }
            else
            {
                _terminal.WriteLine($"❌ Ошибка: {error}");
                StatusBarText = "Ошибка установки";
            }
            UpdateButtonStates();
        }

        private async Task UninstallAsync()
        {
            if (SelectedGame == null) return;
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
                    try { proc.Kill(); } catch { }
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

        public void ClearConsole()
        {
            _terminal.Clear();
            _terminal.WriteLine("=== Fix Traffic Pipeline ===");
            _terminal.WriteLine("\nВыберите игру из списка слева.");
            _terminal.WriteLine("\n• Запустить фикс — применяет настройки, создаёт бэкап lists");
            _terminal.WriteLine("\n  Запуск с WARP — в свойствах фикса (ПКМ > Свойства).");
            _terminal.WriteLine("\n• Мониторинг — только просмотр соединений, без изменения lists");
            _terminal.WriteLine("\nДля обновления списка игр нажмите «Обновить список»\n");
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}