using PlatformLauncher.Services;
using System;
using System.IO;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Security.Principal;
using System.Windows;
using System.Windows.Threading;

namespace PlatformLauncher
{
    public partial class App : Application
    {
        public App()
        {
            DispatcherUnhandledException += App_DispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        }

        // Переносим логику проверки в правильное событие жизненного цикла WPF
        protected override void OnStartup(StartupEventArgs e)
        {
            AppDomain.CurrentDomain.AssemblyResolve += (sender, args) => {
                string assemblyName = new AssemblyName(args.Name).Name + ".dll";
                string assemblyPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "libs", assemblyName);
                return File.Exists(assemblyPath) ? Assembly.LoadFrom(assemblyPath) : null;
            };
            base.OnStartup(e);

            // Проверяем, есть ли среди аргументов флаг отказа от прав
            bool userDeclinedAdmin = e.Args.Contains("--no-admin-prompt");

            // Запрашиваем права ТОЛЬКО если их нет И пользователь еще не отказывался
            if (!IsAdministrator() && !userDeclinedAdmin)
            {
                var result = MessageBox.Show(
                    "Для запуска фиксов и варпа требуются права администратора.\n" +
                    "Без прав доступен только мониторинг соединений.\n" +
                    "Выдать права?",
                    "Требуются права",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result == MessageBoxResult.Yes)
                {
                    RestartAsAdministrator(true);
                }
                else
                {
                    RestartAsAdministrator(false);
                }

                return; // Прерываем выполнение текущей копии, так как мы вызвали Restart
            }

            // Если код дошел досюда, значит:
            // Либо приложение С ПРАВАМИ администратора,
            // Либо пользователь нажал «Нет» и приложение перезапустилось в ограниченном режиме.

            // ЗДЕСЬ МОЖНО ИНИЦИАЛИЗИРОВАТЬ ГЛАВНОЕ ОКНО
            // (Если у вас в App.xaml прописан StartupUri="MainWindow.xaml", оно откроется само)
        }

        private bool IsAdministrator()
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }

        private void RestartAsAdministrator(bool adm)
        {
            var startInfo = new ProcessStartInfo
            {
                UseShellExecute = true,
                Verb = adm ? "runas" : "", // "runas" запустит UAC, пустая строка — обычный запуск
                FileName = Process.GetCurrentProcess().MainModule?.FileName ?? throw new InvalidOperationException("Не удалось получить путь к исполняемому файлу.")
            };

            // Если перезапускаем БЕЗ прав, добавляем аргумент-заглушку
            if (!adm)
            {
                startInfo.Arguments = "--no-admin-prompt";
            }

            try
            {
                Process.Start(startInfo);
                Shutdown(); // Закрываем текущую копию только если новая успешно стартовала
            }
            catch (Exception ex)
            {
                // Сюда мы попадем, например, если при adm = true пользователь нажал «Нет» в самом окне UAC от Windows
                MessageBox.Show($"Не удалось перезапустить приложение: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
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
