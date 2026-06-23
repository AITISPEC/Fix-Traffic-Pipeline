using Microsoft.Extensions.DependencyInjection;
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
    public partial class App : System.Windows.Application
    {
        public App()
        {
            DebugLogger.Write("=== APP CONSTRUCTOR START ===");
            DispatcherUnhandledException += App_DispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            AppDomain.CurrentDomain.FirstChanceException += (s, e) =>
            {
                DebugLogger.WriteException("FIRST CHANCE EXCEPTION", e.Exception);
            };
            DebugLogger.Write("=== APP CONSTRUCTOR END ===");
        }

        static App()
        {
            AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
            {
                string assemblyName = new AssemblyName(args.Name).Name;
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "libs", assemblyName + ".dll");
                if (File.Exists(path))
                    return Assembly.LoadFrom(path);
                return null;
            };
        }

        private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            DebugLogger.WriteException("DispatcherUnhandledException", e.Exception);
            MessageBox.Show($"Ошибка UI: {e.Exception.Message}\n\n{e.Exception.StackTrace}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        }

        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            var ex = e.ExceptionObject as Exception;
            DebugLogger.WriteException("UnhandledException", ex);
            MessageBox.Show($"Критическая ошибка: {ex?.Message}\n\n{ex?.StackTrace}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
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
                string assemblyName = new AssemblyName(args.Name).Name;
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "libs", assemblyName + ".dll");
                DebugLogger.Write($"AssemblyResolve: {args.Name} -> {path}");
                if (File.Exists(path))
                {
                    DebugLogger.Write($"  -> FOUND");
                    return Assembly.LoadFrom(path);
                }
                DebugLogger.Write($"  -> NOT FOUND");
                return null;
            };

            DebugLogger.Write("=== OnStartup START ===");
            base.OnStartup(e);

            try
            {
                if (!IsAdministrator())
                {
                    DebugLogger.Write("RestartAsAdministrator called");
                    RestartAsAdministrator();
                    return;
                }

                DebugLogger.Write("Creating service provider via DiSetup.ConfigureServices()");
                var serviceProvider = DiSetup.ConfigureServices();
                DebugLogger.Write("Service provider created");

                DebugLogger.Write("Resolving MainWindow");
                var mainWindow = serviceProvider.GetRequiredService<MainWindow>();
                DebugLogger.Write("MainWindow resolved");

                MainWindow = mainWindow;
                DebugLogger.Write("MainWindow set as Application.MainWindow");

                DebugLogger.Write("Calling mainWindow.Show()");
                mainWindow.Show();
                DebugLogger.Write("mainWindow.Show() returned");
            }
            catch (Exception ex)
            {
                DebugLogger.WriteException("OnStartup CATCH", ex);
                MessageBox.Show($"Ошибка при запуске: {ex.Message}\n\n{ex.StackTrace}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown();
            }
            DebugLogger.Write("=== OnStartup END ===");
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
                MessageBox.Show($"Не удалось перезапустить приложение с правами администратора: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown();
            }
        }
    }
}