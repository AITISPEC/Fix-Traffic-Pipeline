using EasyWindowsTerminalControl;
using HandyControl.Themes;
using Microsoft.Extensions.DependencyInjection;
using PlatformLauncher.Core.Interfaces;
using PlatformLauncher.Domain.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;

namespace PlatformLauncher.Presentation.Services
{
    public class ThemeApplier(IServiceProvider serviceProvider)
    {
        private readonly IServiceProvider _serviceProvider = serviceProvider;
        private ResourceDictionary? _customColorDict;
        private EasyTerminalControl? _terminal;
        private Window? _window;
        private ThemeItem? _currentTheme;

        public void Attach(EasyTerminalControl terminal, Window window)
        {
            _terminal = terminal;
            _window = window;
        }

        public void ApplyTheme(string themeId, IEnumerable<ThemeItem> allThemes)
        {
            var theme = allThemes.FirstOrDefault(t => t.Id == themeId);
            if (theme == null) return;

            _currentTheme = theme;
            DebugLogger.Info($"Theme applied: {themeId}");

            try
            {
                var appConfigService = _serviceProvider.GetRequiredService<IAppConfigService>();
                var config = appConfigService.Load();
                if (config.Terminal != null && config.Terminal.Theme != theme.TerminalTheme)
                {
                    config.Terminal.Theme = theme.TerminalTheme;
                    appConfigService.Save(config);
                    DebugLogger.Info($"terminal.theme updated: {theme.TerminalTheme}");
                }
            }
            catch (Exception ex)
            {
                DebugLogger.WriteException("Sync terminal.theme", ex);
            }

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

            ApplyCustomColors(theme);
            SetTerminalTheme(theme);

            _window?.Dispatcher.BeginInvoke(
                DispatcherPriority.Background,
                new Action(ApplyTerminalScrollBarStyle));

            _window?.UpdateLayout();

            if (Application.Current?.MainWindow != null && _customColorDict != null)
            {
                var resources = Application.Current.Resources;
                foreach (var key in _customColorDict.Keys.Cast<string>().ToList())
                {
                    if (resources.Contains(key))
                        resources[key] = _customColorDict[key];
                }

                foreach (Window window in Application.Current.Windows)
                {
                    window.Dispatcher.BeginInvoke(
                        DispatcherPriority.Background,
                        new Action(() =>
                        {
                            window.UpdateLayout();
                            window.InvalidateVisual();
                        }));
                }

                // Принудительное переприменение стилей ContextMenu терминала
                if (_terminal?.ContextMenu != null)
                {
                    _window?.Dispatcher.BeginInvoke(
                        DispatcherPriority.Background,
                        new Action(() =>
                        {
                            var menu = _terminal.ContextMenu;

                            // Ищем стили в ресурсах терминала (там они определены)
                            var menuStyle = _terminal.TryFindResource(typeof(ContextMenu)) as Style;
                            var itemStyle = _terminal.TryFindResource(typeof(MenuItem)) as Style;

                            // Переприменяем стиль самого меню
                            if (menuStyle != null)
                            {
                                menu.Style = null;
                                menu.Style = menuStyle;
                            }

                            // Переприменяем стили для каждого MenuItem
                            foreach (var item in menu.Items.OfType<MenuItem>())
                            {
                                if (itemStyle != null)
                                {
                                    item.Style = null;
                                    item.Style = itemStyle;
                                }
                            }

                            menu.UpdateLayout();
                            menu.InvalidateVisual();
                        }));
                }
            }
        }

