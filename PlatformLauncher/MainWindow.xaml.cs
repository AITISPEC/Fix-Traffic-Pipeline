using PlatformLauncher.Models;
using PlatformLauncher.Services;
using PlatformLauncher.Views;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using System.Diagnostics;
using System.Text;
using System.Windows.Media; // <-- для VisualTreeHelper

namespace PlatformLauncher
{
    public partial class MainWindow : Window
    {
        private ObservableCollection<GamePreset> _allPresets = new ObservableCollection<GamePreset>();
        private PythonProcessManager _pythonManager = new PythonProcessManager();
        private GamePreset _selectedGame;
        private string _listsPath;

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
                UpdateStartButtonText();
            }
            catch (Exception ex)
            {
                LauncherLogger.Error($"Ошибка при загрузке: {ex}");
                MessageBox.Show($"Ошибка: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
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
            var filtered = _allPresets
                .Where(p => !onlyInstalled || IsInstalled(p.Id))
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

            // Установлен
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
            var dialog = new OpenFolderDialog();
            dialog.Title = "Выберите папку lists";
            if (dialog.ShowDialog() == true)
            {
                _listsPath = dialog.FolderName;
                StatusBarText.Text = $"Папка lists: {_listsPath}";
            }
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

        // Исправленный метод поиска ProgressBar
        private void SetInstallProgress(string gameId, bool show)
        {
            foreach (var item in GamesListBox.Items)
            {
                var preset = item as GamePreset;
                if (preset?.Id == gameId)
                {
                    var container = GamesListBox.ItemContainerGenerator.ContainerFromItem(item) as ListBoxItem;
                    if (container != null)
                    {
                        var pb = FindVisualChild<ProgressBar>(container);
                        if (pb != null)
                        {
                            pb.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
                        }
                    }
                    break;
                }
            }
        }

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

        private async Task InstallGame(GamePreset preset)
        {
            try
            {
                StatusBarText.Text = $"Установка {preset.Name}...";
                AppendConsoleOutput($"⏳ Установка {preset.Name}...");
                LauncherLogger.Info($"Начало установки {preset.Id}");

                SetInstallProgress(preset.Id, true);

                var (success, error) = await UpdateService.InstallGameAsync(preset);
                LauncherLogger.Info($"Результат установки: success={success}");

                if (!success)
                {
                    MessageBox.Show($"Ошибка установки {preset.Name}:\n{error}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                    StatusBarText.Text = "Ошибка";
                    AppendConsoleOutput($"❌ Ошибка: {error}");
                    SetInstallProgress(preset.Id, false);
                    return;
                }

                AppendConsoleOutput($"✅ Конфиг скачан");
                LauncherLogger.Info($"Конфиг {preset.Id} скачан");

                AppendConsoleOutput("📌 Установка правил портов...");
                await RunPythonScript($"--install-rules {preset.Id}");

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

                SetInstallProgress(preset.Id, false);
                MessageBox.Show($"Фикс {preset.Name} установлен", "Успех", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                LauncherLogger.Error($"Критическая ошибка установки: {ex}");
                AppendConsoleOutput($"❌ Ошибка: {ex.Message}");
                MessageBox.Show($"Ошибка: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                StatusBarText.Text = "Ошибка";
                SetInstallProgress(preset.Id, false);
            }
        }

        private async void UninstallGame_Click(object sender, RoutedEventArgs e)
        {
            var preset = GamesListBox.SelectedItem as GamePreset;
            if (preset == null) return;

            AppendConsoleOutput($"⏳ Удаление {preset.Name}...");
            LauncherLogger.Info($"Начало удаления {preset.Id}");

            if (MessageBox.Show($"Удалить фикс {preset.Name}? Это также удалит правила портов.",
                "Подтверждение", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            if (_pythonManager.IsRunning && _selectedGame?.Id == preset.Id)
                await StopPythonProcessAsync();

            AppendConsoleOutput("📌 Удаление правил портов...");
            await RunPythonScript($"--remove-rules {preset.Id}");

            UpdateService.UninstallGame(preset.Id);
            LauncherLogger.Info($"Конфиг {preset.Id} удалён");

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
            LauncherLogger.Info($"Удаление {preset.Id} завершено");
            MessageBox.Show($"Фикс {preset.Name} удалён", "Готово", MessageBoxButton.OK, MessageBoxImage.Information);
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
            var menu = (sender as ListBox)?.ContextMenu;
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

            ConsoleOutputTextBox.Clear();
            AppendConsoleOutput($"Запуск фикса {_selectedGame.Name}...");
            try
            {
                await _pythonManager.StartAsync(_selectedGame.Id, _listsPath, warpEnabled, "./backups", monitorOnly: false);
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
                StartButton.IsEnabled = true;
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
            AppendConsoleOutput($"Запуск мониторинга {_selectedGame.Name}...");
            try
            {
                bool warpEnabled = SettingsManager.GetWarpEnabled(_selectedGame.Id);
                await _pythonManager.StartAsync(_selectedGame.Id, _listsPath, warpEnabled, "./backups", monitorOnly: true);
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
            StartButton.IsEnabled = IsAdministrator;
            MonitorButton.IsEnabled = true;
            StopButton.IsEnabled = false;
            StatusTextBlock.Text = "Остановлен";
            StatusBarText.Text = "Остановлен";
            UpdateStartButtonText();
        }

        private void OnPythonProcessExited(int exitCode)
        {
            Dispatcher.Invoke(() =>
            {
                StartButton.IsEnabled = IsAdministrator;
                MonitorButton.IsEnabled = true;
                StopButton.IsEnabled = false;
                StatusTextBlock.Text = "Завершён";
                StatusBarText.Text = $"Процесс завершён с кодом {exitCode}";
                AppendConsoleOutput($"Процесс завершён (код {exitCode})");
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
    }
}