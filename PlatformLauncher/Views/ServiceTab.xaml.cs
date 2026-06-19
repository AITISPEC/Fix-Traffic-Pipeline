using PlatformLauncher.Services;
using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace PlatformLauncher.Views
{
    public partial class ServiceTab : UserControl
    {
        public ServiceTab()
        {
            InitializeComponent();
            Loaded += ServiceTab_Loaded;
        }

        private async void ServiceTab_Loaded(object sender, RoutedEventArgs e)
        {
            await UpdateWarpStatus();
        }

        private async Task UpdateWarpStatus()
        {
            try
            {
                string status = await WarpManager.GetStatusAsync();
                bool isConnected = status == "connected";
                StartWarpButton.IsEnabled = !isConnected;
                StopWarpButton.IsEnabled = isConnected;
            }
            catch (Exception ex)
            {
                LauncherLogger.Error($"Ошибка проверки статуса WARP: {ex}");
                StartWarpButton.IsEnabled = true;
                StopWarpButton.IsEnabled = false;
            }
        }

        private async void StartWarpButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                StartWarpButton.IsEnabled = false;
                bool ok = await WarpManager.EnsureStartedAsync();
                if (ok)
                {
                    MessageBox.Show("WARP запущен", "Информация", MessageBoxButton.OK, MessageBoxImage.Information);
                    await UpdateWarpStatus();
                }
                else
                {
                    MessageBox.Show("Ошибка запуска WARP", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                    StartWarpButton.IsEnabled = true;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                StartWarpButton.IsEnabled = true;
            }
        }

        private async void StopWarpButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                StopWarpButton.IsEnabled = false;
                bool ok = await WarpManager.DisconnectAsync();
                if (ok)
                {
                    MessageBox.Show("WARP отключён", "Информация", MessageBoxButton.OK, MessageBoxImage.Information);
                    await UpdateWarpStatus();
                }
                else
                {
                    MessageBox.Show("Ошибка отключения WARP", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                    StopWarpButton.IsEnabled = true;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                StopWarpButton.IsEnabled = true;
            }
        }

        // Обработчик для кнопки "Темы"
        private void ThemesButton_Click(object sender, RoutedEventArgs e)
        {
            // Открываем диалог выбора темы (создадим отдельное окно)
            var dialog = new ThemeDialog();
            if (dialog.ShowDialog() == true)
            {
                // Применяем тему к главному окну
                var mainWindow = Application.Current.MainWindow as MainWindow;
                mainWindow?.SetTerminalTheme(dialog.SelectedTheme);
            }
        }
    }
}