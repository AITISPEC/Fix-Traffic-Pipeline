using System.Collections.Generic;
using PlatformLauncher.Domain.Models;

namespace PlatformLauncher.Core.Interfaces
{
    public interface IThemeService
    {
        void Attach(EasyWindowsTerminalControl.EasyTerminalControl terminal, System.Windows.Window window);
        void ApplyTheme(string themeId, IEnumerable<ThemeItem> allThemes);
        void ApplyTerminalScrollBarStyle();
    }
}