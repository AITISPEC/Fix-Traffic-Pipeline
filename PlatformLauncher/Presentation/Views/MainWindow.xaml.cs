using HandyControl.Themes;
using Microsoft.Extensions.DependencyInjection;
using PlatformLauncher.Core.Interfaces;
using PlatformLauncher.Domain.Models;
using PlatformLauncher.Presentation.Services;
using PlatformLauncher.Presentation.ViewModels;
using System;
using System.Runtime.InteropServices;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace PlatformLauncher.Presentation.Views
{
    public partial class MainWindow : System.Windows.Window
    {
        private readonly IServiceProvider _serviceProvider;
        private ISessionOrchestrator _sessionOrchestrator;
        private ServiceTabViewModel _serviceTabViewModel;
        private readonly ITerminalOutput _terminal;
        private readonly ThemeApplier _themeApplier;
        private delegate IntPtr SubClassProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, IntPtr uIdSubclass, IntPtr dwRefData);
        private IntPtr _terminalHwnd;
        private SubClassProcDelegate _subclassDelegate;
        private const uint SUBCLASS_ID = 1;
        private const uint WM_RBUTTONUP = 0x0205;
        private bool _initialConsoleCleared = false;

        [DllImport("comctl32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool SetWindowSubclass(IntPtr hWnd, SubClassProcDelegate lpfnSubclass, uint uIdSubclass, IntPtr dwRefData);

        [DllImport("comctl32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool RemoveWindowSubclass(IntPtr hWnd, SubClassProcDelegate lpfnSubclass, uint uIdSubclass);

        [DllImport("comctl32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr DefSubclassProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        public MainWindow(
            MainViewModel viewModel,
            ITerminalOutput terminalOutput,
            IServiceProvider serviceProvider,
            ITerminalOutput terminal)
        {
            InitializeComponent();
            DataContext = viewModel;
            _serviceProvider = serviceProvider;

            if (terminalOutput is TerminalOutputAdapter adapter)
                adapter.AttachTerminal(ConsoleOutputTerminal);
            ConsoleOutputTerminal.Loaded += (_, __) => HookTerminalRightClick();
            ConsoleOutputTerminal.Unloaded += (_, __) => UnhookTerminalRightClick();

            _themeApplier = new ThemeApplier(serviceProvider);
            _themeApplier.Attach(ConsoleOutputTerminal, this);

            _serviceTabViewModel = _serviceProvider.GetRequiredService<ServiceTabViewModel>();
            _serviceTabViewModel.ThemeChanged += (id) => _themeApplier.ApplyTheme(id, _serviceTabViewModel.AllThemes);
            _serviceTabViewModel.ListsPathChanged += (newPath) =>
            {
                if (DataContext is MainViewModel mainVm)
                {
                    mainVm.ListsPath = newPath;
                }
            };
            ServiceTabControl.SetViewModel(_serviceTabViewModel);
            SettingsTabControl.SetViewModel(_serviceTabViewModel);
            _sessionOrchestrator = _serviceProvider.GetRequiredService<ISessionOrchestrator>();
            if (viewModel != null)
            {
                viewModel.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == nameof(MainViewModel.ListsPath))
                        _serviceTabViewModel.ListsPath = viewModel.ListsPath;
                    if (e.PropertyName == nameof(MainViewModel.IsRunning))
                        _serviceTabViewModel.IsThemeChangeAllowed = !viewModel.IsRunning;
                };
                _serviceTabViewModel.IsThemeChangeAllowed = !viewModel.IsRunning;
            }
            _sessionOrchestrator.SetAskUserCallback(AskUserToSaveBackup);
            _serviceTabViewModel.ListsPath = (DataContext as MainViewModel)?.ListsPath;
            _terminal = terminal;

            if (viewModel != null)
            {
                _serviceTabViewModel.ListsPath = viewModel.ListsPath;
                viewModel.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == nameof(MainViewModel.ListsPath))
                        _serviceTabViewModel.ListsPath = viewModel.ListsPath;
                };
            }

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

            TrayIcon.Visibility = Visibility.Visible;

            var settingsManager = _serviceProvider.GetRequiredService<ISettingsManager>();
            string savedThemeId = settingsManager.GetTheme()?.Trim();
            ThemeItem themeToApply = null;

            if (!string.IsNullOrEmpty(savedThemeId))
                themeToApply = _serviceTabViewModel.AllThemes.FirstOrDefault(t => t.Id.Equals(savedThemeId, StringComparison.OrdinalIgnoreCase));

            themeToApply ??= _serviceTabViewModel.AllThemes.FirstOrDefault(t => t.Id == "fluent-dark")
              ?? _serviceTabViewModel.AllThemes.FirstOrDefault();

            if (themeToApply != null)
            {
                _serviceTabViewModel.SelectedTheme = themeToApply;
                _themeApplier.ApplyTheme(themeToApply.Id, _serviceTabViewModel.AllThemes);
            }
            UpdateClearButtonVisibility();
            viewModel.ClearConsole();
        }

        private void HookTerminalRightClick()
        {
            UnhookTerminalRightClick();

            var hwndHosts = ThemeApplier.FindVisualChildren<System.Windows.Interop.HwndHost>(ConsoleOutputTerminal.Terminal);
            if (hwndHosts.Count == 0)
            {
                DebugLogger.Write("HwndHost not found in visual tree");
                return;
            }

            _terminalHwnd = hwndHosts[0].Handle;
            if (_terminalHwnd == IntPtr.Zero)
            {
                DebugLogger.Write("HwndHost.Handle is zero");
                return;
            }

            _subclassDelegate = new SubClassProcDelegate(TerminalSubclassProc);
            if (!SetWindowSubclass(_terminalHwnd, _subclassDelegate, SUBCLASS_ID, IntPtr.Zero))
            {
                DebugLogger.Write($"SetWindowSubclass failed: {Marshal.GetLastWin32Error()}");
                return;
            }

            DebugLogger.Write($"Terminal hwnd subclassed: {_terminalHwnd}");
        }

        private void UnhookTerminalRightClick()
        {
            if (_terminalHwnd != IntPtr.Zero && _subclassDelegate != null)
            {
                RemoveWindowSubclass(_terminalHwnd, _subclassDelegate, SUBCLASS_ID);
                _terminalHwnd = IntPtr.Zero;
                _subclassDelegate = null;
                DebugLogger.Write("Terminal subclass removed");
            }
        }

        private IntPtr TerminalSubclassProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, IntPtr uIdSubclass, IntPtr dwRefData)
        {
            if (msg == WM_RBUTTONUP)
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (ConsoleOutputTerminal.ContextMenu != null)
                    {
                        ConsoleOutputTerminal.ContextMenu.DataContext = DataContext;
                        ConsoleOutputTerminal.ContextMenu.IsOpen = true;
                    }
                }));
                return IntPtr.Zero;
            }

            return DefSubclassProc(hWnd, msg, wParam, lParam);
        }

        private void ConsoleOutputTerminal_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            if (ConsoleOutputTerminal.ContextMenu != null)
            {
                ConsoleOutputTerminal.ContextMenu.DataContext = DataContext;
            }
        }

        private void ConsoleOutputTerminal_Loaded(object sender, RoutedEventArgs e)
        {
            Dispatcher.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.Background,
                new Action(_themeApplier.ApplyTerminalScrollBarStyle));

            Dispatcher.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.ApplicationIdle,
                new Action(() =>
                {
                    if (!_initialConsoleCleared && DataContext is MainViewModel vm)
                    {
                        vm.ClearConsole();
                        _initialConsoleCleared = true;
                    }
                }));
        }

        public void ApplyTheme(string themeId)
        {
            _themeApplier?.ApplyTheme(themeId, _serviceTabViewModel.AllThemes);
        }

        private async Task<bool> AskUserToSaveBackup(string backupDir)
        {
            return await Dispatcher.InvokeAsync(() =>
            {
                var result = System.Windows.MessageBox.Show(
                    "Сохранить результат фикса? (Бэкап не будет восстановлен)",
                    "Сохранение результата",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);
                return result == MessageBoxResult.Yes;
            });
        }

        private void ListBox_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is System.Windows.Controls.ListBox listBox)
            {
                var listBoxItem = FindVisualParent<System.Windows.Controls.ListBoxItem>(e.OriginalSource as DependencyObject);
                if (listBoxItem == null)
                {
                    listBox.SelectedItem = null;

                    // Убираем фокус из TextBox поиска при клике по пустому месту
                    Keyboard.ClearFocus();
                    FocusManager.SetFocusedElement(FocusManager.GetFocusScope(listBox), null);
                }
            }
        }

        private void ListBox_PreviewMouseRightButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is System.Windows.Controls.ListBox listBox)
            {
                var listBoxItem = FindVisualParent<System.Windows.Controls.ListBoxItem>(e.OriginalSource as DependencyObject);
                if (listBoxItem != null)
                {
                    // Выделяем элемент, чтобы SelectedGame обновился перед открытием меню
                    listBoxItem.IsSelected = true;
                }
                else
                {
                    // Клик по пустому месту - снимаем выделение
                    listBox.SelectedItem = null;
                }
            }
        }

        private void SearchTextBox_GotFocus(object sender, RoutedEventArgs e)
        {
            SearchPlaceholder.Visibility = Visibility.Collapsed;
        }

        private void SearchTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            UpdatePlaceholderVisibility();
        }

        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            // Обрезка при вставке из буфера (MaxLength не всегда срабатывает при Paste)
            if (SearchTextBox.Text.Length > 16)
            {
                int caretIndex = SearchTextBox.CaretIndex;
                SearchTextBox.Text = SearchTextBox.Text.Substring(0, 16);
                SearchTextBox.CaretIndex = Math.Min(caretIndex, 16);
                // Рекурсивный вызов TextChanged безопасен — длина уже <= 16
            }

            UpdatePlaceholderVisibility();
            UpdateClearButtonVisibility();
        }

        private void ClearSearchButton_Click(object sender, RoutedEventArgs e)
        {
            SearchTextBox.Text = string.Empty;
            SearchTextBox.Focus();
        }

        private void UpdateClearButtonVisibility()
        {
            ClearSearchButton.Visibility = string.IsNullOrEmpty(SearchTextBox.Text)
                ? Visibility.Collapsed
                : Visibility.Visible;
        }

        private void UpdatePlaceholderVisibility()
        {
            if (string.IsNullOrEmpty(SearchTextBox.Text) && !SearchTextBox.IsKeyboardFocusWithin)
            {
                SearchPlaceholder.Visibility = Visibility.Visible;
            }
            else
            {
                SearchPlaceholder.Visibility = Visibility.Collapsed;
            }
        }

        private T FindVisualParent<T>(DependencyObject child) where T : DependencyObject
        {
            DependencyObject current = System.Windows.Media.VisualTreeHelper.GetParent(child);
            while (current != null)
            {
                if (current is T found)
                    return found;
                current = System.Windows.Media.VisualTreeHelper.GetParent(current);
            }
            return null;
        }

        private void RootGrid_PreviewMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (FiltersExpander == null || !FiltersExpander.IsExpanded)
                return;

            // Проверяем, был ли клик внутри Expander с фильтрами
            var source = e.OriginalSource as DependencyObject;
            if (source != null && IsChildOf(source, FiltersExpander))
                return; // Клик внутри фильтров — не сворачиваем

            // Клик вне фильтров — сворачиваем
            FiltersExpander.IsExpanded = false;
        }

        private bool IsChildOf(DependencyObject child, DependencyObject parent)
        {
            DependencyObject current = child;
            while (current != null)
            {
                if (current == parent)
                    return true;
                current = System.Windows.Media.VisualTreeHelper.GetParent(current);
            }
            return false;
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            e.Cancel = true;
            this.Hide();
            if (TrayIcon != null)
                TrayIcon.Visibility = Visibility.Visible;
            base.OnClosing(e);
        }

        private async void ExitMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (_sessionOrchestrator.IsRunning)
            {
                try { await _sessionOrchestrator.StopAsync(); }
                catch (Exception ex) { DebugLogger.WriteException("StopAsync on exit failed", ex); }
            }
            TrayIcon.Dispose();
            UnhookTerminalRightClick();
            Application.Current.Shutdown();
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