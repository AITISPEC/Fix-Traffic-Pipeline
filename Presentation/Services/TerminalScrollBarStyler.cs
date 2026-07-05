using EasyWindowsTerminalControl;
using PlatformLauncher.Domain.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Xml;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;

namespace PlatformLauncher.Presentation.Services
{
    /// <summary>
    /// Стилизует скроллбары терминала под текущую тему.
    /// </summary>
    public class TerminalScrollBarStyler
    {
        private EasyTerminalControl _terminal;
        private Window _window;
        private ThemeItem _currentTheme;

        public void Attach(EasyTerminalControl terminal, Window window)
        {
            _terminal = terminal;
            _window = window;
        }

        public void SetCurrentTheme(ThemeItem theme)
        {
            _currentTheme = theme;
        }

        public void ApplyStyle()
        {
            if (_terminal?.Terminal == null || _currentTheme == null) return;

            var scrollbars = FindVisualChildren<ScrollBar>(_terminal.Terminal);
            if (scrollbars.Count == 0)
            {
                DebugLogger.Warn("ScrollBar not found in terminal");
                return;
            }

            var scrollBg = ParseColor(_currentTheme.ScrollBarBackground ?? _currentTheme.ControlBackground, "#2A2A3E");
            var scrollFg = ParseColor(_currentTheme.ScrollBarForeground ?? _currentTheme.Accent, "#89B4FA");

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
                using (var reader = new StringReader(thumbTemplateXaml))
                using (var xmlReader = XmlReader.Create(reader))
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

        private Color ParseColor(string hex, string fallback)
        {
            try
            {
                if (string.IsNullOrEmpty(hex))
                    return (Color)ColorConverter.ConvertFromString(fallback);
                return (Color)ColorConverter.ConvertFromString(hex);
            }
            catch
            {
                return (Color)ColorConverter.ConvertFromString(fallback);
            }
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