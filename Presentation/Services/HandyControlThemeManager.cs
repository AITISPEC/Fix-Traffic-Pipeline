using HandyControl.Themes;
using System;
using System.Linq;
using System.Windows;

namespace PlatformLauncher.Presentation.Services
{
    /// <summary>
    /// Управляет темами HandyControl (SkinDark/SkinLight).
    /// </summary>
    public class HandyControlThemeManager
    {
        public void SetTheme(string terminalTheme)
        {
            try
            {
                var targetTheme = terminalTheme.Equals("Dark", StringComparison.OrdinalIgnoreCase)
                    ? ApplicationTheme.Dark
                    : ApplicationTheme.Light;

                if (ThemeManager.Current != null)
                {
                    ThemeManager.Current.ApplicationTheme = targetTheme;
                }
                else
                {
                    SetThemeManual(terminalTheme);
                }
            }
            catch
            {
                SetThemeManual(terminalTheme);
            }
        }

        private void SetThemeManual(string themeName)
        {
            var skinName = themeName.Equals("Dark", StringComparison.OrdinalIgnoreCase) ? "SkinDark" : "SkinLight";

            var toRemove = Application.Current.Resources.MergedDictionaries
                .Where(d => d.Source != null && d.Source.OriginalString.Contains("HandyControl"))
                .ToList();

            foreach (var dict in toRemove)
                Application.Current.Resources.MergedDictionaries.Remove(dict);

            Application.Current.Resources.MergedDictionaries.Add(
                new ResourceDictionary { Source = new Uri($"pack://application:,,,/HandyControl;component/Themes/{skinName}.xaml") });
            Application.Current.Resources.MergedDictionaries.Add(
                new ResourceDictionary { Source = new Uri("pack://application:,,,/HandyControl;component/Themes/Theme.xaml") });
        }
    }
}