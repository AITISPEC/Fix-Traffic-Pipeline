using EasyWindowsTerminalControl;
using HandyControl.Themes;
using Microsoft.Extensions.DependencyInjection;
using PlatformLauncher.Core.Interfaces;
using PlatformLauncher.Domain.Models;
using PlatformLauncher.Presentation.Services;
using PlatformLauncher.Presentation.ViewModels;
using System;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;

namespace PlatformLauncher.Presentation.Views
{
    public partial class MainWindow : System.Windows.Window
    {
        private readonly IServiceProvider _serviceProvider;
        private ISessionOrchestrator _sessionOrchestrator;
        private ServiceTabViewModel _serviceTabViewModel;
        private readonly ITerminalOutput _terminal;
        private ResourceDictionary _customColorDict;
        private delegate IntPtr SubClassProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, IntPtr uIdSubclass, IntPtr dwRefData);
        private IntPtr _terminalHwnd;
        private SubClassProcDelegate _subclassDelegate;
        private const uint SUBCLASS_ID = 1;
        private const uint WM_RBUTTONUP = 0x0205;

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

            _serviceTabViewModel = _serviceProvider.GetRequiredService<ServiceTabViewModel>();
            _serviceTabViewModel.ThemeChanged += ApplyTheme;
            _serviceTabViewModel.ListsPathChanged += (newPath) =>
            {
                if (DataContext is MainViewModel mainVm)
                {
                    mainVm.ListsPath = newPath;
                }
            };
            ServiceTabControl.SetViewModel(_serviceTabViewModel);
            _sessionOrchestrator = _serviceProvider.GetRequiredService<ISessionOrchestrator>();
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
                ApplyTheme(themeToApply.Id);
            }
            viewModel.ClearConsole();
        }

        private void HookTerminalRightClick()
        {
            UnhookTerminalRightClick();

            var hwndHosts = FindVisualChildren<System.Windows.Interop.HwndHost>(ConsoleOutputTerminal.Terminal);
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
                new Action(ApplyTerminalScrollBarStyle));
        }

        private void ApplyTerminalScrollBarStyle()
        {
            if (ConsoleOutputTerminal?.Terminal == null) return;

            var scrollbars = FindVisualChildren<ScrollBar>(ConsoleOutputTerminal.Terminal);
            if (scrollbars.Count == 0)
            {
                DebugLogger.Write("ScrollBar NOT FOUND");
                return;
            }

            var theme = _serviceTabViewModel?.SelectedTheme;
            if (theme == null) return;

            var scrollBg = (Color)ColorConverter.ConvertFromString(theme.ScrollBarBackground ?? theme.ControlBackground);
            var scrollFg = (Color)ColorConverter.ConvertFromString(theme.ScrollBarForeground ?? theme.Accent);

            // Создаём НОВЫЙ Template для Thumb через XAML
            string thumbTemplateXaml = $@"
        <ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' 
                         xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' 
                         TargetType='{{x:Type Thumb}}'>
            <Border Background='{scrollFg}' 
                    CornerRadius='2' 
                    Margin='2,4,2,4'/>
        </ControlTemplate>";

            ControlTemplate thumbTemplate;
            try
            {
                using (var reader = new System.IO.StringReader(thumbTemplateXaml))
                using (var xmlReader = System.Xml.XmlReader.Create(reader))
                {
                    thumbTemplate = (ControlTemplate)System.Windows.Markup.XamlReader.Load(xmlReader);
                }
            }
            catch (Exception ex)
            {
                DebugLogger.WriteException("Failed to create Thumb template", ex);
                return;
            }

            foreach (var scrollbar in scrollbars)
            {
                scrollbar.Background = new SolidColorBrush(scrollBg);
                scrollbar.Foreground = new SolidColorBrush(scrollFg);
                scrollbar.Width = 10;
                scrollbar.MinWidth = 10;

                var tracks = FindVisualChildren<Track>(scrollbar);
                foreach (var track in tracks)
                {
                    if (track.Thumb != null)
                    {
                        // ПРИМЕНЯЕМ НОВЫЙ Template — это полностью заменит старый
                        track.Thumb.Template = thumbTemplate;
                        track.Thumb.Background = new SolidColorBrush(scrollFg);

                        DebugLogger.Write($"Thumb template applied, BG={scrollFg}");
                    }

                    var buttons = FindVisualChildren<RepeatButton>(track);
                    foreach (var btn in buttons)
                    {
                        btn.Background = new SolidColorBrush(scrollBg);
                        btn.Foreground = new SolidColorBrush(scrollFg);
                    }
                }
            }

            DebugLogger.Write($"ScrollBar styled: BG={scrollBg}, FG={scrollFg}");
        }

        // Оставляем ТОЛЬКО этот метод поиска (без перегрузок)
        private List<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
        {
            var children = new List<T>();
            if (parent == null) return children;

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T found)
                    children.Add(found);

                children.AddRange(FindVisualChildren<T>(child));
            }
            return children;
        }

        private void SetHandyControlThemeManual(string themeName)
        {
            var skinName = themeName.Equals("Dark", StringComparison.OrdinalIgnoreCase) ? "SkinDark" : "SkinLight";
            var toRemove = Application.Current.Resources.MergedDictionaries
                .Where(d => d.Source != null && d.Source.OriginalString.Contains("HandyControl")).ToList();
            foreach (var dict in toRemove)
                Application.Current.Resources.MergedDictionaries.Remove(dict);

            Application.Current.Resources.MergedDictionaries.Add(
                new ResourceDictionary { Source = new Uri($"pack://application:,,,/HandyControl;component/Themes/{skinName}.xaml") });
            Application.Current.Resources.MergedDictionaries.Add(
                new ResourceDictionary { Source = new Uri("pack://application:,,,/HandyControl;component/Themes/Theme.xaml") });
        }

        public void ApplyTheme(string themeId)
        {
            var theme = _serviceTabViewModel?.AllThemes.FirstOrDefault(t => t.Id == themeId); 
            if (theme == null) return;

            DebugLogger.Write($"ApplyTheme: {themeId}");

            try
            {
                var appConfigService = _serviceProvider.GetRequiredService<IAppConfigService>();
                var config = appConfigService.Load();
                if (config.Terminal != null && config.Terminal.Theme != theme.TerminalTheme)
                {
                    config.Terminal.Theme = theme.TerminalTheme; // "Light" или "Dark"
                    appConfigService.Save(config);
                    DebugLogger.Write($"terminal.theme обновлён на {theme.TerminalTheme}");
                }
            }
            catch (Exception ex)
            {
                DebugLogger.WriteException("Sync terminal.theme", ex);
            }

            // 1. СНАЧАЛА применяем HandyControl тему (Light/Dark)
            try
            {
                var targetTheme = theme.TerminalTheme.Equals("Dark", StringComparison.OrdinalIgnoreCase)
                    ? ApplicationTheme.Dark
                    : ApplicationTheme.Light;

                if (ThemeManager.Current != null)
                    ThemeManager.Current.ApplicationTheme = targetTheme;
                else
                    SetHandyControlThemeManual(theme.TerminalTheme);
            }
            catch
            {
                SetHandyControlThemeManual(theme.TerminalTheme);
            }

            // 2. ПОТОМ кастомные цвета (чтобы перекрыли HandyControl)
            ApplyCustomColors(theme);

            // 3. Терминал
            SetTerminalTheme(theme);

            // 4. Скроллбар
            Dispatcher.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.Background,
                new Action(ApplyTerminalScrollBarStyle));

            // 5. Принудительное обновление всего UI
            Application.Current.MainWindow?.UpdateLayout();

            // 6. Принудительно обновить ВСЕ ресурсы
            if (Application.Current.MainWindow is MainWindow mw)
            {
                // Перезагружаем все стили
                var resources = Application.Current.Resources;

                // Принудительно обновляем кисти
                foreach (var key in _customColorDict.Keys.Cast<string>().ToList())
                {
                    if (resources.Contains(key))
                    {
                        resources[key] = _customColorDict[key];
                    }
                }

                // Обновляем все окна
                foreach (Window window in Application.Current.Windows)
                {
                    window.Dispatcher.BeginInvoke(
                        System.Windows.Threading.DispatcherPriority.Background,
                        new Action(() =>
                        {
                            window.UpdateLayout();
                            window.InvalidateVisual();
                        })
                    );
                }
            }
        }

        private void ApplyCustomColors(ThemeItem theme)
        {
            DebugLogger.Write($"ApplyCustomColors START: {theme.Id}");

            if (_customColorDict != null)
            {
                Application.Current.Resources.MergedDictionaries.Remove(_customColorDict);
                DebugLogger.Write("Старый словарь удалён");
            }

            _customColorDict = new ResourceDictionary();

            var bgColor = (Color)ColorConverter.ConvertFromString(theme.Background);
            var fgColor = (Color)ColorConverter.ConvertFromString(theme.Foreground);
            var accentColor = (Color)ColorConverter.ConvertFromString(theme.Accent);
            var ctrlBgColor = (Color)ColorConverter.ConvertFromString(theme.ControlBackground);
            var ctrlFgColor = (Color)ColorConverter.ConvertFromString(theme.ControlForeground);
            var borderColor = (Color)ColorConverter.ConvertFromString(theme.BorderBrush);
            var hoverColor = !string.IsNullOrEmpty(theme.HoverBrush)
                ? (Color)ColorConverter.ConvertFromString(theme.HoverBrush)
                : borderColor;
            var selectedColor = !string.IsNullOrEmpty(theme.SelectedBrush)
                ? (Color)ColorConverter.ConvertFromString(theme.SelectedBrush)
                : accentColor;
            var disabledColor = !string.IsNullOrEmpty(theme.DisabledBrush)
                ? (Color)ColorConverter.ConvertFromString(theme.DisabledBrush)
                : Color.FromArgb(255, 128, 128, 128);
            var disabledFgColor = !string.IsNullOrEmpty(theme.DisabledForeground)
                ? (Color)ColorConverter.ConvertFromString(theme.DisabledForeground)
                : Color.FromArgb(255, 160, 160, 160);
            var inputBgColor = !string.IsNullOrEmpty(theme.InputBackground)
                ? (Color)ColorConverter.ConvertFromString(theme.InputBackground)
                : ctrlBgColor;
            var inputFgColor = !string.IsNullOrEmpty(theme.InputForeground)
                ? (Color)ColorConverter.ConvertFromString(theme.InputForeground)
                : fgColor;
            var inputBorderColor = !string.IsNullOrEmpty(theme.InputBorderBrush)
                ? (Color)ColorConverter.ConvertFromString(theme.InputBorderBrush)
                : borderColor;
            var errorColor = !string.IsNullOrEmpty(theme.ErrorBrush)
                ? (Color)ColorConverter.ConvertFromString(theme.ErrorBrush)
                : Color.FromArgb(255, 220, 53, 69);
            var warningColor = !string.IsNullOrEmpty(theme.WarningBrush)
                ? (Color)ColorConverter.ConvertFromString(theme.WarningBrush)
                : Color.FromArgb(255, 255, 193, 7);
            var successColor = !string.IsNullOrEmpty(theme.SuccessBrush)
                ? (Color)ColorConverter.ConvertFromString(theme.SuccessBrush)
                : Color.FromArgb(255, 40, 167, 69);
            var separatorColor = !string.IsNullOrEmpty(theme.SeparatorBrush)
                ? (Color)ColorConverter.ConvertFromString(theme.SeparatorBrush)
                : borderColor;
            var overlayColor = !string.IsNullOrEmpty(theme.OverlayColor)
                ? (Color)ColorConverter.ConvertFromString(theme.OverlayColor)
                : Color.FromArgb(128, 0, 0, 0);

            // === КЛЮЧИ HANDYCONTROL (должны совпадать с внутренними) ===
            _customColorDict.Add("BackgroundColor", new SolidColorBrush(bgColor));
            _customColorDict.Add("RegionColor", new SolidColorBrush(ctrlBgColor));
            _customColorDict.Add("SecondaryRegionColor", new SolidColorBrush(ctrlBgColor));
            _customColorDict.Add("ThirdlyRegionColor", new SolidColorBrush(ctrlBgColor));
            _customColorDict.Add("PrimaryColor", accentColor);
            _customColorDict.Add("PrimaryBrush", new SolidColorBrush(accentColor));
            _customColorDict.Add("PrimaryTextBrush", new SolidColorBrush(fgColor));
            _customColorDict.Add("SecondaryTextBrush", new SolidColorBrush(ctrlFgColor));
            _customColorDict.Add("ThirdlyTextBrush", new SolidColorBrush(ctrlFgColor));
            _customColorDict.Add("TextIconBrush", new SolidColorBrush(fgColor));
            _customColorDict.Add("BorderColor", borderColor);
            _customColorDict.Add("SecondaryBorderBrush", new SolidColorBrush(borderColor));
            _customColorDict.Add("DarkMaskColor", Color.FromArgb(32, 0, 0, 0));
            _customColorDict.Add("DarkOpacityColor", Color.FromArgb(64, 0, 0, 0));

            _customColorDict.Add("HoverBrush", new SolidColorBrush(hoverColor));
            _customColorDict.Add("SelectedBrush", new SolidColorBrush(selectedColor));
            _customColorDict.Add("DisabledBrush", new SolidColorBrush(disabledColor));
            _customColorDict.Add("DisabledForeground", new SolidColorBrush(disabledFgColor));
            _customColorDict.Add("InputBackground", new SolidColorBrush(inputBgColor));
            _customColorDict.Add("InputForeground", new SolidColorBrush(inputFgColor));
            _customColorDict.Add("InputBorderBrush", new SolidColorBrush(inputBorderColor));
            _customColorDict.Add("ErrorBrush", new SolidColorBrush(errorColor));
            _customColorDict.Add("WarningBrush", new SolidColorBrush(warningColor));
            _customColorDict.Add("SuccessBrush", new SolidColorBrush(successColor));
            _customColorDict.Add("SeparatorBrush", new SolidColorBrush(separatorColor));
            _customColorDict.Add("OverlayColor", new SolidColorBrush(overlayColor));
            // Дополнительные ключи
            _customColorDict.Add("RegionBrush", new SolidColorBrush(bgColor));
            _customColorDict.Add("SecondaryRegionBrush", new SolidColorBrush(ctrlBgColor));
            _customColorDict.Add("ThirdlyRegionBrush", new SolidColorBrush(ctrlBgColor));
            _customColorDict.Add("DarkPrimaryBrush", new SolidColorBrush(accentColor));
            _customColorDict.Add("LightPrimaryBrush", new SolidColorBrush(bgColor));
            _customColorDict.Add("DarkDefaultBrush", new SolidColorBrush(bgColor));
            _customColorDict.Add("DefaultBrush", new SolidColorBrush(ctrlBgColor));

            // Акцент через ThemeManager
            try
            {
                if (ThemeManager.Current != null)
                {
                    ThemeManager.Current.AccentColor = new SolidColorBrush(accentColor);
                    DebugLogger.Write($"AccentColor установлен: {accentColor}");
                }
            }
            catch (Exception ex)
            {
                DebugLogger.WriteException("ThemeManager.AccentColor", ex);
            }

            // Вставляем ПЕРЕД HandyControl (индекс 0)
            Application.Current.Resources.MergedDictionaries.Insert(0, _customColorDict);
            DebugLogger.Write($"Словарь добавлен. Всего словарей: {Application.Current.Resources.MergedDictionaries.Count}");
            DebugLogger.Write($"Цвета: BG={theme.Background}, FG={theme.Foreground}, Accent={theme.Accent}");
        }

        public void SetTerminalTheme(ThemeItem theme)
        {
            if (ConsoleOutputTerminal == null) return;

            var terminalTheme = new Microsoft.Terminal.Wpf.TerminalTheme();

            try
            {
                var bgColor = (Color)ColorConverter.ConvertFromString(theme.Background);
                var fgColor = (Color)ColorConverter.ConvertFromString(theme.Foreground);

                terminalTheme.DefaultBackground = EasyTerminalControl.ColorToVal(bgColor);
                terminalTheme.DefaultForeground = EasyTerminalControl.ColorToVal(fgColor);
                terminalTheme.DefaultSelectionBackground = 0xcccccc;
                terminalTheme.CursorStyle = Microsoft.Terminal.Wpf.CursorStyle.BlinkingBar;
                terminalTheme.ColorTable = GenerateColorTable(theme);

                ConsoleOutputTerminal.Theme = terminalTheme;
                ConsoleOutputTerminal.InvalidateVisual();
            }
            catch (Exception ex)
            {
                DebugLogger.WriteException("SetTerminalTheme", ex);
            }
        }

        private uint[] GenerateColorTable(ThemeItem theme)
        {
            var bg = (Color)ColorConverter.ConvertFromString(theme.Background);
            var fg = (Color)ColorConverter.ConvertFromString(theme.Foreground);
            var accent = (Color)ColorConverter.ConvertFromString(theme.Accent);

            uint ToUint(Color c) => (uint)(c.R | (c.G << 8) | (c.B << 16));

            // Базовая палитра Windows Terminal (16 цветов)
            uint[] table = {
                0x0C0C0C, 0x1F0FC5, 0x0EA113, 0x009CC1, 0xDA3700, 0x981788, 0xDD963A, 0xCCCCCC,
                0x767676, 0x5648E7, 0x0CC616, 0xA5F1F9, 0xFF783B, 0x9E00B4, 0xD6D661, 0xF2F2F2
            };

            // Заменяем ключевые цвета на цвета текущей темы
            table[0] = ToUint(bg);     // Black (фон)
            table[7] = ToUint(fg);     // White (основной текст)
            table[2] = ToUint(accent); // Green (акцентный цвет)

            table[8] = ToUint(bg);     // Bright Black
            table[15] = ToUint(fg);     // Bright White
            table[10] = ToUint(accent); // Bright Green

            return table;
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
                try { await _sessionOrchestrator.StopAsync(); } catch { }
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