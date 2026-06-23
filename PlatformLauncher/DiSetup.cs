using Microsoft.Extensions.DependencyInjection;
using PlatformLauncher.Core.Interfaces;
using PlatformLauncher.Core.UseCases;
using PlatformLauncher.Infrastructure.Backup;
using PlatformLauncher.Infrastructure.Configuration;
using PlatformLauncher.Infrastructure.Logging;
using PlatformLauncher.Infrastructure.Network;
using PlatformLauncher.Infrastructure.ProcessManagement;
using PlatformLauncher.Infrastructure.Python;
using PlatformLauncher.Infrastructure.Services;
using PlatformLauncher.Presentation.Services;
using PlatformLauncher.Presentation.ViewModels;
using PlatformLauncher.Presentation.Views;
using System;

namespace PlatformLauncher
{
    public static class DiSetup
    {
        public static IServiceProvider ConfigureServices()
        {
            var services = new ServiceCollection();

            // Логирование
            services.AddSingleton<ILogger, Logger>();

            // Конфигурация
            services.AddSingleton<IAppConfigService, AppConfigService>();
            services.AddSingleton<ISettingsManager, SettingsManager>();

            // Обновление и установка
            services.AddSingleton<IUpdateService, UpdateService>();
            services.AddSingleton<IGameInstallService, GameInstallService>();

            // Python
            services.AddSingleton<IPythonEnvironmentManager, PythonEnvironmentManager>();
            services.AddSingleton<IPythonProcessManager, PythonProcessManager>();

            // WARP
            services.AddSingleton<IWarpManager, WarpManager>();

            // Процессы
            services.AddSingleton<IProcessKiller, ProcessKiller>();

            // Zapret / lists
            services.AddSingleton<IZapretManager, ZapretManager>();
            services.AddSingleton<IWinwsLocator, WinwsLocator>();

            // Backup и Ports – без gameId
            services.AddSingleton<IBackupManager>(provider =>
            {
                var logger = provider.GetRequiredService<ILogger>();
                return new BackupManager(logger, "./backups");
            });
            services.AddSingleton<IPortsManager>(provider =>
            {
                var logger = provider.GetRequiredService<ILogger>();
                return new PortsManager(logger);
            });

            // Session orchestrator – без gameId
            services.AddSingleton<ISessionOrchestrator>(provider =>
            {
                var logger = provider.GetRequiredService<ILogger>();
                var pythonManager = provider.GetRequiredService<IPythonProcessManager>();
                var backupManager = provider.GetRequiredService<IBackupManager>();
                var warpManager = provider.GetRequiredService<IWarpManager>();
                var processKiller = provider.GetRequiredService<IProcessKiller>();
                return new SessionOrchestrator(pythonManager, backupManager, warpManager, processKiller, logger);
            });

            // Терминал
            services.AddSingleton<ITerminalOutput, TerminalOutputAdapter>();

            // UseCases
            services.AddTransient<InstallGameUseCase>();
            services.AddTransient<UninstallGameUseCase>();
            services.AddTransient<SyncPresetsUseCase>();
            services.AddTransient<StartMonitoringUseCase>();
            services.AddTransient<StopMonitoringUseCase>();

            // ViewModels
            services.AddTransient<MainViewModel>();
            services.AddSingleton<ServiceTabViewModel>();

            // Окна и контролы
            services.AddSingleton<MainWindow>();
            services.AddTransient<ServiceTab>();
            services.AddTransient<GamePropertiesDialog>();

            // Тема (MainWindow реализует IThemeManager)
            // services.AddSingleton<IThemeManager>(provider => provider.GetRequiredService<MainWindow>());

            return services.BuildServiceProvider();
        }
    }
}