using Microsoft.Win32;
using PlatformLauncher.Models;
using PlatformLauncher.Services;
using PlatformLauncher.Views;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PlatformLauncher
{
    public partial class MainWindow : Window
    {
        private ObservableCollection<GamePreset> _allPresets = new ObservableCollection<GamePreset>();
        private PythonProcessManager _pythonManager = new PythonProcessManager();
        private GamePreset _selectedGame;
        private string _listsPath;
        private BackupManager _backupManager;
        private string _currentBackupDir;
        private bool _backupRestored = false;
        private bool _warpStartedByUs = false; // флаг, чтобы отключать WARP только если мы его включили

        public bool IsAdministrator { get; }

        public MainWindow()
        {
            InitializeComponent();
            IsAdministrator = IsCurrentUserAdministrator();
            Loaded += MainWindow_Loaded;
            _pythonManager.OutputReceived += AppendConsoleOutput;
            _pythonManager.ProcessExited += OnPythonProcessExited;
            UpdateStartButtonText();
        }

        private static bool IsCurrentUserAdministrator()
        {
            using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            var principal = new System.Security.Principal.WindowsPrincipal(identity);
            return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                AppendConsoleOutput("Проверка окружения Python...");
                var progress = new Progress<string>(msg => AppendConsoleOutput(msg));
                bool envOk = await PythonEnvironmentManager.EnsureEnvironmentAsync(
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data"), progress);
                if (!envOk)
                {
                    MessageBox.Show("Не удалось подготовить окружение Python. Функционал фиксов будет недоступен.",
                        "Предупреждение", MessageBoxButton.OK, MessageBoxImage.Warning);
                }

                string configDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "configs");
                Directory.CreateDirectory(configDir);
                string presetsPath = Path.Combine(configDir, "presets.yaml");
                if (!File.Exists(presetsPath))
                {
                    UpdateService.SavePresetsFile(new PresetsFile());
                    AppendConsoleOutput("Создан пустой presets.yaml");
                }

                _listsPath = await Task.Run(() => WinwsLocator.FindListsPath());
                if (string.IsNullOrEmpty(_listsPath))
                {
                    var result = MessageBox.Show(
                        "Не удалось автоматически найти папку lists. Указать путь вручную?",
                        "Путь не найден",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning);
                    if (result == MessageBoxResult.Yes)
                    {
                        var dialog = new OpenFolderDialog();
                        dialog.Title = "Выберите папку lists";
                        if (dialog.ShowDialog() == true)
                            _listsPath = dialog.FolderName;
                    }
                }

                OpenListsButton.IsEnabled = true;
                await LoadPresets();

                if (!string.IsNullOrEmpty(_listsPath))
                    await CheckAndRestoreBackups();

                UpdateStartButtonText();
            }
            catch (Exception ex)
            {
                LauncherLogger.Error($"Ошибка при загрузке: {ex}");
                MessageBox.Show($"Ошибка: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task CheckAndRestoreBackups()
        {
            var presets = UpdateService.LoadPresets();
            foreach (var game in presets.Where(g => g.Installed))
            {
                var backupMgr = new BackupManager("./backups", game.Id);
                var unrestored = backupMgr.GetUnrestoredBackups();
                if (unrestored.Count == 0) continue;

                var latest = unrestored.OrderByDescending(d => Directory.GetCreationTime(d)).First();
                var creationTime = Directory.GetCreationTime(latest);
                string msg = $"Обнаружен незавершённый бэкап игры \"{game.Name}\" от {creationTime:dd.MM.yyyy HH:mm}.\nВосстановить папку lists?";

                var result = MessageBox.Show(msg, "Восстановление бэкапа", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result == MessageBoxResult.Yes)
                {
                    bool restored = await backupMgr.RestoreBackupAsync(_listsPath, latest);
                    if (restored)
                        AppendConsoleOutput($"✅ Бэкап для {game.Name} восстановлен");
                    else
                        AppendConsoleOutput($"⚠️ Ошибка восстановления бэкапа для {game.Name}");
                }
                else
                {
                    backupMgr.MarkAsNoRestore(latest);
                    AppendConsoleOutput($"ℹ️ Бэкап для {game.Name} помечен как невосстанавливаемый");
                }

                foreach (var other in unrestored.Where(d => d != latest))
                {
                    backupMgr.MarkAsNoRestore(other);
                }
            }
        }

        private async Task LoadPresets()
        {
            StatusBarText.Text = "Загрузка пресетов...";
            var presets = UpdateService.LoadPresets();
            _allPresets.Clear();
            foreach (var p in presets)
                _allPresets.Add(p);
            FilterGames();
            StatusBarText.Text = $"Загружено {_allPresets.Count} пресетов";
            LauncherLogger.Info($"Загружено {_allPresets.Count} пресетов");
        }

        private void FilterGames()
        {
            bool onlyInstalled = ShowOnlyInstalledCheckBox.IsChecked == true;
            bool onlyLocal = ShowOnlyLocalCheckBox.IsChecked == true;

            var filtered = _allPresets
                .Where(p => (!onlyInstalled || IsInstalled(p.Id)) &&
                            (!onlyLocal || IsLocal(p.Id)))
                .OrderBy(p => IsInstalled(p.Id) ? 0 : 1)
                .ThenBy(p => p.Name)
                .ToList();
            GamesListBox.ItemsSource = filtered;
        }

        private bool IsInstalled(string gameId)
        {
            var presets = UpdateService.LoadPresets();
            var p = presets.Find(g => g.Id == gameId);
            return p != null && p.Installed;
        }

        private bool IsLocal(string gameId)
        {
            string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "configs", $"{gameId}.yaml");
            return File.Exists(configPath);
        }

        private void UpdateStartButtonText()
        {
            if (_selectedGame == null)
            {
                StartButton.Content = "Запустить фикс";
                StartButton.IsEnabled = false;
                MonitorButton.IsEnabled = false;
                return;
            }

            bool installed = IsInstalled(_selectedGame.Id);
            if (!installed)
            {
                StartButton.Content = "Установить";
                StartButton.IsEnabled = true;
                MonitorButton.IsEnabled = false;
                return;
            }

            StartButton.Content = "Запустить фикс";
            StartButton.IsEnabled = IsAdministrator;
            MonitorButton.IsEnabled = true;
        }

        private void GamesListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _selectedGame = GamesListBox.SelectedItem as GamePreset;
            UpdateStartButtonText();
            if (_selectedGame != null)
                StatusBarText.Text = $"Выбран: {_selectedGame.Name}";
        }

        private async void RefreshPresetsButton_Click(object sender, RoutedEventArgs e)
        {
            StatusBarText.Text = "Синхронизация с GitHub...";
            AppendConsoleOutput("⏳ Синхронизация пресетов...");
            bool ok = await UpdateService.SyncFromGitHubAsync();
            if (ok)
                AppendConsoleOutput("✅ Синхронизация завершена");
            else
                AppendConsoleOutput("❌ Ошибка синхронизации (подробности в логе)");
            await LoadPresets();
            StatusBarText.Text = "Готово";
        }

        private void OpenListsButton_Click(object sender, RoutedEventArgs e)
        {
            ((Button)sender).ContextMenu.IsOpen = true;
        }

        private void SelectListsPath_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog();
            dialog.Title = "Выберите папку lists";
            if (dialog.ShowDialog() == true)
            {
                _listsPath = dialog.FolderName;
                StatusBarText.Text = $"Папка lists: {_listsPath}";
            }
        }

        private void OpenListsFolder_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(_listsPath) && Directory.Exists(_listsPath))
                Process.Start("explorer.exe", _listsPath);
            else
                MessageBox.Show("Папка lists не выбрана или не существует.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        private async void InstallGame_Click(object sender, RoutedEventArgs e)
        {
            var preset = GamesListBox.SelectedItem as GamePreset;
            if (preset == null) return;
            await InstallGame(preset);
        }

        private async Task<bool> RunPythonScript(string arguments)
        {
            string pythonExe = PythonEnvironmentManager.GetVenvPythonPath();
            if (string.IsNullOrEmpty(pythonExe) || !File.Exists(pythonExe))
            {
                AppendConsoleOutput("❌ Виртуальное окружение Python не найдено.");
                return false;
            }

            string monitorScript = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "monitor.py");
            if (!File.Exists(monitorScript))
            {
                AppendConsoleOutput($"❌ Скрипт монитора не найден: {monitorScript}");
                return false;
            }

            var psi = new ProcessStartInfo(pythonExe, $"\"{monitorScript}\" {arguments}")
            {
                WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            using var process = new Process { StartInfo = psi };
            process.OutputDataReceived += (s, e) => { if (!string.IsNullOrEmpty(e.Data)) AppendConsoleOutput(e.Data); };
            process.ErrorDataReceived += (s, e) => { if (!string.IsNullOrEmpty(e.Data)) AppendConsoleOutput($"⚠️ {e.Data}"); };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            await process.WaitForExitAsync();
            return process.ExitCode == 0;
        }

        private void SetInstallProgress(GamePreset preset, bool show)
        {
            preset.IsInstalling = show;
        }

        private async Task InstallGame(GamePreset preset)
        {
            try
            {
                StatusBarText.Text = $"Установка {preset.Name}...";
                AppendConsoleOutput($"⏳ Установка {preset.Name}...");
                LauncherLogger.Info($"Начало установки {preset.Id}");

                SetInstallProgress(preset, true);

                var (success, error) = await UpdateService.InstallGameAsync(preset);
                LauncherLogger.Info($"Результат установки: success={success}");

                if (!success)
                {
                    MessageBox.Show($"Ошибка установки {preset.Name}:\n{error}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                    StatusBarText.Text = "Ошибка";
                    AppendConsoleOutput($"❌ Ошибка: {error}");
                    SetInstallProgress(preset, false);
                    return;
                }

                AppendConsoleOutput($"✅ Конфиг скачан");
                LauncherLogger.Info($"Конфиг {preset.Id} скачан");

                var config = UpdateService.LoadGameConfig(preset.Id);
                if (config?.Ports != null)
                {
                    AppendConsoleOutput("📌 Установка правил портов...");
                    var pm = new PortsManager(preset.Id);
                    var (portOk, portError) = await pm.AddRulesAsync(config.Ports.Tcp, config.Ports.Udp);
                    if (portOk)
                        AppendConsoleOutput("✅ Правила портов добавлены");
                    else
                        AppendConsoleOutput($"❌ Ошибка добавления правил портов: {portError}");
                }
                else
                {
                    AppendConsoleOutput("ℹ️ В конфиге нет портов");
                }

                LauncherLogger.Info("Перезагрузка списка пресетов");
                var freshPresets = UpdateService.LoadPresets();
                _allPresets.Clear();
                foreach (var p in freshPresets)
                    _allPresets.Add(p);

                FilterGames();

                _selectedGame = _allPresets.FirstOrDefault(p => p.Id == preset.Id);
                if (_selectedGame != null)
                {
                    GamesListBox.SelectedItem = _selectedGame;
                    LauncherLogger.Info($"Выбран: {_selectedGame.Name}, Installed={_selectedGame.Installed}");
                }

                UpdateStartButtonText();
                StatusBarText.Text = "Готово";
                AppendConsoleOutput($"✅ Фикс {preset.Name} установлен");
                LauncherLogger.Info($"Установка {preset.Id} завершена");

                SetInstallProgress(preset, false);
                MessageBox.Show($"Фикс {preset.Name} установлен", "Успех", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                LauncherLogger.Error($"Критическая ошибка установки: {ex}");
                AppendConsoleOutput($"❌ Ошибка: {ex.Message}");
                MessageBox.Show($"Ошибка: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                StatusBarText.Text = "Ошибка";
                SetInstallProgress(preset, false);
            }
        }

        private async void UninstallGame_Click(object sender, RoutedEventArgs e)
        {
            var preset = GamesListBox.SelectedItem as GamePreset;
            if (preset == null) return;

            preset.IsUninstalling = true;

            AppendConsoleOutput("📌 Удаление правил портов...");
            var pm = new PortsManager(preset.Id);
            var (removed, removeError) = await pm.RemoveAllRulesAsync();
            if (removed)
                AppendConsoleOutput("✅ Правила портов удалены");
            else
                AppendConsoleOutput($"❌ Ошибка удаления правил портов: {removeError}");

            UpdateService.UninstallGame(preset.Id);
            LauncherLogger.Info($"Конфиг {preset.Id} помечен как неустановленный");

            var freshPresets = UpdateService.LoadPresets();
            _allPresets.Clear();
            foreach (var p in freshPresets)
                _allPresets.Add(p);

            FilterGames();

            _selectedGame = _allPresets.FirstOrDefault(p => p.Id == preset.Id);
            if (_selectedGame != null)
                GamesListBox.SelectedItem = _selectedGame;
            else
                _selectedGame = null;

            UpdateStartButtonText();
            StatusBarText.Text = "Готово";
            AppendConsoleOutput($"✅ Фикс {preset.Name} удалён (конфиг сохранён)");
            LauncherLogger.Info($"Удаление {preset.Id} завершено");
            MessageBox.Show($"Фикс {preset.Name} удалён", "Готово", MessageBoxButton.OK, MessageBoxImage.Information);

            preset.IsUninstalling = false;
        }

        private void PropertiesGame_Click(object sender, RoutedEventArgs e)
        {
            var preset = GamesListBox.SelectedItem as GamePreset;
            if (preset == null) return;
            var dialog = new GamePropertiesDialog(preset);
            if (dialog.ShowDialog() == true)
            {
                SettingsManager.SetWarpEnabled(preset.Id, dialog.WarpEnabled);
            }
        }

        private void GamesListBox_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            var source = e.OriginalSource as DependencyObject;
            if (source == null) return;

            ContextMenu menu = null;
            DependencyObject current = source;
            while (current != null)
            {
                if (current is FrameworkElement fe && fe.ContextMenu != null)
                {
                    menu = fe.ContextMenu;
                    break;
                }
                current = VisualTreeHelper.GetParent(current);
            }

            if (menu == null)
            {
                var item = FindParent<ListBoxItem>(source);
                if (item != null)
                {
                    var textBlock = FindVisualChild<TextBlock>(item);
                    if (textBlock?.ContextMenu != null)
                        menu = textBlock.ContextMenu;
                }
            }

            if (menu == null) return;

            var preset = GamesListBox.SelectedItem as GamePreset;
            if (preset == null)
            {
                menu.Visibility = Visibility.Collapsed;
                return;
            }

            menu.Visibility = Visibility.Visible;
            bool installed = IsInstalled(preset.Id);

            foreach (MenuItem item in menu.Items)
            {
                if (item.Header?.ToString() == "Установить")
                    item.Visibility = installed ? Visibility.Collapsed : Visibility.Visible;
                else if (item.Header?.ToString() == "Удалить")
                    item.Visibility = installed ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private async void StartButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedGame == null) return;

            if (!IsInstalled(_selectedGame.Id))
            {
                await InstallGame(_selectedGame);
                return;
            }

            if (!IsAdministrator)
            {
                MessageBox.Show("Запуск фикса требует прав администратора.", "Недостаточно прав", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            bool warpEnabled = SettingsManager.GetWarpEnabled(_selectedGame.Id);

            if (string.IsNullOrEmpty(_listsPath))
            {
                MessageBox.Show("Папка lists не найдена. Укажите путь через кнопку LISTS.",
                    "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _backupRestored = false;
            _backupManager = new BackupManager("./backups", _selectedGame.Id);
            _currentBackupDir = await _backupManager.CreateBackupAsync(_listsPath);
            AppendConsoleOutput($"✅ Бэкап создан: {_currentBackupDir}");

            _warpStartedByUs = false;
            if (warpEnabled)
            {
                AppendConsoleOutput("⏳ Запуск WARP...");
                bool warpStarted = await WarpManager.EnsureStartedAsync();
                if (warpStarted)
                {
                    AppendConsoleOutput("✅ WARP запущен");
                    _warpStartedByUs = true;
                }
                else
                    AppendConsoleOutput("⚠️ Не удалось запустить WARP");
            }

            ConsoleOutputTextBox.Clear();
            AppendConsoleOutput($"Запуск мониторинга {_selectedGame.Name}...");

            try
            {
                await _pythonManager.StartAsync(_selectedGame.Id, _listsPath, monitorOnly: false);
                StartButton.IsEnabled = false;
                MonitorButton.IsEnabled = false;
                StopButton.IsEnabled = true;
                StatusTextBlock.Text = "Активен";
                StatusBarText.Text = $"{_selectedGame.Name} запущен";
            }
            catch (Exception ex)
            {
                LauncherLogger.Error($"Ошибка запуска: {ex}");
                AppendConsoleOutput($"Ошибка: {ex.Message}");
                StartButton.IsEnabled = IsAdministrator;
                MonitorButton.IsEnabled = true;
                StopButton.IsEnabled = false;
                StatusTextBlock.Text = "Ошибка";
            }
        }

        private async void MonitorButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedGame == null || !IsInstalled(_selectedGame.Id)) return;

            if (string.IsNullOrEmpty(_listsPath))
            {
                MessageBox.Show("Папка lists не найдена. Укажите путь через кнопку LISTS.",
                    "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            ConsoleOutputTextBox.Clear();
            AppendConsoleOutput($"Запуск мониторинга (только просмотр) {_selectedGame.Name}...");

            try
            {
                await _pythonManager.StartAsync(_selectedGame.Id, _listsPath, monitorOnly: true);
                StartButton.IsEnabled = false;
                MonitorButton.IsEnabled = false;
                StopButton.IsEnabled = true;
                StatusTextBlock.Text = "Мониторинг";
                StatusBarText.Text = $"{_selectedGame.Name} мониторинг";
            }
            catch (Exception ex)
            {
                LauncherLogger.Error($"Ошибка мониторинга: {ex}");
                AppendConsoleOutput($"Ошибка: {ex.Message}");
                StartButton.IsEnabled = IsAdministrator;
                MonitorButton.IsEnabled = true;
                StopButton.IsEnabled = false;
                StatusTextBlock.Text = "Ошибка";
            }
            // В мониторинге бэкапов нет, поэтому finally не нужен
        }

        private async void StopButton_Click(object sender, RoutedEventArgs e)
        {
            await StopPythonProcessAsync();
        }

        private async Task StopPythonProcessAsync()
        {
            if (!_pythonManager.IsRunning) return;
            AppendConsoleOutput("Остановка процесса...");
            await _pythonManager.StopAsync();

            if (_warpStartedByUs)
            {
                AppendConsoleOutput("⏳ Остановка WARP...");
                await WarpManager.DisconnectAsync();
                AppendConsoleOutput("✅ WARP остановлен");
                _warpStartedByUs = false;
            }

            StartButton.IsEnabled = IsAdministrator;
            MonitorButton.IsEnabled = true;
            StopButton.IsEnabled = false;
            StatusTextBlock.Text = "Остановлен";
            StatusBarText.Text = "Остановлен";
            UpdateStartButtonText();
        }

        private int _restoreInProgress = 0;

        private void OnPythonProcessExited(int exitCode)
        {
            Dispatcher.Invoke(async () =>
            {
                StartButton.IsEnabled = IsAdministrator;
                MonitorButton.IsEnabled = true;
                StopButton.IsEnabled = false;
                StatusTextBlock.Text = "Завершён";
                StatusBarText.Text = $"Процесс завершён с кодом {exitCode}";
                AppendConsoleOutput($"Процесс завершён (код {exitCode})");

                // Восстановление только один раз
                if (Interlocked.CompareExchange(ref _restoreInProgress, 1, 0) == 0)
                {
                    try
                    {
                        if (!_backupRestored && _backupManager != null && !string.IsNullOrEmpty(_currentBackupDir))
                        {
                            bool restored = await _backupManager.RestoreBackupAsync(_listsPath, _currentBackupDir);
                            Dispatcher.Invoke(() =>
                            {
                                if (restored)
                                {
                                    AppendConsoleOutput("✅ Бэкап восстановлен");
                                    _backupRestored = true;
                                }
                                else
                                    AppendConsoleOutput("⚠️ Ошибка восстановления бэкапа");
                                _currentBackupDir = null;
                                _backupManager = null;
                            });
                        }
                    }
                    finally
                    {
                        Interlocked.Exchange(ref _restoreInProgress, 0);
                    }
                }

                if (_warpStartedByUs)
                {
                    AppendConsoleOutput("⏳ Остановка WARP...");
                    await WarpManager.DisconnectAsync();
                    AppendConsoleOutput("✅ WARP остановлен");
                    _warpStartedByUs = false;
                }

                UpdateStartButtonText();
            });
        }

        private void AppendConsoleOutput(string line)
        {
            Dispatcher.Invoke(() =>
            {
                if (!string.IsNullOrEmpty(line))
                    ConsoleOutputTextBox.AppendText(line + Environment.NewLine);
                ConsoleOutputTextBox.ScrollToEnd();
                if (ConsoleOutputTextBox.LineCount > 1000)
                {
                    var lines = ConsoleOutputTextBox.Text.Split(Environment.NewLine);
                    var newText = string.Join(Environment.NewLine, lines.Skip(lines.Length - 800));
                    ConsoleOutputTextBox.Text = newText;
                    ConsoleOutputTextBox.ScrollToEnd();
                }
            });
        }

        private void FilterGames_Changed(object sender, RoutedEventArgs e) => FilterGames();

        // ИСПРАВЛЕНИЕ: методы возвращают null
        private T FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T t)
                    return t;
                var result = FindVisualChild<T>(child);
                if (result != null)
                    return result;
            }
            return null; // добавлено
        }

        private T FindParent<T>(DependencyObject child) where T : DependencyObject
        {
            var parent = VisualTreeHelper.GetParent(child);
            while (parent != null && !(parent is T))
                parent = VisualTreeHelper.GetParent(parent);
            return parent as T; // может быть null
        }
    }
}