        private void ApplyCustomColors(ThemeItem theme)
        {
            if (_customColorDict == null)
            {
                _customColorDict = new ResourceDictionary();
                Application.Current.Resources.MergedDictionaries.Insert(0, _customColorDict);
            }

            var bgColor = (Color)ColorConverter.ConvertFromString(theme.Background);
            var fgColor = (Color)ColorConverter.ConvertFromString(theme.Foreground);
            var accentColor = (Color)ColorConverter.ConvertFromString(theme.Accent);
            var ctrlBgColor = (Color)ColorConverter.ConvertFromString(theme.ControlBackground);
            var ctrlFgColor = (Color)ColorConverter.ConvertFromString(theme.ControlForeground);
            var borderColor = (Color)ColorConverter.ConvertFromString(theme.BorderBrush);
            var hoverColor = !string.IsNullOrEmpty(theme.HoverBrush)
                ? (Color)ColorConverter.ConvertFromString(theme.HoverBrush) : borderColor;
            var selectedColor = !string.IsNullOrEmpty(theme.SelectedBrush)
                ? (Color)ColorConverter.ConvertFromString(theme.SelectedBrush) : accentColor;
            var disabledColor = !string.IsNullOrEmpty(theme.DisabledBrush)
                ? (Color)ColorConverter.ConvertFromString(theme.DisabledBrush)
                : Color.FromArgb(255, 128, 128, 128);
            var disabledFgColor = !string.IsNullOrEmpty(theme.DisabledForeground)
                ? (Color)ColorConverter.ConvertFromString(theme.DisabledForeground)
                : Color.FromArgb(255, 160, 160, 160);
            var inputBgColor = !string.IsNullOrEmpty(theme.InputBackground)
                ? (Color)ColorConverter.ConvertFromString(theme.InputBackground) : ctrlBgColor;
            var inputFgColor = !string.IsNullOrEmpty(theme.InputForeground)
                ? (Color)ColorConverter.ConvertFromString(theme.InputForeground) : fgColor;
            var inputBorderColor = !string.IsNullOrEmpty(theme.InputBorderBrush)
                ? (Color)ColorConverter.ConvertFromString(theme.InputBorderBrush) : borderColor;
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
                ? (Color)ColorConverter.ConvertFromString(theme.SeparatorBrush) : borderColor;
            var overlayColor = !string.IsNullOrEmpty(theme.OverlayColor)
                ? (Color)ColorConverter.ConvertFromString(theme.OverlayColor)
                : Color.FromArgb(128, 0, 0, 0);

            _customColorDict["BackgroundColor"] = new SolidColorBrush(bgColor);
            _customColorDict["RegionColor"] = new SolidColorBrush(ctrlBgColor);
            _customColorDict["SecondaryRegionColor"] = new SolidColorBrush(ctrlBgColor);
            _customColorDict["ThirdlyRegionColor"] = new SolidColorBrush(ctrlBgColor);
            _customColorDict["PrimaryColor"] = accentColor;
            _customColorDict["PrimaryBrush"] = new SolidColorBrush(accentColor);
            _customColorDict["PrimaryTextBrush"] = new SolidColorBrush(fgColor);
            _customColorDict["SecondaryTextBrush"] = new SolidColorBrush(ctrlFgColor);
            _customColorDict["ThirdlyTextBrush"] = new SolidColorBrush(disabledFgColor);
            _customColorDict["TextIconBrush"] = new SolidColorBrush(fgColor);
            _customColorDict["BorderColor"] = borderColor;
            _customColorDict["SecondaryBorderBrush"] = new SolidColorBrush(borderColor);
            _customColorDict["DarkMaskColor"] = Color.FromArgb(32, 0, 0, 0);
            _customColorDict["DarkOpacityColor"] = Color.FromArgb(64, 0, 0, 0);
            _customColorDict["HoverBrush"] = new SolidColorBrush(hoverColor);
            _customColorDict["SelectedBrush"] = new SolidColorBrush(selectedColor);
            _customColorDict["DisabledBrush"] = new SolidColorBrush(disabledColor);
            _customColorDict["DisabledForeground"] = new SolidColorBrush(disabledFgColor);
            _customColorDict["InputBackground"] = new SolidColorBrush(inputBgColor);
            _customColorDict["InputForeground"] = new SolidColorBrush(inputFgColor);
            _customColorDict["InputBorderBrush"] = new SolidColorBrush(inputBorderColor);
            _customColorDict["ErrorBrush"] = new SolidColorBrush(errorColor);
            _customColorDict["WarningBrush"] = new SolidColorBrush(warningColor);
            _customColorDict["SuccessBrush"] = new SolidColorBrush(successColor);
            _customColorDict["SeparatorBrush"] = new SolidColorBrush(separatorColor);
            _customColorDict["OverlayColor"] = new SolidColorBrush(overlayColor);
            _customColorDict["RegionBrush"] = new SolidColorBrush(bgColor);
            _customColorDict["SecondaryRegionBrush"] = new SolidColorBrush(ctrlBgColor);
            _customColorDict["ThirdlyRegionBrush"] = new SolidColorBrush(ctrlBgColor);
            _customColorDict["DarkPrimaryBrush"] = new SolidColorBrush(accentColor);
            _customColorDict["LightPrimaryBrush"] = new SolidColorBrush(bgColor);
            _customColorDict["DarkDefaultBrush"] = new SolidColorBrush(bgColor);
            _customColorDict["DefaultBrush"] = new SolidColorBrush(ctrlBgColor);
            _customColorDict["MainBackground"] = new SolidColorBrush(bgColor);
            _customColorDict["MainForeground"] = new SolidColorBrush(fgColor);
            _customColorDict["ControlBackground"] = new SolidColorBrush(ctrlBgColor);
            _customColorDict["ControlForeground"] = new SolidColorBrush(ctrlFgColor);

            // Принудительная замена всех возможных ресурсов для disabled
            Application.Current.Resources["{x:Static SystemColors.GrayTextBrushKey}"] = new SolidColorBrush(disabledFgColor);
            Application.Current.Resources[SystemColors.GrayTextBrushKey] = new SolidColorBrush(disabledFgColor);

            // Для HandyControl
            _customColorDict["SecondaryTextBrush"] = new SolidColorBrush(disabledFgColor);

            try
            {
                if (ThemeManager.Current != null)
                {
                    ThemeManager.Current.AccentColor = new SolidColorBrush(accentColor);
                }
            }
            catch (Exception ex)
            {
                DebugLogger.WriteException("ThemeManager.AccentColor", ex);
            }
        }

