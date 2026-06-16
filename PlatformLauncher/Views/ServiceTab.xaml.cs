using PlatformLauncher.Services;
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
        }

        private async void StartWarpButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await Task.Run(() => WarpManager.EnsureStarted());
                MessageBox.Show("WARP запущен", "Информация", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (System.Exception ex)
            {
                MessageBox.Show($"Ошибка: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void StopWarpButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                WarpManager.Disconnect();
                MessageBox.Show("WARP отключён", "Информация", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (System.Exception ex)
            {
                MessageBox.Show($"Ошибка: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}