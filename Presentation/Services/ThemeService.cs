using EasyWindowsTerminalControl;
using PlatformLauncher.Core.Interfaces;
using PlatformLauncher.Domain.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Threading;

namespace PlatformLauncher.Presentation.Services
{
    /// <summary>
    /// Фасад для применения тем. Координирует работу всех тематических сервисов.
    /// </summary>
    public class ThemeService : IThemeService
    {
        private readonly ThemeColorApplier _colorApplier;
        private readonly TerminalThemeApplier _terminalThemeApplier;
        private readonly HandyControlThemeManager _hcThemeManager;
        private readonly TerminalScrollBarStyler _scrollBarStyler;
        private readonly IAppConfigService _appConfigService;
        private ThemeItem _currentTheme;
        private Window _window;

        public ThemeService(
            ThemeColorApplier colorApplier,
            TerminalThemeApplier terminalThemeApplier,
            HandyControlThemeManager hcThemeManager,
            TerminalScrollBarStyler scrollBarStyler,
            IAppConfigService appConfigService)
        {
            _colorApplier = colorApplier;
            _terminalThemeApplier = terminalThemeApplier;
            _hcThemeManager = hcThemeManager;
            _scrollBarStyler = scrollBarStyler;
            _appConfigService = appConfigService;
        }

        public void Attach(EasyTerminalControl terminal, Window window)
        {
            _terminalThemeApplier.Attach(terminal);
            _scrollBarStyler.Attach(terminal, window);
            _window = window;
        }

        public void ApplyTheme(string themeId, IEnumerable<ThemeItem> allThemes)
        {
            var theme = allThemes.FirstOrDefault(t => t.Id == themeId);
            if (theme == null) return;

            _currentTheme = theme;
            DebugLogger.Info($"Theme applied: {themeId}");

            // Синхронизируем terminal.theme в app_config.yaml
            try
            {
                var config = _appConfigService.Load();
                if (config.Terminal != null && config.Terminal.Theme != theme.TerminalTheme)
                {
                    config.Terminal.Theme = theme.TerminalTheme;
                    _appConfigService.Save(config);
                    DebugLogger.Info($"terminal.theme updated: {theme.TerminalTheme}");
                }
            }
            catch (Exception ex)
            {
                DebugLogger.WriteException("Sync terminal.theme", ex);
            }

            // Применяем тему HandyControl
            _hcThemeManager.SetTheme(theme.TerminalTheme);

            // Применяем цвета
            _colorApplier.ApplyColors(theme);

            // Применяем тему терминала
            _terminalThemeApplier.ApplyTheme(theme);

            // Стилізуем скроллбары (с задержкой, чтобы терминал успел отрисоваться)
            _scrollBarStyler.SetCurrentTheme(theme);
            _window?.Dispatcher.BeginInvoke(
                DispatcherPriority.Background,
                new Action(() =>
                {
                    _scrollBarStyler.ApplyStyle();
                    _window?.UpdateLayout();
                }));

            // Принудительное обновление всех окон
            if (Application.Current?.MainWindow != null)
            {
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
            }
        }

        public void ApplyTerminalScrollBarStyle()
        {
            _scrollBarStyler.ApplyStyle();
        }
    }
}