        private void SetTerminalTheme(ThemeItem theme)
        {
            if (_terminal == null) return;

            var terminalTheme = new Microsoft.Terminal.Wpf.TerminalTheme();
            try
            {
                var bgColor = (Color)ColorConverter.ConvertFromString(theme.ControlBackground);
                var fgColor = (Color)ColorConverter.ConvertFromString(theme.ControlForeground);

                terminalTheme.DefaultBackground = EasyTerminalControl.ColorToVal(bgColor);
                terminalTheme.DefaultForeground = EasyTerminalControl.ColorToVal(fgColor);
                terminalTheme.DefaultSelectionBackground = 0xcccccc;
                terminalTheme.CursorStyle = Microsoft.Terminal.Wpf.CursorStyle.BlinkingBar;
                terminalTheme.ColorTable = GenerateColorTable(theme);

                _terminal.Theme = terminalTheme;
                _terminal.InvalidateVisual();
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

            uint[] table = {
                0x0C0C0C, 0x1F0FC5, 0x0EA113, 0x009CC1, 0xDA3700, 0x981788, 0xDD963A, 0xCCCCCC,
                0x767676, 0x5648E7, 0x0CC616, 0xA5F1F9, 0xFF783B, 0x9E00B4, 0xD6D661, 0xF2F2F2
            };

            table[0] = ToUint(bg);
            table[7] = ToUint(fg);
            table[2] = ToUint(accent);
            table[8] = ToUint(bg);
            table[15] = ToUint(fg);
            table[10] = ToUint(accent);

            return table;
        }

        public void ApplyTerminalScrollBarStyle()
        {
            if (_terminal?.Terminal == null || _currentTheme == null) return;

            var scrollbars = FindVisualChildren<ScrollBar>(_terminal.Terminal);
            if (scrollbars.Count == 0)
            {
                DebugLogger.Warn("ScrollBar not found in terminal");
                return;
            }

            var scrollBg = (Color)ColorConverter.ConvertFromString(_currentTheme.ScrollBarBackground ?? _currentTheme.ControlBackground);
            var scrollFg = (Color)ColorConverter.ConvertFromString(_currentTheme.ScrollBarForeground ?? _currentTheme.Accent);

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

            var arrowStyle = _window?.FindResource("ScrollBarArrowButton") as Style;

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
                        track.Thumb.Template = thumbTemplate;
                        track.Thumb.Background = new SolidColorBrush(scrollFg);
                    }

                    var pageButtons = FindVisualChildren<RepeatButton>(track);
                    foreach (var btn in pageButtons)
                    {
                        btn.Background = new SolidColorBrush(scrollBg);
                        btn.Foreground = new SolidColorBrush(scrollFg);
                    }
                }

                var arrowButtons = FindVisualChildren<RepeatButton>(scrollbar);
                foreach (var btn in arrowButtons)
                {
                    if (btn.Command == ScrollBar.LineUpCommand || btn.Command == ScrollBar.LineDownCommand)
                    {
                        if (arrowStyle != null)
                            btn.Style = arrowStyle;
                        btn.Background = new SolidColorBrush(scrollBg);
                        btn.Foreground = new SolidColorBrush(scrollFg);

                        string geometry = btn.Command == ScrollBar.LineUpCommand
                            ? "M 0 4 L 4 0 L 8 4 Z"
                            : "M 0 0 L 4 4 L 8 0 Z";
                        btn.Tag = geometry;
                    }
                }
            }
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

        public static List<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
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
    }
}