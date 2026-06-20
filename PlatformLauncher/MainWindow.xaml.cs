using EasyWindowsTerminalControl;
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
using System.Windows.Threading;

namespace PlatformLauncher
{
    public partial class MainWindow : Window
    {
        private ObservableCollection<GamePreset> _allPresets = new ObservableCollection<GamePreset>();
        private GamePreset _selectedGame;
        private string _listsPath;
        private StreamWriter _terminalLogWriter;

        // Менеджер сессии (запуск/остановка мониторинга, WARP, бэкапы)
        private GameSessionManager _gameSessionManager;

        public bool IsAdministrator { get; }

        public static MainWindow Instance { get; private set; }

        public MainWindow()
        {
            InitializeComponent();
            Instance = this;
            IsAdministrator = IsCurrentUserAdministrator();
            Loaded += MainWindow_Loaded;
            UpdateStartButtonText();
            SetTerminalTheme("Light");
        }

        private static bool IsCurrentUserAdministrator()
        {
            using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            var principal = new System.Security.Principal.WindowsPrincipal(identity);
            return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }

        private void ApplyAppConfig()
        {
            var config = ConfigService.Load();
            SetTerminalTheme(config.Terminal.Theme);
            ConsoleOutputTerminal.FontFamilyWhenSettingTheme = new FontFamily(config.Terminal.FontFamily);
            ConsoleOutputTerminal.FontSizeWhenSettingTheme = config.Terminal.FontSize;
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                ApplyAppConfig();
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

                // Открываем лог-файл для записи вывода терминала
                string logDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
                Directory.CreateDirectory(logDir);
                string logFile = Path.Combine(logDir, "console.log");
                _terminalLogWriter = new StreamWriter(logFile, append: true, Encoding.UTF8);
                _terminalLogWriter.AutoFlush = true;

                AppendConsoleOutput("===== ТЕРМИНАЛ ЗАПУЩЕН =====");
                AppendConsoleOutput("Тестовая строка с цветом \x1b[32mзелёный\x1b[0m и \x1b[31mкрасный\x1b[0m");
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
                    AppendConsoleOutput($"ℹ️ Бэкап для {game.Name} помечен .norestored");
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

        private void SetInstallProgress(GamePreset preset, bool show)
        {
            preset.IsInstalling = show;
        }

        private async Task InstallGame(GamePreset preset)
        {
            try
            {
                SetInstallProgress(preset, true);
                var progress = new Progress<string>(msg => AppendConsoleOutput(msg));
                var (success, error) = await GameInstallService.InstallGameAsync(preset, progress);
                if (!success)
                {
                    MessageBox.Show($"Ошибка установки {preset.Name}:\n{error}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                    AppendConsoleOutput($"❌ Ошибка: {error}");
                    return;
                }

                // Обновляем список пресетов
                var freshPresets = UpdateService.LoadPresets();
                _allPresets.Clear();
                foreach (var p in freshPresets)
                    _allPresets.Add(p);
                FilterGames();

                _selectedGame = _allPresets.FirstOrDefault(p => p.Id == preset.Id);
                if (_selectedGame != null)
                    GamesListBox.SelectedItem = _selectedGame;

                UpdateStartButtonText();
                StatusBarText.Text = "Готово";
                AppendConsoleOutput($"✅ Фикс {preset.Name} установлен");
                MessageBox.Show($"Фикс {preset.Name} установлен", "Успех", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                LauncherLogger.Error($"Критическая ошибка установки: {ex}");
                AppendConsoleOutput($"❌ Ошибка: {ex.Message}");
                MessageBox.Show($"Ошибка: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                SetInstallProgress(preset, false);
            }
        }

        private async void UninstallGame_Click(object sender, RoutedEventArgs e)
        {
            var preset = GamesListBox.SelectedItem as GamePreset;
            if (preset == null) return;

            preset.IsUninstalling = true;
            try
            {
                var progress = new Progress<string>(msg => AppendConsoleOutput(msg));
                bool ok = await GameInstallService.UninstallGameAsync(preset, progress);
                if (!ok)
                {
                    MessageBox.Show("Ошибка удаления правил портов", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

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
                AppendConsoleOutput($"✅ Фикс {preset.Name} удалён");
                MessageBox.Show($"Фикс {preset.Name} удалён", "Готово", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                LauncherLogger.Error($"Ошибка удаления: {ex}");
                AppendConsoleOutput($"❌ Ошибка: {ex.Message}");
                MessageBox.Show($"Ошибка: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                preset.IsUninstalling = false;
            }
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

        private DispatcherTimer _statusAnimationTimer;
        private int _dotCount = 0;

        private void StartStatusAnimation(string baseText)
        {
            _dotCount = 0;
            _statusAnimationTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };
            _statusAnimationTimer.Tick += (s, e) =>
            {
                _dotCount = (_dotCount + 1) % 4;
                StatusTextBlock.Text = baseText + new string('.', _dotCount);
            };
            _statusAnimationTimer.Start();
        }

        private void StopStatusAnimation()
        {
            _statusAnimationTimer?.Stop();
            _statusAnimationTimer = null;
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

            if (string.IsNullOrEmpty(_listsPath))
            {
                MessageBox.Show("Папка lists не найдена. Укажите путь через кнопку LISTS.",
                    "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            bool warpEnabled = SettingsManager.GetWarpEnabled(_selectedGame.Id);

            ConsoleOutputTerminal.ConPTYTerm.ClearUITerminal(fullReset: false);
            LauncherLogger.Info($"Запуск мониторинга {_selectedGame.Name}...");

            _gameSessionManager = new GameSessionManager(_selectedGame.Id, _listsPath);
            _gameSessionManager.OutputReceived += AppendConsoleOutput;
            _gameSessionManager.SessionEnded += OnSessionEnded;

            try
            {
                await _gameSessionManager.StartAsync(monitorOnly: false, warpEnabled);
                StartButton.IsEnabled = false;
                MonitorButton.IsEnabled = false;
                StopButton.IsEnabled = true;
                StatusTextBlock.Text = "Активен";
                StatusBarText.Text = $"{_selectedGame.Name} запущен";
            }
            catch (UnauthorizedAccessException ex)
            {
                LauncherLogger.Error($"Ошибка прав доступа: {ex}");
                AppendConsoleOutput($"❌ Недостаточно прав: {ex.Message}");
                MessageBox.Show($"Недостаточно прав для запуска. Запустите лаунчер от имени администратора.\n{ex.Message}",
                    "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                StartButton.IsEnabled = IsAdministrator;
                MonitorButton.IsEnabled = true;
                StopButton.IsEnabled = false;
                StatusTextBlock.Text = "Ошибка";
                _gameSessionManager = null;
            }
            catch (Exception ex)
            {
                LauncherLogger.Error($"Ошибка запуска: {ex}");
                AppendConsoleOutput($"❌ ОШИБКА: {ex.Message}");
                AppendConsoleOutput($"   Подробности: {ex.StackTrace}");
                MessageBox.Show($"Ошибка запуска: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                StartButton.IsEnabled = IsAdministrator;
                MonitorButton.IsEnabled = true;
                StopButton.IsEnabled = false;
                StatusTextBlock.Text = "Ошибка";
                _gameSessionManager = null;
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

            ConsoleOutputTerminal.ConPTYTerm.ClearUITerminal(fullReset: false);
            AppendConsoleOutput($"Запуск мониторинга (только просмотр) {_selectedGame.Name}...");

            _gameSessionManager = new GameSessionManager(_selectedGame.Id, _listsPath);
            _gameSessionManager.OutputReceived += AppendConsoleOutput;
            _gameSessionManager.SessionEnded += OnSessionEnded;

            try
            {
                await _gameSessionManager.StartAsync(monitorOnly: true, warpEnabled: false);
                StartButton.IsEnabled = false;
                MonitorButton.IsEnabled = false;
                StopButton.IsEnabled = true;
                StatusTextBlock.Text = "Мониторинг";
                StatusBarText.Text = $"{_selectedGame.Name} мониторинг";
            }
            catch (UnauthorizedAccessException ex)
            {
                LauncherLogger.Error($"Ошибка прав доступа: {ex}");
                AppendConsoleOutput($"❌ Недостаточно прав: {ex.Message}");
                MessageBox.Show($"Недостаточно прав для мониторинга.\n{ex.Message}",
                    "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                StartButton.IsEnabled = IsAdministrator;
                MonitorButton.IsEnabled = true;
                StopButton.IsEnabled = false;
                StatusTextBlock.Text = "Ошибка";
                _gameSessionManager = null;
            }
            catch (Exception ex)
            {
                LauncherLogger.Error($"Ошибка мониторинга: {ex}");
                AppendConsoleOutput($"❌ ОШИБКА: {ex.Message}");
                AppendConsoleOutput($"   Подробности: {ex.StackTrace}");
                MessageBox.Show($"Ошибка мониторинга: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                StartButton.IsEnabled = IsAdministrator;
                MonitorButton.IsEnabled = true;
                StopButton.IsEnabled = false;
                StatusTextBlock.Text = "Ошибка";
                _gameSessionManager = null;
            }
        }

        private async void StopButton_Click(object sender, RoutedEventArgs e)
        {
            if (_gameSessionManager == null || !_gameSessionManager.IsRunning)
                return;

            // Отключаем кнопку, показываем статус
            StopButton.IsEnabled = false;
            StartStatusAnimation("");
            AppendConsoleOutput("Остановка фикса...");

            await _gameSessionManager.StopAsync();

            // После остановки менеджер вызовет SessionEnded, который сбросит состояние
        }

        private void OnSessionEnded(bool success)
        {
            Dispatcher.Invoke(() =>
            {
                StartButton.IsEnabled = IsAdministrator;
                MonitorButton.IsEnabled = true;
                StopButton.IsEnabled = false;
                StopStatusAnimation();
                StatusTextBlock.Text = "Завершён";
                StatusBarText.Text = success ? "Завершено успешно" : "Завершено с ошибкой";
                UpdateStartButtonText();
                _gameSessionManager = null;
                _terminalLogWriter?.Flush();
            });
        }

        // Главный метод вывода: пишет в терминал и в лог-файл
        private void AppendConsoleOutput(string line)
        {
            Dispatcher.Invoke(() =>
            {
                if (!string.IsNullOrEmpty(line))
                {
                    ConsoleOutputTerminal.ConPTYTerm.WriteToUITerminal(line + Environment.NewLine);
                    _terminalLogWriter?.WriteLine(line);
                    _terminalLogWriter?.Flush();
                }
            });
        }

        private void FilterGames_Changed(object sender, RoutedEventArgs e) => FilterGames();

        // Обработчик кнопки "Ввод" — открывает контекстное меню
        private void InputButton_Click(object sender, RoutedEventArgs e)
        {
            InputContextMenu.PlacementTarget = InputButton;
            InputContextMenu.IsOpen = true;
        }

        // Обработчик пунктов меню "Ввод"
        private void InputMenu_Click(object sender, RoutedEventArgs e)
        {
            var menuItem = sender as MenuItem;
            if (menuItem?.Tag is string command)
            {
                AppendConsoleOutput($"> {command}");
                Task.Run(() =>
                {
                    try
                    {
                        string pythonExe = PythonEnvironmentManager.GetVenvPythonPath();
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
                            StandardOutputEncoding = Encoding.UTF8,
                            StandardErrorEncoding = Encoding.UTF8
                        };
                        using var proc = Process.Start(psi);
                        string output = proc.StandardOutput.ReadToEnd();
                        string error = proc.StandardError.ReadToEnd();
                        proc.WaitForExit();
                        Dispatcher.Invoke(() =>
                        {
                            if (!string.IsNullOrEmpty(output))
                                AppendConsoleOutput(output);
                            if (!string.IsNullOrEmpty(error))
                                AppendConsoleOutput($"⚠️ {error}");
                            LauncherLogger.Info($"Команда завершена с кодом {proc.ExitCode}");
                        });
                    }
                    catch (Exception ex)
                    {
                        Dispatcher.Invoke(() => AppendConsoleOutput($"❌ Ошибка: {ex.Message}"));
                    }
                });
            }
        }

        // Настройка темы терминала
        public void SetTerminalTheme(string themeName)
        {
            var theme = new Microsoft.Terminal.Wpf.TerminalTheme();
            switch (themeName)
            {
                case "Dark":
                    theme.DefaultBackground = EasyTerminalControl.ColorToVal(System.Windows.Media.Color.FromRgb(0, 0, 30));
                    theme.DefaultForeground = EasyTerminalControl.ColorToVal(System.Windows.Media.Color.FromRgb(255, 255, 200));
                    theme.DefaultSelectionBackground = 0xcccccc;
                    theme.CursorStyle = Microsoft.Terminal.Wpf.CursorStyle.BlinkingBar;
                    theme.ColorTable = new uint[] {
                        0x0C0C0C, 0x1F0FC5, 0x0EA113, 0x009CC1, 0xDA3700, 0x981788, 0xDD963A, 0xCCCCCC,
                        0x767676, 0x5648E7, 0x0CC616, 0xA5F1F9, 0xFF783B, 0x9E00B4, 0xD6D661, 0xF2F2F2
                    };
                    break;
                case "Light":
                    theme.DefaultBackground = EasyTerminalControl.ColorToVal(System.Windows.Media.Color.FromRgb(255, 255, 255));
                    theme.DefaultForeground = EasyTerminalControl.ColorToVal(System.Windows.Media.Color.FromRgb(0, 0, 0));
                    theme.DefaultSelectionBackground = 0xcccccc;
                    theme.CursorStyle = Microsoft.Terminal.Wpf.CursorStyle.BlinkingBar;
                    theme.ColorTable = new uint[] {
                        0x0C0C0C, 0x1F0FC5, 0x0EA113, 0x009CC1, 0xDA3700, 0x981788, 0xDD963A, 0xCCCCCC,
                        0x767676, 0x5648E7, 0x0CC616, 0xA5F1F9, 0xFF783B, 0x9E00B4, 0xD6D661, 0xF2F2F2
                    };
                    break;
                default:
                    return;
            }
            ConsoleOutputTerminal.Theme = theme;
        }

        // Вспомогательные методы для поиска элементов
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
            return null;
        }

        private T FindParent<T>(DependencyObject child) where T : DependencyObject
        {
            var parent = VisualTreeHelper.GetParent(child);
            while (parent != null && !(parent is T))
                parent = VisualTreeHelper.GetParent(parent);
            return parent as T;
        }

        // Закрытие приложения – сохраняем лог
        protected override void OnClosed(EventArgs e)
        {
            _terminalLogWriter?.Dispose();
            base.OnClosed(e);
        }
    }
}