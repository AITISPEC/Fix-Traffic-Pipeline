using PlatformLauncher.Services;
using System;
using System.IO;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Security.Principal;
using System.Windows;
using System.Windows.Threading;
using System.Runtime.InteropServices;

namespace PlatformLauncher
{
    public partial class App : Application
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetDllDirectory(string lpPathName);
        public App()
        {
            DispatcherUnhandledException += App_DispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;

            string libsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "libs");
            if (Directory.Exists(libsPath))
            {
                SetDllDirectory(libsPath);
            }
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
            {
                string assemblyName = new AssemblyName(args.Name).Name + ".dll";
                string libsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "libs", assemblyName);
                if (File.Exists(libsPath))
                    return Assembly.LoadFrom(libsPath);
                return null;
            };
            base.OnStartup(e);

            // Обязательный запуск от администратора
            if (!IsAdministrator())
            {
                RestartAsAdministrator();
                return;
            }

            // Если админ – запускаем главное окно (StartupUri в App.xaml)
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
                FileName = Process.GetCurrentProcess().MainModule?.FileName ?? throw new InvalidOperationException("Не удалось получить путь к исполняемому файлу.")
            };

            try
            {
                Process.Start(startInfo);
                Shutdown();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Не удалось перезапустить приложение с правами администратора: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown();
            }
        }

        private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            LauncherLogger.Error($"UI Exception: {e.Exception}");
            MessageBox.Show($"Произошла ошибка: {e.Exception.Message}\nПодробности в логе.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        }

        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            var ex = e.ExceptionObject as Exception;
            LauncherLogger.Error($"Unhandled Exception: {ex}");
            MessageBox.Show($"Критическая ошибка: {ex?.Message}\nПриложение будет закрыто.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}