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
using System.Windows.Input;
using Microsoft.Win32;

namespace PlatformLauncher
{
    public partial class MainWindow : Window
    {
        private ObservableCollection<GamePreset> _allPresets = new ObservableCollection<GamePreset>();
        private PythonProcessManager _pythonManager = new PythonProcessManager();
        private GamePreset _selectedGame;
        private string _listsPath;

        public bool IsAdministrator => PortsManager.IsAdministrator();

        public MainWindow()
        {
            InitializeComponent();
            Loaded += MainWindow_Loaded;
            _pythonManager.OutputReceived += AppendConsoleOutput;
            _pythonManager.ProcessExited += OnPythonProcessExited;
            UpdateStartButtonText();
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

                // Если presets.yaml отсутствует – создаём пустой
                string presetsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "configs", "presets.yaml");
                if (!File.Exists(presetsPath))
                {
                    // Создаём пустой файл
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
                await RecoverFromCrash();
                await LoadPresets();
                UpdateStartButtonText();
            }
            catch (Exception ex)
            {
                LauncherLogger.Error($"Ошибка при загрузке: {ex}");
                MessageBox.Show($"Ошибка: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task RecoverFromCrash()
        {
            try
            {
                string backupRoot = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "backups");
                if (!Directory.Exists(backupRoot)) return;

                var lastBackupDir = BackupManager.GetLatestBackupForAnyGame(backupRoot);
                if (lastBackupDir == null) return;

                if (string.IsNullOrEmpty(_listsPath))
                {
                    var result = MessageBox.Show(
                        "Для восстановления бэкапа необходимо указать папку lists. Выбрать сейчас?",
                        "Выбор папки lists",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);
                    if (result == MessageBoxResult.Yes)
                    {
                        var dialog = new OpenFolderDialog();
                        dialog.Title = "Выберите папку lists";
                        if (dialog.ShowDialog() == true)
                        {
                            _listsPath = dialog.FolderName;
                        }
                        else
                        {
                            BackupManager.MarkBackupAsNotRestored(lastBackupDir);
                            AppendConsoleOutput($"⛔ Бэкап {Path.GetFileName(lastBackupDir)} отмечен как не восстановленный (путь не выбран)");
                            return;
                        }
                    }
                    else
                    {
                        BackupManager.MarkBackupAsNotRestored(lastBackupDir);
                        AppendConsoleOutput($"⛔ Бэкап {Path.GetFileName(lastBackupDir)} отмечен как не восстановленный (путь не выбран)");
                        return;
                    }
                }

                var result2 = MessageBox.Show(
                    "Обнаружен незавершённый сеанс. Восстановить папку lists из последнего бэкапа?",
                    "Восстановление",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);
                if (result2 == MessageBoxResult.Yes)
                {
                    BackupManager.RestoreBackup(_listsPath, lastBackupDir);
                    AppendConsoleOutput($"✅ Восстановлен бэкап: {Path.GetFileName(lastBackupDir)}");
                }
                else
                {
                    BackupManager.MarkBackupAsNotRestored(lastBackupDir);
                    AppendConsoleOutput($"⛔ Бэкап {Path.GetFileName(lastBackupDir)} отмечен как не восстановленный пользователем");
                }
            }
            catch (Exception ex)
            {
                LauncherLogger.Error($"Ошибка восстановления после краша: {ex}");
                AppendConsoleOutput($"❌ Ошибка при восстановлении: {ex.Message}");
            }
        }

        private async Task LoadPresets()
        {
            StatusBarText.Text = "Загрузка пресетов...";
            var presets = UpdateService.LoadPresets();
            AppendConsoleOutput($"📊 Загружено пресетов: {presets.Count}");
            _allPresets.Clear();
            foreach (var p in presets)
                _allPresets.Add(p);
            FilterGames();
            StatusBarText.Text = $"Загружено {_allPresets.Count} пресетов";
        }

        private void FilterGames()
        {
            bool onlyInstalled = ShowOnlyInstalledCheckBox.IsChecked == true;
            var filtered = _allPresets
                .Where(p => !onlyInstalled || p.Installed)
                .OrderBy(p => p.Installed ? 0 : 1)
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

        private void GamesListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _selectedGame = GamesListBox.SelectedItem as GamePreset;
            UpdateStartButtonText();
            if (_selectedGame != null)
                StatusBarText.Text = $"Выбран: {_selectedGame.Name}";
        }

        private void UpdateStartButtonText()
        {
            if (_selectedGame == null)
            {
                StartButton.Content = "Запустить фикс";
                StartButton.IsEnabled = false;
                return;
            }

            bool installed = _selectedGame.Installed;
            if (!installed)
            {
                StartButton.Content = "Установить";
                StartButton.IsEnabled = true;
                return;
            }

            if (IsAdministrator)
                StartButton.Content = "Запустить фикс";
            else
                StartButton.Content = "Мониторинг";
            StartButton.IsEnabled = true;
        }

        private async void RefreshPresetsButton_Click(object sender, RoutedEventArgs e)
        {
            StatusBarText.Text = "Синхронизация с GitHub...";
            AppendConsoleOutput("⏳ Синхронизация пресетов...");
            bool ok = await UpdateService.SyncFromGitHubAsync();
            if (ok)
                AppendConsoleOutput("✅ Синхронизация завершена");
            else
                AppendConsoleOutput("❌ Ошибка синхронизации (проверьте лог для деталей)");
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

        private async Task InstallGame(GamePreset preset)
        {
            StatusBarText.Text = $"Установка {preset.Name}...";
            var (success, error) = await UpdateService.InstallGameAsync(preset);
            if (success)
            {
                var config = UpdateService.LoadGameConfig(preset.Id);
                if (config?.Ports != null)
                {
                    try
                    {
                        await PortsManager.AddRulesAsync(config.Ports.Tcp, config.Ports.Udp, preset.Id);
                    }
                    catch (UnauthorizedAccessException ex)
                    {
                        MessageBox.Show(ex.Message, "Недостаточно прав", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
                // Перезагружаем список, чтобы обновить статус installed
                await LoadPresets();
                UpdateStartButtonText();
                MessageBox.Show($"Фикс {preset.Name} установлен", "Успех", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show($"Ошибка установки {preset.Name}:\n{error}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            StatusBarText.Text = "Готов";
        }

        private async void UninstallGame_Click(object sender, RoutedEventArgs e)
        {
            var preset = GamesListBox.SelectedItem as GamePreset;
            if (preset == null) return;
            if (MessageBox.Show($"Удалить фикс {preset.Name}? Это также удалит правила портов.", "Подтверждение", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            if (_pythonManager.IsRunning && _selectedGame?.Id == preset.Id)
                await StopPythonProcessAsync();

            var config = UpdateService.LoadGameConfig(preset.Id);
            if (config?.Ports != null)
            {
                try
                {
                    await PortsManager.RemoveRulesAsync(config.Ports.Tcp, config.Ports.Udp, preset.Id);
                }
                catch (UnauthorizedAccessException ex)
                {
                    MessageBox.Show(ex.Message, "Недостаточно прав", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }

            UpdateService.UninstallGame(preset.Id);
            await LoadPresets();
            UpdateStartButtonText();
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

        private async void StartButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedGame == null) return;

            if (!_selectedGame.Installed)
            {
                await InstallGame(_selectedGame);
                return;
            }

            bool warpEnabled = SettingsManager.GetWarpEnabled(_selectedGame.Id);
            bool monitorOnly = !IsAdministrator;

            if (string.IsNullOrEmpty(_listsPath))
            {
                MessageBox.Show("Папка lists не найдена. Укажите путь через кнопку LISTS.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            ConsoleOutputTextBox.Clear();
            AppendConsoleOutput($"Запуск {(monitorOnly ? "мониторинга" : "фикса")} {_selectedGame.Name}...");
            try
            {
                await _pythonManager.StartAsync(_selectedGame.Id, _listsPath, warpEnabled, "./backups", monitorOnly);
                StartButton.IsEnabled = false;
                StopButton.IsEnabled = true;
                StatusTextBlock.Text = monitorOnly ? "Мониторинг" : "Активен";
                StatusBarText.Text = $"{_selectedGame.Name} запущен";
            }
            catch (Exception ex)
            {
                LauncherLogger.Error($"Ошибка запуска: {ex}");
                AppendConsoleOutput($"Ошибка: {ex.Message}");
                StartButton.IsEnabled = true;
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
            StartButton.IsEnabled = true;
            StopButton.IsEnabled = false;
            StatusTextBlock.Text = "Остановлен";
            StatusBarText.Text = "Остановлен";
            UpdateStartButtonText();
        }

        private void OnPythonProcessExited(int exitCode)
        {
            Dispatcher.Invoke(() =>
            {
                StartButton.IsEnabled = true;
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
            bool installed = preset.Installed;
            foreach (MenuItem item in menu.Items)
            {
                if (item.Header?.ToString() == "Установить")
                    item.Visibility = installed ? Visibility.Collapsed : Visibility.Visible;
                else if (item.Header?.ToString() == "Удалить")
                    item.Visibility = installed ? Visibility.Visible : Visibility.Collapsed;
                // "Свойства" всегда видны
            }
        }
    }
}