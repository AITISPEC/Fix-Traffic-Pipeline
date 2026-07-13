using Microsoft.Extensions.DependencyInjection;
using PlatformLauncher.Core.Interfaces;
using PlatformLauncher.Presentation.Views;
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Principal;
using System.Windows;
using System.Windows.Threading;

namespace PlatformLauncher.AppHost
{
    public partial class App : Application
    {
        private static ILogger? _globalLogger;

        public static void SetGlobalLogger(ILogger logger) => _globalLogger = logger;

        public App()
        {
            DispatcherUnhandledException += App_DispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        }

        static App()
        {
            AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
            {
                string? assemblyName = new AssemblyName(args.Name).Name;
                string? path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "libs", assemblyName + ".dll");
                if (File.Exists(path))
                    return Assembly.LoadFrom(path);
                return null;
            };
        }

        private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            _globalLogger?.Error("Необработанное исключение в UI (Dispatcher)", e.Exception);
            DebugLogger.WriteException("DispatcherUnhandledException", e.Exception);
            MessageBox.Show($"Ошибка: {e.Exception.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        }

        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            var ex = e.ExceptionObject as Exception;
            _globalLogger?.Error("Критическая ошибка (AppDomain)", ex ?? new Exception("Unknown"));
            DebugLogger.WriteException("UnhandledException", ex!);
            MessageBox.Show($"Критическая ошибка: {ex?.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            string libsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "libs");
            if (Directory.Exists(libsDir))
            {
                foreach (string dll in Directory.GetFiles(libsDir, "*.dll"))
                {
                    try { Assembly.LoadFrom(dll); } catch { }
                }
            }

            AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
            {
                string? assemblyName = new AssemblyName(args.Name).Name;
                string? path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "libs", assemblyName + ".dll");
                if (File.Exists(path))
                    return Assembly.LoadFrom(path);
                return null;
            };

            System.Net.ServicePointManager.SecurityProtocol = System.Net.SecurityProtocolType.Tls12 | System.Net.SecurityProtocolType.Tls11 | System.Net.SecurityProtocolType.Tls13;
            System.Net.ServicePointManager.ServerCertificateValidationCallback = delegate { return true; };

            base.OnStartup(e);

            try
            {
                if (!IsAdministrator())
                {
                    RestartAsAdministrator();
                    return;
                }

                var serviceProvider = DiSetup.ConfigureServices();
                var logger = serviceProvider.GetRequiredService<ILogger>();
                App.SetGlobalLogger(logger);
                logger.Info("=== Приложение запущено ===");

                var appConfigService = serviceProvider.GetRequiredService<IAppConfigService>();
                var config = appConfigService.Load();
                DebugLogger.SetEnabled(config.Logging?.DebugEnabled ?? false);

                var mainWindow = serviceProvider.GetRequiredService<MainWindow>();
                MainWindow = mainWindow;
                mainWindow.Show();
            }
            catch (Exception ex)
            {
                DebugLogger.WriteException("OnStartup failed", ex);
                MessageBox.Show($"Ошибка при запуске: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown();
            }
        }

        private bool IsAdministrator()
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }

        private void RestartAsAdministrator()
        {
            var startInfo = new ProcessStartInfo
            {
                UseShellExecute = true,
                Verb = "runas",
                FileName = Process.GetCurrentProcess().MainModule?.FileName
            };

            try
            {
                Process.Start(startInfo);
                Shutdown();
            }
            catch (Exception ex)
            {
                DebugLogger.WriteException("RestartAsAdministrator", ex);
                MessageBox.Show($"Не удалось перезапустить с правами администратора: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown();
            }
        }
    }
}