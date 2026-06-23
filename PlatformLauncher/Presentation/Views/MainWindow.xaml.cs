using EasyWindowsTerminalControl;
using Hardcodet.Wpf.TaskbarNotification;
using Microsoft.Extensions.DependencyInjection;
using PlatformLauncher.AppHost;
using PlatformLauncher.Core.Interfaces;
using PlatformLauncher.Domain.Models;
using PlatformLauncher.Presentation.Services;
using PlatformLauncher.Presentation.ViewModels;
using System;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace PlatformLauncher.Presentation.Views
{
    public partial class MainWindow : Window
    {
        private readonly IServiceProvider _serviceProvider;
        private ISessionOrchestrator _sessionOrchestrator;
        private ServiceTabViewModel _serviceTabViewModel;

        public MainWindow(MainViewModel viewModel, ITerminalOutput terminalOutput, IServiceProvider serviceProvider)
        {
            InitializeComponent();
            DataContext = viewModel;
            _serviceProvider = serviceProvider;

            if (terminalOutput is TerminalOutputAdapter adapter)
                adapter.AttachTerminal(ConsoleOutputTerminal);

            _serviceTabViewModel = _serviceProvider.GetRequiredService<ServiceTabViewModel>();
            _serviceTabViewModel.ThemeChanged += ApplyTheme;
            ServiceTabControl.SetViewModel(_serviceTabViewModel);
            _sessionOrchestrator = _serviceProvider.GetRequiredService<ISessionOrchestrator>();
            _sessionOrchestrator.SetAskUserCallback(AskUserToSaveBackup);

            // Загружаем иконку трея
            try
            {
                string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "trayicon.ico");
                if (File.Exists(iconPath))
                    TrayIcon.Icon = new Icon(iconPath);
                else
                    DebugLogger.Warn("trayicon.ico not found");
            }
            catch (Exception ex)
            {
                DebugLogger.Error($"Failed to load tray icon: {ex.Message}");
            }

            _sessionOrchestrator = _serviceProvider.GetRequiredService<ISessionOrchestrator>();
            TrayIcon.Visibility = Visibility.Visible;

            // --- ПРИНУДИТЕЛЬНОЕ ПРИМЕНЕНИЕ ТЕМЫ ПРИ СТАРТЕ ---
            var settingsManager = _serviceProvider.GetRequiredService<ISettingsManager>();
            string savedThemeId = settingsManager.GetTheme()?.Trim();
            if (!string.IsNullOrEmpty(savedThemeId))
            {
                var theme = _serviceTabViewModel.Themes.FirstOrDefault(t => t.Id.Equals(savedThemeId, StringComparison.OrdinalIgnoreCase));
                if (theme != null)
                {
                    // Устанавливаем SelectedTheme (через сеттер, чтобы сохранить в JSON, если вдруг изменилось)
                    _serviceTabViewModel.SelectedTheme = theme;
                    // Применяем тему (на случай, если событие не сработало)
                    ApplyTheme(theme.Id);
                }
                else
                {
                    // Если сохранённая тема не найдена – применяем светлую
                    var defaultTheme = _serviceTabViewModel.Themes.FirstOrDefault(t => t.Id == "fluent-light") ?? _serviceTabViewModel.Themes.FirstOrDefault();
                    if (defaultTheme != null)
                    {
                        _serviceTabViewModel.SelectedTheme = defaultTheme;
                        ApplyTheme(defaultTheme.Id);
                    }
                }
            }
            else
            {
                // Если в JSON нет темы – применяем светлую по умолчанию
                var defaultTheme = _serviceTabViewModel.Themes.FirstOrDefault(t => t.Id == "fluent-light") ?? _serviceTabViewModel.Themes.FirstOrDefault();
                if (defaultTheme != null)
                {
                    _serviceTabViewModel.SelectedTheme = defaultTheme;
                    ApplyTheme(defaultTheme.Id);
                }
            }
        }

        public void ApplyTheme(string themeId)
        {
            var theme = _serviceTabViewModel?.Themes.FirstOrDefault(t => t.Id == themeId);
            if (theme == null) return;

            SetTerminalTheme(theme.TerminalTheme);

            var dict = new ResourceDictionary();
            // Явно используем System.Windows.Media, чтобы избежать конфликта с System.Drawing
            dict.Add("MainBackground", new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(theme.Background)));
            dict.Add("MainForeground", new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(theme.Foreground)));
            dict.Add("AccentBrush", new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(theme.Accent)));
            dict.Add("ControlBackground", new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(theme.ControlBackground)));
            dict.Add("ControlForeground", new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(theme.ControlForeground)));
            dict.Add("BorderBrush", new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(theme.BorderBrush)));

            var resources = Application.Current.Resources;
            resources.MergedDictionaries.Clear();
            resources.MergedDictionaries.Add(dict);
        }

        public void SetTerminalTheme(string themeName)
        {
            if (ConsoleOutputTerminal == null) return;
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
            ConsoleOutputTerminal.InvalidateVisual();
        }

        private async Task<bool> AskUserToSaveBackup(string backupDir)
        {
            return await Dispatcher.InvokeAsync(() =>
            {
                var result = MessageBox.Show(
                    "Сохранить результат фикса? (Бэкап не будет восстановлен)",
                    "Сохранение результата",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);
                return result == MessageBoxResult.Yes;
            });
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            e.Cancel = true;
            this.Hide();
            TrayIcon.Visibility = Visibility.Visible;
            base.OnClosing(e);
        }

        private async void ExitMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (_sessionOrchestrator.IsRunning)
            {
                try
                {
                    await _sessionOrchestrator.StopAsync();
                }
                catch { }
            }
            TrayIcon.Dispose();
            Application.Current.Shutdown();
        }

        private void OpenListsButton_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            if (button != null)
            {
                ListsContextMenu.DataContext = button.DataContext;
            }
            ListsContextMenu.IsOpen = true;
        }

        private void InputButton_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            if (button != null)
            {
                InputContextMenu.DataContext = button.DataContext;
            }
            InputContextMenu.IsOpen = true;
        }

        private void ConsoleOutputTerminal_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.C && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                var text = ConsoleOutputTerminal.Terminal?.GetSelectedText();
                if (!string.IsNullOrEmpty(text))
                {
                    Clipboard.SetText(text);
                    e.Handled = true;
                }
            }
        }
    }
}