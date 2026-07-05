using EasyWindowsTerminalControl;
using PlatformLauncher.Domain.Models;
using System;
using System.Windows.Media;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;

namespace PlatformLauncher.Presentation.Services
{
    /// <summary>
    /// Настраивает тему терминала (EasyWindowsTerminalControl).
    /// Отвечает только за TerminalTheme и ColorTable.
    /// </summary>
    public class TerminalThemeApplier
    {
        private EasyTerminalControl _terminal;

        public void Attach(EasyTerminalControl terminal)
        {
            _terminal = terminal;
        }

        public void ApplyTheme(ThemeItem theme)
        {
            if (_terminal == null || theme == null) return;

            try
            {
                var terminalTheme = new Microsoft.Terminal.Wpf.TerminalTheme();
                var bgColor = ParseColor(theme.ControlBackground, "#2A2A3E");
                var fgColor = ParseColor(theme.ControlForeground, "#CDD6F4");

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
                DebugLogger.WriteException("TerminalThemeApplier.ApplyTheme", ex);
            }
        }

        private uint[] GenerateColorTable(ThemeItem theme)
        {
            var bg = ParseColor(theme.Background, "#1A1A2E");
            var fg = ParseColor(theme.Foreground, "#CDD6F4");
            var accent = ParseColor(theme.Accent, "#89B4FA");

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