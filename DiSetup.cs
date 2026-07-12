using Microsoft.Extensions.DependencyInjection;
using PlatformLauncher.Core.Interfaces;
using PlatformLauncher.Core.UseCases;
using PlatformLauncher.Infrastructure.Backup;
using PlatformLauncher.Infrastructure.Configuration;
using PlatformLauncher.Infrastructure.Lists;
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

            // Game list management
            services.AddSingleton<IGameListService, GameListService>();

            // Validators
            services.AddSingleton<IPythonValidatorService, PythonValidatorService>();
            services.AddSingleton<IZapretValidatorService, ZapretValidatorService>();

            // Command runner
            services.AddSingleton<ICommandRunnerService, CommandRunnerService>();

            // Обновление и установка
            services.AddSingleton<IUpdateService>(provider =>
                new UpdateService(
                    provider.GetRequiredService<ILogger>(),
                    provider.GetRequiredService<IAppConfigService>()));
            services.AddSingleton<IGameInstallService, GameInstallService>();

            // Python
            services.AddSingleton<IPythonEnvironmentManager, PythonEnvironmentManager>();
            services.AddSingleton<IPythonProcessManager, PythonProcessManager>();

            // WARP
            services.AddSingleton<IWarpManager, WarpManager>();

            // Network fix
            services.AddSingleton<INetworkFixService, NetworkFixService>();

            // Процессы
            services.AddSingleton<IProcessKiller, ProcessKiller>();

            // Theme services
            services.AddSingleton<ThemeColorApplier>();
            services.AddSingleton<TerminalThemeApplier>();
            services.AddSingleton<HandyControlThemeManager>();
            services.AddSingleton<TerminalScrollBarStyler>();
            services.AddSingleton<IThemeService, ThemeService>();

            // Zapret / lists
            services.AddSingleton<IZapretManager, ZapretManager>();
            services.AddSingleton<IWinwsLocator, WinwsLocator>();
            services.AddSingleton<IListsSanitizer>(provider =>
            {
                var logger = provider.GetRequiredService<ILogger>();
                var appConfigService = provider.GetRequiredService<IAppConfigService>();
                return new ListsSanitizer(logger, appConfigService);
            });
            services.AddSingleton<ISessionOrchestrator>(provider =>
            {
                var pythonManager = provider.GetRequiredService<IPythonProcessManager>();
                var pythonEnvManager = provider.GetRequiredService<IPythonEnvironmentManager>();
                var backupManager = provider.GetRequiredService<IBackupManager>();
                var warpManager = provider.GetRequiredService<IWarpManager>();
                var processKiller = provider.GetRequiredService<IProcessKiller>();
                var logger = provider.GetRequiredService<ILogger>();
                var listsSanitizer = provider.GetRequiredService<IListsSanitizer>();
                var updateService = provider.GetRequiredService<IUpdateService>();
                return new SessionOrchestrator(pythonManager, backupManager, warpManager, processKiller, logger, listsSanitizer, updateService, pythonEnvManager);
            });

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

            services.AddSingleton<ServiceTabViewModel>(provider =>
            {
                var warpManager = provider.GetRequiredService<IWarpManager>();
                var networkFixService = provider.GetRequiredService<INetworkFixService>();
                var settingsManager = provider.GetRequiredService<ISettingsManager>();
                var logger = provider.GetRequiredService<ILogger>();
                var terminal = provider.GetRequiredService<ITerminalOutput>();
                var appConfigService = provider.GetRequiredService<IAppConfigService>();
                var pythonEnvManager = provider.GetRequiredService<IPythonEnvironmentManager>();
                return new ServiceTabViewModel(
                    warpManager,
                    settingsManager,
                    logger,
                    terminal,
                    appConfigService,
                    pythonEnvManager,
                    networkFixService,
                    provider);
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
            // services.AddTransient<MainViewModel>();
            // services.AddSingleton<ServiceTabViewModel>();

            services.AddTransient<MainViewModel>(provider =>
            {
                return new MainViewModel(
                    provider.GetRequiredService<IGameListService>(),
                    provider.GetRequiredService<IPythonValidatorService>(),
                    provider.GetRequiredService<IZapretValidatorService>(),
                    provider.GetRequiredService<ICommandRunnerService>(),
                    provider.GetRequiredService<ISettingsManager>(),
                    provider.GetRequiredService<InstallGameUseCase>(),
                    provider.GetRequiredService<UninstallGameUseCase>(),
                    provider.GetRequiredService<SyncPresetsUseCase>(),
                    provider.GetRequiredService<StartMonitoringUseCase>(),
                    provider.GetRequiredService<StopMonitoringUseCase>(),
                    provider.GetRequiredService<IWinwsLocator>(),
                    provider.GetRequiredService<ILogger>(),
                    provider.GetRequiredService<ITerminalOutput>(),
                    provider.GetRequiredService<ISessionOrchestrator>(),
                    provider,
                    provider.GetRequiredService<IUpdateService>(),
                    provider.GetRequiredService<IPortsManager>());
            });

            services.AddSingleton<MainWindow>(provider =>
            {
                var viewModel = provider.GetRequiredService<MainViewModel>();
                var terminal = provider.GetRequiredService<ITerminalOutput>();
                var serviceProvider = provider;
                return new MainWindow(viewModel, terminal, serviceProvider, terminal);
            });

            services.AddTransient<ServiceTab>();
            services.AddTransient<SettingsTab>();
            services.AddTransient<GamePropertiesDialog>();

            // Тема (MainWindow реализует IThemeManager)
            // services.AddSingleton<IThemeManager>(provider => provider.GetRequiredService<MainWindow>());

            return services.BuildServiceProvider();
        }
    }
}