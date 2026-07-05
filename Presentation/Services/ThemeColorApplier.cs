using PlatformLauncher.Domain.Models;
using System;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;

namespace PlatformLauncher.Presentation.Services
{
    /// <summary>
    /// Применяет цвета темы к ресурсам приложения (App.Current.Resources).
    /// Отвечает только за цветовые кисти — не трогает терминал и HandyControl.
    /// </summary>
    public class ThemeColorApplier
    {
        private ResourceDictionary _customColorDict;

        public void ApplyColors(ThemeItem theme)
        {
            if (_customColorDict == null)
            {
                _customColorDict = new ResourceDictionary();
                Application.Current.Resources.MergedDictionaries.Insert(0, _customColorDict);
            }

            var bgColor = ParseColor(theme.Background, "#1A1A2E");
            var fgColor = ParseColor(theme.Foreground, "#CDD6F4");
            var accentColor = ParseColor(theme.Accent, "#89B4FA");
            var ctrlBgColor = ParseColor(theme.ControlBackground, "#2A2A3E");
            var ctrlFgColor = ParseColor(theme.ControlForeground, "#CDD6F4");
            var borderColor = ParseColor(theme.BorderBrush, "#454560");
            var hoverColor = ParseColor(theme.HoverBrush, borderColor.ToString());
            var selectedColor = ParseColor(theme.SelectedBrush, accentColor.ToString());
            var disabledColor = ParseColor(theme.DisabledBrush, "#808080");
            var disabledFgColor = ParseColor(theme.DisabledForeground, "#A0A0A0");
            var inputBgColor = ParseColor(theme.InputBackground, ctrlBgColor.ToString());
            var inputFgColor = ParseColor(theme.InputForeground, fgColor.ToString());
            var inputBorderColor = ParseColor(theme.InputBorderBrush, borderColor.ToString());
            var errorColor = ParseColor(theme.ErrorBrush, "#DC3545");
            var warningColor = ParseColor(theme.WarningBrush, "#FFC107");
            var successColor = ParseColor(theme.SuccessBrush, "#28A745");
            var separatorColor = ParseColor(theme.SeparatorBrush, borderColor.ToString());
            var overlayColor = ParseColor(theme.OverlayColor, "#80000000");

            // Основные кисти
            SetBrush("RegionBrush", bgColor);
            SetBrush("PrimaryTextBrush", fgColor);
            SetBrush("SecondaryRegionBrush", ctrlBgColor);
            SetBrush("PrimaryBrush", accentColor);
            SetBrush("BorderBrush", borderColor);
            SetBrush("MainBackground", bgColor);
            SetBrush("MainForeground", fgColor);
            SetBrush("AccentBrush", accentColor);
            SetBrush("ControlBackground", ctrlBgColor);
            SetBrush("ControlForeground", ctrlFgColor);
            SetBrush("ScrollBarBackground", ParseColor(theme.ScrollBarBackground, ctrlBgColor.ToString()));
            SetBrush("ScrollBarForeground", ParseColor(theme.ScrollBarForeground, accentColor.ToString()));
            SetBrush("ThirdlyTextBrush", disabledFgColor);
            SetBrush("HoverBrush", hoverColor);
            SetBrush("SelectedBrush", selectedColor);
            SetBrush("InputBackground", inputBgColor);
            SetBrush("InputForeground", inputFgColor);
            SetBrush("InputBorderBrush", inputBorderColor);
            SetBrush("SeparatorBrush", separatorColor);
            SetBrush("DisabledForeground", disabledFgColor);

            // Специальные кисти
            SetBrush("ErrorBrush", errorColor);
            SetBrush("WarningBrush", warningColor);
            SetBrush("SuccessBrush", successColor);
            SetBrush("OverlayColor", overlayColor);
            SetBrush("DisabledBrush", disabledColor);

            // HandyControl-совместимые
            SetBrush("BackgroundColor", bgColor);
            SetBrush("RegionColor", ctrlBgColor);
            SetBrush("SecondaryRegionColor", ctrlBgColor);
            SetBrush("ThirdlyRegionColor", ctrlBgColor);
            SetBrush("PrimaryColor", accentColor);
            SetBrush("SecondaryTextBrush", disabledFgColor);
            SetBrush("TextIconBrush", fgColor);
            SetBrush("BorderColor", borderColor);
            SetBrush("SecondaryBorderBrush", borderColor);
            SetBrush("DarkPrimaryBrush", accentColor);
            SetBrush("LightPrimaryBrush", bgColor);
            SetBrush("DarkDefaultBrush", bgColor);
            SetBrush("DefaultBrush", ctrlBgColor);

            // Системные цвета
            try
            {
                Application.Current.Resources[SystemColors.GrayTextBrushKey] = new SolidColorBrush(disabledFgColor);
            }
            catch { }

            // КРИТИЧНО: Прямая замена ресурсов в Application.Current.Resources
            // Это необходимо, потому что в App.xaml дефолтные цвета определены в основном ResourceDictionary,
            // а не в MergedDictionaries. DynamicResource ищет сначала в основном ResourceDictionary.
            var resources = Application.Current.Resources;
            foreach (var key in _customColorDict.Keys.Cast<string>().ToList())
            {
                if (resources.Contains(key))
                    resources[key] = _customColorDict[key];
                else
                    resources.Add(key, _customColorDict[key]);
            }
        }

        private void SetBrush(string key, Color color)
        {
            _customColorDict[key] = new SolidColorBrush(color);
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
    }
}